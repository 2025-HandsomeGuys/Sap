// @tags: ui, code-generated, notification, toast, pickup, acquire, mineral, item, relic, underground

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 왼쪽 아래에 "방금 입수한 것"을 잠깐 띄우는 획득 알림(토스트).
/// 광물·아이템·유물을 이름 + 아이콘 + 갯수로 보여준다.
///
/// 특징
///  - <b>전부 코드 생성</b> — 씬 세팅 불필요. 첫 <see cref="Notify"/> 호출 때 지연 생성되고
///    <see cref="Object.DontDestroyOnLoad"/>로 살아남는다(SoundManager/BugReportSystem과 같은 패턴).
///  - 여러 <b>종류</b>를 획득하면 코너부터 한 줄씩 위로 쌓이고, 각자 수명이 지나면
///    아래부터(가장 오래된 것부터) 한 줄씩 자연스럽게 접히며 사라진다.
///  - 같은 종류를 연달아 주우면 새 줄을 만들지 않고 그 줄의 <b>갯수만 올리고</b> 수명을 리셋한다.
///    이때 <b>왼쪽 가장자리는 고정</b>이고 줄은 오른쪽으로만 늘어난다(숫자 라벨만 톡 튄다).
///
/// 애니메이션은 전부 <see cref="Time.unscaledDeltaTime"/> 기반 — 디버그 배속·일시정지와 무관하게 돈다.
/// 레이아웃과 안 싸우도록 위치는 건드리지 않고 <b>높이·알파</b>만 애니메이트한다
/// (가로 스케일 펀치는 왼쪽 가장자리를 밀어내므로 쓰지 않는다).
/// </summary>
public class AcquisitionNotifier : MonoBehaviour
{
    // ── 튜닝 ──
    private const float FadeInDur     = 0.22f;
    private const float HoldDur       = 2.6f;
    private const float FadeOutDur    = 0.45f;
    private const float FadeOutFastDur = 0.24f;  // 같은 종류로 교체돼 밀려나는 예전 줄이 더 빨리 사라진다
    private const float PopDur      = 0.26f;   // 갯수 증가 시 숫자 라벨이 튀는 시간
    private const float RowHeight   = 56f;
    private const int   MaxVisible  = 5;        // 넘치면 가장 오래된 줄을 즉시 퇴장시킨다

    // 배경 패널 스프라이트 (Stock_UISheet_8 하나만 사용).
    private const string PanelSheetResource = "UI/Stock_UISheet";
    private const string PanelSpriteName    = "Stock_UISheet_8";
    private const float  PanelPPU           = 0.4f;

    // 검정 배경 위에서 은은하게 읽히는 팔레트.
    //  - 패널: 살짝 푸른 기운의 어두운 남색을 반투명으로 → 검정 위에 뜬 유리 카드 느낌.
    //  - 이름: 차가운 밝은 화이트 → 어두운 패널 위에서 또렷.
    //  - ×N 갯수: 카테고리 색(광물=청록 / 아이템=연두 / 유물=분홍)으로 포인트.
    private static readonly Color PanelColor = new Color32(0x23, 0x2B, 0x4A, 0xD9); // #232B4A, α≈0.85
    private static readonly Color NameColor  = new Color32(0xEA, 0xEF, 0xFB, 0xFF); // #EAEFFB

    private static AcquisitionNotifier _instance;

    /// <summary>없으면 만들어서 반환(지연 생성). 파괴 중이면 null.</summary>
    private static AcquisitionNotifier Instance
    {
        get
        {
            if (_instance != null) return _instance;
            if (!Application.isPlaying) return null;

            var go = new GameObject("AcquisitionNotifier");
            _instance = go.AddComponent<AcquisitionNotifier>();
            return _instance;
        }
    }

    private RectTransform _stack;                 // 세로로 쌓이는 컨테이너
    private readonly List<Toast> _toasts = new List<Toast>();

    // ================================================================
    //  공개 API
    // ================================================================
    /// <param name="isNew">이번이 이 광물의 <b>첫 발견</b>이면 true — 토스트에 '새 광물!' 뱃지가 붙는다.
    /// 호출부에서 <c>CollectionCodex.IsDiscovered(Mineral, ..)</c>를 획득 전에 확인해 넘긴다.</param>
    public static void NotifyMineral(MineralSO mineral, int count, bool isNew = false)
    {
        if (mineral == null || count <= 0) return;
        Notify("min:" + mineral.mineralID, mineral.Icon, mineral.DisplayName, count, CodeUI.MineralColor, isNew);
    }

    public static void NotifyItem(ItemSO item, int count)
    {
        if (item == null || count <= 0) return;
        Notify("itm:" + item.itemID, item.Icon, item.DisplayName, count, CodeUI.ItemColor);
    }

