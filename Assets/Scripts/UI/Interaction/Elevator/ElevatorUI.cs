using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 엘리베이터 UI 패널 - 층 선택 인터페이스
/// </summary>
public class ElevatorUI : MonoBehaviour
{
    [Header("UI 참조")]
    public GameObject panelRoot;           // 전체 UI 패널
    public TextMeshProUGUI titleText;      // 제목 텍스트
    public Button closeButton;             // 닫기 버튼
    public Button surfaceButton;           // 지상으로 나가기 버튼

    [Header("확인 팝업")]
    public ConfirmationPrompt confirmationPrompt;
    
    [Header("Localization Keys")]
    public string surfaceWarningTitleKey = "ui_elevator_surface_title";
    public string surfaceWarningMsgKey = "ui_elevator_surface_msg";

    [Header("레이어 버튼 (layerIndex 순서대로)")]
    // 지층마다 상층·중층·하층 3정류장 → 총 12개.
    //  0:땅 상층    1:땅 중층    2:땅 하층
    //  3:얼음땅 상층 4:얼음땅 중층 5:얼음땅 하층
    //  6:용암땅 상층 7:용암땅 중층 8:용암땅 하층
    //  9:우주 상층  10:우주 중층  11:우주 하층
    public Button[] layerButtons;

    [Header("씬 전환")]
    [SerializeField] private ExploreExitController exitController;

    [Header("설정")]
    public Color normalColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
    public Color currentLayerColor = new Color(0.8f, 0.6f, 0.2f, 0.9f);

    private int currentXPosition;          // 현재 엘리베이터 X 좌표
    private int currentLayerIndex;         // 현재 층
    private bool _warnedButtonShortage;    // 버튼 부족 경고를 한 번만 찍기 위한 플래그

    // 게임 일시정지 상태 (다른 시스템에서 참조 가능)
    public static bool IsUIOpen { get; private set; } = false;

