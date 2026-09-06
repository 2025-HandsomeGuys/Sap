// @tags: ui, slot, drag, drop, click, tooltip, code-generated, inventory, warehouse, shop

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 코드 생성 UI용 아이템 슬롯 한 칸.
/// 프리팹 없이 <see cref="Create"/>로 만들고, 클릭·드래그·드롭은 콜백으로 소유자에게 넘긴다.
/// 기존 InventorySlotDragHandler(프리팹 슬롯 전용)와 독립적으로 동작한다.
///
/// zone: 소유자가 정의하는 구역 번호(창고/가방 아이템/가방 광물/가방 장비 등).
/// index: 그 구역 안에서의 슬롯 인덱스. 두 값으로 드롭 시 출발지·도착지를 구분한다.
/// </summary>
public class CodeSlotView : MonoBehaviour,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
    ITooltipProvider, ICodeNavItem
{
    // ── 식별 ──
    public int zone;
    public int index;

    /// <summary>이 슬롯이 현재 들고 있는 데이터(빈 슬롯이면 item == null).</summary>
    public InventorySlot Slot { get; private set; }

    public bool HasItem => Slot != null && Slot.item != null;

    /// <summary>BindCustom으로 채워진 칸인지 (유물처럼 인벤토리 아이템이 아닌 것).</summary>
    public bool HasCustom => Slot == null && _customTitle != null;

    /// <summary>무언가 들어 있는 칸인지 (아이템이든 커스텀이든).</summary>
    public bool HasContent => HasItem || HasCustom;

    // ── 콜백 (소유자가 채운다) ──
    public System.Action<CodeSlotView> onLeftClick;
    public System.Action<CodeSlotView> onRightClick;
    /// <summary>좌클릭 더블클릭. null이면 두 번째 클릭도 그냥 onLeftClick으로 처리된다(기존 동작).</summary>
    public System.Action<CodeSlotView> onDoubleClick;
    /// <summary>다른 슬롯이 이 슬롯 위에 떨어졌을 때. (도착지 = this, 출발지 = source)</summary>
    public System.Action<CodeSlotView, CodeSlotView> onDropReceived;
    /// <summary>드래그를 시작해도 되는지. null이면 아이템이 있을 때만 허용.</summary>
    public System.Func<CodeSlotView, bool> canDrag;

    // ── 내부 참조 ──
    private Image _bg;
    private Image _icon;
    private TextMeshProUGUI _quantity;
    private TextMeshProUGUI _placeholderLabel;
    private TextMeshProUGUI _levelBadge; // +N 강화 배지(좌상단). 필요할 때만 생성.
    private UISkin _skin;
    private Color _idleColor;
    private Sprite _placeholderSprite;
    private string _placeholderText = string.Empty;
    // 빈 칸 실루엣에 입힐 색. 흰 스프라이트를 카테고리 색(장비·아이템·유물)으로 물들인다.
    // 기본값은 예전과 같은 흐린 흰색 — 소유자가 색을 안 주면 티가 안 나게.
    private Color _placeholderTint = new Color(1f, 1f, 1f, 0.28f);
    private bool _hovered;
    // 키보드(WASD) 커서가 올라와 있는지 — 호버와 같은 강조를 쓴다(CodeSlotNavigator가 세팅).
    private bool _focused;

    // BindCustom으로 물린 데이터(유물 등). Slot이 null일 때만 유효.
    private string _customTitle, _customContent;
    private Sprite _customIcon;

    // 드래그할 수 없는 칸(빈 칸 등)에서 시작한 드래그는 부모 스크롤로 넘겨
    // 목록이 손가락을 따라 움직이게 한다. 안 그러면 격자 위에서 스크롤이 안 먹는다.
    private ScrollRect _parentScroll;
    private bool _forwardingToScroll;

    private void Awake() => _parentScroll = GetComponentInParent<ScrollRect>();

    // 드래그 고스트 (전역 1개 재사용)
    private static GameObject _ghost;
    private static Image _ghostIcon;
    private static TextMeshProUGUI _ghostQty;
    private static CodeSlotView _dragSource;

    /// <summary>현재 드래그 중인 슬롯 (없으면 null).</summary>
    public static CodeSlotView DragSource => _dragSource;

    // ===================================================
    // 키보드 커서 (ICodeNavItem)
    // ===================================================
    public RectTransform NavRect => (RectTransform)transform;
    public bool NavUsable => this != null && gameObject.activeInHierarchy;
    public void SetNavFocus(bool on) => SetFocused(on);

    /// <summary>스페이스바 = 좌클릭과 같은 동작(콜백이 없는 표시 전용 칸은 아무 일도 없다).</summary>
    public void NavActivate() => onLeftClick?.Invoke(this);

    // ===================================================
    // 생성
    // ===================================================
    /// <summary>슬롯 한 칸 생성. size는 정사각형 한 변 픽셀(레이아웃이 제어하면 무시해도 됨).</summary>
    public static CodeSlotView Create(Transform parent, UISkin skin, float size, string name = "Slot")
    {
        var bg = CodeUI.CreateImage(parent, name, CodeUI.SlotBg, skin?.slotSprite, skin);
        var rt = bg.rectTransform;
        if (size > 0f)
        {
            var le = bg.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            rt.sizeDelta = new Vector2(size, size);
        }

        var view = bg.gameObject.AddComponent<CodeSlotView>();
        view._bg = bg;
        view._skin = skin;
        view._idleColor = CodeUI.SlotBg;

        // 아이콘
        view._icon = CodeUI.CreateImage(bg.transform, "Icon", Color.white, rounded: false);
        var ic = view._icon.rectTransform;
        ic.anchorMin = Vector2.zero;
        ic.anchorMax = Vector2.one;
        ic.offsetMin = new Vector2(10f, 10f);
        ic.offsetMax = new Vector2(-10f, -10f);
        view._icon.preserveAspect = true;
        view._icon.raycastTarget = false;
        view._icon.enabled = false;

        // 수량 (우하단)
        view._quantity = CodeUI.CreateText(bg.transform, "Quantity", 17f, FontStyles.Bold,
            Color.white, TextAlignmentOptions.BottomRight);
        var qt = view._quantity.rectTransform;
        qt.anchorMin = Vector2.zero;
        qt.anchorMax = Vector2.one;
        qt.offsetMin = new Vector2(0f, 4f);
        qt.offsetMax = new Vector2(-7f, 0f);
        // 외곽선(outlineWidth)은 TMP가 텍스트마다 머티리얼 인스턴스를 만들어 배칭이 깨진다.
        // 슬롯 배경이 충분히 어두우므로 흰 볼드만으로 가독성이 확보된다.

        // 빈 슬롯 안내 문구 (장비 칸의 '머리/옷/신발/유물' 등)
        view._placeholderLabel = CodeUI.CreateText(bg.transform, "Placeholder", 14f, FontStyles.Normal,
            CodeUI.MutedColor, TextAlignmentOptions.Center);
        CodeUI.StretchFull(view._placeholderLabel.rectTransform);
        view._placeholderLabel.gameObject.SetActive(false);

        // 툴팁 (기존 TooltipManager 재사용 — CodeSlotView가 직접 제공자 역할)
        var trigger = bg.gameObject.AddComponent<TooltipTrigger>();
        trigger.tooltipProvider = view;

        return view;
    }

    // ===================================================
    // 바인딩
    // ===================================================
    /// <summary>슬롯 데이터를 물린다. null이거나 item이 null이면 빈 칸으로 표시.</summary>
    public void Bind(InventorySlot slot, Color? accentColor = null)
    {
        Slot = slot;
        _customTitle = _customContent = null;
        _customIcon = null;
        SetLevelBadge(0); // 풀 재사용 시 이전 배지 제거 — 필요하면 소유자가 다시 지정

        bool has = HasItem;
        if (has)
        {
            _icon.sprite = slot.item.Icon;
            _icon.enabled = (_icon.sprite != null);
            ApplyHeldAwareContent(); // 수량 텍스트·투명도 (손에 집은 원본이면 '남은 개수'로 표시)
            _placeholderLabel.gameObject.SetActive(false);
        }
        else
        {
            if (_placeholderSprite != null)
            {
                _icon.sprite = _placeholderSprite;
                _icon.enabled = true;
                _icon.color = _placeholderTint;
            }
            else
            {
                _icon.enabled = false;
            }
            _quantity.text = string.Empty;
            ShowPlaceholderLabel();
        }

        RefreshBackground();
    }

    /// <summary>
    /// InventorySlot이 아닌 임의 데이터를 표시용으로 물린다(유물처럼 인벤토리 아이템이 아닌 것).
    /// 드래그는 되지 않고, 툴팁은 여기 넘긴 title/content를 쓴다.
    /// 아이콘이 없는 항목은 이름을 글자로 대신 보여준다.
    /// </summary>
    public void BindCustom(Sprite icon, string title, string content, Color? accentColor, bool filled)
    {
        Slot = null;
        _customTitle = filled ? title : null;
        _customContent = filled ? content : null;
        _customIcon = filled ? icon : null;
        _quantity.text = string.Empty;
        SetLevelBadge(0); // 풀 재사용 시 이전 배지 제거 — 필요하면 소유자가 다시 지정

        if (filled)
        {
            _icon.sprite = icon;
            _icon.enabled = (icon != null);
            _icon.color = Color.white;

            // 아이콘 미지정 유물이 빈 칸처럼 보이지 않도록 이름을 띄운다
            _placeholderLabel.text = (icon == null) ? (title ?? string.Empty) : string.Empty;
            _placeholderLabel.color = CodeUI.LabelColor;
            _placeholderLabel.gameObject.SetActive(icon == null && !string.IsNullOrEmpty(title));
        }
        else
        {
            if (_placeholderSprite != null)
            {
                _icon.sprite = _placeholderSprite;
                _icon.enabled = true;
                _icon.color = _placeholderTint;
            }
            else
            {
                _icon.enabled = false;
            }
            ShowPlaceholderLabel();
        }

        RefreshBackground();
    }

    private void ShowPlaceholderLabel()
    {
        _placeholderLabel.text = _placeholderText;
        _placeholderLabel.color = CodeUI.MutedColor;
        _placeholderLabel.gameObject.SetActive(_placeholderSprite == null &&
                                               !string.IsNullOrEmpty(_placeholderText));
    }

    /// <summary>
    /// 빈 칸일 때 보여줄 실루엣/문구 (장비 슬롯의 부위 표시 등). 한 번만 설정하면 된다.
    /// tint를 주면 흰 실루엣을 그 색으로 물들인다(카테고리 색). 알파는 넘긴 색 그대로 쓴다.
    /// 비우면 예전과 같은 흐린 흰색.
    /// </summary>
    public void SetPlaceholder(Sprite sprite, string label, Color? tint = null)
    {
        _placeholderSprite = sprite;
        _placeholderText = label ?? string.Empty;
        _placeholderLabel.text = _placeholderText;
        _placeholderTint = tint ?? new Color(1f, 1f, 1f, 0.28f);
    }

    /// <summary>
    /// 강화 단계 배지(+N)를 좌상단에 표시한다. level &lt;= 0 이면 숨긴다.
    /// 장비 강화 UI와 같은 표기(금색 +N)를 창고·인벤토리 슬롯에서도 쓰기 위한 것.
    /// </summary>
    public void SetLevelBadge(int level)
    {
        if (level <= 0)
        {
            if (_levelBadge != null) _levelBadge.gameObject.SetActive(false);
            return;
        }

        if (_levelBadge == null)
        {
            // 장비 강화 화면(EquipmentUpgradeOverlayUI)의 배지와 동일한 스타일.
            _levelBadge = CodeUI.CreateText(_bg.transform, "LevelBadge", 20f, FontStyles.Bold,
                CodeUI.GoldColor, TextAlignmentOptions.TopLeft);
            var rt = _levelBadge.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(5f, 0f);
            rt.offsetMax = new Vector2(0f, -3f);
            _levelBadge.raycastTarget = false;
        }

        _levelBadge.text = $"+{level}";
        _levelBadge.gameObject.SetActive(true);
    }

    /// <summary>슬롯 기본 배경색 변경 (예: 잠긴 칸을 더 어둡게).</summary>
    public void SetIdleColor(Color color)
    {
        _idleColor = color;
        RefreshBackground();
    }

    /// <summary>
    /// 이 칸이 <see cref="CodeHeldItem"/>로 집어 든 원본이면 '남은 개수'(전체 − 손에 든 개수)로 그린다.
    /// 전부 손에 있으면(남은 0) 빈 것처럼 흐리게. 손에 집기 상태가 바뀔 때마다 컨트롤러가 호출한다.
    /// </summary>
    public void RefreshHeldDisplay()
    {
        if (HasItem) ApplyHeldAwareContent();
    }

    /// <summary>수량 텍스트·아이콘 투명도를 현재 슬롯 + 손에 집기 상태로 다시 그린다.</summary>
    private void ApplyHeldAwareContent()
    {
        if (!HasItem) return;

        int shown = Slot.quantity;
        bool heldHere = CodeHeldItem.IsSource(this);
        if (heldHere) shown = Mathf.Max(0, Slot.quantity - CodeHeldItem.HeldQuantity);

        _quantity.text = (Slot.item.Stackable && shown > 1) ? shown.ToString() : string.Empty;

        // 전부 손에 있으면(남은 0) 원본을 빈 칸처럼 흐리게, 그 외엔 정상.
        float a = (heldHere && shown == 0) ? 0.28f : 1f;
        _icon.color = new Color(1f, 1f, 1f, a);
        var qc = _quantity.color;
        _quantity.color = new Color(qc.r, qc.g, qc.b, a);
    }

    /// <summary>키보드 커서(WASD)가 이 칸에 있는지. 호버와 같은 강조를 쓴다.</summary>
    public void SetFocused(bool on)
    {
        if (_focused == on) return;
        _focused = on;
        RefreshBackground();
    }

    private void RefreshBackground()
    {
        // 드래그 중에도 포인터 enter/exit는 그대로 오므로, 드롭 대상 강조는 호버 강조가 겸한다.
        bool lit = _hovered || _focused;
        Sprite sprite = (lit && _skin?.slotHighlightSprite != null) ? _skin.slotHighlightSprite : _skin?.slotSprite;
        Color color = lit ? CodeUI.SlotHover : _idleColor;
        CodeUI.ApplySkin(_bg, color, sprite, _skin);
    }

    // ===================================================
    // 입력
    // ===================================================
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            // 두 번째 클릭이 오면 더블클릭 콜백을 먼저 부른다(첫 클릭은 이미 onLeftClick으로 처리됨).
            // 더블클릭 콜백이 없으면 평소처럼 좌클릭으로 취급한다.
            if (eventData.clickCount == 2 && onDoubleClick != null) onDoubleClick.Invoke(this);
            else onLeftClick?.Invoke(this);
        }
        else if (eventData.button == PointerEventData.InputButton.Right) onRightClick?.Invoke(this);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovered = true;
        RefreshBackground();
        CodeNav.HoverEntered?.Invoke(this); // 키보드 커서를 마우스 쪽으로 옮긴다
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovered = false;
        RefreshBackground();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // 손에 든 상태(CodeHeldItem)에서는 드래그를 시작하지 않는다 — 고스트가 겹친다. 클릭으로 내려놓게 한다.
        if (CodeHeldItem.Active) return;

        bool allowed = canDrag != null ? canDrag(this) : HasContent;
        if (!allowed)
        {
            // 빈 칸에서 시작한 드래그 = 목록 스크롤
            _forwardingToScroll = (_parentScroll != null);
            if (_forwardingToScroll) _parentScroll.OnBeginDrag(eventData);
            return;
        }

        _dragSource = this;
        ShowGhost(HasItem ? Slot.item.Icon : _customIcon,
                  (HasItem && Slot.item.Stackable) ? Slot.quantity : 0);
        MoveGhost(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_forwardingToScroll) { _parentScroll.OnDrag(eventData); return; }
        if (_dragSource != this) return;
        MoveGhost(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_forwardingToScroll)
        {
            _parentScroll.OnEndDrag(eventData);
            _forwardingToScroll = false;
            return;
        }
        if (_dragSource != this) return;
        _dragSource = null;
        HideGhost();
        RefreshBackground();
    }

    public void OnDrop(PointerEventData eventData)
    {
        var source = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<CodeSlotView>() : null;
        if (source == null || source == this) return;
        onDropReceived?.Invoke(this, source);
    }

    // ===================================================
    // 드래그 고스트
    // ===================================================
    private void ShowGhost(Sprite icon, int quantity)
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var root = canvas.rootCanvas;

        if (_ghost == null)
        {
            _ghost = new GameObject("DragGhost");
            var group = _ghost.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; // 아래 슬롯이 OnDrop을 받아야 한다
            group.alpha = 0.85f;
            _ghostIcon = _ghost.AddComponent<Image>();
            _ghostIcon.preserveAspect = true;
            _ghostIcon.raycastTarget = false;
            _ghostIcon.rectTransform.sizeDelta = new Vector2(64f, 64f);

            // 개수 표시 — 손에 집기(CodeHeldItem) 고스트와 같은 표기로 맞춘다.
            _ghostQty = new GameObject("Qty").AddComponent<TextMeshProUGUI>();
            _ghostQty.transform.SetParent(_ghost.transform, false);
            _ghostQty.fontSize = 22f;
            _ghostQty.fontStyle = FontStyles.Bold;
            _ghostQty.color = Color.white;
            _ghostQty.alignment = TextAlignmentOptions.BottomRight;
            _ghostQty.raycastTarget = false;
            var gq = _ghostQty.rectTransform;
            gq.anchorMin = Vector2.zero; gq.anchorMax = Vector2.one;
            gq.offsetMin = new Vector2(0f, 2f); gq.offsetMax = new Vector2(-4f, 0f);
        }

        // 고스트는 전역 1개를 재사용한다. 창고·지하 등 오버레이 캔버스가 서로 다르므로
        // 매 드래그마다 '지금 이 슬롯이 속한' 캔버스로 옮겨야 그 화면 위에서 마우스를 따라온다.
        // (안 그러면 처음 만들어진 캔버스가 닫혀 있어 다른 오버레이에서는 안 보인다)
        if (_ghost.transform.parent != root.transform)
            _ghost.transform.SetParent(root.transform, false);

        _ghost.transform.SetAsLastSibling();
        _ghost.SetActive(true);
        _ghostIcon.sprite = icon;
        _ghostIcon.enabled = (icon != null);
        if (_ghostQty != null) _ghostQty.text = (quantity > 1) ? quantity.ToString() : string.Empty;
    }

    private static void MoveGhost(PointerEventData eventData)
    {
        if (_ghost == null) return;
        var rt = (RectTransform)_ghost.transform;
        var parent = (RectTransform)rt.parent;
        var canvas = parent.GetComponentInParent<Canvas>();
        Camera cam = (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : eventData.pressEventCamera;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, cam, out var local))
            rt.localPosition = local;
    }

    private static void HideGhost()
    {
        if (_ghost != null) _ghost.SetActive(false);
    }

    private void OnDisable()
    {
        // 슬롯이 꺼지는 순간 드래그 중이었다면 잔여 고스트 정리
        if (_dragSource == this)
        {
            _dragSource = null;
            HideGhost();
        }
        _forwardingToScroll = false;
        _hovered = false;
        _focused = false; // 꺼진 칸이 켜질 때 옛 커서가 남아 있으면 안 된다(내비게이터가 다시 잡는다)
    }

    // ===================================================
    // 툴팁 (ITooltipProvider)
    // ===================================================
    public string GetTooltipTitle()
    {
        if (!HasItem) return _customTitle ?? string.Empty;

        string name = Slot.item.DisplayName;
        // 장비·유물은 강화 레벨을 이름 옆에 함께 표기(+N)
        if (Slot.item is EquipmentSO eq)
        {
            int lv = EquipmentUpgradeStore.GetLevel(eq);
            if (lv > 0) name += $"  +{lv}";
        }
        return name;
    }

    public string GetTooltipContent()
    {
        if (Slot == null) return _customContent ?? string.Empty;
        if (!HasItem) return string.Empty;

        // 장비·유물: 이름·설명·강화·효과만 표시 (수량·무게 라인은 붙이지 않는다)
        if (Slot.item is EquipmentSO equip)
            return BuildEquipmentTooltip(equip);

        var sb = new System.Text.StringBuilder();
        string desc = Slot.item.Description;
        if (!string.IsNullOrEmpty(desc)) sb.AppendLine(desc);

        // ⚠ tt_quantity/tt_weight는 "수량: {0}"처럼 포맷 문자열이라 라벨로 쓰면 {0}이 그대로 노출된다.
        //   라벨 전용 키(tt_quantity_label/tt_weight_label)를 사용한다.
        if (Slot.item.Stackable)
            sb.AppendLine($"{CodeUI.L("tt_quantity_label", "수량")}: {Slot.quantity} / {Slot.item.MaxStackSize}");

        // 지상(창고)에선 과적이 없어 무게가 의미 없다 → 무게 줄을 붙이지 않는다.
        if (!IsSurfaceScene())
            sb.Append($"{CodeUI.L("tt_weight_label", "무게")}: {(Slot.item.Weight * Slot.quantity):F1}");
        // StringBuilder 끝 개행 정리(무게 줄을 생략한 경우 마지막 개행이 남을 수 있다)
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>현재 씬이 지상(창고가 있는) 씬인지. 지상에선 과적/무게 개념이 없다.</summary>
    private static bool IsSurfaceScene()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        return scene == "UpgroundScene" || scene == "DemoUpground" || scene == "SettlementScene";
    }

    /// <summary>장비/유물 툴팁: 이름(제목)·설명·강화 레벨·효과 목록. 수량·무게는 표시하지 않는다.</summary>
    private static string BuildEquipmentTooltip(EquipmentSO eq)
    {
        var sb = new System.Text.StringBuilder();

        // 설명
        string desc = eq.Description;
        if (!string.IsNullOrEmpty(desc)) sb.AppendLine(desc);

        int level = EquipmentUpgradeStore.GetLevel(eq);
        int maxLevel = EquipmentUpgradeStore.MaxLevel(eq);

        // 강화
        sb.AppendLine();
        string enhLabel = CodeUI.L("tt_enhance_label", "강화");
        sb.AppendLine(maxLevel > 0
            ? $"{enhLabel}: +{level} / +{maxLevel}"
            : $"{enhLabel}: +{level}");

        // 효과 — 방어력 + 스탯 수정치(강화 보너스 포함)를 (스탯, 방식)별로 합산
        var lines = new List<string>();

        float defense = eq.defense + EquipmentUpgradeStore.EffectiveBonusDefense(eq, level);
        if (!Mathf.Approximately(defense, 0f))
            lines.Add($"{CodeUI.L("stat_defense", "방어력")} {EquipmentUpgradeOverlayUI.FmtMod(ModifierType.Flat, defense)}");

        var agg = new Dictionary<(StatType, ModifierType), float>();
        var order = new List<(StatType, ModifierType)>();
        foreach (var m in EquipmentUpgradeStore.BuildEffectiveModifiers(eq, level))
        {
            var key = (m.statType, m.modifierType);
            if (!agg.ContainsKey(key))
            {
                agg[key] = m.modifierType == ModifierType.Percent ? 1f : 0f;
                order.Add(key);
            }
            if (m.modifierType == ModifierType.Percent) agg[key] *= m.value;
            else agg[key] += m.value;
        }
        foreach (var key in order)
        {
            lines.Add($"{EquipmentUpgradeOverlayUI.StatDisplayName(key.Item1)} " +
                      $"{EquipmentUpgradeOverlayUI.FmtMod(key.Item2, agg[key])}");
        }

        if (lines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(CodeUI.L("tt_effect_label", "효과").TrimEnd(' ', ':', '：'));
            foreach (var l in lines) sb.AppendLine("  " + l);
        }

        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// 슬롯이 아닌 넓은 영역(패널 배경)에 드롭을 받는 존.
/// 예: 가방 아이템을 창고 패널 아무 데나 떨어뜨리면 '넣기'로 처리.
/// </summary>
public class CodeDropZone : MonoBehaviour, IDropHandler, IPointerClickHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    public System.Action<CodeSlotView> onDrop;

    /// <summary>
    /// 손에 든 상태로 이 영역을 좌클릭했을 때(드래그가 아니라 CodeHeldItem으로 들고 있을 때).
    /// 예: 쓰레기통에 든 것을 클릭으로 버리기. null이면 무시.
    /// </summary>
    public System.Action onHeldClick;

    /// <summary>
    /// 손에 든 상태로 이 영역을 우클릭했을 때. 쓰레기통에서 '하나씩 버리기'로 쓴다. null이면 무시.
    /// </summary>
    public System.Action onHeldRightClick;

    /// <summary>
    /// 포인터가 이 영역에 들어오고 나갈 때 (쓰레기통처럼 '여기 놓으면 된다'를 보여줘야 하는 곳에서 사용).
    /// 드래그 고스트는 blocksRaycasts=false라 드래그 중에도 그대로 호출된다.
    /// </summary>
    public System.Action<bool> onHoverChanged;

    public void OnDrop(PointerEventData eventData)
    {
        var source = eventData.pointerDrag != null ? eventData.pointerDrag.GetComponent<CodeSlotView>() : null;
        onHoverChanged?.Invoke(false);
        if (source == null) return;
        onDrop?.Invoke(source);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!CodeHeldItem.Active) return;

        if (eventData.button == PointerEventData.InputButton.Left && onHeldClick != null)
        {
            onHoverChanged?.Invoke(false);
            onHeldClick.Invoke();
        }
        else if (eventData.button == PointerEventData.InputButton.Right && onHeldRightClick != null)
        {
            // 하나씩 버리기 — 손에 남은 게 있으면 계속 들고 있으므로 강조(hot)는 유지한다.
            onHeldRightClick.Invoke();
        }
    }

    public void OnPointerEnter(PointerEventData eventData) => onHoverChanged?.Invoke(true);
    public void OnPointerExit(PointerEventData eventData) => onHoverChanged?.Invoke(false);

    /// <summary>대상 오브젝트에 드롭존을 붙인다(레이캐스트를 받으려면 Graphic이 있어야 함).</summary>
    public static CodeDropZone Attach(GameObject target, System.Action<CodeSlotView> handler)
    {
        var zone = target.GetComponent<CodeDropZone>();
        if (zone == null) zone = target.AddComponent<CodeDropZone>();
        zone.onDrop = handler;
        return zone;
    }
}
