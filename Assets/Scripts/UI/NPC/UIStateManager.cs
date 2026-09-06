using UnityEngine;
using Unity.Cinemachine;

public enum UIState
{ 
    None,           // UI 없음
    Dialogue,        // 대화 중
    Popup,           // NPC 팝업 메뉴
    Shop,            // 상점
    Upgrade,         // 업그레이드
    Quest,           // 퀘스트 패널
    SubQuestBoard,   // 게시판 NPC (서브퀘스트 수락)
    Truck,           // 트럭 NPC (퀘스트 완료)
    Inventory,       // 인벤토리
    Elevator,         // 엘리베이터 (신규 추가)
    Settlement,        // 정산 (신규 추가)
    Pause,            // 일시정지 (신규 추가)
    Market,          // 거래소 단말기 (MarketScene Additive — UI는 별도 씬, 여기선 입력 차단용 상태만)
    WorldMap,        // 전체 지도 오버레이 (M 키 — UI는 코드 생성 오버레이, 여기선 입력 차단용 상태만)
    Workbench,       // 작업대 (장비·유물 강화 — 코드 생성 오버레이)
    BedRest,         // 침대 낮잠/수면 선택 팝업 (코드 생성 오버레이 — 여기선 입력 차단용 상태만)
    Codex            // 도감 오버레이 (광물·장비·아이템·유물 — 코드 생성, 일시정지 메뉴에서 열림)
}

public class UIStateManager : MonoBehaviour
{
    public static UIStateManager Instance { get; private set; }
    public UIState CurrentState { get; private set; } = UIState.None;
    
    private Transform _activeInteractionSource;
    private Unity.Cinemachine.CinemachineCamera _activeDialogueCamera;
    private Transform _playerTransform;
    private const float AUTO_CLOSE_DISTANCE = 3.0f; // NPC 클릭 사거리(5m)보다 커야 즉시 닫히지 않음

    [Header("UI 루트 패널")]
    public GameObject dialogueRoot;
    public GameObject popupRoot;
    public GameObject shopRoot;
    public GameObject upgradeRoot;
    public GameObject questRoot;
    public GameObject subQuestBoardRoot;   // 서브퀘스트 게시판 UI
    public GameObject truckRoot;           // 트럭 UI
    public GameObject inventoryRoot;       // 인벤토리 UI (신규 추가)
    public GameObject settlementRoot;    // 정산 UI (신규 추가)
    public GameObject pauseRoot;         // 일시정지 UI (신규 추가)

    [Header("Manager 참조")]
    public ShopManager shopManager;

    [Header("코드 생성 UI 사용 (지상씬용 — 씬마다 지정)")]
    [Tooltip("체크하면 상점을 씬 프리팹(shopRoot) 대신 코드 생성 ShopOverlayUI로 연다")]
    public bool useCodeBuiltShopUI = false;
    [Tooltip("체크하면 인벤토리 대신 코드 생성 WarehouseOverlayUI(창고+가방 통합)를 연다. 지상씬에서만 체크할 것")]
    public bool useCodeBuiltWarehouseUI = false;
    [Tooltip("체크하면 인벤토리를 코드 생성 InventoryOverlayUI(광물+가방)로 연다. 지하씬에서만 체크할 것")]
    public bool useCodeBuiltInventoryUI = false;
    [Tooltip("체크하면 일시정지를 씬 프리팹(pauseRoot) 대신 코드 생성 PauseOverlayUI로 연다")]
    public bool useCodeBuiltPauseUI = false;
    [Tooltip("체크하면 퀘스트 로그를 씬 프리팹(questRoot) 대신 코드 생성 QuestOverlayUI로 연다")]
    public bool useCodeBuiltQuestUI = false;
    [Tooltip("체크하면 서브퀘스트 게시판을 씬 프리팹 대신 코드 생성 SubQuestBoardOverlayUI로 연다")]
    public bool useCodeBuiltSubQuestUI = false;
    [Tooltip("체크하면 NPC 팝업(트럭)을 씬 프리팹(NpcPopup) 대신 코드 생성 NpcPopupOverlayUI로 연다")]
    public bool useCodeBuiltPopupUI = false;
    [Tooltip("체크하면 업그레이드를 씬 프리팹(upgradeRoot) 대신 코드 생성 UpgradeOverlayUI로 연다")]
    public bool useCodeBuiltUpgradeUI = false;
    [Tooltip("체크하면 긴급 탈출 결과(잃은/챙긴 광물)를 코드 생성 EmergencyEscapeOverlayUI로 지상 도착 시 띄운다. 지상씬에서 체크")]
    public bool useCodeBuiltSettlementUI = false;

    [Header("전역 단축키를 끌 씬")]
    [Tooltip("이 씬들에서는 ESC/Tab/J/M 전역 단축키를 처리하지 않는다.\n" +
             "UIStateManager는 DontDestroyOnLoad라 자체 인스턴스가 없는 씬(메인메뉴·로딩)에도 따라오는데, " +
             "거기서 단축키가 먹으면 코드 생성 오버레이가 그 위에 떠서 클릭을 전부 막는다. " +
             "(옛 프리팹 UI는 그 씬에 패널이 없어 아무 일도 없었지만, 코드 오버레이는 없으면 스스로 만든다)")]
    public string[] shortcutDisabledScenes = { "MainMenuScene", "LoadingScene" };

