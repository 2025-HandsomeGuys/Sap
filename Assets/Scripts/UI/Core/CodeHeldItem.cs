// @tags: ui, held, hand, pickup, quantity, wheel, slot, code-generated, inventory, warehouse, shop

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 코드 생성 오버레이(창고 · 지하 인벤토리)가 공유하는 "손에 집기" 컨트롤러.
///
/// 옛 <see cref="HeldItemManager"/>(프리팹 UI 전용)를 대체한다 — 이쪽은 <see cref="CodeSlotView"/> 기반.
///  - 좌클릭 : 칸의 스택을 통째로 손에 집는다.
///  - 우클릭 : 손에 든 것을 마우스 아래 칸에 1개씩 내려놓는다.
///  - 마우스 휠 : 손에 든 개수를 조절한다(아래로 = 집은 자리로 1개 반납, 위로 = 1개 더 집기).
///  - 다시 좌클릭 : 손에 든 걸 그 칸에 전부 내려놓는다(다른 아이템이면 맞바꿈 → 손 비움).
///  - 빈 곳 클릭 / ESC / 오버레이 닫힘 : 원래대로(손 비움).
///
/// ★ 핵심 불변식: <b>집는 순간에는 데이터를 건드리지 않는다.</b> 실제 이동/버리기가 일어날 때만
///   호스트(오버레이)의 기존 이동 로직을 통해 인벤토리를 바꾼다. 덕분에 무게 한도·창고 용량·
///   세이브 정합성이 기존 클릭/드래그 경로와 완전히 동일하게 유지된다.
///
/// 드래그(자유 이동·스왑)는 <see cref="CodeSlotView"/>가 그대로 담당한다 — 이 컨트롤러는 클릭·휠만 쓴다.
/// </summary>
public class CodeHeldItem : MonoBehaviour
{
    // ===================================================
    // 호스트(오버레이)가 구현하는 계약
    // ===================================================
    public interface IHost
    {
        /// <summary>오버레이가 아직 열려 있는지. 닫히면 컨트롤러가 손을 비운다(안전망).</summary>
        bool HeldHostOpen { get; }

        /// <summary>손에 든 동안 휠이 목록 스크롤로 새지 않도록 잠글 스크롤(없으면 null).</summary>
        ScrollRect HeldScrollRect { get; }

        /// <summary>source 칸이 실제로 들고 있는 아이템의 현재 수량(라이브 인벤토리 기준, 없으면 0).</summary>
        int HeldLiveQuantity(CodeSlotView source);

        /// <summary>source 칸의 현재 아이템 식별자(라이브 기준, 없으면 null). 스왑/소진 감지용.</summary>
        string HeldLiveItemId(CodeSlotView source);

        /// <summary>
        /// source 스택에서 amount개를 target 칸으로 옮긴다(기존 이동 로직 재사용).
        /// 반환값 = <b>손에서 덜어낸 개수</b>. 스왑/소진 등으로 손을 통째로 비워야 하면 <see cref="int.MaxValue"/>.
        /// 실패(넣을 자리 없음 등)면 0.
        /// </summary>
        int HeldCommit(CodeSlotView source, CodeSlotView target, int amount);

        /// <summary>source 칸의 아이템 amount개를 버린다(호스트의 확인 팝업을 거친다).</summary>
        void HeldTrash(CodeSlotView source, int amount);

        /// <summary>
        /// source 칸의 아이템을 <b>1개만 즉시</b> 버린다(확인 팝업 없음 — 우클릭 연타로 하나씩 버리는 용도).
        /// 실제로 지웠으면 true. 실패하면 손 개수를 건드리지 않는다.
        /// </summary>
        bool HeldTrashOne(CodeSlotView source);

        /// <summary>손 상태가 바뀐 뒤 UI 갱신 훅(하이라이트 등).</summary>
        void HeldRefresh();

        /// <summary>이동/집기 효과음.</summary>
        void HeldPlaySfx();
    }

    // ===================================================
    // 싱글톤 (자동 생성 — 씬 세팅 불필요)
    // ===================================================
    private static CodeHeldItem _instance;

