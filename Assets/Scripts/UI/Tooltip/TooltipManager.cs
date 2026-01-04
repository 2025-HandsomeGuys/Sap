using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 범용 툴팁 매니저 - 마우스 호버 시 상세 설명 표시
/// </summary>
public class TooltipManager : MonoBehaviour
{
    private static TooltipManager _instance;
    public static TooltipManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<TooltipManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("TooltipManager");
                    _instance = go.AddComponent<TooltipManager>();
                }
            }
            return _instance;
        }
    }

    [Header("툴팁 UI")]
    public GameObject tooltipPanel;
    public TextMeshProUGUI tooltipTitleText;
    public TextMeshProUGUI tooltipContentText;
    public RectTransform tooltipRectTransform;
    public Canvas tooltipCanvas;

    [Header("설정")]
    public float offsetX = 20f; // 마우스로부터의 X 오프셋 (더 멀리)
    public float offsetY = 20f; // 마우스로부터의 Y 오프셋 (더 멀리)
    public float showDelay = 0.3f; // 표시 지연 시간

    private float showTimer = 0f;
    private bool isShowing = false;
    private string pendingTitle = "";
    private string pendingContent = "";

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // Canvas 찾기
        if (tooltipCanvas == null)
        {
            tooltipCanvas = GetComponentInParent<Canvas>();
            if (tooltipCanvas == null)
            {
                tooltipCanvas = FindFirstObjectByType<Canvas>();
            }
        }

        // 툴팁 패널이 없으면 생성
        if (tooltipPanel == null)
        {
            CreateTooltipPanel();
        }
        else
        {
            tooltipPanel.SetActive(false);
        }
    }

    void Update()
    {
        if (isShowing && tooltipPanel != null && tooltipPanel.activeSelf)
        {
            UpdateTooltipPosition();
        }

        // 지연 표시 처리
        if (!string.IsNullOrEmpty(pendingTitle) || !string.IsNullOrEmpty(pendingContent))
        {
            showTimer += Time.unscaledDeltaTime;
            if (showTimer >= showDelay)
            {
                ShowTooltip(pendingTitle, pendingContent);
                pendingTitle = "";
                pendingContent = "";
                showTimer = 0f;
            }
        }
    }

    private void CreateTooltipPanel()
    {
        // Canvas 찾기
        Canvas canvas = tooltipCanvas;
        if (canvas == null)
        {
            canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObj = new GameObject("TooltipCanvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000; // 최상위에 표시
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
            }
        }

        // 툴팁 패널 생성
        tooltipPanel = new GameObject("TooltipPanel");
        tooltipPanel.transform.SetParent(canvas.transform, false);
        tooltipRectTransform = tooltipPanel.AddComponent<RectTransform>();
        
        // 배경 이미지
        Image bg = tooltipPanel.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
        
        // CanvasGroup 추가 - raycast 차단 방지
        CanvasGroup canvasGroup = tooltipPanel.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false; // 마우스 이벤트를 차단하지 않음
        canvasGroup.interactable = false; // 상호작용 불가
        
        // 레이아웃 그룹
        VerticalLayoutGroup layout = tooltipPanel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 5f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // Content Size Fitter
        ContentSizeFitter sizeFitter = tooltipPanel.AddComponent<ContentSizeFitter>();
        sizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // 제목 텍스트
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(tooltipPanel.transform, false);
        tooltipTitleText = titleObj.AddComponent<TextMeshProUGUI>();
        tooltipTitleText.fontSize = 18;
        tooltipTitleText.fontStyle = FontStyles.Bold;
        tooltipTitleText.color = Color.white;
        tooltipTitleText.text = "";

        // 내용 텍스트
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(tooltipPanel.transform, false);
        tooltipContentText = contentObj.AddComponent<TextMeshProUGUI>();
        tooltipContentText.fontSize = 14;
        tooltipContentText.color = new Color(0.9f, 0.9f, 0.9f);
        tooltipContentText.text = "";
        tooltipContentText.enableWordWrapping = true;

        tooltipPanel.SetActive(false);
    }

    public void ShowTooltip(string title, string content)
    {
        if (tooltipPanel == null)
        {
            CreateTooltipPanel();
        }

        if (tooltipTitleText != null)
            tooltipTitleText.text = title;
        if (tooltipContentText != null)
            tooltipContentText.text = content;

        tooltipPanel.SetActive(true);
        isShowing = true;
        UpdateTooltipPosition();
    }

    public void ShowTooltipDelayed(string title, string content)
    {
        pendingTitle = title;
        pendingContent = content;
        showTimer = 0f;
    }

    public void HideTooltip()
    {
        if (tooltipPanel != null)
        {
            tooltipPanel.SetActive(false);
        }
        isShowing = false;
        pendingTitle = "";
        pendingContent = "";
        showTimer = 0f;
    }

    private void UpdateTooltipPosition()
    {
        if (tooltipRectTransform == null || tooltipCanvas == null) return;

        Vector2 mousePos = Input.mousePosition;
        RectTransform canvasRect = tooltipCanvas.transform as RectTransform;

        // 마우스 위치를 Canvas 로컬 좌표로 변환
        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            mousePos,
            tooltipCanvas.worldCamera != null ? tooltipCanvas.worldCamera : null,
            out localPoint);

        // 툴팁 크기 가져오기 (레이아웃이 업데이트되도록 강제)
        Canvas.ForceUpdateCanvases();
        float tooltipWidth = tooltipRectTransform.rect.width;
        float tooltipHeight = tooltipRectTransform.rect.height;

        // Canvas 경계 확인
        float canvasWidth = canvasRect.rect.width;
        float canvasHeight = canvasRect.rect.height;

        // 기본 위치: 마우스 오른쪽 아래
        float x = localPoint.x + offsetX;
        float y = localPoint.y - offsetY;

        // 오른쪽 경계 체크 - 오른쪽으로 넘어가면 왼쪽에 표시
        if (x + tooltipWidth > canvasWidth * 0.5f)
        {
            x = localPoint.x - tooltipWidth - offsetX;
        }

        // 왼쪽 경계 체크
        if (x < -canvasWidth * 0.5f)
        {
            x = -canvasWidth * 0.5f + 10f;
        }

        // 아래쪽 경계 체크 - 아래로 넘어가면 위에 표시
        if (y - tooltipHeight < -canvasHeight * 0.5f)
        {
            y = localPoint.y + tooltipHeight + offsetY;
        }

        // 위쪽 경계 체크
        if (y > canvasHeight * 0.5f)
        {
            y = canvasHeight * 0.5f - 10f;
        }

        tooltipRectTransform.anchoredPosition = new Vector2(x, y);
    }
}