    [Header("인벤토리 카메라")]
    public CinemachineCamera inventoryCamera; // CM_InventoryCam — 인벤토리 오픈 시 활성화 (줌인 + 플레이어 우측 프레이밍)

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // [중요] 씬이 바뀔 때 새로운 씬의 UI 루트들을 기존 싱글톤 인스턴스에 넘겨줌
            Instance.UpdateReferences(this);
            Destroy(gameObject);
            return;
        }
        Instance = this;
        transform.SetParent(null); // 최상위로 분리
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // ShopManager 참조
        if (shopManager == null)
            shopManager = FindFirstObjectByType<ShopManager>();

        // 초기 상태: 모든 UI 숨김
        SetState(UIState.None);
    }

    /// <summary>
    /// 지금 플레이어 조작(이동·점프·상호작용·채굴·도구)을 막아야 하는지.
    ///
    /// <see cref="UIState"/>를 쓰는 UI뿐 아니라, 상태를 갖지 않고 전면에 뜨는 코드 오버레이
    /// (설정·가이드·탐험 종료 확인창)까지 한자리에서 판정한다. 이 오버레이들은 W/A/S/D·Space를
    /// 메뉴 조작 키로 쓰기 때문에, 막지 않으면 뒤에서 플레이어가 같이 움직이고 캔다.
    ///
    /// 새 전면 오버레이를 추가하면 여기에 한 줄만 더하면 된다 — 호출부를 다시 훑지 않도록.
    /// </summary>
    public static bool IsInputBlocked =>
        (Instance != null && Instance.CurrentState != UIState.None)
        || SettingsOverlayUI.IsOpen
        || GuideOverlayUI.IsOpen
        || ExploreExitOverlayUI.IsOpen
        || DaySummaryUI.IsShowing
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
        || BugReport.BugReportOverlayUI.IsOpen
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
        || DebugTools.DebugConsoleOverlayUI.IsOpen