    private static CodeHeldItem Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<CodeHeldItem>();
                if (_instance == null)
                {
                    var go = new GameObject("CodeHeldItem");
                    _instance = go.AddComponent<CodeHeldItem>();
                }
            }
            return _instance;
        }
    }

    /// <summary>지금 무언가를 손에 들고 있는지.</summary>
    public static bool Active => _instance != null && _instance._item != null && _instance._qty > 0;

    // ===================================================
    // 상태
    // ===================================================
    private InterfaceInventoryItem _item;
    private int _qty;                 // 손에 든 개수
    private int _max;                 // 집은 자리에서 최대로 집을 수 있는 개수(집을 때 스냅샷)
    private string _itemId;           // 스왑/소진 감지용
    private CodeSlotView _source;     // 집어 온 칸
    private IHost _host;

    // 마우스를 따라다니는 고스트
    private Canvas _canvas;
    private RectTransform _canvasRect;
    private RectTransform _root;
    private Image _icon;
    private TextMeshProUGUI _qtyText;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        BuildGhost();
        HideGhost();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // ===================================================
    // 정적 진입점 (오버레이가 클릭 콜백에서 부른다)
    // ===================================================

    /// <summary>칸의 스택을 통째로 집는다. 이미 뭔가 들고 있으면 무시(먼저 내려놓아야 한다).</summary>
    public static void PickUp(CodeSlotView view, IHost host)
    {
        if (view == null || host == null || !view.HasItem) return;
        Instance.DoPickUp(view, host);
    }

    /// <summary>
    /// 손에 든 걸 target 칸에 전부 내려놓는다(다른 아이템이면 맞바꿈). 든 게 없으면 무시.
    /// 좌클릭은 '한 번에 끝내는' 동작이라, 최대 스택 때문에 일부만 들어가도 손을 마저 비운다
    /// (넘친 몫은 원래 집은 칸에 그대로 남는다 — 집을 때 데이터를 안 뺐으므로). 남은 걸 손에 들고
    /// 있는 상태로 두지 않는다.
    /// </summary>
    public static void PlaceAll(CodeSlotView target)
    {
        if (!Active) return;
        Instance.DoPlace(target, Instance._qty, clearRemainder: true);
    }

    /// <summary>손에 든 걸 target 칸에 1개 내려놓는다(우클릭 — 남은 건 계속 손에 든다).</summary>
    public static void PlaceOne(CodeSlotView target)
    {
        if (!Active) return;
        Instance.DoPlace(target, 1, clearRemainder: false);
    }

    /// <summary>손에 든 걸 버린다(호스트 확인 팝업). 든 게 없으면 무시.</summary>
    public static void TrashHeld()
    {
        if (!Active) return;
        Instance.DoTrash();
    }

    /// <summary>손에 든 것 중 1개만 즉시 버린다(우클릭 — 나머지는 계속 손에 든다).</summary>
    public static void TrashOne()
    {
        if (!Active) return;
        Instance.DoTrashOne();
    }

    /// <summary>손을 비운다(데이터 변화 없음). 오버레이 닫기·ESC·빈 곳 클릭 등에서 호출.</summary>
    public static void Cancel()
    {
        if (_instance != null) _instance.DoCancel();
    }

    /// <summary>현재 든 아이템(없으면 null) — 호스트가 드롭존 판정 등에 쓸 수 있다.</summary>
    public static InterfaceInventoryItem HeldItem => _instance != null ? _instance._item : null;

    /// <summary>손에 든 개수(없으면 0) — 원본 칸이 '남은 개수'를 그릴 때 쓴다.</summary>
    public static int HeldQuantity => _instance != null ? _instance._qty : 0;

    /// <summary>손에 집어 든 원본 칸(없으면 null) — 무게 미리보기처럼 출처를 알아야 하는 곳에서 쓴다.</summary>
    public static CodeSlotView HeldSource => Active ? _instance._source : null;

    /// <summary>view가 지금 손에 집어 든 원본 칸인지 — CodeSlotView가 '남은 개수' 표시에 쓴다.</summary>
    public static bool IsSource(CodeSlotView view) =>
        view != null && Active && _instance._source == view;

    // ===================================================
    // 내부 동작
    // ===================================================
    private void DoPickUp(CodeSlotView view, IHost host)
    {
        if (Active) return; // 손이 이미 차 있으면 새로 집지 않는다

        _host = host;
        _source = view;
        _item = view.Slot.item;
        _itemId = _item.Id;
        _qty = view.Slot.quantity;
        _max = _qty;

        view.RefreshHeldDisplay(); // 원본은 이제 '남은 0'으로 — 전부 손에 들렸다
        SetScrollLocked(true);
        ShowGhost();
        UpdateGhost();
        MoveGhostToMouse();
        _host.HeldPlaySfx();
    }

    private void DoPlace(CodeSlotView target, int amount, bool clearRemainder)
    {
        if (!Active || target == null) return;

        // 집은 자리에 다시 놓기 = 그만큼 반납(데이터 변화 없음 — 애초에 안 뺐다).
        if (target == _source)
        {
            _qty -= Mathf.Clamp(amount, 1, _qty);
            if (_qty <= 0) DoCancel();
            else { UpdateGhost(); _source.RefreshHeldDisplay(); }
            return;
        }

        int want = Mathf.Clamp(amount, 1, _qty);
        var src = _source;
        int consumed = _host != null ? _host.HeldCommit(_source, target, want) : 0;

        if (consumed <= 0) return; // 실패 — 손 그대로

        if (consumed == int.MaxValue) { AfterCommitClear(); return; }

        _qty -= Mathf.Min(consumed, _qty);
        _host.HeldPlaySfx();

        // 좌클릭(전부 놓기)은 일부만 들어가도 손을 마저 비운다 — 남은 몫은 집은 칸에 그대로 있다.
        // 우클릭(1개씩)은 남은 걸 계속 손에 들고 다음 칸에 나눠 놓을 수 있게 유지한다.
        if (_qty <= 0 || clearRemainder) AfterCommitClear();
        else { UpdateGhost(); src.RefreshHeldDisplay(); }
    }

    private void DoTrash()
    {
        if (!Active) return;
        var host = _host;      // DoCancel이 _host를 비우므로 미리 잡아 둔다
        var src = _source;
        int amount = _qty;
        // 확인 팝업은 호스트가 띄운다 — 손은 즉시 비운다(데이터는 안 뺐으므로 원본이 그대로 있다).
        DoCancel();
        host?.HeldTrash(src, amount);
    }

    private void DoTrashOne()
    {
        if (!Active) return;
        if (_host == null || !_host.HeldTrashOne(_source)) return;

        // 원본에서 1개가 실제로 빠졌으니 손에 든 몫도 1 줄인다(손 개수는 원본에 대한 '예약'이다).
        _qty -= 1;
        if (_qty <= 0) { DoCancel(); return; }
        UpdateGhost();
        _source.RefreshHeldDisplay();
    }

    /// <summary>이동이 성사돼 손을 비울 때(원본 표시를 실제 수량으로 되돌린다).</summary>
    private void AfterCommitClear()
    {
        var old = _source;
        ClearState();
        old?.RefreshHeldDisplay(); // 이제 IsSource=false → 실제 남은 수량으로 그린다
    }

    private void DoCancel()
    {
        var old = _source;
        ClearState();
        old?.RefreshHeldDisplay();
    }

    private void ClearState()
    {
        _item = null;
        _itemId = null;
        _qty = 0;
        _max = 0;
        _source = null;
        SetScrollLocked(false);
        HideGhost();
        _host?.HeldRefresh();
        _host = null;
    }

    private void SetScrollLocked(bool locked)
    {
        var scroll = _host != null ? _host.HeldScrollRect : null;
        if (scroll != null) scroll.vertical = !locked;
    }

    // ===================================================
    // 전역 입력 (손에 들고 있을 때만)
    // ===================================================
    private void Update()
    {
        if (!Active) return;

        // 오버레이가 닫혔으면 손을 비운다(데이터는 원본에 그대로 남아 있다).
        if (_host == null || !_host.HeldHostOpen) { DoCancel(); return; }

        MoveGhostToMouse();

        // ESC/Tab 취소는 오버레이가 처리한다(닫기 단축키와의 순서 경합을 피하려고).

        // 마우스 휠 = 개수 조절
        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f)
        {
            if (wheel < 0)
            {
                // 아래로 = 집은 자리로 1개 반납(원본의 '남은 개수'가 하나 늘어난다)
                _qty -= 1;
                if (_qty <= 0) { DoCancel(); return; }
                UpdateGhost();
                _source.RefreshHeldDisplay();
            }
            else
            {
                // 위로 = 1개 더 집기 (집은 자리에 라이브로 남아 있는 만큼까지)
                int live = _host != null ? _host.HeldLiveQuantity(_source) : _max;
                int cap = Mathf.Min(_max, live);
                if (_qty < cap) { _qty += 1; UpdateGhost(); _source.RefreshHeldDisplay(); }
            }
        }

        // 빈 곳(칸·드롭존이 아닌 곳) 좌클릭 = 취소
        if (Input.GetMouseButtonDown(0) && !IsPointerOverInteractive())
            DoCancel();
    }

    /// <summary>마우스 아래에 칸(CodeSlotView)이나 드롭존(CodeDropZone)이 있는지.</summary>
    private static bool IsPointerOverInteractive()
    {
        var es = EventSystem.current;
        if (es == null) return true; // 판단 불가면 보수적으로 유지

        var data = new PointerEventData(es) { position = Input.mousePosition };
        var results = new List<RaycastResult>();
        es.RaycastAll(data, results);
        foreach (var r in results)
        {
            if (r.gameObject.GetComponentInParent<CodeSlotView>() != null) return true;
            if (r.gameObject.GetComponentInParent<CodeDropZone>() != null) return true;
        }
        return false;
    }

    // ===================================================
    // 고스트
    // ===================================================
    private void BuildGhost()
    {
        var go = new GameObject("HeldItemCanvas");
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 31000; // 오버레이(≈30500)·설정 위, 마우스를 항상 따라오도록 최상위
        _canvasRect = (RectTransform)_canvas.transform;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _root = new GameObject("Ghost").AddComponent<RectTransform>();
        _root.SetParent(go.transform, false);
        _root.sizeDelta = new Vector2(72f, 72f);
        _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.pivot = new Vector2(0.5f, 0.5f);

        _icon = new GameObject("Icon").AddComponent<Image>();
        _icon.transform.SetParent(_root, false);
        _icon.preserveAspect = true;
        _icon.raycastTarget = false;
        var ir = _icon.rectTransform;
        ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
        ir.offsetMin = Vector2.zero; ir.offsetMax = Vector2.zero;

        _qtyText = new GameObject("Qty").AddComponent<TextMeshProUGUI>();
        _qtyText.transform.SetParent(_root, false);
        _qtyText.fontSize = 22f;
        _qtyText.fontStyle = FontStyles.Bold;
        _qtyText.color = Color.white;
        _qtyText.alignment = TextAlignmentOptions.BottomRight;
        _qtyText.raycastTarget = false;
        var qr = _qtyText.rectTransform;
        qr.anchorMin = Vector2.zero; qr.anchorMax = Vector2.one;
        qr.offsetMin = new Vector2(0f, 2f); qr.offsetMax = new Vector2(-4f, 0f);

        var cg = _root.gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false; // 아래 칸이 클릭·드롭을 받아야 한다
        cg.interactable = false;
        cg.alpha = 0.9f;
    }

    private void ShowGhost()
    {
        if (_root != null) _root.gameObject.SetActive(true);
    }

    private void HideGhost()
    {
        if (_root != null) _root.gameObject.SetActive(false);
    }

    private void UpdateGhost()
    {
        if (_item == null) return;
        if (_icon != null)
        {
            _icon.sprite = _item.Icon;
            _icon.enabled = (_icon.sprite != null);
        }
        if (_qtyText != null)
            _qtyText.text = (_item.Stackable && _qty > 1) ? _qty.ToString() : string.Empty;
    }

    private void MoveGhostToMouse()
    {
        if (_root == null || _canvasRect == null) return;
        // TooltipManager와 동일한 방식(오버레이 캔버스 rect 기준 anchoredPosition) — 스케일 캔버스에서도 정확히 따라온다.
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, Input.mousePosition, null, out var local))
            _root.anchoredPosition = local;
    }
}
