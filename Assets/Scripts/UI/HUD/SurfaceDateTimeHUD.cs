// @tags: hud, surface, day, time, daycycle, ui, code-generated, localization, sprite

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 지상 <b>좌상단</b>에 현재 날짜(N일차)와 시간대(오전/오후) 스프라이트를 띄우는 HUD.
/// 캔버스/모서리 앵커/지상 표시 판정은 <see cref="SurfaceCornerHUD"/>가 처리하고, 이 클래스는
/// 날짜/시간 블록만 채운다. 데이터는 <see cref="DayCycleManager"/>에서 읽고 날짜·언어 변경 시 갱신한다.
///
/// 인스펙터에서 <see cref="morningSprite"/>/<see cref="afternoonSprite"/>(비우면 오전/오후 텍스트),
/// 배경(<see cref="showBackgroundPanel"/>), 아이콘↔일차 간격, 일차 외곽선 등을 조정한다.
/// 붙일 모서리는 베이스의 Corner로 바꿀 수 있다(기본 좌상단).
/// </summary>
public class SurfaceDateTimeHUD : SurfaceCornerHUD
{
    // ===================================================
    // 인스펙터 — 스프라이트 / 배경
    // ===================================================
    [Header("스프라이트 (비우면 코드 생성/텍스트로 폴백)")]
    [Tooltip("배경 패널을 그린다. 끄면 아이콘+날짜만 보이고 크기는 내용에 맞춰 자동")]
    [SerializeField] private bool showBackgroundPanel = false;
    [Tooltip("패널 배경. 비우면 코드 생성 라운드 상자 (showBackgroundPanel일 때만)")]
    [SerializeField] private Sprite backgroundSprite;
    [Tooltip("오전(Morning) 시간대 아이콘. 비우면 '오전' 로컬라이즈 텍스트")]
    [SerializeField] private Sprite morningSprite;
    [Tooltip("오후(Afternoon) 시간대 아이콘. 비우면 '오후' 로컬라이즈 텍스트")]
    [SerializeField] private Sprite afternoonSprite;

    [Tooltip("배경 스프라이트에 곱할 색(흰 9-슬라이스용). 이미 색이 입혀진 도트면 흰색으로")]
    [SerializeField] private Color panelTint = new Color(0.067f, 0.102f, 0.18f, 0.92f);
    [Range(0.25f, 8f)][SerializeField] private float spritePixelsPerUnit = 1f;

    // ===================================================
    // 인스펙터 — 레이아웃 (1920x1080 기준 px)
    // ===================================================
    [Header("레이아웃 (1920x1080 기준 px)")]
    [Tooltip("패널 크기 (showBackgroundPanel일 때만. 배경 없으면 내용에 맞춰 자동)")]
    [SerializeField] private Vector2 panelSize = new Vector2(260f, 120f);
    [Tooltip("패널 내부 여백 (x=좌, y=우, z=상, w=하)")]
    [SerializeField] private Vector4 padding = new Vector4(16f, 16f, 12f, 12f);
    [Tooltip("시간대 아이콘 한 변 크기")]
    [SerializeField] private float iconSize = 88f;
    [Tooltip("아이콘(오전/오후)과 일차수 텍스트 사이 가로 간격")]
    [SerializeField] private float iconDaySpacing = 12f;
    [SerializeField] private float dayFontSize = 34f;
    [SerializeField] private float timeFontSize = 26f;
    [SerializeField] private Color dayTextColor = new Color(0.85f, 0.89f, 0.97f, 1f);
    [SerializeField] private Color timeTextColor = new Color(0.96f, 0.78f, 0.25f, 1f);
    [Tooltip("일차 텍스트 외곽선 두께. 0이면 없음. 0.1~0.2가 얇은 테두리")]
    [Range(0f, 0.5f)][SerializeField] private float dayOutlineWidth = 0.15f;
    [Tooltip("일차 텍스트 외곽선 색")]
    [SerializeField] private Color dayOutlineColor = Color.black;

    [Header("로컬라이제이션 키 (UI_Localization.csv)")]
    [SerializeField] private string dayCountKey = "hud_day_count";
    [SerializeField] private string morningKey = "hud_time_morning";
    [SerializeField] private string afternoonKey = "hud_time_afternoon";

    // ===================================================
    // 내부 상태
    // ===================================================
    private TextMeshProUGUI _dayText;
    private Image _timeIcon;
    private TextMeshProUGUI _timeText;
    private bool _subscribedDay;

