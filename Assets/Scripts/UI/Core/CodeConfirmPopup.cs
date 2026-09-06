// @tags: ui, confirm, popup, dialog, code-generated, shared

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 코드 생성 오버레이가 함께 쓰는 확인/취소 팝업.
/// 프리팹 세팅 없이 <see cref="Create"/>로 만들고 <see cref="Show"/>로 띄운다.
/// (지하 인벤토리·창고 오버레이의 '버리기' 확인이 이걸 쓴다)
/// </summary>
public class CodeConfirmPopup
{
    private readonly GameObject _root;
    private readonly TextMeshProUGUI _title, _message;
    private readonly TextMeshProUGUI _confirmLabel;
    private System.Action _action;

    // 소유 오버레이의 CodeSlotNavigator가 A/D로 확인↔취소를 오갈 수 있게 노출한다.
    private readonly List<ICodeNavItem> _nav = new List<ICodeNavItem>();

    public bool IsOpen => _root != null && _root.activeSelf;

    /// <summary>
    /// 팝업이 떠 있을 때만 확인·취소 버튼을 커서 후보로 넘긴다.
    /// 소유자는 팝업이 열려 있으면 <b>이것만</b> 넘겨야 한다 — 뒤에 깔린 목록까지 같이 넘기면
    /// 커서가 팝업 밖으로 새어 나간다.
    /// </summary>
    public void CollectNavItems(List<ICodeNavItem> into)
    {
        if (into == null || !IsOpen) return;
        foreach (var item in _nav)
            if (item != null && item.NavUsable) into.Add(item);
    }

    /// <param name="confirmKey">확인 버튼 로컬라이즈 키 / 폴백 (예: "버리기")</param>
    public CodeConfirmPopup(Transform canvas, UISkin skin, LocTextBinder loc,
        string confirmKey, string confirmFallback, System.Action onClickSfx = null)
    {
        _root = new GameObject("ConfirmPopup", typeof(RectTransform));
        _root.transform.SetParent(canvas, false);
        CodeUI.StretchFull((RectTransform)_root.transform);

        var backdrop = CodeUI.CreateImage(_root.transform, "Backdrop", new Color(0f, 0f, 0f, 0.6f), rounded: false);
        CodeUI.StretchFull(backdrop.rectTransform);
        var backdropBtn = backdrop.gameObject.AddComponent<Button>();
        backdropBtn.transition = Selectable.Transition.None;
        backdropBtn.onClick.AddListener(Hide);

        var panel = CodeUI.CreateImage(_root.transform, "Panel", CodeUI.PanelBg, skin?.panelSprite, skin);
        var rt = panel.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(600f, 0f);

        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 24, 24);
        layout.spacing = 14f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _title = CodeUI.CreateText(panel.transform, "Title", 25f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center, loc);
        _title.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        _message = CodeUI.CreateText(panel.transform, "Message", 19f, FontStyles.Normal, CodeUI.LabelColor,
            TextAlignmentOptions.Center, loc);
        _message.textWrappingMode = TextWrappingModes.Normal;
        _message.gameObject.AddComponent<LayoutElement>().preferredHeight = 76f;

        // 확인(왼쪽) / 취소(오른쪽) — 다른 오버레이와 같은 순서
        var buttons = CodeUI.CreateRow(panel.transform, "Buttons", 54f, 12f, TextAnchor.MiddleCenter);

        var ok = CodeUI.CreateTextButton(buttons, "Confirm", CodeUI.NegativeColor, Color.white, 20f,
            ConfirmYes, out _confirmLabel, skin?.buttonSprite, skin, loc);
        var okLe = ok.gameObject.AddComponent<LayoutElement>();
        okLe.flexibleWidth = 1f;
        okLe.preferredHeight = 50f;
        loc?.Bind(_confirmLabel, confirmKey, confirmFallback);
        var okNav = CodeNavButton.Attach(ok, skin);
        if (okNav != null) _nav.Add(okNav);

        var cancel = CodeUI.CreateTextButton(buttons, "Cancel", CodeUI.NeutralBg, Color.white, 20f,
            () => { onClickSfx?.Invoke(); Hide(); }, out var cancelLabel, skin?.buttonSprite, skin, loc);
        var cancelLe = cancel.gameObject.AddComponent<LayoutElement>();
        cancelLe.flexibleWidth = 1f;
        cancelLe.preferredHeight = 50f;
        loc?.Bind(cancelLabel, "ui_pause_cancel", "취소");
        var cancelNav = CodeNavButton.Attach(cancel, skin);
        if (cancelNav != null) _nav.Add(cancelNav);

        _root.SetActive(false);
    }

    public void Show(string title, string message, System.Action onYes)
    {
        if (_root == null) return;
        _title.text = title;
        _message.text = message;
        _action = onYes;
        _root.transform.SetAsLastSibling();
        _root.SetActive(true);
    }

    public void Hide()
    {
        if (_root != null) _root.SetActive(false);
        _action = null;
    }

    /// <summary>키보드(Enter) 등으로 확인을 대신 눌러 준다.</summary>
    public void ConfirmNow() => ConfirmYes();

    private void ConfirmYes()
    {
        var action = _action;
        Hide();
        action?.Invoke();
    }
}
