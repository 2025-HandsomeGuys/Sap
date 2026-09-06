// @tags: interaction, unified, world, kind, dispatch, interactable, inspector
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 플레이어가 상호작용하는 월드 오브젝트의 <b>종류</b>.
/// 인스펙터에서 고르면 <see cref="WorldInteractable"/>이 그에 맞는 동작(Behaviour)을 자동으로 붙인다.
/// 새 종류를 추가하려면: 여기에 값 하나 + <see cref="InteractionBehaviour.Create"/>에 한 줄.
/// </summary>
public enum WorldInteractableKind
{
    [InspectorName("청크 입구 (지하 → 청크)")]
    ChunkEntrance = 0,

    [InspectorName("청크 출구 (청크 → 지하)")]
    ChunkExit = 1,

    [InspectorName("엘리베이터 (지하 ↔ 지상)")]
    Elevator = 2,

    [InspectorName("땅굴 입구 (지상 → 지하)")]
    TunnelEntrance = 3,

    [InspectorName("서브퀘스트 게시판")]
    SubQuestBoard = 4,

    [InspectorName("트럭 NPC (팝업 → 선택 UI)")]
    TruckNpc = 5,

    [InspectorName("침대 (하루 정산)")]
    Bed = 6,

    [InspectorName("옷장 (지상 창고)")]
    Wardrobe = 7,

    [InspectorName("PC (마켓 씬)")]
    MarketTerminal = 8,

    [InspectorName("엘리베이터 입구 (지상 → 지하 엘리베이터)")]
    ElevatorEntrance = 9,

    [InspectorName("작업대 (장비·유물 강화)")]
    Workbench = 10,

    [InspectorName("지상 출구 (지하 → 지상, 빛기둥)")]
    SurfaceExit = 11,
}