    // 화면에 '지금 그려지고 있는' 값. 실제(DayCycleManager) 값과 별개로 두고,
    // 시간대 전환이 페이드로 가려졌을 때만 이 표시값을 따라잡게 해 스와프가 눈에 안 띄게 한다.
    private bool _shown;
    private int _shownDay = 1;
    private TimeOfDay _shownTime = TimeOfDay.Morning;
    private float _pendingSince = -1f;               // 표시 대기 시작 시각(unscaled), -1=대기 없음
    private const float PendingApplyTimeout = 1.5f;  // 페이드가 안 오는 경우(디버그·일부 경로)용 폴백

    private static readonly Color DefaultDayColor = new Color(0.85f, 0.89f, 0.97f, 1f);
    private static readonly Color DefaultTimeColor = new Color(0.96f, 0.78f, 0.25f, 1f);

    // ===================================================
    // 구독 (언어는 base, 날짜는 여기서 확장)
    // ===================================================
    protected override void EnsureSubscriptions()
    {
        base.EnsureSubscriptions();
        if (!_subscribedDay && DayCycleManager.Instance != null)
        {
            DayCycleManager.Instance.OnDateTimeChanged += OnDateTimeChanged;
            _subscribedDay = true;
            RefreshContent();
        }
    }

    protected override void RemoveSubscriptions()
    {
        base.RemoveSubscriptions();
        if (_subscribedDay && DayCycleManager.Instance != null)
        {
            DayCycleManager.Instance.OnDateTimeChanged -= OnDateTimeChanged;
            _subscribedDay = false;
        }
    }

    private void OnDateTimeChanged(int day, TimeOfDay time) => RefreshContent();

    // ===================================================
    // 조립
    // ===================================================
    protected override void BuildContent(RectTransform column)
    {
        var panelRt = CodeUI.CreateRect(column, "DateTime");
        var panelGo = panelRt.gameObject;

        if (showBackgroundPanel)
        {
            var bg = panelGo.AddComponent<Image>();
            CodeUI.ApplySkin(bg, panelTint, backgroundSprite, null);
            bg.pixelsPerUnitMultiplier = Mathf.Max(0.01f, spritePixelsPerUnit);
            bg.raycastTarget = false;
            var le = panelGo.AddComponent<LayoutElement>();
            le.preferredWidth = panelSize.x;
            le.preferredHeight = panelSize.y;
        }
        // 배경 없으면 아래 HorizontalLayoutGroup의 preferred 크기를 컬럼이 그대로 읽어 내용에 딱 맞는다.

        var row = panelGo.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(
            Mathf.RoundToInt(padding.x), Mathf.RoundToInt(padding.y),
            Mathf.RoundToInt(padding.z), Mathf.RoundToInt(padding.w));
        row.spacing = Mathf.Max(0f, iconDaySpacing);
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        row.childAlignment = AnchorRight ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;

        // 인스펙터 값이 0/검정으로 남아 있어도 화면에 뜨도록 런타임 보정.
        float iconPx = iconSize > 1f ? iconSize : 88f;
        float dayPx = dayFontSize > 1f ? dayFontSize : 34f;
        float timePx = timeFontSize > 1f ? timeFontSize : 26f;

        // 시간대 아이콘 (스프라이트)
        _timeIcon = CodeUI.CreateImage(panelGo.transform, "TimeIcon", Color.white, rounded: false);
        _timeIcon.preserveAspect = true;
        _timeIcon.raycastTarget = false;
        var iconLe = _timeIcon.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredWidth = iconLe.minWidth = iconPx;
        iconLe.preferredHeight = iconLe.minHeight = iconPx;

        // 시간대 텍스트 (스프라이트 미지정 시 폴백)
        _timeText = CodeUI.CreateText(panelGo.transform, "TimeText", timePx, FontStyles.Bold,
            Visible(timeTextColor, DefaultTimeColor), TextAlignmentOptions.Center, _binder);
        _timeText.gameObject.AddComponent<LayoutElement>().minWidth = iconPx;

        // 날짜 텍스트 (모서리 방향에 맞춰 정렬)
        _dayText = CodeUI.CreateText(panelGo.transform, "DayText", dayPx, FontStyles.Bold,
            Visible(dayTextColor, DefaultDayColor),
            AnchorRight ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft, _binder);

        ApplyDayOutline();
    }

