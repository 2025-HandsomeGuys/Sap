// @tags: ui, code-generated, kit, skin, sprite, 9-slice, panel, button, scroll, blur, localization

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 인스펙터에서 지정하는 도트 스프라이트 묶음 (SettingsOverlayUI와 동일한 규칙).
/// 비워둔 항목은 코드 생성 라운드 스프라이트로 자동 폴백되므로 원하는 것만 넣으면 된다.
/// </summary>
[System.Serializable]
public class UISkin
{
    [Tooltip("메인 패널 배경 — 오버레이 최상위 프레임")]
    public Sprite panelSprite;
    [Tooltip("카드/섹션 배경 — 패널 안쪽 구획(창고 칸, 가방 칸, 구매/판매 칸)")]
    public Sprite cardSprite;
    [Tooltip("슬롯 한 칸 배경")]
    public Sprite slotSprite;
    [Tooltip("슬롯 강조 — 마우스 호버·드롭 대상일 때. 비우면 색만 밝아진다")]
    public Sprite slotHighlightSprite;
    [Tooltip("일반 버튼 — 정렬·닫기·구매 등")]
    public Sprite buttonSprite;
    [Tooltip("탭 버튼 (비선택 상태)")]
    public Sprite tabSprite;
    [Tooltip("탭 버튼 (선택 상태). 비우면 tabSprite에 밝기만 다르게 적용")]
    public Sprite tabSelectedSprite;
    [Tooltip("값 상자 — 골드·무게 등 수치 표시 상자")]
    public Sprite boxSprite;
    [Tooltip("선택 강조 — 키보드(WASD)/마우스 포커스가 올라간 버튼을 감싸는 이미지. " +
             "흰색 꽉 찬 스프라이트를 넣어도 된다: focusSpriteAlpha만큼 자동으로 투명해지고 버튼 내용 뒤에 깔린다. " +
             "비우면 코드 생성 테두리 링. (SettingsOverlayUI의 focusSprite와 같은 규칙)")]
    public Sprite focusSprite;
    [Tooltip("선택 강조 스프라이트의 불투명도. 1 = 불투명(글자가 가려짐), 0.25~0.4 권장")]
    [Range(0f, 1f)] public float focusSpriteAlpha = 0.3f;
    [Tooltip("선택 강조를 버튼보다 얼마나 키울지 (x=좌우, y=위아래, px). 음수면 버튼 안쪽에 그린다")]
    public Vector2 focusPadding = new Vector2(3f, 3f);

    [Tooltip("9-슬라이스 픽셀 배율. 값이 클수록 테두리(모서리)가 작게 렌더된다")]
    [Range(0.25f, 8f)] public float spritePixelsPerUnit = 1f;

    [Tooltip("체크 시 스프라이트에 팔레트 색을 곱한다(흰색 9-슬라이스용). 이미 색이 입혀진 도트 아트면 해제")]
    public bool tintSprites = true;
}

/// <summary>
/// 코드로 UI를 조립할 때 쓰는 저수준 헬퍼 모음.
/// SettingsOverlayUI가 자체적으로 갖고 있던 생성 로직을 공용화한 것으로,
/// 팔레트·라운드 스프라이트·9-슬라이스 적용 규칙을 세 오버레이가 공유해 한 벌로 보이게 한다.
/// </summary>
public static class CodeUI
{
    // ===================================================
    // 팔레트 (SettingsOverlayUI와 동일 계열 다크 네이비)
    // ===================================================
    public static readonly Color PanelBg = Hex(0x111A2E);
    public static readonly Color CardBg = Hex(0x18223C);
    public static readonly Color BoxBg = Hex(0x0B1224);
    public static readonly Color SlotBg = Hex(0x0E1628);
    public static readonly Color SlotHover = Hex(0x243257);
    public static readonly Color LabelColor = Hex(0xC7CFE2);
    public static readonly Color MutedColor = Hex(0x7B86A0);
    public static readonly Color DividerColor = Hex(0x2A3452);
    public static readonly Color AccentFill = Hex(0x7B8FD4);
    public static readonly Color TabIdleBg = Hex(0x232D4A);
    public static readonly Color GoldColor = Hex(0xF5C63F);
    public static readonly Color GoldFg = Hex(0x33270B);
    public static readonly Color NeutralBg = Hex(0x3A4668);
    public static readonly Color PositiveColor = Hex(0x58C275);
    public static readonly Color NegativeColor = Hex(0xE06C6C);
    public static readonly Color WarnColor = Hex(0xF09A3E);
    public static readonly Color ScrollHandle = Hex(0x3A4668);

