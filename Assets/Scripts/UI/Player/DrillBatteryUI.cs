using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 통합 스태미나 HUD의 드릴 게이지 섹션을 담당하는 UI 스크립트입니다.
/// 드릴 해금(획득) 전에는 드릴 게이지를 숨기고 기본 프레임 스프라이트를,
/// 해금 후에는 드릴 게이지 포함 프레임으로 교체하고 배터리를 상시 표시합니다. (장착 여부 무관)
/// </summary>
public class DrillBatteryUI : MonoBehaviour
{
    [Header("References")]
    public PlayerMining playerMining;
    public ToolController toolController;

    [Header("UI References")]
    [Tooltip("드릴 게이지 섹션만 껐다 켤 때 사용할 부모 객체 (비워두면 이 스크립트가 포함된 객체의 자식들을 켜고 끕니다). 스태미나 바·프레임을 포함시키면 안 됩니다.")]
    public GameObject uiContainer;
    public RectTransform fill;          // 현재 배터리량 표시
    public RectTransform barContainer;  // 전체 바 컨테이너 (너비 계산용)

    [Header("통합 프레임 (스태미나 + 드릴)")]
    [Tooltip("통합 HUD 배경 프레임 Image. 드릴 해금 여부에 따라 스프라이트가 교체됩니다. 비워두면 교체 없음.")]
    public Image frameImage;
    [Tooltip("드릴 획득 전 프레임 (기본 스태미나 바만 있는 스프라이트)")]
    public Sprite frameBeforeDrill;
    [Tooltip("드릴 획득 후 프레임 (아래 드릴 게이지가 포함된 스프라이트)")]
    public Sprite frameAfterDrill;

    [Header("UI Settings")]
    public float paddingLeft = 6f;
    public float paddingRight = 6f;

    private RectTransform _selfRect;
    private CanvasGroup _canvasGroup;
    private SkewedBarEffect _fillSkew;   // fill에 사선 이펙트가 붙어 있으면 폭 보정에 사용

    // 드릴 해금 상태 캐시 — IsDrillUnlocked는 내부에서 씬 탐색(FindFirstObjectByType)이 일어나므로
    // 매 프레임 호출하지 않고 해금 이벤트 + 저빈도 폴백 폴링으로만 갱신한다.
    private bool _drillUnlocked;
    private bool _unlockEventSubscribed;
    private float _unlockPollTimer;
    private const float UNLOCK_POLL_INTERVAL = 1f;