    // ===================================================
    // 갱신
    // ===================================================
    // 폴링(보일 때 0.4s)마다 재평가 — 표시 대기 중이면 타임아웃 폴백을 확인한다.
    protected override void PollContent() => RefreshContent();

    protected override void RefreshContent()
    {
        if (_dayText == null) return; // 아직 Build 전

        int day = 1;
        TimeOfDay time = TimeOfDay.Morning;
        if (DayCycleManager.Instance != null)
        {
            day = DayCycleManager.Instance.CurrentDay;
            time = DayCycleManager.Instance.CurrentTime;
        }

        // 실제 값이 바뀌었으면 '화면이 페이드로 덮였을 때'만 표시값을 따라잡는다.
        // (침대 수면·탐험 종료 등은 페이드 뒤에서 시간이 바뀌므로 스와프가 안 보인다.)
        // 페이드가 없는 경로를 대비해 일정 시간 지나면 그냥 반영한다.
        bool changed = !_shown || day != _shownDay || time != _shownTime;
        if (changed)
        {
            bool covered = IsScreenCovered();
            bool timedOut = _pendingSince >= 0f && (Time.unscaledTime - _pendingSince) >= PendingApplyTimeout;

            if (!_shown || covered || timedOut)
            {
                _shownDay = day;
                _shownTime = time;
                _shown = true;
                _pendingSince = -1f;
            }
            else if (_pendingSince < 0f)
            {
                _pendingSince = Time.unscaledTime; // 대기 시작 (표시는 옛 값 유지)
            }
        }
        else
        {
            _pendingSince = -1f;
        }

        RenderDayTime(_shownDay, _shownTime);

        _binder.Refresh();  // 폰트 갱신 (언어 변경 시 폰트=머티리얼 교체)
        ApplyDayOutline();  // 폰트 교체로 초기화된 외곽선 재적용
    }

    private void RenderDayTime(int day, TimeOfDay time)
    {
        bool isTutorial = !TutorialProgress.IsCompleted;
        if (_dayText != null && _dayText.transform.parent != null)
        {
            _dayText.transform.parent.gameObject.SetActive(!isTutorial);
        }
        
        if (isTutorial) return;

        _dayText.text = DayLabel(day);

        Sprite icon = time == TimeOfDay.Morning ? morningSprite : afternoonSprite;
        if (icon != null)
        {
            _timeIcon.sprite = icon;
            _timeIcon.gameObject.SetActive(true);
            if (_timeText != null) _timeText.gameObject.SetActive(false);
        }
        else
        {
            if (_timeIcon != null) _timeIcon.gameObject.SetActive(false);
            if (_timeText != null)
            {
                _timeText.gameObject.SetActive(true);
                _timeText.text = CodeUI.L(time == TimeOfDay.Morning ? morningKey : afternoonKey,
                                          time == TimeOfDay.Morning ? "오전" : "오후");
            }
        }
    }

    /// <summary>"{0}일차" 포맷을 현재 언어로. 키가 없으면 한글 폴백.</summary>
    private string DayLabel(int day)
    {
        var lm = LanguageManager.Instance;
        if (lm != null && !string.IsNullOrEmpty(dayCountKey))
        {
            string template = lm.L(dayCountKey);
            if (!string.IsNullOrEmpty(template) && template != dayCountKey)
            {
                try { return string.Format(template, day); }
                catch (System.FormatException) { /* 폴백으로 */ }
            }
        }
        return $"{day}일차";
    }

    /// <summary>
    /// 일차 텍스트에 얇은 외곽선. 공유 폰트 머티리얼이 아니라 이 텍스트 전용 인스턴스
    /// (<see cref="TMP_Text.fontMaterial"/>)에만 적용하므로 다른 텍스트엔 안 번진다.
    /// 언어 변경으로 폰트(=머티리얼)가 바뀌면 초기화되므로 <see cref="RefreshContent"/>에서 다시 부른다.
    /// </summary>
    private void ApplyDayOutline()
    {
        if (_dayText == null) return;
        if (dayOutlineWidth <= 0.001f) return; // 외곽선 없음

        var mat = _dayText.fontMaterial; // 접근 시 이 텍스트 전용 인스턴스가 생성됨
        if (mat == null) return;

        mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
        mat.SetColor(ShaderUtilities.ID_OutlineColor, dayOutlineColor.a > 0.01f ? dayOutlineColor : Color.black);
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, Mathf.Clamp01(dayOutlineWidth));
    }
}