#endif
        ;

    /// <summary>
    /// 팝업(<see cref="UIState.Popup"/>)에서 들어간 UI(상점·업그레이드·퀘스트·대화)를 닫을 때 호출한다.
    /// 그 UI를 연 NPC(<see cref="INpcPopupSource"/>)가 아직 유효하고 코드 팝업을 쓰는 씬이면
    /// <b>None으로 닫지 않고 그 NPC의 팝업으로 되돌린다</b>. (NPC → 팝업 → 상점/퀘스트/대화 → ESC → 팝업)
    ///
    /// 되돌릴 대상이 없으면(소스 파괴·인터페이스 없음·프리팹 팝업 씬) 기존대로 None으로 닫는다.
    /// 팝업으로 돌아간 뒤 다시 ESC를 누르면 Popup 상태의 일반 ESC 처리가 None으로 닫는다.
    /// </summary>
    public void ReturnToPopupOrClose()
    {
        // NPC 상호작용이 '머리 위 팝업'에서 '근접 선택지 목록'으로 바뀐 뒤로는 되돌아갈 팝업이 없다.
        // 대화·상점·강화를 닫으면 그냥 월드로 돌아가고, NPC 옆에 서 있으면 목록이 다시 떠 있다.
        // (예전처럼 팝업을 되살리면 목록과 팝업이 겹쳐 두 번 고르게 된다)
        SetState(UIState.None);
    }

    public void SetState(UIState newState, Transform source = null)
    {
        // 대화 중에는 다른 UI로 전환 차단 (Close는 허용)
        if (CurrentState == UIState.Dialogue && newState != UIState.None && newState != UIState.Dialogue)
        {
            Debug.Log("[UIStateManager] 대화 중에는 다른 UI를 열 수 없습니다.");
            return;
        }

        // 대화 상태를 벗어날 때 사용 중이던 대화 전용 카메라가 있다면 끕니다.
        if (CurrentState == UIState.Dialogue && newState != UIState.Dialogue)
        {
            if (_activeDialogueCamera != null)
            {
                _activeDialogueCamera.enabled = false;
                _activeDialogueCamera.gameObject.SetActive(false); // 비활성화된 게임오브젝트를 켰을 수 있으므로 다시 끕니다.
                _activeDialogueCamera = null;

                // 대화 중 카메라 이동 시 어두워짐을 방지하기 위해 껐던 시야 제한 오버레이를 원래대로 복구
                var visionOverlay = FindFirstObjectByType<PlayerVisionOverlay>();
                if (visionOverlay != null)
                {
                    visionOverlay.ClearDarknessOverride();
                }
            }
        }

        CurrentState = newState;
        _activeInteractionSource = source; // 상호작용 지점 저장

        // 모든 UI 비활성화
        if (dialogueRoot != null) dialogueRoot.SetActive(false);
        if (popupRoot != null) popupRoot.SetActive(false);
        if (shopRoot != null) shopRoot.SetActive(false);
        if (upgradeRoot != null) upgradeRoot.SetActive(false);
        if (questRoot != null) questRoot.SetActive(false);
        if (subQuestBoardRoot != null) subQuestBoardRoot.SetActive(false);
        if (truckRoot != null) truckRoot.SetActive(false);
        if (inventoryRoot != null)
        {
            // 인벤토리는 InventoryUI를 통해 닫기 로직을 타게 함
            var invUI = inventoryRoot.GetComponentInChildren<InventoryUI>();
            if (invUI != null) invUI.CloseInventory();
            else inventoryRoot.SetActive(false);
        }
        if (settlementRoot != null) settlementRoot.SetActive(false);
        if (pauseRoot != null) pauseRoot.SetActive(false);

        // [신규] 카메라 줌 처리
        var cam = CameraFollow.Instance;
        if (cam == null) cam = FindFirstObjectByType<CameraFollow>();

        if (cam != null)
        {
            // 상점(Shop) UI가 열릴 때만 카메라 줌인 활성화
            cam.SetUIZoom(newState == UIState.Shop);
        }
        else
        {
            Debug.LogWarning("[UIStateManager] CameraFollow를 찾을 수 없어 줌 기능을 생략합니다.");
        }

        // 인벤토리 전용 시네머신 카메라 전환 (활성화되면 CinemachineBrain이 자동 블렌드)
        if (inventoryCamera != null)
            inventoryCamera.enabled = (newState == UIState.Inventory);

        // Shop 종료 처리 (상태 전환 시)
        if (shopManager != null && shopManager.IsShopOpen() && newState != UIState.Shop)
        {
            shopManager.CloseShop();
        }

        // 엘리베이터 UI 동기화 (신규 추가)
        if (ElevatorUI.Instance != null && newState != UIState.Elevator)
        {
            ElevatorUI.Instance.HidePanel();
        }

        // 코드 생성 엘리베이터 오버레이 동기화 — Elevator 상태를 벗어나면 닫는다
        if (newState != UIState.Elevator && ElevatorOverlayUI.IsOpen)
        {
            ElevatorOverlayUI.CloseStatic();
        }
        if (newState != UIState.Elevator && ElevatorEntryUI.IsOpen)
        {
            ElevatorEntryUI.CloseStatic();
        }

        // 전체 지도 오버레이 동기화 — WorldMap 상태를 벗어나면 오버레이도 닫는다
        if (newState != UIState.WorldMap && WorldMapOverlay.IsOpen)
        {
            WorldMapOverlay.CloseStatic();
        }

        // 코드 생성 창고/인벤토리/상점 오버레이 동기화 — 해당 상태를 벗어나면 닫는다
        if (newState != UIState.Inventory && WarehouseOverlayUI.IsOpen)
        {
            WarehouseOverlayUI.CloseStatic();
        }
        if (newState != UIState.Inventory && InventoryOverlayUI.IsOpen)
        {
            InventoryOverlayUI.CloseStatic();
        }
        if (newState != UIState.Shop && ShopOverlayUI.IsOpen)
        {
            ShopOverlayUI.CloseStatic();
        }
        if (newState != UIState.Pause && PauseOverlayUI.IsOpen)
        {
            PauseOverlayUI.CloseStatic();
        }
        if (newState != UIState.Quest && QuestOverlayUI.IsOpen)
        {
            QuestOverlayUI.CloseStatic();
        }
        if (newState != UIState.SubQuestBoard && SubQuestBoardOverlayUI.IsOpen)
        {
            SubQuestBoardOverlayUI.CloseStatic();
        }
        if (newState != UIState.Popup && NpcPopupOverlayUI.IsOpen)
        {
            NpcPopupOverlayUI.CloseStatic();
        }
        if (newState != UIState.Upgrade && UpgradeOverlayUI.IsOpen)
        {
            UpgradeOverlayUI.CloseStatic();
        }
        if (newState != UIState.Workbench && EquipmentUpgradeOverlayUI.IsOpen)
        {
            EquipmentUpgradeOverlayUI.CloseStatic();
        }
        if (newState != UIState.Codex && CodexOverlayUI.IsOpen)
        {
            CodexOverlayUI.CloseStatic();
        }

        // 선택된 UI만 활성화
        switch (newState)
        {
            case UIState.Dialogue:
                if (dialogueRoot != null) dialogueRoot.SetActive(true);
                
                // 대화 카메라 전환
                if (source != null)
                {
                    string targetCamName = null;
                    var autoTrigger = source.GetComponent<AutoDialogueTrigger>();
                    var npc = source.GetComponent<INpcPopupSource>();

                    if (autoTrigger != null && autoTrigger.dialogueData != null && !string.IsNullOrEmpty(autoTrigger.dialogueData.targetCameraName))
                    {
                        targetCamName = autoTrigger.dialogueData.targetCameraName;
                    }
                    else if (npc != null && npc.Dialogue != null && !string.IsNullOrEmpty(npc.Dialogue.targetCameraName))
                    {
                        targetCamName = npc.Dialogue.targetCameraName;
                    }

                    if (!string.IsNullOrEmpty(targetCamName))
                    {
                        // GameObject.Find는 비활성화된 오브젝트를 못 찾으므로 전체 검색 사용
                        var cams = Resources.FindObjectsOfTypeAll<Unity.Cinemachine.CinemachineCamera>();
                        foreach (var c in cams)
                        {
                            if (c.gameObject.name == targetCamName && c.gameObject.scene.isLoaded)
                            {
                                _activeDialogueCamera = c;
                                break;
                            }
                        }
                    }
                    else
                    {
                        if (autoTrigger != null && autoTrigger.dialogueCamera != null)
                        {
                            _activeDialogueCamera = autoTrigger.dialogueCamera;
                        }
                        else if (npc != null && npc.DialogueCamera != null)
                        {
                            _activeDialogueCamera = npc.DialogueCamera;
                        }
                    }

                    if (_activeDialogueCamera != null)
                    {
                        // 게임오브젝트가 꺼져있으면 켜줍니다 (인스펙터에서 껐을 경우)
                        if (!_activeDialogueCamera.gameObject.activeSelf)
                            _activeDialogueCamera.gameObject.SetActive(true);
                            
                        _activeDialogueCamera.enabled = true;

                        // 지하 등에서 시야 제한(어둠막)이 켜져 있을 때, 카메라가 NPC로 이동하면 
                        // 플레이어 주변만 밝게 뚫어주는 셰이더 특성상 화면 전체가 캄캄해지는 문제가 있습니다.
                        // 이를 막기 위해 대화 전용 카메라가 켜지면 일시적으로 어둠막을 투명하게 만듭니다.
                        var visionOverlay = FindFirstObjectByType<PlayerVisionOverlay>();
                        if (visionOverlay != null)
                        {
                            visionOverlay.SetDarknessOverride(0f);
                        }
                    }
                }
                break;
            case UIState.Popup:
                if (popupRoot != null) popupRoot.SetActive(true);
                break;
            case UIState.Shop:
                // 코드 생성 상점 오버레이 — 자체적으로 구매/판매 목록을 갖고 있어 shopRoot·인벤토리를 열지 않는다
                if (useCodeBuiltShopUI)
                {
                    ShopOverlayUI.Open();
                    break;
                }
                if (shopRoot != null) shopRoot.SetActive(true);
                if (shopManager != null)
                {
                    shopManager.OpenShop();
                }
                // 상점 개봉 시 인벤토리도 함께 열기
                if (inventoryRoot != null)
                {
                    var invUI = inventoryRoot.GetComponentInChildren<InventoryUI>();
                    if (invUI != null && !invUI.IsOpen()) invUI.OpenInventory();
                }
                break;
            case UIState.Upgrade:
                // 코드 생성 업그레이드 오버레이 — 자체적으로 트리·상세·해금을 갖고 있어 upgradeRoot를 열지 않는다
                if (useCodeBuiltUpgradeUI)
                {
                    UpgradeOverlayUI.Open();
                    break;
                }
                if (upgradeRoot != null) upgradeRoot.SetActive(true);
                // 업그레이드 패널이 켜질 때 필요한 초기화 로직이 있다면 여기서 호출 가능
                break;
            case UIState.Quest:
                // 코드 생성 퀘스트 로그 오버레이 (제출 허용 여부는 QuestOverlayUI.RequestSubmission으로 전달됨)
                if (useCodeBuiltQuestUI)
                {
                    QuestOverlayUI.Open();
                    break;
                }
                if (questRoot != null)
                {
                    questRoot.SetActive(true);
                    // 켜질 때 즉시 최신 정보로 갱신되도록 호출
                    questRoot.GetComponentInChildren<QuestPanelUI>()?.RefreshLog();
                }
                else
                {
                    Debug.LogWarning("[UIStateManager] Quest Root가 할당되지 않았습니다!");
                }
                break;
            case UIState.SubQuestBoard:
                // 코드 생성 서브퀘스트 게시판 오버레이
                if (useCodeBuiltSubQuestUI)
                {
                    SubQuestBoardOverlayUI.Open();
                    break;
                }
                if (subQuestBoardRoot != null)
                {
                    subQuestBoardRoot.SetActive(true);
                }
                else
                {
                    Debug.LogWarning("[UIStateManager] SubQuestBoard Root가 할당되지 않았습니다!");
                }
                break;
            case UIState.Truck:
                if (truckRoot != null)
                {
                    truckRoot.SetActive(true);
                }
                else
                {
                    Debug.LogWarning("[UIStateManager] Truck Root가 할당되지 않았습니다!");
                }
                break;
            case UIState.Inventory:
                // 코드 생성 창고 오버레이 — 창고와 가방을 한 화면에 담고 있어 기존 인벤토리 패널을 대체한다
                if (useCodeBuiltWarehouseUI)
                {
                    WarehouseOverlayUI.Open();
                    break;
                }
                // 지하용 코드 생성 인벤토리 오버레이 (광물 + 가방)
                if (useCodeBuiltInventoryUI)
                {
                    InventoryOverlayUI.Open();
                    break;
                }
                if (inventoryRoot != null)
                {
                    var invUI = inventoryRoot.GetComponentInChildren<InventoryUI>();
                    if (invUI != null) invUI.OpenInventory();
                    else inventoryRoot.SetActive(true);
                }
                else
                {
                    Debug.LogWarning("[UIStateManager] Inventory Root가 할당되지 않았습니다!");
                }
                break;
            case UIState.Workbench:
                // 코드 생성 장비·유물 강화 오버레이 (작업대). 자체적으로 목록·상세·강화를 갖는다.
                EquipmentUpgradeOverlayUI.Open();
                break;
            case UIState.Codex:
                // 코드 생성 도감 오버레이(4탭). 자체적으로 격자·상세·발견 백필을 갖는다.
                CodexOverlayUI.Open();
                break;
            case UIState.Elevator:
                // ElevatorUI.ShowPanel은 외부(ElevatorInteraction)에서 인자와 함께 호출되므로
                // 여기서는 상태 동기화만 담당하거나, 필요한 추가 처리가 있다면 수행.
                break;
            case UIState.Settlement:
                if (settlementRoot != null) settlementRoot.SetActive(true);
                break;
            case UIState.Pause:
                // 코드 생성 일시정지 오버레이 — 자체적으로 timeScale을 0으로 만들고 복원한다
                if (useCodeBuiltPauseUI)
                {
                    PauseOverlayUI.Open();
                    break;
                }
                if (pauseRoot != null) pauseRoot.SetActive(true);
                break;
            case UIState.Market:
                // 마켓 UI는 Additive로 로드된 MarketScene 안에 있으므로 여기선 토글하지 않는다.
                // 이 상태값은 플레이어 입력/상호작용 차단(PlayerInputHandler·PlayerInteractor) 용도다.
                break;
            case UIState.WorldMap:
                // 전체 지도 오버레이 열기 (코드 생성 오버레이 — timeScale·입력은 오버레이가 관리)
                WorldMapOverlay.Open();
                break;
            case UIState.BedRest:
                // 침대 낮잠/수면 선택 오버레이는 BedInteractable이 직접 Show한다.
                // 이 상태값은 플레이어 입력/상호작용 차단 용도(다른 코드 오버레이와 동일).
                break;
            case UIState.None:
                // ShopManager를 통해 Shop UI 닫기
                if (shopManager != null && shopManager.IsShopOpen())
                {
                    shopManager.CloseShop();
                }
                break;
        }
    }

    void Update()
    {
        // 로딩 중 기습적인 UI 토글 차단
        if (LoadingData.IsLoading) return;

        // 메인메뉴·로딩처럼 게임 UI가 없는 씬에서는 전역 단축키를 아예 처리하지 않는다.
        // (이 매니저는 DontDestroyOnLoad라 그런 씬까지 따라온다)
        if (IsShortcutDisabledScene()) return;

        // 설정 오버레이가 열려 있는 동안엔 전역 단축키(ESC/J/Tab)를 처리하지 않는다.
        // ClosedThisFrame: 오버레이가 ESC로 닫힌 그 프레임에 Pause 토글이 중복 실행되는 것 방지.
        if (SettingsOverlayUI.IsOpen || SettingsOverlayUI.ClosedThisFrame) return;

        // 코드 생성 창고/상점 오버레이도 동일 — ESC/Tab을 자기가 처리하고 닫히면서 상태를 되돌린다.
        if (WarehouseOverlayUI.IsOpen || WarehouseOverlayUI.ClosedThisFrame) return;
        if (InventoryOverlayUI.IsOpen || InventoryOverlayUI.ClosedThisFrame) return;
        if (ShopOverlayUI.IsOpen || ShopOverlayUI.ClosedThisFrame) return;
        if (PauseOverlayUI.IsOpen || PauseOverlayUI.ClosedThisFrame) return;
        // 퀘스트/서브퀘스트 코드 오버레이도 자기가 J/E/ESC를 처리하고 닫히면서 상태를 되돌린다.
        if (QuestOverlayUI.IsOpen || QuestOverlayUI.ClosedThisFrame) return;
        if (SubQuestBoardOverlayUI.IsOpen || SubQuestBoardOverlayUI.ClosedThisFrame) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
        // 버그 리포트 메모 입력 중 — Enter/ESC를 자기가 처리한다.
        // 여기서 막지 않으면 한글 메모를 치는 동안 Tab(인벤토리)·M(지도)이 같이 발동한다.
        if (BugReport.BugReportOverlayUI.IsOpen || BugReport.BugReportOverlayUI.ClosedThisFrame) return;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
        // 디버그 콘솔에 명령을 치는 동안 Tab(인벤토리)·M(지도)·ESC가 같이 발동하는 것을 막는다.
        if (DebugTools.DebugConsoleOverlayUI.IsOpen || DebugTools.DebugConsoleOverlayUI.ClosedThisFrame) return;
#endif
        // 코드 생성 업그레이드 오버레이도 ESC를 자기가 처리하고 닫히면서 상태를 되돌린다.
        if (UpgradeOverlayUI.IsOpen || UpgradeOverlayUI.ClosedThisFrame) return;
        // 작업대(장비·유물 강화) 오버레이도 ESC/Tab을 자기가 처리하고 닫히면서 상태를 None으로 되돌린다.
        // 이 가드가 없으면 오버레이가 먼저 닫혀 CurrentState==None이 된 뒤 UIStateManager가 같은 프레임의 ESC를
        // 다시 처리해 일시정지 화면이 뜬다.
        if (EquipmentUpgradeOverlayUI.IsOpen || EquipmentUpgradeOverlayUI.ClosedThisFrame) return;
        // 도감 오버레이도 ESC/WASD/QE를 자기가 처리하고 닫히면서 상태를 None으로 되돌린다.
        if (CodexOverlayUI.IsOpen || CodexOverlayUI.ClosedThisFrame) return;
        // 긴급 탈출 결과 오버레이도 자기가 Enter/ESC를 처리한다.
        if (EmergencyEscapeOverlayUI.IsOpen || EmergencyEscapeOverlayUI.ClosedThisFrame) return;
        // 게임오버 연출(사망·긴급탈출 공용)이 재생되는 동안엔 모든 전역 단축키를 막는다(연출은 Input 폴링으로 스킵).
        if (GameOverSequenceUI.IsPlaying) return;
        if (EmergencyEscapeSequenceUI.IsPlaying) return;
        // 가이드 오버레이도 자기가 ESC/방향키를 처리하고 닫히면서 게임을 재개한다.
        if (GuideOverlayUI.IsOpen || GuideOverlayUI.ClosedThisFrame) return;
        // 탐험 종료 확인창은 UIState를 쓰지 않으므로(트리거 재진입 판정과 물린다) 여기서 직접 막는다.
        // 없으면 ESC 한 번에 확인창이 닫히고 같은 프레임에 일시정지까지 열린다.
        if (ExploreExitOverlayUI.IsOpen || ExploreExitOverlayUI.ClosedThisFrame) return;
        // 엘리베이터 오버레이(지하 층 선택 / 지상 입구)도 자기가 ESC를 처리한다.
        if (ElevatorOverlayUI.IsOpen || ElevatorOverlayUI.ClosedThisFrame) return;
        if (ElevatorEntryUI.IsOpen || ElevatorEntryUI.ClosedThisFrame) return;

        // 전체 지도 오버레이가 열려 있으면 M/ESC로 닫기만 처리하고 나머지 단축키는 무시한다.
        if (WorldMapOverlay.IsOpen)
        {
            if (Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.Escape))
            {
                // ESC는 뒤로가기음, M은 토글이라 평소 클릭음 그대로.
                if (Input.GetKeyDown(KeyCode.Escape)) CodeUI.PlayBack();
                SetState(UIState.None); // → WorldMapOverlay.CloseStatic()
            }
            return;
        }
        if (WorldMapOverlay.ClosedThisFrame) return; // 닫힌 그 프레임의 M/ESC 중복 처리 방지

        // 'M' 키 — UI가 없을 때만 전체 지도 열기. 던전 안에서는 '신호 없음'이라 열리지 않고,
        // 지도 장비(MapUnlockGate)를 사기 전에는 아예 존재하지 않는다.
        if (Input.GetKeyDown(KeyCode.M) && CurrentState == UIState.None)
        {
            if (!DungeonOverlayController.IsInDungeon && MapUnlockGate.IsUnlocked)
                SetState(UIState.WorldMap);
            return;
        }

        // [추가] 긴급 탈출·사망으로 지상에 막 도착했으면 결과 오버레이를 띄운다(정산 팝업보다 우선).
        // 사유는 EmergencyEscapeReport.Reason에 실려 오고, 오버레이가 제목·부제를 그에 맞춰 바꾼다.
        if (CurrentState == UIState.None && useCodeBuiltSettlementUI && EmergencyEscapeReport.HasPending)
        {
            string escScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (escScene.Contains("Upground"))
            {
                EmergencyEscapeOverlayUI.Open();
                return;
            }
        }

        // [추가] 지상 씬(DemoUpground, UpgroundScene 등)이고 정산 데이터가 있다면 정산 UI 팝업
        if (CurrentState == UIState.None && SettlementManager.Instance != null && SettlementManager.Instance.IsDataPending)
        {
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (sceneName.Contains("Upground"))
            {
                Debug.Log("[UIStateManager] 정산 데이터가 있습니다. Settlement UI를 엽니다.");
                SetState(UIState.Settlement);
            }
        }

        // 'J' 키 입력 처리 (퀘스트 로그)
        if (Input.GetKeyDown(KeyCode.J))
        {
            HandleQuestToggle();
        }

        // 'Tab' 키 입력 처리 (인벤토리)
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            HandleInventoryToggle();
        }

        // 'ESC' 키 입력 처리 (일시정지 및 닫기)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (CurrentState == UIState.None)
            {
                SetState(UIState.Pause); // UI가 없으면 일시정지 켬
            }
            else if (CurrentState == UIState.Pause)
            {
                CodeUI.PlayBack();
                SetState(UIState.None);  // 일시정지 중이면 해제
            }
            else if (CurrentState == UIState.Market)
            {
                // 마켓은 MarketSceneController.Exit()가 ESC→씬 언로드를 처리하고,
                // 언로드 콜백에서 상태를 None으로 되돌린다. 여기서 먼저 None으로 바꾸면
                // timeScale 복귀 전에 입력이 풀려 충돌하므로 건드리지 않는다.
            }
            else if (CurrentState == UIState.Dialogue)
            {
                // 대화도 팝업(NPC)에서 들어왔다면 None이 아니라 그 팝업으로 되돌린다
                // (NPC → 팝업 → 대화 → ESC → 팝업). 팝업에서 온 게 아니면(source 없음) 그대로 None.
                CodeUI.PlayBack();
                ReturnToPopupOrClose();
            }
            else
            {
                // 다른 UI가 켜져 있으면 해당 UI를 닫고 None으로 복원
                // (Popup 상태의 ESC도 여기 — 팝업 자체를 닫는다. ReturnToPopupOrClose를 쓰면 도로 열린다.)
                CodeUI.PlayBack();
                SetState(UIState.None);
            }
        }

        // 거리 기반 자동 종료 체크
        CheckDistanceToSource();
    }

    /// <summary>현재 씬이 전역 단축키를 꺼야 하는 씬인지.</summary>
    private bool IsShortcutDisabledScene()
    {
        if (shortcutDisabledScenes == null || shortcutDisabledScenes.Length == 0) return false;

        string current = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        foreach (var name in shortcutDisabledScenes)
        {
            if (!string.IsNullOrEmpty(name) &&
                string.Equals(name, current, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 플레이어가 상호작용 소스의 콜라이더를 벗어나면 팝업 UI를 닫습니다.
    /// </summary>
    private void CheckDistanceToSource()
    {
        // 1. 상태 및 팝업 여부 확인 (Popup 상태일 때만 자동 종료 유지)
        if (CurrentState != UIState.Popup || _activeInteractionSource == null) return;

        // 2. 상호작용 소스가 파괴되었는지 확인
        if (_activeInteractionSource.Equals(null))
        {
            SetState(UIState.None);
            return;
        }

        // 3. 플레이어 참조 확인
        if (_playerTransform == null || _playerTransform.Equals(null))
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) _playerTransform = player.transform;
            else return;
        }

        // 4. 콜라이더 기반 겹침 확인
        Collider2D sourceCol = _activeInteractionSource.GetComponent<Collider2D>();
        Collider2D playerCol = _playerTransform.GetComponent<Collider2D>();

        // 둘 중 하나라도 콜라이더가 없으면 기존 거리(약 3m)로 폴백 하거나 무시
        if (sourceCol != null && playerCol != null)
        {
            if (!sourceCol.IsTouching(playerCol))
            {
                Debug.Log($"[UIStateManager] NPC 영역을 벗어나 UI를 닫습니다. (대상: {_activeInteractionSource.name})");
                SetState(UIState.None);
            }
        }
        else
        {
            // 폴백: 콜라이더가 없는 경우 기존 거리 체크
            float dist = Vector2.Distance(_playerTransform.position, _activeInteractionSource.position);
            if (dist > AUTO_CLOSE_DISTANCE)
            {
                SetState(UIState.None);
            }
        }
    }

    private void HandleInventoryToggle()
    {
        // 대화 중에는 인벤토리 열기 차단
        if (CurrentState == UIState.Dialogue) return;
        // 마켓 단말기 사용 중엔 차단 — Tab은 마켓 씬의 주식/코인 전환 키로 쓰인다(MarketSceneController)
        if (CurrentState == UIState.Market) return;

        if (CurrentState == UIState.Inventory)
        {
            SetState(UIState.None);
        }
        else
        {
            SetState(UIState.Inventory);
        }
    }

    private void HandleQuestToggle()
    {
        // 대화 중에는 퀘스트 창 토글 차단
        if (CurrentState == UIState.Dialogue) return;
        // 마켓 단말기 사용 중엔 게임 UI를 열지 않는다
        if (CurrentState == UIState.Market) return;

        if (CurrentState == UIState.Quest)
        {
            SetState(UIState.None);
        }
        else if (useCodeBuiltQuestUI)
        {
            // 코드 오버레이는 SetState 가 열어준다 (J 키라 제출 불가 모드)
            SetState(UIState.Quest);
        }
        else
        {
            if (questRoot != null)
            {
                var panel = questRoot.GetComponentInChildren<QuestPanelUI>();
                if (panel != null)
                {
                    panel.OpenPanel(false);
                }
                else
                {
                    SetState(UIState.Quest);
                }
            }
        }
    }

    public void CloseAll()
    {
        SetState(UIState.None);
        
        // ShopManager를 통해 Shop UI 닫기
        if (shopManager != null && shopManager.IsShopOpen())
        {
            shopManager.CloseShop();
        }
    }

    /// <summary>
    /// 새로운 씬으로 전환될 때 인스펙터에 할당된 새로운 레퍼런스들을 적용합니다.
    /// </summary>
    public void UpdateReferences(UIStateManager newSceneManager)
    {
        if (newSceneManager == null) return;

        this.dialogueRoot = newSceneManager.dialogueRoot;
        this.popupRoot = newSceneManager.popupRoot;
        this.shopRoot = newSceneManager.shopRoot;
        this.upgradeRoot = newSceneManager.upgradeRoot;
        this.questRoot = newSceneManager.questRoot;
        this.subQuestBoardRoot = newSceneManager.subQuestBoardRoot;
        this.truckRoot = newSceneManager.truckRoot;
        this.inventoryRoot = newSceneManager.inventoryRoot;
        this.settlementRoot = newSceneManager.settlementRoot;
        this.pauseRoot = newSceneManager.pauseRoot;
        this.inventoryCamera = newSceneManager.inventoryCamera; // 씬별 인벤토리 vcam 갱신 (없는 씬은 null → 줌 생략)

        // 코드 생성 UI 사용 여부도 씬마다 다르므로 함께 넘겨받는다 (지상씬만 켜는 식)
        this.useCodeBuiltShopUI = newSceneManager.useCodeBuiltShopUI;
        this.useCodeBuiltWarehouseUI = newSceneManager.useCodeBuiltWarehouseUI;
        this.useCodeBuiltInventoryUI = newSceneManager.useCodeBuiltInventoryUI;
        this.useCodeBuiltPauseUI = newSceneManager.useCodeBuiltPauseUI;
        // [버그픽스] 긴급탈출 정산창(EmergencyEscapeOverlayUI) 트리거 플래그도 씬마다 다르므로 반드시 인계한다.
        // 지상씬만 1, 지하씬은 0인데 이 줄이 없으면 지하에서 Play를 시작한 세션은 싱글톤 값이 0으로 굳어
        // 지상 도착 시에도 정산창이 뜨지 않는다(DontDestroyOnLoad 싱글톤이라 씬 인스펙터 값이 자동 반영 안 됨).
        this.useCodeBuiltSettlementUI = newSceneManager.useCodeBuiltSettlementUI;
        // 퀘스트/서브퀘스트 코드 생성 여부도 같은 누락 케이스였다(지하 0 / 지상 1). 안 넘기면 지하에서 시작한
        // 세션이 지상에서 J·서브퀘스트를 눌러도 프리팹 경로로 빠져 의도(코드 생성 오버레이)와 어긋난다.
        this.useCodeBuiltQuestUI = newSceneManager.useCodeBuiltQuestUI;
        this.useCodeBuiltSubQuestUI = newSceneManager.useCodeBuiltSubQuestUI;
        this.useCodeBuiltPopupUI = newSceneManager.useCodeBuiltPopupUI;
        // 업그레이드 코드 생성 여부도 씬마다 다르므로 함께 인계한다(DontDestroyOnLoad 싱글톤이라 안 넘기면
        // 시작 씬 값에 고정 → 지하에서 시작한 세션이 지상 NPC 업그레이드를 눌러도 프리팹 경로로 빠진다).
        this.useCodeBuiltUpgradeUI = newSceneManager.useCodeBuiltUpgradeUI;
        this.shortcutDisabledScenes = newSceneManager.shortcutDisabledScenes;

        // 매니저 참조도 갱신 시도
        if (newSceneManager.shopManager != null)
            this.shopManager = newSceneManager.shopManager;
        else
            this.shopManager = FindFirstObjectByType<ShopManager>();

        Debug.Log("[UIStateManager] 새로운 씬의 UI 레퍼런스가 정상적으로 갱신되었습니다.");

        // [추가] 씬 전환 시 상태 초기화 강제 (이전 씬의 상태가 남아 상호작용을 막는 것 방지)
        this._playerTransform = null; // 플레이어 참조 초기화 (CheckDistanceToSource에서 새로 찾음)
        this.SetState(UIState.None);
        
        // 카메라 인스턴스 강제 갱신 시도
        var camFollow = FindFirstObjectByType<CameraFollow>();
        if (camFollow != null)
        {
            // CameraFollow.Instance는 해당 클래스의 Awake에서 설정되지만, 
            // 씬 전환 시점에 UIStateManager가 먼저 동작할 수 있으므로 명시적으로 확인
            Debug.Log("[UIStateManager] CameraFollow 이벤트를 위해 새 씬의 카메라를 확인했습니다.");
        }
    }
}