    private void Awake()
    {
        _selfRect = GetComponent<RectTransform>();
        
        // uiContainer가 자기 자신으로 지정되어 꺼지면 스크립트가 동작하지 않으므로,
        // 이를 방지하기 위해 CanvasGroup을 사용해 투명도와 상호작용만 제어하는 방식으로 보완합니다.
        if (uiContainer == gameObject)
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }
    }

    private void Start()
    {
        // barContainer 미지정 시 fill의 부모를 기준 너비로 사용
        if (barContainer == null && fill != null)
            barContainer = fill.parent as RectTransform;

        if (playerMining == null)
            playerMining = FindFirstObjectByType<PlayerMining>();

        if (toolController == null)
            toolController = FindFirstObjectByType<ToolController>();

        // fill은 barContainer 기준으로 배치되므로 반드시 그 자식이어야 한다 (설정 실수 조기 감지)
        if (fill != null && barContainer != null && fill.parent != barContainer)
            Debug.LogWarning("[DrillBatteryUI] fill이 barContainer의 자식이 아닙니다. 게이지가 컨테이너 안에 배치되지 않습니다.", this);

        // 스태미나 바와 동일하게 'X축 왼쪽 앵커 + 절대폭' 배치를 가정하므로 앵커를 정규화한다
        // (스트레치 앵커면 sizeDelta가 폭이 아니게 되어 컨테이너를 넘어감 — StaminaBar와 같은 처리)
        NormalizeFillXAxis(fill);

        _fillSkew = (fill != null) ? fill.GetComponent<SkewedBarEffect>() : null;

        // 캔버스 강제 업데이트로 레이아웃 크기를 갱신 (너비 0 방지)
        Canvas.ForceUpdateCanvases();

        if (toolController != null)
            _drillUnlocked = toolController.IsDrillUnlocked;
    }

    private static void NormalizeFillXAxis(RectTransform segment)
    {
        if (segment == null) return;
        if (segment.anchorMin.x != 0f) segment.anchorMin = new Vector2(0f, segment.anchorMin.y);
        if (segment.anchorMax.x != 0f) segment.anchorMax = new Vector2(0f, segment.anchorMax.y);
        if (segment.pivot.x != 0f)     segment.pivot     = new Vector2(0f, segment.pivot.y);
    }

    private void OnDestroy()
    {
        if (_unlockEventSubscribed && UpgradeManager.Instance != null)
            UpgradeManager.Instance.OnUpgradeStateChanged -= OnUpgradeStateChanged;
    }

    private void Update()
    {
        if (playerMining == null || toolController == null) return;

        // 드릴 해금(획득) 이후에는 장착 여부와 무관하게 게이지를 상시 표시
        RefreshDrillUnlocked();
        bool drillUnlocked = _drillUnlocked;

        // 해금 여부에 따라 통합 프레임 스프라이트 교체
        UpdateFrameSprite(drillUnlocked);

        // UI 켜기/끄기 처리
        if (uiContainer != null)
        {
            if (uiContainer == gameObject && _canvasGroup != null)
            {
                // 자기 자신을 끄면 Update가 멈추므로 CanvasGroup으로 처리
                _canvasGroup.alpha = drillUnlocked ? 1f : 0f;
                _canvasGroup.interactable = drillUnlocked;
                _canvasGroup.blocksRaycasts = drillUnlocked;
            }
            else
            {
                if (uiContainer.activeSelf != drillUnlocked)
                    uiContainer.SetActive(drillUnlocked);
            }
        }
        else
        {
            // uiContainer가 없으면 개별 UI 요소 제어
            if (fill != null && fill.gameObject.activeSelf != drillUnlocked)
                fill.gameObject.SetActive(drillUnlocked);

            if (barContainer != null && barContainer.gameObject.activeSelf != drillUnlocked)
                barContainer.gameObject.SetActive(drillUnlocked);
        }

        if (drillUnlocked)
        {
            UpdateUI();
        }
    }

    private void UpdateFrameSprite(bool drillUnlocked)
    {
        if (frameImage == null) return;

        Sprite target = drillUnlocked ? frameAfterDrill : frameBeforeDrill;
        if (target != null && frameImage.sprite != target)
            frameImage.sprite = target;
    }

    private void RefreshDrillUnlocked()
    {
        // UpgradeManager가 늦게 생성될 수 있어 Update에서 구독을 시도
        if (!_unlockEventSubscribed && UpgradeManager.Instance != null)
        {
            UpgradeManager.Instance.OnUpgradeStateChanged += OnUpgradeStateChanged;
            _unlockEventSubscribed = true;
            _drillUnlocked = toolController.IsDrillUnlocked;
            return;
        }

        // 이벤트 누락(세이브 로드 타이밍 등) 대비 저빈도 폴백 폴링
        _unlockPollTimer += Time.deltaTime;
        if (_unlockPollTimer >= UNLOCK_POLL_INTERVAL)
        {
            _unlockPollTimer = 0f;
            _drillUnlocked = toolController.IsDrillUnlocked;
        }
    }

    private void OnUpgradeStateChanged()
    {
        if (toolController != null)
            _drillUnlocked = toolController.IsDrillUnlocked;
    }

    private void UpdateUI()
    {
        float containerWidth = (barContainer != null) ? barContainer.rect.width : 0f;
        float totalWidth = containerWidth > 0.5f ? containerWidth : (_selfRect != null ? _selfRect.rect.width : 0f);

        // 장착 도구와 무관하게 드릴 배터리 비율(0~1)을 가져옵니다. (상시 표시용)
        float chargeRatio = Mathf.Clamp01(playerMining.GetDrillBatteryRatio());

        if (totalWidth <= 0.5f || fill == null) return;

        // 사선(스큐) 이펙트가 있으면 기울어진 만큼 렌더 폭이 넓어지므로 그만큼 가용 폭에서 뺀다
        float skew = (_fillSkew != null && _fillSkew.isActiveAndEnabled) ? _fillSkew.skewX : 0f;
        float availableWidth = Mathf.Max(0f, totalWidth - paddingLeft - paddingRight - Mathf.Abs(skew));

        // 스큐가 음수(위쪽이 왼쪽으로 기움)면 왼쪽으로 삐져나가므로 시작점을 그만큼 오른쪽으로 민다
        float startX = paddingLeft + Mathf.Max(0f, -skew);

        // 스태미나 바와 동일한 배치: 컨테이너 왼쪽 기준 절대폭 (앵커는 Start에서 정규화됨)
        fill.sizeDelta        = new Vector2(availableWidth * chargeRatio, fill.sizeDelta.y);
        fill.anchoredPosition = new Vector2(startX, fill.anchoredPosition.y);
    }
}