    // 카테고리 색 (창고 탭 다이아몬드·슬롯 테두리 톤)
    public static readonly Color MineralColor = Hex(0x5FC7D8);
    public static readonly Color ItemColor = Hex(0x9BD35F);
    public static readonly Color EquipColor = Hex(0xD8A05F);
    public static readonly Color RelicColor = Hex(0xD87FC0);
    public static readonly Color QuestColor = Hex(0xB98FD4);

    public static Color Hex(int rgb) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

    // ===================================================
    // 기본 생성 헬퍼
    // ===================================================
    public static RectTransform CreateRect(Transform parent, string name)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        return (RectTransform)obj.transform;
    }

    /// <summary>
    /// Image 생성. skin이 지정되면 그 스프라이트를 직접 적용(표준 9-슬라이스)하고,
    /// 없으면 코드 생성 라운드 스프라이트로 폴백한다.
    /// </summary>
    public static Image CreateImage(Transform parent, string name, Color color,
        Sprite skin = null, UISkin opt = null, bool rounded = true)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var img = obj.AddComponent<Image>();
        img.color = color;

        ApplySkin(img, color, skin, opt, rounded);
        return img;
    }

    /// <summary>기존 Image에 스킨 규칙을 적용(슬롯 호버 전환처럼 런타임 교체에도 사용).</summary>
    public static void ApplySkin(Image img, Color color, Sprite skin, UISkin opt, bool rounded = true)
    {
        if (img == null) return;

        if (skin != null)
        {
            img.sprite = skin;
            img.type = (skin.border.sqrMagnitude > 0.01f) ? Image.Type.Sliced : Image.Type.Simple;
            img.pixelsPerUnitMultiplier = Mathf.Max(0.01f, opt != null ? opt.spritePixelsPerUnit : 1f);
            img.color = (opt == null || opt.tintSprites) ? color : Color.white;
            return;
        }

        img.color = color;
        if (rounded)
        {
            img.sprite = Rounded();
            img.type = Image.Type.Sliced;
        }
        else
        {
            img.sprite = null;
            img.type = Image.Type.Simple;
        }
    }

    public static Button CreateButton(Transform parent, string name, Color color, System.Action onClick,
        Sprite skin = null, UISkin opt = null, bool rounded = true)
    {
        var img = CreateImage(parent, name, color, skin, opt, rounded);
        var btn = img.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        btn.colors = colors;
        if (onClick != null) btn.onClick.AddListener(() => onClick());
        return btn;
    }

    /// <summary>라벨이 가운데 붙은 버튼. 반환값은 버튼, out으로 텍스트를 받는다.</summary>
    public static Button CreateTextButton(Transform parent, string name, Color bg, Color fg, float fontSize,
        System.Action onClick, out TextMeshProUGUI label,
        Sprite skin = null, UISkin opt = null, LocTextBinder binder = null)
    {
        var btn = CreateButton(parent, name, bg, onClick, skin, opt);
        label = CreateText(btn.transform, "Text", fontSize, FontStyles.Bold, fg, TextAlignmentOptions.Center, binder);
        StretchFull(label.rectTransform);
        return btn;
    }

    public static TextMeshProUGUI CreateText(Transform parent, string name, float fontSize, FontStyles style,
        Color color, TextAlignmentOptions align, LocTextBinder binder = null)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;

        var font = LanguageManager.Instance != null ? LanguageManager.Instance.GetCurrentFont() : null;
        if (font != null) tmp.font = font;

        binder?.Track(tmp);
        return tmp;
    }

    public static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>가로 줄 컨테이너 (HorizontalLayoutGroup + LayoutElement 높이).</summary>
    public static RectTransform CreateRow(Transform parent, string name, float height, float spacing = 10f,
        TextAnchor align = TextAnchor.MiddleLeft, RectOffset padding = null)
    {
        var rt = CreateRect(parent, name);
        var layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = align;
        if (height > 0f) rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return rt;
    }

    /// <summary>세로 컨테이너 (VerticalLayoutGroup).</summary>
    public static RectTransform CreateColumn(Transform parent, string name, float spacing = 10f,
        RectOffset padding = null, bool expandWidth = true)
    {
        var rt = CreateRect(parent, name);
        var layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = padding ?? new RectOffset(0, 0, 0, 0);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = expandWidth;
        layout.childForceExpandHeight = false;
        return rt;
    }

    /// <summary>여백 채우기용 투명 스페이서 (flexible).</summary>
    public static LayoutElement CreateSpacer(Transform parent, float flexibleWidth = 1f, float flexibleHeight = 0f)
    {
        var rt = CreateRect(parent, "Spacer");
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth = flexibleWidth;
        le.flexibleHeight = flexibleHeight;
        return le;
    }

    public static Image CreateDivider(Transform parent, float height = 2f)
    {
        var img = CreateImage(parent, "Divider", DividerColor, rounded: false);
        img.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return img;
    }

    // ===================================================
    // 스크롤 뷰
    // ===================================================
    /// <summary>
    /// 스크롤 뷰 골격 생성. 반환된 content에 GridLayoutGroup/VerticalLayoutGroup을 붙여 쓴다.
    /// content에는 ContentSizeFitter(PreferredSize)가 이미 달려 있다.
    /// </summary>
    public static ScrollRect CreateScrollView(Transform parent, string name, out RectTransform content,
        float scrollbarWidth = 8f)
    {
        var scrollRt = CreateRect(parent, name);
        var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 35f;

        var viewport = CreateRect(scrollRt, "Viewport");
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = new Vector2(-(scrollbarWidth + 6f), 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var viewportImg = viewport.gameObject.AddComponent<Image>(); // 빈 공간 드래그 스크롤용
        viewportImg.color = Color.clear;
        scroll.viewport = viewport;

        content = CreateRect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;

        scroll.verticalScrollbar = CreateScrollbar(scrollRt, scrollbarWidth);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return scroll;
    }

    public static Scrollbar CreateScrollbar(Transform parent, float width = 8f)
    {
        var rt = CreateRect(parent, "Scrollbar");
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(width, 0f);
        rt.anchoredPosition = Vector2.zero;

        var bg = rt.gameObject.AddComponent<Image>();
        bg.sprite = Rounded(4, 0.5f);
        bg.type = Image.Type.Sliced;
        bg.color = BoxBg;

        var slideArea = CreateRect(rt, "Sliding Area");
        slideArea.anchorMin = Vector2.zero;
        slideArea.anchorMax = Vector2.one;
        slideArea.offsetMin = Vector2.zero;
        slideArea.offsetMax = Vector2.zero;

        var handle = CreateImage(slideArea, "Handle", ScrollHandle);
        handle.sprite = Rounded(4, 0.5f);
        handle.rectTransform.offsetMin = Vector2.zero;
        handle.rectTransform.offsetMax = Vector2.zero;

        var sb = rt.gameObject.AddComponent<Scrollbar>();
        sb.handleRect = handle.rectTransform;
        sb.targetGraphic = handle;
        sb.direction = Scrollbar.Direction.BottomToTop;
        return sb;
    }

    // ===================================================
    // 공용 유틸
    // ===================================================
    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;

        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();

        // 이 프로젝트는 모든 씬이 InputSystemUIInputModule을 쓴다.
        // 여기서 StandaloneInputModule을 붙이면 프로젝트와 다른 입력 경로가 생겨
        // 클릭이 먹히지 않거나 이중 처리될 수 있다.
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    /// <summary>
    /// EventSystem의 선택 상태를 지운다.
    /// UI 확인 키를 스페이스바로 통일했는데, Space는 Input Manager/Input System 양쪽에서 Submit로도 잡힌다.
    /// 직전에 마우스로 누른 버튼이 선택된 채 남아 있으면 Space 한 번에
    /// (1) 오버레이가 폴링한 확인 + (2) 선택된 버튼의 onClick 재실행이 겹쳐 이중 처리된다.
    /// Space를 폴링하는 오버레이는 Update에서 이걸 호출해 선택을 비워 둔다.
    /// </summary>
    public static void ClearSelection()
    {
        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null)
            es.SetSelectedGameObject(null);
    }

    /// <summary>Localization 조회. 키가 없으면(키 그대로 돌아오면) 폴백 사용.</summary>
    public static string L(string key, string fallback)
    {
        var lm = LanguageManager.Instance;
        if (lm == null || string.IsNullOrEmpty(key)) return fallback;
        string s = lm.L(key);
        return (string.IsNullOrEmpty(s) || s == key) ? fallback : s;
    }

    /// <summary>
    /// SoundDataSO에 등록된 SFX만 재생(없으면 무음).
    /// 이름이 비었거나 아직 음원이 없는 키(SfxKeys.UiClick 등)면 공용 버튼음으로 폴백한다
    /// → 코드 생성 UI의 모든 버튼이 자동으로 소리를 낸다.
    /// </summary>
    public static void PlaySfx(string sfxName)
    {
        var sm = SoundManager.Instance;
        if (sm == null) return;

        // 이 프레임에 이미 ESC 뒤로가기음이 났다면, 뒤이어 나오는 '닫기 클릭음'은 중복이라 삼킨다.
        // (ESC → PlayBack() → Close() 안의 PlaySfx(clickSfxName) 순서로 두 소리가 겹치는 것을 막는다.
        //  전용음이 등록돼 있는 소리 — 거래·강화 등 — 는 클릭음이 아니므로 그대로 난다.)
        if (_backSfxFrame == Time.frameCount &&
            (string.IsNullOrEmpty(sfxName) || sfxName == SfxKeys.UiClick ||
             sfxName == SfxKeys.UiButton || !sm.HasSFX(sfxName)))
            return;

        if (!string.IsNullOrEmpty(sfxName) && sm.HasSFX(sfxName))
        {
            sm.PlaySFX(sfxName);
            return;
        }

        if (sm.HasSFX(SfxKeys.UiButton))
            sm.PlaySFX(SfxKeys.UiButton);
    }

    private static int _backSfxFrame = -1;

    /// <summary>
    /// ESC(뒤로가기·취소)로 UI를 닫을 때 부른다 — <see cref="SfxKeys.UiBack"/>이 난다.
    ///
    /// 같은 프레임에 여러 번 불려도 한 번만 난다. 오버레이 자신과 UIStateManager가
    /// 같은 ESC를 겹쳐 처리하는 경로가 있어서 필요하다.
    /// 호출 뒤 같은 프레임의 클릭음은 <see cref="PlaySfx"/>가 삼킨다 — 닫기 경로마다
    /// isEscape 플래그를 새로 뚫지 않아도 되도록 한 것.
    /// </summary>
    public static void PlayBack(string sfxName = null)
    {
        if (_backSfxFrame == Time.frameCount) return;
        PlaySfx(string.IsNullOrEmpty(sfxName) ? SfxKeys.UiBack : sfxName);
        _backSfxFrame = Time.frameCount;
    }

    /// <summary>1,234 형태로 골드 표기.</summary>
    public static string Gold(long amount) => amount.ToString("N0");

    // ===================================================
    // 소지금 표시 (동전 아이콘 + 숫자)
    // ===================================================
    /// <summary>
    /// 소지금 값 상자 — 화폐 이미지 + 숫자만 가로로 놓는다("G" 같은 접미사 없음).
    /// 숫자는 반환된 TMP에 <see cref="Gold"/>로 채운다. coinSprite가 null이면 코드 생성 동전(<see cref="CoinIcon"/>).
    /// </summary>
    public static TextMeshProUGUI CreateGoldBox(Transform parent, Sprite coinSprite, LocTextBinder binder,
        UISkin skin = null, float width = 190f, float height = 46f, float iconSize = 28f, float fontSize = 24f)
    {
        var box = CreateImage(parent, "GoldBox", BoxBg, skin != null ? skin.boxSprite : null, skin);
        var le = box.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = width;
        le.preferredHeight = height;

        var row = box.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(14, 16, 4, 4);
        row.spacing = 8f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        row.childAlignment = TextAnchor.MiddleCenter; // [동전][숫자] 묶음을 상자 가운데로

        var icon = CreateImage(box.transform, "CoinIcon", Color.white, rounded: false);
        icon.sprite = coinSprite != null ? coinSprite : CoinIcon();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        var iconLe = icon.gameObject.AddComponent<LayoutElement>();
        iconLe.preferredWidth = iconLe.minWidth = iconSize;
        iconLe.preferredHeight = iconLe.minHeight = iconSize;

        var text = CreateText(box.transform, "Gold", fontSize, FontStyles.Bold, GoldColor,
            TextAlignmentOptions.MidlineLeft, binder);
        return text;
    }

    // ── 코드 생성 금화 아이콘 (색이 구워진 원형 동전, 캐시) ──
    private static Sprite _coinIcon;

    /// <summary>인스펙터 화폐 스프라이트를 안 넣었을 때 쓰는 코드 생성 금화(테두리 링 + 각인 링 + 금색 면).</summary>
    public static Sprite CoinIcon()
    {
        if (_coinIcon != null) return _coinIcon;

        const int S = 64;
        float c = (S - 1) * 0.5f;
        float R = S * 0.47f;
        float innerR = R * 0.55f;

        Color rim = Hex(0x8A6A16);  // 바깥 테두리
        Color face = Hex(0xF5C63F); // 기본 금색 (GoldColor)
        Color hi = Hex(0xFFE38A);   // 좌상단 하이라이트
        Color mark = Hex(0xB98A1E); // 가운데 각인 링

        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = x - c, dy = y - c;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(R - dist + 0.5f); // 가장자리 1px 안티앨리어싱
                if (a <= 0f) { px[y * S + x] = new Color32(0, 0, 0, 0); continue; }

                Color col;
                if (dist > R * 0.82f)
                    col = rim;
                else if (Mathf.Abs(dist - innerR) < 1.6f)
                    col = mark;
                else
                {
                    float g = Mathf.Clamp01(0.55f + (-(dx + dy)) / (R * 2.6f));
                    col = Color.Lerp(face, hi, g * 0.55f);
                }
                col.a = a;
                px[y * S + x] = col;
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        _coinIcon = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return _coinIcon;
    }

    // ===================================================
    // 둥근 모서리 스프라이트 (코드 생성, 9-슬라이스, 크기별 캐시)
    // ===================================================
    private static readonly Dictionary<int, Sprite> _roundedSprites = new Dictionary<int, Sprite>();

    /// <summary>기본(큰 카드용): 64px, 모서리 반경 ≈18px.</summary>
    public static Sprite Rounded() => Rounded(64, 0.28f);

    /// <summary>radiusRatio 0.5 = 알약/원형(끝이 완전히 둥근 형태).</summary>
    public static Sprite Rounded(int size, float radiusRatio)
    {
        int key = size * 1000 + Mathf.RoundToInt(radiusRatio * 100f);
        if (_roundedSprites.TryGetValue(key, out var cached) && cached != null) return cached;

        float radius = Mathf.Max(size * radiusRatio - 0.5f, 1f);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 모서리 원 중심까지의 거리로 알파 결정 (1px 안티앨리어싱)
                float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
                float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(radius - dist + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        float border = Mathf.Min(Mathf.Ceil(radius) + 1f, size * 0.5f);
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        _roundedSprites[key] = sprite;
        return sprite;
    }

    // ── 라운드 사각 '테두리만' 스프라이트 (키보드 포커스 표시용, 9-슬라이스) ──
    private static Sprite _outlineSprite;

    /// <summary>
    /// 속이 빈 라운드 사각 테두리. 버튼 위에 겹쳐 두면 원래 색을 건드리지 않고
    /// '지금 선택된 버튼'만 표시할 수 있다(<see cref="CodeNavButton"/>이 사용).
    /// </summary>
    public static Sprite Outline()
    {
        if (_outlineSprite != null) return _outlineSprite;

        const int size = 48;
        const float radius = 11f;
        const float thickness = 2.5f;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 라운드 사각형의 부호 있는 거리(안쪽 음수) → 경계에서 thickness만큼만 칠한다
                float dx = Mathf.Max(radius - x, x - (size - 1 - radius), 0f);
                float dy = Mathf.Max(radius - y, y - (size - 1 - radius), 0f);
                float sd = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                float a = Mathf.Clamp01(0.5f - sd) * Mathf.Clamp01(sd + thickness + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);

        float border = Mathf.Ceil(radius) + 1f;
        _outlineSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        return _outlineSprite;
    }

    // ── 삼각형 화살표 스프라이트 (◀▶ 글리프 폰트 미지원 대체) ──
    private static readonly Dictionary<int, Sprite> _triangleSprites = new Dictionary<int, Sprite>();

    /// <summary>좌(dir&lt;0)/우(dir&gt;=0)를 가리키는 이등변 삼각형 스프라이트.</summary>
    public static Sprite Triangle(int dir)
    {
        int key = dir < 0 ? 0 : 1;
        if (_triangleSprites.TryGetValue(key, out var cached) && cached != null) return cached;

        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color32[S * S];
        const float baseX = 0.26f, tipX = 0.74f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S;
                float v = (y + 0.5f) / S;
                float fx = dir < 0 ? 1f - u : u;
                float a = 0f;
                if (fx >= baseX && fx <= tipX)
                {
                    float halfH = (tipX - fx) / (tipX - baseX) * 0.5f;
                    a = Mathf.Clamp01((halfH - Mathf.Abs(v - 0.5f)) * S);
                }
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        _triangleSprites[key] = sprite;
        return sprite;
    }
}