    /// <summary>유물 획득(항상 1개). RelicSO에서 이름·아이콘을 뽑는다.</summary>
    public static void NotifyRelic(Relic.Data.RelicSO relic)
    {
        if (relic == null) return;
        string name = CodeUI.L(relic.displayNameKey, relic.displayNameKey);
        Notify("rel:" + relic.id, relic.icon, name, 1, CodeUI.RelicColor);
    }

    /// <summary>
    /// 저수준 진입점. 같은 key가 아직 화면에 살아 있으면 갯수만 더하고 수명을 리셋한다.
    /// </summary>
    public static void Notify(string key, Sprite icon, string displayName, int count, Color accent, bool isNew = false)
    {
        var inst = Instance;
        if (inst == null) return;
        inst.Push(key, icon, displayName, count, accent, isNew);
    }

    // ================================================================
    //  생성 / 로직
    // ================================================================
    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        BuildCanvas();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void BuildCanvas()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // HUD 위, 풀스크린 모달 아래 정도. 비상호작용이라 입력은 안 막는다.

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        // 왼쪽 아래 코너에 고정된 세로 스택.
        _stack = CodeUI.CreateRect(transform, "Stack");
        _stack.anchorMin = _stack.anchorMax = _stack.pivot = new Vector2(0f, 0f);
        _stack.anchoredPosition = new Vector2(28f, 28f);
        _stack.sizeDelta = new Vector2(560f, 0f);

        var vlg = _stack.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.LowerLeft;   // 모든 줄을 같은 왼쪽 가장자리에 정렬
        vlg.childControlWidth = false;   // 각 줄이 스스로 폭을 정한다(오른쪽으로만 늘어남)
        vlg.childControlHeight = true;   // 높이는 줄의 LayoutElement로 주고, 등장·퇴장 때 우리가 조절
        vlg.childForceExpandWidth = false;
        vlg.childForceExpandHeight = false;

        var fitter = _stack.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    private void Push(string key, Sprite icon, string displayName, int count, Color accent, bool isNew = false)
    {
        // 활성(등장/유지 중) 토스트만 본다. 리스트 순서 = 쌓인 순서(앞=위=오래됨, 뒤=아래=최근).
        Toast existing = null;      // 같은 종류 중 활성인 것
        Toast newestActive = null;  // 가장 최근(맨 아래) 활성 토스트
        for (int i = 0; i < _toasts.Count; i++)
        {
            var t = _toasts[i];
            if (t.Phase != ToastPhase.In && t.Phase != ToastPhase.Hold) continue;
            newestActive = t;
            if (t.Key == key) existing = t;
        }

        if (existing != null)
        {
            if (existing == newestActive)
            {
                // 이미 맨 아래에 있으면 그 자리에서 갯수만 올린다(옮길 필요 없음).
                existing.Bump(count);
            }
            else
            {
                // 위에 떨어져 있던 같은 종류는 사라지게 하고, 누적 갯수로 맨 아래에 새로 띄운다.
                //   예) [페트병][쓰레기봉투]에서 페트병을 또 주우면
                //       위 페트병은 사라지고 맨 아래에 "페트병 ×2"가 새로 뜬다.
                // 이미 발견된 광물이므로 '새 광물!' 뱃지는 붙이지 않는다(isNew=false).
                int total = existing.Count + count;
                existing.BeginExit(fast: true);   // 밀려나는 예전 줄은 더 빨리 사라진다(자연스럽게)
                Spawn(key, icon, displayName, total, accent, false);
            }
            return;
        }

        Spawn(key, icon, displayName, count, accent, isNew);
    }