/// <summary>
/// 플레이어 상호작용 오브젝트 <b>통합 컴포넌트</b>.
/// 오브젝트에 이것 하나만 붙이고 인스펙터에서 <see cref="WorldInteractableKind"/>를 고르면
/// 해당 동작이 자동으로 연결된다. (침대·PC·게시판·트럭·엘리베이터·땅굴·청크문·옷장)
///
/// <b>왜 통합했나</b> — 기존에는 종류마다 컴포넌트가 따로였고, 입력 처리 방식도 갈렸다.
/// 어떤 건 <see cref="IInteractable"/>로 PlayerInteractor가 E를 처리하고(엘리베이터·게시판·NPC·청크문),
/// 어떤 건 자기 Update에서 직접 <c>Input.GetKeyDown(E)</c>를 폴링했다(침대·PC·땅굴).
/// 여기서는 <b>PlayerInteractor 경로 하나로 통일</b>하고, 콜라이더 구성이 특수한 경우에만
/// <see cref="selfHandleInput"/>로 기존 폴링 방식을 켤 수 있게 남겨뒀다.
///
/// <b>세팅</b>
///  1. 오브젝트에 Collider2D(Is Trigger 권장) — 플레이어 콜라이더와 겹쳐야 탐지된다.
///  2. 이 컴포넌트 부착 → Kind 선택 → 그 아래 종류별 항목만 채운다.
///  3. 근접 시 <b>느낌표·문구 라벨·강조 연출</b>은 이 컴포넌트가 직접 만들어 띄운다.
///     ('피드백' 섹션 참고 — 구 <c>InteractionIndicator</c>/<c>InteractionPromptLabel</c>/
///     <c>InteractableHighlight</c>를 통합했다. 관련 코드는 <c>WorldInteractable.Feedback.cs</c>).
///
/// 상세: <c>Assets/Docs/world-interactable.md</c>
/// </summary>
[DisallowMultipleComponent]
public partial class WorldInteractable : MonoBehaviour,
                                 IInteractable, IInteractionPrompt, IInteractionAvailability,
                                 IHighlightable, IMapEntrance, IMapEntranceToggle,
                                 IMapElevator, IMapElevatorToggle, IElevatorStop,
                                 IChunkInitializer, INpcPopupSource
{
    // ─────────────────────────────── 종류 ───────────────────────────────

    [Header("종류")]
    [Tooltip("이 오브젝트가 무엇인지. 고른 종류에 맞는 동작이 자동으로 연결된다")]
    [SerializeField] private WorldInteractableKind kind = WorldInteractableKind.Bed;

    // ─────────────────────────────── 공통 ───────────────────────────────

    [Header("공통 — 문구")]
    [Tooltip("Localization 키를 직접 지정해 종류 기본 문구를 덮어쓴다. 비우면 종류 기본값")]
    [SerializeField] private string promptKeyOverride = "";
    [Tooltip("키를 못 찾았을 때 쓸 원문. 비우면 종류 기본값")]
    [SerializeField] private string promptTextOverride = "";

    [Header("공통 — 우선순위/입력")]
    [Tooltip("겹쳤을 때 먼저 선택될 우선순위. -1이면 종류 기본값(엘리베이터 10 / 나머지 5)")]
    [SerializeField] private int priorityOverride = -1;
    [Tooltip("PlayerInteractor 대신 이 오브젝트가 직접 상호작용 입력을 받는다(기존 침대·PC 방식). " +
             "플레이어에 PlayerInteractor가 없거나 콜라이더가 안 겹치는 큰 트리거존에만 사용")]
    [SerializeField] private bool selfHandleInput = false;
    // 상호작용 키는 인스펙터로 두지 않는다 — 프로젝트 전역 단일 원천(InteractionKeys.Interact = F).
    // 오브젝트마다 다른 키를 두면 안내 아이콘([F])과 실제 키가 조용히 어긋난다.

    [Header("공통 — 시각 피드백")]
    [Tooltip("비우면 이 오브젝트의 SpriteRenderer를 자동으로 찾는다")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("플레이어가 가까이 오면 색을 바꾼다(엘리베이터·청크문 방식). 침대·PC 등은 꺼둘 것")]
    [SerializeField] private bool tintOnNearby = false;
    [SerializeField] private Color idleColor = Color.white;
    [SerializeField] private Color nearbyColor = Color.yellow;

    [Header("공통 — 해금")]
    [Tooltip("이 시설을 열어주는 업그레이드 노드 ID. 비우면 해금 조건 없음(항상 사용 가능).\n" +
             "예: 컴퓨터 Facility_Computer_T0 / 게시판 Facility_Board_T0")]
    [SerializeField] private string unlockNodeId = "";
    [Tooltip("지하에서 엘리베이터를 한 번이라도 직접 찾아간 뒤에 열린다(지상 엘리베이터 입구용).\n" +
             "업그레이드로 사는 게 아니라 '발견'이 조건이다. 다른 조건과 함께 켜면 전부 만족해야 열린다")]
    [SerializeField] private bool requireElevatorDiscovered = false;
    [Tooltip("튜토리얼을 끝낸 뒤에 열린다(작업대용). PlayerData.isTutorialCompleted를 본다.\n" +
             "다른 조건과 함께 켜면 전부 만족해야 열린다")]
    [SerializeField] private bool requireTutorialCompleted = false;
    [Tooltip("잠겨 있을 때 스프라이트 색. 오브젝트는 보이되 '아직 못 쓴다'가 한눈에 보이게 한다")]
    [SerializeField] private Color lockedColor = new Color(0.42f, 0.42f, 0.46f, 1f);

    [Header("공통 — 이벤트")]
    [Tooltip("상호작용 성공 시 추가로 부를 것이 있으면 연결")]
    [SerializeField] private UnityEvent<GameObject> onInteract;

    // ────────────────────────── 종류별 설정 ──────────────────────────

    [Header("[청크 입구] 지하 → 청크")]
    [Tooltip("이 문이 여는 청크(던전) 지오메트리 프리팹 — DungeonEntryPoint 포함")]
    [SerializeField] private GameObject chunkPrefab;
    [Tooltip("이미 탐험해 재입장 불가일 때의 스프라이트 색")]
    [SerializeField] private Color exploredColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    [Header("[청크 출구] 청크 → 지하")]
    [Tooltip("클리어 보상 골드. 보상을 따로 주면 0")]
    [SerializeField] private int clearRewardGold = 0;

    [Header("[엘리베이터] 지하 ↔ 지상")]
    [Tooltip("같은 오브젝트에 ElevatorController가 있으면 그 값을 쓴다(스포너가 채워줌). " +
             "직접 배치한 엘리베이터만 아래 값을 사용")]
    [SerializeField] private int elevatorXChunk = 0;
    [SerializeField] private int elevatorLayerIndex = 0;
    [Tooltip("정류장 청크 Y(깊이). 해금 기록의 키 — 스포너가 채운다")]
    [SerializeField] private int elevatorYChunk = 0;

    [Header("[땅굴 입구] 지상 → 지하")]
    [SerializeField] private string sceneToLoad = "DemoUnderground";
    [Tooltip("켜면 저녁(Afternoon)에는 지하로 못 내려간다. 지상으로 나가는 방향은 항상 가능")]
    [SerializeField] private bool blockAtNight = true;

    [Header("[트럭 NPC]")]
    [Tooltip("로컬라이제이션 키를 넣으면 번역된 이름이 표시된다(예: npc_truck_name — Dialogues.csv).\n" +
             "키가 아닌 그냥 이름을 넣으면 그 문자열이 모든 언어에서 그대로 보인다.")]
    [SerializeField] private string npcName = "npc_truck_name";
    [SerializeField] private DialogueData dialogueData;
    [SerializeField] private QuestSO mainQuestData;
    [SerializeField] private bool isMainQuestNpc = false;
    [SerializeField] private bool canOpenShop = true;
    [Tooltip("팝업이 뜰 머리 위 오프셋")]
    [SerializeField] private Vector3 popupOffset = new Vector3(0f, 2f, 0f);
    [Tooltip("대화 시 비출 특정 카메라 (비워두면 기존 화면 유지)")]
    [SerializeField] private Unity.Cinemachine.CinemachineCamera dialogueCamera;

    [Header("[PC] 마켓 씬")]
    [SerializeField] private string marketSceneName = "MarketScene";
    // pauseTimeOnEnter는 제거됨 — 마켓 진입 중에도 게임 시간이 흐른다.
    // (점프 중에 열면 공중에서 얼었다가 닫는 순간 이어서 떨어지던 문제. MarketTerminalBehaviour 주석 참고)

    // ─────────────────────────── 내부 상태 ───────────────────────────

    private InteractionBehaviour _behaviour;
    private WorldInteractableKind _builtKind;
    private bool _highlighted;
    private bool _playerInTrigger;
    private GameObject _triggerPlayer;
    private int _lastInteractFrame = -1;
    private bool _started;
    private Vector2Int _chunkCoord;
    private bool _chunkCoordResolved;
    private bool _unlockCached = true;
    private float _nextUnlockCheck;
    private bool _upgradeSubscribed;
    private Color _baseColor = Color.white;

    // ─────────────────────────── 설정 읽기 ───────────────────────────
    // 동작(Behaviour)들이 읽는 창구. 인스펙터 필드는 전부 private으로 두고 여기서만 노출한다.

    public WorldInteractableKind Kind => kind;
    public GameObject ChunkPrefab => chunkPrefab;
    public Color ExploredColor => exploredColor;
    public int ClearRewardGold => clearRewardGold;
    public int ElevatorXChunk => elevatorXChunk;
    public int ElevatorLayerIndex => elevatorLayerIndex;
    public int ElevatorYChunk => elevatorYChunk;

    /// <summary>
    /// 스포너가 청크 생성 중에 이 엘리베이터의 정류장 좌표를 주입한다.
    /// 프리팹에는 ElevatorController가 없으므로 이 경로가 없으면 모든 엘리베이터가
    /// 인스펙터 기본값(0,0) 그대로가 된다 — 현재 층 판정도 해금도 전부 층 0으로 뭉개진다.
    /// </summary>
    public void SetElevatorPlacement(int xChunk, int yChunk, int layerIndex)
    {
        // 좌표가 바뀌면 등록 키(X)도 바뀐다 — 옛 키로 남지 않도록 먼저 뺀다.
        UnregisterElevatorSelf();

        elevatorXChunk     = xChunk;
        elevatorYChunk     = yChunk;
        elevatorLayerIndex = layerIndex;

        RegisterElevatorSelf();
    }

    // ─────────────────────── IElevatorStop ───────────────────────

    public int StopDepth      => elevatorYChunk;
    public int StopXChunk     => elevatorXChunk;
    public int StopLayerIndex => elevatorLayerIndex;
    public Transform StopTransform => transform;

    // ElevatorManager 등록 상태. 등록 시점이 두 갈래라(스포너 주입 / 손배치 Start) 중복을 막는다.
    private bool _elevatorRegistered;

    private void RegisterElevatorSelf()
    {
        if (kind != WorldInteractableKind.Elevator || _elevatorRegistered) return;
        if (ElevatorManager.Instance == null) return;

        ElevatorManager.Instance.RegisterElevator(this);
        _elevatorRegistered = true;
    }

    private void UnregisterElevatorSelf()
    {
        if (!_elevatorRegistered) return;

        if (ElevatorManager.Instance != null) ElevatorManager.Instance.UnregisterElevator(this);
        _elevatorRegistered = false;
    }

    /// <summary>도달 시 정류장 해금(엘리베이터일 때만).</summary>
    public void NotifyReached()
    {
        if (kind != WorldInteractableKind.Elevator) return;

        if (ElevatorStopUnlockStore.Unlock(elevatorYChunk))
        {
            Debug.Log($"[WorldInteractable] 정류장 해금: Layer {elevatorLayerIndex} (깊이 {elevatorYChunk})");
            GuideManager.Trigger("elevator_basic");   // 처음 해금한 정류장에서 한 번만
        }
    }
    public string SceneToLoad => sceneToLoad;
    public bool BlockAtNight => blockAtNight;
    public string MarketSceneName => marketSceneName;

    /// <summary>이 오브젝트가 속한 청크 좌표. 청크 입구/출구가 인스턴스를 구분하는 데 쓴다.</summary>
    public Vector2Int ChunkCoord
    {
        get
        {
            if (!_chunkCoordResolved) ResolveChunkCoord();
            return _chunkCoord;
        }
    }

    // ─────────────────────────── 해금 게이트 ───────────────────────────
    // 컴퓨터·작업대·엘리베이터처럼 업그레이드로 열리는 시설을 위한 공통 관문.
    // 종류별 동작(Behaviour)은 이걸 전혀 모른다 — 잠김 판정이 동작보다 먼저 걸리므로
    // 새 종류를 추가해도 자동으로 같은 규칙을 따른다.
    //
    // 드릴·곡괭이는 여기가 아니라 toolConfig.json의 unlockNodeIds로 걸린다
    // (ToolController.IsToolUnlocked). 도구는 월드 오브젝트가 아니라 손에 드는 것이라
    // 순환 선택 로직까지 함께 봐야 해서 경로를 합치지 않았다.

    /// <summary>노드 조회 캐시 수명. GetPrompt가 매 프레임 불리는데 IsNodeUnlocked는
    /// 내부에서 FindFirstObjectByType&lt;SaveManager&gt;를 타므로 그대로 두면 비싸다.
    /// 구매 순간의 반응은 OnUpgradeStateChanged가 캐시를 즉시 버려서 보장한다.
    /// 이 간격은 이벤트가 안 오는 경로(세이브 로드 등)를 위한 보험이다.</summary>
    private const float UnlockCheckInterval = 0.5f;

    /// <summary>해금 게이트를 하나라도 쓰는 오브젝트인가(색 복구·경고 대상 판정).</summary>
    private bool HasUnlockGate =>
        requireElevatorDiscovered || requireTutorialCompleted || !string.IsNullOrEmpty(unlockNodeId);

    /// <summary>
    /// 이 시설을 지금 쓸 수 있는가. 조건 필드가 전부 비어 있으면 항상 true라
    /// 기존 오브젝트(침대·옷장·청크문 등)의 동작은 전혀 바뀌지 않는다.
    /// 업그레이드 매니저를 못 찾으면 <b>해금된 것으로 본다</b> — 테스트 씬에서 시설이
    /// 통째로 잠겨버리는 쪽이 더 나쁘다(ToolController.IsToolUnlocked와 같은 규칙).
    ///
    /// 조건을 여러 개 켜면 <b>AND</b>다. 시설마다 여는 방식이 다르다 —
    /// 단말기·게시판은 업그레이드 노드, 지상 엘리베이터 입구는 '지하에서 발견',
    /// 작업대는 '튜토리얼 완료'. 사는 게 아닌 두 개는 트리에 노드를 만들지 않았다.
    /// </summary>
    public bool IsUnlocked
    {
        get
        {
            // 지하에서 엘리베이터에 직접 걸어가 본 적이 있는가(ElevatorStopUnlockStore).
            // 층 0의 '항상 해금' 예외는 _depths에 안 들어가므로 여기에 걸리지 않는다 —
            // 첫 잠수는 땅굴 입구로만 가능하고, 엘리베이터를 본 다음부터 이 입구가 열린다.
            if (requireElevatorDiscovered && ElevatorStopUnlockStore.UnlockedCount == 0) return false;

            // 튜토리얼 완료 여부는 세이브에 있다(PlayerData.isTutorialCompleted).
            // 세이브를 못 찾으면 잠그지 않는다 — 위와 같은 규칙(테스트 씬 보호).
            if (requireTutorialCompleted && !TutorialProgress.IsCompleted) return false;

            if (string.IsNullOrEmpty(unlockNodeId)) return true;

            if (Time.unscaledTime >= _nextUnlockCheck)
            {
                _nextUnlockCheck = Time.unscaledTime + UnlockCheckInterval;
                UpgradeManager mgr = UpgradeManager.Instance;
                _unlockCached = mgr == null || mgr.IsNodeUnlocked(unlockNodeId);
            }
            return _unlockCached;
        }
    }

    private void SubscribeUpgrade()
    {
        // 해금 조건이 없는 오브젝트는 매니저를 건드리지 않는다
        // (UpgradeManager.Instance는 없으면 새로 만들기 때문에, 안 쓰는 씬에 매니저가 생기는 걸 막는다).
        if (_upgradeSubscribed || string.IsNullOrEmpty(unlockNodeId)) return;

        UpgradeManager mgr = UpgradeManager.Instance;
        if (mgr == null) return;

        mgr.OnUpgradeStateChanged += OnUpgradeStateChanged;
        _upgradeSubscribed = true;
    }

    private void UnsubscribeUpgrade()
    {
        if (!_upgradeSubscribed) return;
        _upgradeSubscribed = false;

        UpgradeManager mgr = UpgradeManager.Instance;
        if (mgr != null) mgr.OnUpgradeStateChanged -= OnUpgradeStateChanged;
    }

    /// <summary>업그레이드를 사는 순간 다음 조회에서 다시 계산하게 한다.</summary>
    private void OnUpgradeStateChanged() => _nextUnlockCheck = 0f;

    /// <summary>
    /// nodeId 오타 방어. 자유 문자열이라 한 글자만 틀려도 그 시설이 <b>영원히 잠긴 채</b>
    /// 아무 소리 없이 실패한다 — 트리에 없는 id면 여기서 알린다.
    /// </summary>
    private void WarnIfUnknownUnlockNode()
    {
        if (string.IsNullOrEmpty(unlockNodeId)) return;

        UpgradeManager mgr = UpgradeManager.Instance;
        if (mgr == null || mgr.upgradeTree == null) return; // 트리가 없는 씬은 판단 불가 → 침묵

        if (mgr.GetNodeFromCache(unlockNodeId) == null)
        {
            Debug.LogWarning($"[WorldInteractable] {name}: 해금 노드 '{unlockNodeId}'가 업그레이드 트리에 없습니다. " +
                             "id 오타이거나 트리 생성기를 아직 돌리지 않았습니다 — 이 시설은 계속 잠긴 채로 남습니다.");
        }
    }

    // ─────────────────────── INpcPopupSource ───────────────────────
    // NpcPopup이 기존 NpcInteraction과 이 컴포넌트를 똑같이 다루기 위한 창구.

    /// <summary>
    /// 표시용 NPC 이름. npcName을 <b>로컬라이제이션 키로 먼저 조회</b>하고, 등록된 키가 아니면
    /// 넣은 문자열을 그대로 쓴다 — 기존 씬처럼 한글 이름이 박혀 있어도 깨지지 않는다.
    /// 팝업 이름과 상호작용 프롬프트("{0}와 대화")가 같은 값을 쓰므로 한 곳만 고치면 둘 다 번역된다.
    /// </summary>
    public string NpcName => CodeUI.L(npcName, npcName);
    public DialogueData Dialogue => dialogueData;
    public QuestSO MainQuest => mainQuestData;
    public bool IsMainQuestNpc => isMainQuestNpc;
    public bool CanOpenShop => canOpenShop;
    // 업그레이드(연구 트리) 진입은 트럭이 아니라 작업대(Workbench)로 옮겼다.
    // 구 NpcPopup 계열이 이 값을 보고 버튼을 그리므로 항상 false로 고정한다.
    public bool CanOpenUpgrade => false;
    public Vector3 PopupOffset => popupOffset;
    public Transform SourceTransform => transform;
    /// <summary>씬에서 알아서 찾게 둔다(NpcPopup이 null이면 FindFirstObjectByType로 폴백).</summary>
    public QuestDialogueUI DialogueUI => null;
    public Unity.Cinemachine.CinemachineCamera DialogueCamera => dialogueCamera;

    // ─────────────────────────── 라이프사이클 ───────────────────────────

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        // 해금되는 순간 되돌릴 색. tintOnNearby가 꺼진 오브젝트는 자기 색을 갖고 있으므로
        // idleColor로 되돌리면 안 된다(원래 색을 그대로 기억해둔다).
        if (spriteRenderer != null) _baseColor = spriteRenderer.color;
        if (spriteRenderer != null && tintOnNearby) spriteRenderer.color = idleColor;

        BuildFeedback();
    }

    private void Start()
    {
        // 먼저 만들고(_started가 false라 getter는 OnStart를 부르지 않는다) 여기서 한 번만 부른다.
        InteractionBehaviour behaviour = Behaviour;
        _started = true;
        behaviour.OnStart();

        WarnIfUnknownUnlockNode();

        // 손으로 배치한 엘리베이터용. 스포너가 심은 것은 SetElevatorPlacement에서 이미 등록됐고,
        // 등록은 멱등이라 여기서 다시 불러도 중복되지 않는다.
        RegisterElevatorSelf();
    }

    private void Update()
    {
        Behaviour.Tick();

        if (selfHandleInput && _playerInTrigger && InteractionKeys.InteractPressed)
        {
            // PlayerInteractor와 같은 규칙 — 다른 UI가 떠 있으면 무시한다.
            if (UIStateManager.Instance == null || UIStateManager.Instance.CurrentState == UIState.None)
                Interact(_triggerPlayer != null ? _triggerPlayer : gameObject);
        }

        if (spriteRenderer != null) ApplyTint();

        DriveFeedback();
    }

    private void OnEnable()
    {
        TrySubscribeLanguage();
        SubscribeUpgrade();
    }

    private void OnDisable()
    {
        // 마켓 단말기처럼 씬 이벤트를 구독하는 동작이 있어 반드시 정리한다(timeScale 복귀 포함).
        _behaviour?.OnDisabled();
        _playerInTrigger = false;
        _triggerPlayer = null;
        _highlighted = false;

        ResetFeedback();
        UnsubscribeLanguage();
        UnsubscribeUpgrade();
        UnregisterElevatorSelf();   // 청크 언로드로 꺼질 때 유령 등록이 남지 않게
    }

    private void OnDestroy()
    {
        DisposeFeedback();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInTrigger = true;
        _triggerPlayer = other.gameObject;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInTrigger = false;
        _triggerPlayer = null;
    }

    // ─────────────────────────── IInteractable ───────────────────────────

    public string InteractionPrompt => InteractionPrompts.Resolve(GetInteractionPrompt());

    public int InteractionPriority => priorityOverride >= 0 ? priorityOverride : Behaviour.DefaultPriority;

    public void Interact(GameObject interactor)
    {
        // 자체 입력과 PlayerInteractor가 같은 프레임에 겹쳐도 한 번만 실행되게 한다.
        if (_lastInteractFrame == Time.frameCount) return;
        _lastInteractFrame = Time.frameCount;

        // 라벨은 잠겼을 때도 띄운다 — 눌러본 사람에게 왜 안 되는지 보여줘야 한다
        // ('상호작용 시에만' 옵션을 켠 오브젝트는 이 순간이 유일한 표시 기회다).
        FlashPromptLabel();

        // 잠긴 시설은 여기서 끝. selfHandleInput 경로는 IsInteractionAvailable을 보지 않고
        // 바로 이 함수를 부르므로, 문구만 막아서는 새어나간다.
        if (!IsUnlocked) return;

        onInteract?.Invoke(interactor);

        // 선택지가 여럿이면 지금 고른 것을 실행한다(하나뿐이면 기본 구현이 Interact로 흘려보낸다).
        Behaviour.InteractOption(SelectedOption, interactor != null ? interactor : gameObject);
    }

    // ─────────────────────── 근접 선택지 (목록 + 휠 선택) ───────────────────────
    // 근접하면 이 오브젝트로 지금 할 수 있는 일이 전부 뜨고, 휠로 고른 뒤 F로 실행한다.
    // 선택 인덱스는 여기(오브젝트)가 갖는다 — 목록을 그리는 쪽(피드백 뷰)과 실행하는 쪽
    // (PlayerInteractor)이 같은 값을 봐야 '보고 있는 것'과 '눌러서 실행되는 것'이 어긋나지 않는다.

    private int _selectedOption;

    /// <summary>지금 띄울 선택지 개수. 잠긴 시설은 '왜 안 되는지' 한 줄만 띄운다.</summary>
    public int OptionCount => IsUnlocked ? Behaviour.OptionCount : 1;

    /// <summary>현재 고른 선택지 인덱스(항상 유효 범위로 잘린다).</summary>
    public int SelectedOption
    {
        get
        {
            int count = OptionCount;
            if (count <= 0) return 0;
            if (_selectedOption >= count) _selectedOption = count - 1;
            if (_selectedOption < 0) _selectedOption = 0;
            return _selectedOption;
        }
    }

    /// <summary>선택을 맨 위(첫 줄)로 되돌린다. 목록이 닫힐 때 불린다 —
    /// 다시 다가왔을 때 지난번 선택이 남아 있으면 무엇이 골라져 있는지 예측할 수 없다.</summary>
    private void ResetSelectedOption() => _selectedOption = 0;

    /// <summary>휠/키로 선택지를 옮긴다. 끝에서 반대쪽으로 돈다(항목이 2개일 때 휠 한 번으로 오가게).</summary>
    public void CycleOption(int delta)
    {
        int count = OptionCount;
        if (count <= 1 || delta == 0) return;
        _selectedOption = ((SelectedOption + delta) % count + count) % count;
    }

    /// <summary>index번째 선택지의 문구. 잠김·인스펙터 덮어쓰기는 <see cref="GetInteractionPrompt"/>와 같은 규칙.</summary>
    public InteractionPromptInfo GetOptionPrompt(int index)
    {
        if (!IsUnlocked)
            return InteractionPromptInfo.Blocked("interact_locked_facility", "아직 사용할 수 없다");

        InteractionPromptInfo info = Behaviour.GetOption(index);
        return ApplyPromptOverride(info);
    }

    /// <summary>지금 고른 선택지를 실행한다(PlayerInteractor가 F로 부른다).</summary>
    public void InteractSelected(GameObject interactor)
    {
        Interact(interactor);
    }

    // ─────────────────── IInteractionPrompt / Availability ───────────────────

    public InteractionPromptInfo GetInteractionPrompt()
    {
        // 잠김이 종류별 문구보다 우선한다 — "작업대 (강화)"를 보여주고 눌렀는데
        // 아무 일도 안 일어나는 것보다, 왜 안 되는지 먼저 알려주는 게 맞다.
        if (!IsUnlocked)
            return InteractionPromptInfo.Blocked("interact_locked_facility", "아직 사용할 수 없다");

        return ApplyPromptOverride(Behaviour.GetPrompt());
    }

    /// <summary>인스펙터 덮어쓰기는 '가능' 상태 문구에만 적용한다.
    /// (차단 사유 문구까지 덮어쓰면 왜 안 되는지 알 수 없어진다)</summary>
    private InteractionPromptInfo ApplyPromptOverride(in InteractionPromptInfo info)
    {
        bool hasOverride = !string.IsNullOrEmpty(promptKeyOverride) || !string.IsNullOrEmpty(promptTextOverride);
        if (!info.Available || !hasOverride) return info;

        string key = string.IsNullOrEmpty(promptKeyOverride) ? info.Key : promptKeyOverride;
        string text = string.IsNullOrEmpty(promptTextOverride) ? info.Fallback : promptTextOverride;
        return new InteractionPromptInfo(key, text, true, info.Args);
    }

    public bool IsInteractionAvailable => IsUnlocked && Behaviour.IsAvailable;

    /// <summary>지금 실제로 할 일이 있는가(느낌표 표시 기준). 종류별 동작이 판정한다.
    /// 잠긴 시설은 할 일이 있을 수 없으므로 느낌표를 띄우지 않는다.</summary>
    public bool HasPendingTask => IsUnlocked && Behaviour.HasPendingTask;

    // ─────────────────────────── IHighlightable ───────────────────────────

    public void SetHighlighted(bool highlighted)
    {
        _highlighted = highlighted;
        if (spriteRenderer != null) ApplyTint();
    }

    private void ApplyTint()
    {
        // 잠김이 가장 강한 상태다. 회색으로 두면 "여긴 나중에 열린다"가 한눈에 보여
        // 업그레이드를 사러 갈 이유가 생긴다(오브젝트를 숨기면 존재조차 모른다).
        if (!IsUnlocked)
        {
            spriteRenderer.color = lockedColor;
            return;
        }

        // 동작이 색을 강제하는 상태(예: 탐험 완료 청크문의 회색)가 우선이다.
        Color? forced = Behaviour.OverrideTint();
        if (forced.HasValue)
        {
            spriteRenderer.color = forced.Value;
            return;
        }

        if (!tintOnNearby)
        {
            // 해금 직후 회색을 벗겨준다. 해금 게이트를 쓰는 오브젝트에만 관여해
            // 스스로 색을 바꾸는 기존 오브젝트를 건드리지 않는다.
            if (HasUnlockGate && spriteRenderer.color != _baseColor)
                spriteRenderer.color = _baseColor;
            return;
        }

        spriteRenderer.color = _highlighted ? nearbyColor : idleColor;
    }

    // ─────────────────────── IMapEntranceToggle ───────────────────────

    /// <summary>청크 입구일 때만 지도에 '입구' 마커로 등록된다(종류가 인스펙터에서 바뀌므로 런타임 판정).</summary>
    public bool IsMapEntranceNow => kind == WorldInteractableKind.ChunkEntrance;

    // ─────────────────────── IMapElevatorToggle ───────────────────────

    /// <summary>엘리베이터일 때만 지도에 '엘리베이터' 마커로 등록된다(근접·이용 시점).</summary>
    public bool IsMapElevatorNow => kind == WorldInteractableKind.Elevator;

    // ─────────────────────── IChunkInitializer ───────────────────────

    public int InitializationOrder => 0;

    /// <summary>
    /// 특수청크 스폰 경로가 호출한다. 풀에서 재사용될 때도 매번 불리므로
    /// 청크 좌표는 Start가 아니라 여기서 갱신해야 한다(DungeonDoorChunk와 같은 이유).
    /// </summary>
    public void Initialize(Transform parent)
    {
        ResolveChunkCoord();
    }

    private void ResolveChunkCoord()
    {
        var chunk = GetComponentInParent<IChunk>();
        _chunkCoord = chunk != null ? chunk.Coord : Vector2Int.zero;
        _chunkCoordResolved = true;
    }

    // ─────────────────────────── 동작 해석 ───────────────────────────

    /// <summary>종류에 맞는 동작. 인스펙터에서 종류를 바꾸면 다음 조회 때 다시 만들어진다.</summary>
    private InteractionBehaviour Behaviour
    {
        get
        {
            if (_behaviour == null || _builtKind != kind)
            {
                _behaviour?.OnDisabled();
                _behaviour = InteractionBehaviour.Create(kind);
                _builtKind = kind;
                _behaviour.Bind(this);

                // Start 이후에 종류를 바꾼 경우(플레이 중 인스펙터 조작)에도 참조를 찾게 한다.
                if (_started) _behaviour.OnStart();
            }
            return _behaviour;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        var col = GetComponent<Collider2D>();
        if (col != null) Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}