/// <summary>
/// 코드로 만든 텍스트들의 언어 갱신 담당.
/// Bind()로 등록한 텍스트는 언어 전환 시 문자열까지, Track()만 한 텍스트는 폰트만 갱신된다.
/// </summary>
public class LocTextBinder
{
    private class Entry { public TextMeshProUGUI tmp; public string key; public string fallback; }

    private readonly List<Entry> _loc = new List<Entry>();
    private readonly List<TextMeshProUGUI> _all = new List<TextMeshProUGUI>();

    /// <summary>폰트 갱신 대상으로만 등록 (문자열은 코드가 직접 채우는 동적 텍스트).</summary>
    public void Track(TextMeshProUGUI tmp)
    {
        if (tmp != null) _all.Add(tmp);
    }

    /// <summary>Localization 키에 묶어 등록. 즉시 현재 언어로 채운다.</summary>
    public void Bind(TextMeshProUGUI tmp, string key, string fallback)
    {
        if (tmp == null) return;
        _loc.Add(new Entry { tmp = tmp, key = key, fallback = fallback });
        tmp.text = CodeUI.L(key, fallback);
    }

    /// <summary>모든 텍스트의 폰트·문자열을 현재 언어로 갱신.</summary>
    public void Refresh()
    {
        var font = LanguageManager.Instance != null ? LanguageManager.Instance.GetCurrentFont() : null;

        for (int i = _all.Count - 1; i >= 0; i--)
        {
            var t = _all[i];
            if (t == null) { _all.RemoveAt(i); continue; }
            if (font != null) t.font = font;
        }

        for (int i = _loc.Count - 1; i >= 0; i--)
        {
            var e = _loc[i];
            if (e.tmp == null) { _loc.RemoveAt(i); continue; }
            e.tmp.text = CodeUI.L(e.key, e.fallback);
        }
    }
}

