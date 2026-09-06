using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StaminaBar : MonoBehaviour
{
    [Header("References")]
    public PlayerStat player;
    public StaminaManager staminaManager;

    [Header("UI References")]
    public RectTransform fill;          // 현재 사용 가능한 스테미나
    public RectTransform used;          // 소모되어 회복 대기 중인 스테미나

    [Header("Reduction Indicators")]
    public RectTransform injuryFill;    // 부상 누적
    public RectTransform burnFill;      // 화상 누적
    public RectTransform frostbiteFill; // 동상 누적
    public RectTransform radiationFill; // 방사선 누적 (미연결이면 바에 표시만 안 됨)
    public RectTransform diggingFill;   // 채굴로 인한 감소

    [Tooltip("모든 세그먼트의 부모이자 전체 너비 기준이 되는 RectTransform. 비워두면 fill.parent로 자동 설정됩니다.")]
    public RectTransform barContainer;  // 전체 바 컨테이너

    public TextMeshProUGUI staminaText;

    private RectTransform _selfRect;   // StaminaBar 자신의 RectTransform (너비 기준)

    private void Start()
    {
        _selfRect = GetComponent<RectTransform>();

        // Canvas 레이아웃을 강제 갱신하여 rect.width가 즉시 올바른 값을 반환하도록 함
        Canvas.ForceUpdateCanvases();

        // barContainer 미지정 시 fill의 부모를 기준 너비로 사용
        if (barContainer == null && fill != null)
            barContainer = fill.parent as RectTransform;

        // 인스펙터에서 미지정 시 자동 검색
        if (player == null)
            player = FindFirstObjectByType<PlayerStat>();

        if (staminaManager == null)
            staminaManager = FindFirstObjectByType<StaminaManager>();

        // 세그먼트 X축 앵커 정규화 (스트레치 앵커면 sizeDelta가 폭이 아니게 되어 Background를 넘어감)
        NormalizeSegmentXAxis(fill);
        NormalizeSegmentXAxis(used);
        NormalizeSegmentXAxis(injuryFill);
        NormalizeSegmentXAxis(burnFill);
        NormalizeSegmentXAxis(frostbiteFill);
        NormalizeSegmentXAxis(radiationFill);
        NormalizeSegmentXAxis(diggingFill);

        // 초기 세그먼트 비활성화 (기본 상태 리셋)
        SetAllSegmentsInactive();


    }

    // UpdateSegment의 배치 계산은 '왼쪽 앵커(anchor.x=0) + pivot.x=0 + 절대폭(sizeDelta.x)'을 가정한다.
    // 에디터에서 X축을 스트레치 앵커로 바꿔두면 실제 폭 = 부모 폭 + sizeDelta.x가 되어
    // 세그먼트가 Background 범위를 벗어나므로, 시작 시 X축만 왼쪽 기준으로 되돌린다. (Y축 설정은 유지)
    private static void NormalizeSegmentXAxis(RectTransform segment)
    {
        if (segment == null) return;
        if (segment.anchorMin.x != 0f) segment.anchorMin = new Vector2(0f, segment.anchorMin.y);
        if (segment.anchorMax.x != 0f) segment.anchorMax = new Vector2(0f, segment.anchorMax.y);
        if (segment.pivot.x != 0f)     segment.pivot     = new Vector2(0f, segment.pivot.y);
    }

    private void Update()
    {


        if (player == null) return;
        if (staminaManager == null) return;
        if (_selfRect == null) return;
        UpdateUI();
    }

    private void UpdateUI()
    {
        // StaminaManager가 실제로 참조하는 PlayerStat 우선 사용. 없으면 연결된 player 필드 사용
        PlayerStat stat = (staminaManager != null && staminaManager.PlayerStats != null) ? staminaManager.PlayerStats : player;
        if (stat == null) return;

        // barContainer(Background) 너비 우선, 없으면 StaminaBar 자신의 너비로 폴백
        float containerWidth = (barContainer != null) ? barContainer.rect.width : 0f;
        float totalWidth = containerWidth > 0.5f ? containerWidth : _selfRect.rect.width;

        // stat.MaxStamina는 StaminaManager의 감소 modifier가 '이미 반영된' 값이다.
        // 여기서 감소를 또 빼면 current가 그만큼 과하게 잘려, 벽타기로 감소분을 다 쓸 때까지
        // 바가 전혀 안 줄어드는 것처럼 보인다. 감소 전 최대치는 StaminaBarLayout이 복원한다.
        float effectiveMax = stat.MaxStamina;
        var seg = StaminaBarLayout.Compute(
            effectiveMax, stat.CurrentStamina, stat.GetPercentMultiplier(StatType.MaxStamina),
            staminaManager.Injury, staminaManager.Burn, staminaManager.Frostbite,
            staminaManager.Radiation, staminaManager.DiggingReduction);

        float originalMax = seg.total;
        if (originalMax <= 0) return;

        int activeSegmentsCount = 0;
        if (seg.current > 0) activeSegmentsCount++;
        if (seg.recoverable > 0) activeSegmentsCount++;
        if (seg.injury > 0) activeSegmentsCount++;
        if (seg.burn > 0) activeSegmentsCount++;
        if (seg.frostbite > 0) activeSegmentsCount++;
        if (seg.radiation > 0) activeSegmentsCount++;
        if (seg.digging > 0) activeSegmentsCount++;

        // 간격은 (활성 세그먼트 개수 - 1) 번 들어감
        float totalSpacing = Mathf.Max(0, (activeSegmentsCount - 1)) * segmentSpacing;

        // 실제 각 세그먼트들이 차지할 수 있는 순수 너비(간격 제외)
        float availableWidth = totalWidth - totalSpacing;
        if (availableWidth < 0) availableWidth = 0;

        float offset = 0;
        UpdateSegment(fill,         seg.current     / originalMax, ref offset, availableWidth);
        UpdateSegment(used,         seg.recoverable / originalMax, ref offset, availableWidth);
        UpdateSegment(injuryFill,   seg.injury      / originalMax, ref offset, availableWidth);
        UpdateSegment(burnFill,     seg.burn        / originalMax, ref offset, availableWidth);
        UpdateSegment(frostbiteFill,seg.frostbite   / originalMax, ref offset, availableWidth);
        UpdateSegment(radiationFill,seg.radiation   / originalMax, ref offset, availableWidth);
        UpdateSegment(diggingFill,  seg.digging     / originalMax, ref offset, availableWidth);

        if (staminaText != null)
            staminaText.text = $"{Mathf.RoundToInt(seg.current)} / {Mathf.RoundToInt(Mathf.Max(0f, effectiveMax))}";
    }

    [Header("UI Settings")]
    [Tooltip("세그먼트 사이의 간격 (픽셀 단위)")]
    public float segmentSpacing = 1f;

    private void UpdateSegment(RectTransform segment, float ratio, ref float offset, float totalWidth)
    {
        if (segment == null) return;

        float width = Mathf.Max(0f, ratio * totalWidth);
        bool active = width > 0.5f;
        segment.gameObject.SetActive(active);

        if (active)
        {
            segment.sizeDelta       = new Vector2(width, segment.sizeDelta.y);
            segment.anchoredPosition = new Vector2(offset, segment.anchoredPosition.y);
            
            // 다음 세그먼트는 본인 너비 + 설정된 간격(기본 1px)만큼 뒤에서 시작
            offset += (width + segmentSpacing);
        }
    }

    private void SetAllSegmentsInactive()
    {
        if (fill != null)         fill.gameObject.SetActive(false);
        if (used != null)         used.gameObject.SetActive(false);
        if (injuryFill != null)   injuryFill.gameObject.SetActive(false);
        if (burnFill != null)     burnFill.gameObject.SetActive(false);
        if (frostbiteFill != null)frostbiteFill.gameObject.SetActive(false);
        if (radiationFill != null)radiationFill.gameObject.SetActive(false);
        if (diggingFill != null)  diggingFill.gameObject.SetActive(false);
    }
}
