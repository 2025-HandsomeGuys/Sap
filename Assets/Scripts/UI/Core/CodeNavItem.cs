// @tags: ui, navigation, keyboard, wasd, focus, button, code-generated

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// <see cref="CodeSlotNavigator"/>가 WASD로 돌아다닐 수 있는 항목.
/// 슬롯(<see cref="CodeSlotView"/>)과 버튼(<see cref="CodeNavButton"/>)이 이 인터페이스로 섞여
/// 하나의 판 위에서 좌표 기준으로 이동한다.
/// </summary>
public interface ICodeNavItem
{
    /// <summary>이동 계산에 쓰는 사각형(중심 좌표를 뽑는다).</summary>
    RectTransform NavRect { get; }

    /// <summary>지금 화면에 살아 있고 조작 가능한지. 꺼진 항목은 후보에서 빠진다.</summary>
    bool NavUsable { get; }

    /// <summary>키보드 커서 표시 on/off.</summary>
    void SetNavFocus(bool on);

    /// <summary>스페이스바 실행.</summary>
    void NavActivate();
}

/// <summary>내비게이션 공용 훅.</summary>
public static class CodeNav
{
    /// <summary>
    /// 마우스가 항목 위에 올라왔을 때. <see cref="CodeSlotNavigator"/>가 구독해
    /// 키보드 커서를 마우스 쪽으로 옮긴다 — 두 입력이 커서 하나를 공유한다.
    /// </summary>
    public static System.Action<ICodeNavItem> HoverEntered;
}

/// <summary>
/// 코드 생성 버튼(탭·정렬·가방 비우기 등)을 WASD 이동 대상으로 만든다.
///
/// 버튼 자체 색은 건드리지 않는다 — 탭은 선택/비선택 색을 매 갱신마다 다시 칠하므로
/// 색을 바꾸면 서로 싸운다. 대신 **테두리 링을 겹쳐** 켜고 끈다.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class CodeNavButton : MonoBehaviour, ICodeNavItem, IPointerEnterHandler
{
    private static readonly Color RingColor = new Color(0.62f, 0.89f, 0.96f, 1f); // 설정 화면 포커스 테두리와 같은 톤
    private const float DefaultPad = 3f; // 스킨이 없을 때의 테두리 여백

    private Button _button;
    private Image _ring;

    public RectTransform NavRect => (RectTransform)transform;

    public bool NavUsable =>
        this != null && gameObject.activeInHierarchy && (_button == null || _button.interactable);

    public void SetNavFocus(bool on)
    {
        if (_ring != null) _ring.gameObject.SetActive(on);
    }

    public void NavActivate()
    {
        if (_button != null && _button.interactable) _button.onClick.Invoke();
    }

    public void OnPointerEnter(PointerEventData eventData) => CodeNav.HoverEntered?.Invoke(this);

    /// <summary>
    /// 버튼에 키보드 커서 대상을 붙인다(이미 붙어 있으면 그대로 반환).
    /// </summary>
    /// <param name="skin">
    /// 오버레이의 도트 스킨. <see cref="UISkin.focusSprite"/>가 지정돼 있으면 코드 생성 테두리 대신
    /// 그 스프라이트를 <see cref="UISkin.focusSpriteAlpha"/> 투명도로 라벨 뒤에 깐다
    /// (SettingsOverlayUI의 선택 강조와 같은 규칙). 비우면 기존 테두리 링 그대로.
    /// </param>
    /// <param name="pad">테두리를 버튼보다 얼마나 크게 그릴지 (px). 생략하면 스킨의 focusPadding</param>
    public static CodeNavButton Attach(Button button, UISkin skin = null, float pad = float.NaN)
    {
        if (button == null) return null;

        var nav = button.GetComponent<CodeNavButton>();
        if (nav != null) return nav;

        nav = button.gameObject.AddComponent<CodeNavButton>();
        nav._button = button;

        Sprite focus = skin != null ? skin.focusSprite : null;
        Vector2 padding = float.IsNaN(pad)
            ? (skin != null ? skin.focusPadding : new Vector2(DefaultPad, DefaultPad))
            : new Vector2(pad, pad);

        var ring = CodeUI.CreateImage(button.transform, "NavFocusRing", RingColor, focus, skin, rounded: false);
        if (focus == null)
        {
            ring.sprite = CodeUI.Outline();
            ring.type = Image.Type.Sliced;
        }
        else
        {
            // 꽉 찬 스프라이트를 넣어도 라벨이 비쳐 보이도록 알파를 자동으로 입힌다.
            var c = ring.color;
            c.a = skin.focusSpriteAlpha;
            ring.color = c;
        }
        ring.raycastTarget = false;

        var rt = ring.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-padding.x, -padding.y);
        rt.offsetMax = new Vector2(padding.x, padding.y);
        // 채워진 스프라이트는 라벨 '뒤'에 깔아야 글자가 덮이지 않는다.
        // 코드 생성 테두리는 속이 비어 있으므로 맨 위에 둔다(다른 요소에 가리지 않게).
        if (focus != null) rt.SetAsFirstSibling();
        else rt.SetAsLastSibling();

        // 버튼이 레이아웃 그룹 안에 있어도 테두리가 자리를 차지하면 안 된다
        ring.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        ring.gameObject.SetActive(false);

        nav._ring = ring;
        return nav;
    }
}