/// <summary>
/// 화면 캡처 → 다운샘플 블러 배경 (SettingsOverlayUI와 동일 방식).
/// 오버레이가 열릴 때 Capture, 닫힐 때 Release를 호출한다.
/// </summary>
public class ScreenBlur
{
    private RenderTexture _rt;

    /// <summary>현재 화면을 캡처해 target에 블러 텍스처로 물린다. WaitForEndOfFrame 이후에 호출할 것.</summary>
    public void Capture(RawImage target, int downsamples, bool flipVertically)
    {
        Release(target);
        if (target == null) return;

        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();

        int w = Mathf.Max(shot.width >> 1, 8);
        int h = Mathf.Max(shot.height >> 1, 8);
        RenderTexture cur = RenderTexture.GetTemporary(w, h, 0);
        cur.filterMode = FilterMode.Bilinear;
        Graphics.Blit(shot, cur);

        for (int i = 1; i < downsamples; i++)
        {
            w = Mathf.Max(w >> 1, 8);
            h = Mathf.Max(h >> 1, 8);
            var next = RenderTexture.GetTemporary(w, h, 0);
            next.filterMode = FilterMode.Bilinear;
            Graphics.Blit(cur, next);
            RenderTexture.ReleaseTemporary(cur);
            cur = next;
        }

        // 한 단계 업샘플을 거쳐 계단 현상 완화 (bilinear 왕복 = 저비용 블러)
        var up = RenderTexture.GetTemporary(w * 2, h * 2, 0);
        up.filterMode = FilterMode.Bilinear;
        Graphics.Blit(cur, up);
        RenderTexture.ReleaseTemporary(cur);

        _rt = up;
        target.texture = _rt;
        target.uvRect = flipVertically ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
        Object.Destroy(shot);
    }

    public void Release(RawImage target)
    {
        if (_rt == null) return;
        if (target != null) target.texture = null;
        RenderTexture.ReleaseTemporary(_rt);
        _rt = null;
    }
}