    public static ElevatorUI Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(ClosePanel);
            Debug.Log("[ElevatorUI] Close button listener added");
        }
        else
        {
            Debug.LogWarning("[ElevatorUI] Close button reference is NULL!");
        }

        if (surfaceButton != null)
        {
            surfaceButton.onClick.AddListener(OnSurfaceButtonClicked);
        }
        else
        {
            Debug.LogWarning("[ElevatorUI] Surface button reference is NULL!");
        }

        if (layerButtons != null)
        {
            for (int i = 0; i < layerButtons.Length; i++)
            {
                if (layerButtons[i] == null) continue;
                int capturedIndex = i;
                layerButtons[i].onClick.AddListener(() => OnLayerButtonClicked(capturedIndex));
            }
        }

        HidePanel();
    }

    void Start()
    {
        if (confirmationPrompt != null)
        {
            confirmationPrompt.gameObject.SetActive(false);
        }
    }

    void Update()
    {

    }

    /// <summary>
    /// UI 패널 표시
    /// </summary>
    public void ShowPanel(int xPosition, int currentLayer)
    {
        if (panelRoot == null)
        {
            Debug.LogError("[ElevatorUI] Panel root is null!");
            return;
        }

        if (confirmationPrompt != null)
        {
            confirmationPrompt.gameObject.SetActive(false);
        }

        currentXPosition = xPosition;
        currentLayerIndex = currentLayer;

        // 타이틀 업데이트
        if (titleText != null)
        {
            titleText.text = $"엘리베이터 - X:{xPosition}";
        }

        // 버튼 상태 갱신
        RefreshLayerButtons();

        // 패널 활성화
        panelRoot.SetActive(true);
        
        // UI 열림 플래그 설정
        IsUIOpen = true;

        // UIStateManager 상태 동기화
        if (UIStateManager.Instance != null && UIStateManager.Instance.CurrentState != UIState.Elevator)
        {
            UIStateManager.Instance.SetState(UIState.Elevator);
        }

        Debug.Log($"[ElevatorUI] Panel opened at X:{xPosition}, Current Layer:{currentLayer}");
    }

    /// <summary>
    /// UI 패널 숨기기
    /// </summary>
    public void HidePanel()
    {
        if (panelRoot != null && panelRoot.activeSelf)
        {
            panelRoot.SetActive(false);
        }

        if (confirmationPrompt != null)
        {
            confirmationPrompt.gameObject.SetActive(false);
        }

        // UI 닫힘 플래그
        IsUIOpen = false;

        // UIStateManager 상태 동기화
        if (UIStateManager.Instance != null && UIStateManager.Instance.CurrentState == UIState.Elevator)
        {
            UIStateManager.Instance.SetState(UIState.None);
        }
    }

    /// <summary>
    /// 지상으로 나가기 버튼 핸들러
    /// </summary>
    void OnSurfaceButtonClicked()
    {
        if (confirmationPrompt != null)
        {
            // ElevatorUI 메인 패널만 먼저 비활성화 (UI 상태는 유지하여 뒷배경 입력 방지)
            if (panelRoot != null && panelRoot.activeSelf)
            {
                panelRoot.SetActive(false);
            }

            confirmationPrompt.gameObject.SetActive(true); // 명시적 활성화

            string title = LanguageManager.Instance?.L(surfaceWarningTitleKey) ?? "지상으로 이동";
            string msg = LanguageManager.Instance?.L(surfaceWarningMsgKey) ?? "정산 후 지상으로 이동하시겠습니까?";

            confirmationPrompt.Show(title, msg, () =>
            {
                HidePanel(); // 여기서 완전히 UI 닫힘 처리 및 상태 리셋
                if (exitController != null)
                {
                    exitController.ExecuteExit(); // 두 번째 확인창 없이 바로 이동
                }
                else
                {
                    Debug.LogWarning("[ElevatorUI] ExploreExitController reference is NULL!");
                }
            }, () => 
            {
                HidePanel(); // 취소 시에도 UI 닫기 처리
            });
        }
        else
        {
            HidePanel();
            if (exitController != null)
            {
                exitController.ExecuteExit();
            }
            else
            {
                Debug.LogWarning("[ElevatorUI] ExploreExitController reference is NULL!");
            }
        }
    }

    /// <summary>
    /// 닫기 버튼 핸들러
    /// </summary>
    void ClosePanel()
    {
        //Debug.Log("[ElevatorUI] ClosePanel() called");
        HidePanel();
    }

    /// <summary>
    /// 미리 배치된 레이어 버튼들의 텍스트·색상·활성화 상태를 갱신
    /// </summary>
    void RefreshLayerButtons()
    {
        if (layerButtons == null || ElevatorManager.Instance == null) return;

        var allLayers = ElevatorManager.Instance.layers;

        // 버튼이 정류장보다 적으면 남는 층은 영영 선택할 수 없다 — 조용히 넘어가지 않도록 한 번 경고한다.
        if (!_warnedButtonShortage && layerButtons.Length < allLayers.Count)
        {
            _warnedButtonShortage = true;
            Debug.LogWarning($"[ElevatorUI] 층 버튼이 {layerButtons.Length}개인데 정류장은 {allLayers.Count}개다. " +
                             $"{allLayers.Count - layerButtons.Length}개 층을 선택할 수 없다 — 씬에 버튼을 추가할 것.");
        }

        for (int i = 0; i < layerButtons.Length; i++)
        {
            var button = layerButtons[i];
            if (button == null) continue;

            // 레이어 정보가 없으면 버튼 비활성화
            if (i >= allLayers.Count)
            {
                button.gameObject.SetActive(false);
                continue;
            }

            button.gameObject.SetActive(true);
            var layerInfo = allLayers[i];

            bool isLoaded = ElevatorManager.Instance.GetElevatorAt(currentXPosition, i) != null;
            bool isCurrent = (i == currentLayerIndex);
            // 직접 가본 적 없는 정류장은 고를 수 없다(ElevatorStopUnlockStore).
            bool unlocked = isCurrent || ElevatorStopUnlockStore.IsUnlocked(layerInfo);

            // 텍스트 갱신
            var text = button.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                if (!unlocked)
                {
                    // 미해금 — 이름·깊이를 가린다.
                    text.text = "<b><color=#888888>???</color></b>\n<size=70%>미발견</size>";
                }
                else
                {
                    string loadedTag = isLoaded ? "" : " <color=#888888>(미로드)</color>";
                    text.text = $"<b>{layerInfo.layerName}</b>{loadedTag}\n<size=70%>깊이: {layerInfo.startDepth}</size>";
                }
            }

            // 색상 갱신
            var img = button.GetComponent<Image>();
            if (img != null)
            {
                if (isCurrent)
                    img.color = currentLayerColor;
                else if (!unlocked || !isLoaded)
                    img.color = new Color(normalColor.r * 0.7f, normalColor.g * 0.7f, normalColor.b * 0.7f, normalColor.a);
                else
                    img.color = normalColor;
            }

            // 현재 층·미해금 층은 클릭 불가
            button.interactable = !isCurrent && unlocked;
        }
    }

    /// <summary>
    /// 층 버튼 클릭 핸들러
    /// </summary>
    void OnLayerButtonClicked(int targetLayer)
    {
        Debug.Log($"[ElevatorUI] Layer button clicked: {targetLayer}");

        if (ElevatorManager.Instance != null)
        {
            ElevatorManager.Instance.TeleportPlayer(currentXPosition, targetLayer);
        }

        HidePanel();
    }

    void OnDestroy()
    {
        // UI가 열린 채로 씬이 종료되는 경우 플래그 리셋
        IsUIOpen = false;
    }
}