    // 맨 아래에 새 토스트를 띄운다. 화면이 꽉 차 있으면 가장 오래된 활성 줄부터 퇴장시킨다.
    private void Spawn(string key, Sprite icon, string displayName, int count, Color accent, bool isNew)
    {
        int alive = 0;
        for (int i = 0; i < _toasts.Count; i++)
            if (_toasts[i].Phase == ToastPhase.In || _toasts[i].Phase == ToastPhase.Hold) alive++;
        if (alive >= MaxVisible)
        {
            for (int i = 0; i < _toasts.Count; i++)
                if (_toasts[i].Phase == ToastPhase.In || _toasts[i].Phase == ToastPhase.Hold)
                { _toasts[i].BeginExit(); break; }
        }

        var toast = Toast.Create(_stack, icon, displayName, count, accent, RowHeight, PanelSprite(), isNew);
        toast.Key = key;
        _toasts.Add(toast);
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            var t = _toasts[i];
            t.Tick(dt);
            if (t.Phase == ToastPhase.Dead)
            {
                if (t.Root != null) Destroy(t.Root.gameObject);
                _toasts.RemoveAt(i);
            }
        }
    }

    // ── 배경 스프라이트 (Stock_UISheet_8 한 장만, 정적 캐시) ──
    private static Sprite _panelSprite;
    private static bool _panelLoaded;

    private static Sprite PanelSprite()
    {
        if (_panelLoaded) return _panelSprite;
        _panelLoaded = true;

        var all = Resources.LoadAll<Sprite>(PanelSheetResource);
        if (all != null)
            foreach (var s in all)
                if (s != null && s.name == PanelSpriteName) { _panelSprite = s; break; }

        if (_panelSprite == null)
            Debug.LogWarning($"[AcquisitionNotifier] '{PanelSpriteName}' 스프라이트를 " +
                             $"Resources/{PanelSheetResource}에서 찾지 못했습니다. 코드 생성 배경으로 폴백합니다.");
        return _panelSprite;
    }

    // ================================================================
    //  한 줄(토스트) — MonoBehaviour 없이 노티파이어 Update가 구동한다.
    // ================================================================
    private enum ToastPhase { In, Hold, Out, Dead }

    private class Toast
    {
        public string Key;
        public ToastPhase Phase = ToastPhase.In;
        public RectTransform Root;
        public int Count => _count;

        private CanvasGroup _cg;
        private LayoutElement _le;
        private TextMeshProUGUI _text;
        private RectTransform _labelRt;  // 갯수 펀치 대상(레이아웃 영향 없음 — 스케일만)
        private string _name;
        private Color _accent;
        private int _count;
        private float _fullHeight;

        private float _t;        // 현재 페이즈 경과
        private float _hold;     // 남은 유지 시간
        private float _pop;      // 남은 펀치 시간
        private float _outDur = FadeOutDur;  // 이 줄의 퇴장 시간(교체로 밀려나면 더 짧게)

        public static Toast Create(Transform parent, Sprite icon, string name, int count, Color accent,
            float height, Sprite panelSprite, bool isNew = false)
        {
            var toast = new Toast { _name = name, _count = count, _accent = accent, _fullHeight = height };

            // 줄 루트: 가로 배치 + 폭 자동, 높이는 LayoutElement로 컨트롤.
            var root = CodeUI.CreateRect(parent, "Toast");
            toast.Root = root;

            // ★ 핵심: 피벗을 왼쪽(x=0)에 둔다.
            //   세로 레이아웃 그룹은 앵커·위치·크기만 제어하고 피벗은 안 건드린다.
            //   피벗이 중앙(0.5)이면 ContentSizeFitter가 폭을 늘릴 때 중심 기준으로 커져
            //   왼쪽으로도 번진다 → 왼쪽 가장자리를 앵커에 못박고 오른쪽으로만 늘리려면 피벗 x=0.
            root.pivot = new Vector2(0f, 0.5f);

            var hlg = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(16, 18, 8, 8);
            hlg.spacing = 12f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;   // Content 행에 확정 높이를 준다(안 그러면 0으로 접힘)
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            var fit = root.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            toast._le = root.gameObject.AddComponent<LayoutElement>();
            toast._le.preferredHeight = 0f;   // 등장할 때 0 → full 로 자라 오른다
            toast._le.minHeight = 0f;

            toast._cg = root.gameObject.AddComponent<CanvasGroup>();
            toast._cg.alpha = 0f;
            toast._cg.blocksRaycasts = false;
            toast._cg.interactable = false;

            // 배경 패널 — 레이아웃에서 빼고 루트에 꽉 채운다. Stock_UISheet_8 한 장만 사용.
            var bg = CodeUI.CreateImage(root, "Bg", Color.white);
            if (panelSprite != null)
            {
                bg.sprite = panelSprite;
                bg.type = (panelSprite.border.sqrMagnitude > 0.01f) ? Image.Type.Sliced : Image.Type.Simple;
                bg.pixelsPerUnitMultiplier = PanelPPU;
                bg.color = PanelColor;  // 스프라이트에 남색 틴트 + 반투명
            }
            else
            {
                // 스프라이트 로드 실패 시 코드 생성 라운드 알약으로 폴백(같은 색).
                CodeUI.ApplySkin(bg, PanelColor, null, null, rounded: true);
            }
            IgnoreAndStretch(bg.rectTransform, Vector2.zero, Vector2.zero);
            bg.rectTransform.SetAsFirstSibling();

            // 아이콘 + 텍스트 행.
            var contentRow = CodeUI.CreateRow(root, "Content", 0f, 12f, TextAnchor.MiddleLeft);

            // 첫 발견이면 아이콘 앞에 금색 '새 광물!' 뱃지를 붙인다.
            if (isNew)
            {
                var badge = CodeUI.CreateImage(contentRow, "NewBadge", CodeUI.GoldColor);
                badge.sprite = CodeUI.Rounded(24, 0.5f);
                badge.type = Image.Type.Sliced;
                var ble = badge.gameObject.AddComponent<LayoutElement>();
                ble.preferredWidth = ble.minWidth = 84f;
                ble.preferredHeight = ble.minHeight = 32f;

                var btxt = CodeUI.CreateText(badge.transform, "Label", 16f, FontStyles.Bold,
                    CodeUI.GoldFg, TextAlignmentOptions.Center);
                CodeUI.StretchFull(btxt.rectTransform);
                btxt.text = CodeUI.L("ui_codex_new_badge", "새 광물!");
                badge.transform.SetAsFirstSibling();
            }

            if (icon != null)
            {
                var iconImg = CodeUI.CreateImage(contentRow, "Icon", Color.white, rounded: false);
                iconImg.sprite = icon;
                iconImg.type = Image.Type.Simple;
                iconImg.preserveAspect = true;
                var ile = iconImg.gameObject.AddComponent<LayoutElement>();
                ile.preferredWidth = ile.preferredHeight = 40f;
            }
            else
            {
                // 아이콘이 없으면 카테고리 색 점으로 대체.
                var dot = CodeUI.CreateImage(contentRow, "IconDot", accent);
                dot.sprite = CodeUI.Rounded(32, 0.5f);
                var dle = dot.gameObject.AddComponent<LayoutElement>();
                dle.preferredWidth = dle.preferredHeight = 34f;
            }

            // 텍스트: "이름  ×N".
            toast._text = CodeUI.CreateText(contentRow, "Label", 26f, FontStyles.Bold,
                NameColor, TextAlignmentOptions.MidlineLeft);
            toast._text.richText = true;
            toast._labelRt = toast._text.rectTransform;
            toast.RefreshText();

            return toast;
        }

        public void Bump(int add)
        {
            _count += add;
            RefreshText();
            _hold = HoldDur;
            _pop = PopDur;
            if (Phase == ToastPhase.Out)
            {
                // 퇴장 중이었으면 되살린다.
                Phase = ToastPhase.Hold;
                if (_le != null) _le.preferredHeight = _fullHeight;
                if (_cg != null) _cg.alpha = 1f;
            }
        }

        public void BeginExit(bool fast = false)
        {
            if (Phase == ToastPhase.Out || Phase == ToastPhase.Dead) return;
            Phase = ToastPhase.Out;
            _outDur = fast ? FadeOutFastDur : FadeOutDur;
            _t = 0f;
        }

        public void Tick(float dt)
        {
            switch (Phase)
            {
                case ToastPhase.In:
                {
                    _t += dt;
                    float p = FadeInDur > 0f ? Mathf.Clamp01(_t / FadeInDur) : 1f;
                    if (_cg != null) _cg.alpha = p;
                    if (_le != null) _le.preferredHeight = Mathf.Lerp(0f, _fullHeight, EaseOut(p));
                    if (p >= 1f) { Phase = ToastPhase.Hold; _hold = HoldDur; }
                    break;
                }
                case ToastPhase.Hold:
                {
                    if (_cg != null) _cg.alpha = 1f;
                    if (_le != null) _le.preferredHeight = _fullHeight;
                    _hold -= dt;
                    if (_hold <= 0f) BeginExit();
                    break;
                }
                case ToastPhase.Out:
                {
                    // 접지 않고(높이 유지) 그 자리에서 알파만 빠진다. 사라지면 리스트에서 제거된다.
                    _t += dt;
                    float p = _outDur > 0f ? Mathf.Clamp01(_t / _outDur) : 1f;
                    if (_cg != null) _cg.alpha = 1f - p;
                    if (_le != null) _le.preferredHeight = _fullHeight;
                    if (p >= 1f) Phase = ToastPhase.Dead;
                    break;
                }
            }

            // 갯수 펀치: 숫자 라벨만 자기 중심에서 톡 튄다 → 줄의 왼쪽 가장자리는 안 움직인다.
            if (_labelRt != null)
            {
                float s = 1f;
                if (_pop > 0f)
                {
                    _pop -= dt;
                    s = 1f + 0.18f * Mathf.Clamp01(_pop / PopDur);
                }
                _labelRt.localScale = new Vector3(s, s, 1f);
            }
        }

        private void RefreshText()
        {
            if (_text == null) return;
            string hex = ColorUtility.ToHtmlStringRGB(_accent);
            _text.text = _count > 1
                ? $"{_name}  <color=#{hex}>×{_count}</color>"
                : _name;
        }

        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x);
    }

    private static void IgnoreAndStretch(RectTransform rt, Vector2 offMin, Vector2 offMax)
    {
        var le = rt.gameObject.GetComponent<LayoutElement>() ?? rt.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offMin;
        rt.offsetMax = offMax;
    }
}
