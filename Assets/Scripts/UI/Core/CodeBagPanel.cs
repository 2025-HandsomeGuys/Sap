// @tags: ui, bag, inventory, equipment, relic, slot, preview, code-generated, shared

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 창고 오버레이와 지하 인벤토리 오버레이가 함께 쓰는 '가방 칸'.
///
/// 왼쪽에 **장비 → 아이템 → 유물**을 한 줄(세로)로 쌓고, 남는 가로 폭은 전부
/// 오른쪽 <see cref="CodePlayerPreview"/>(페이퍼돌)가 가져간다.
///
/// 슬롯의 <see cref="CodeSlotView.index"/>는 각 인벤토리의 실제 인덱스를 그대로 쓰므로,
/// 소유자는 zone 값만 보고 처리하면 된다.
/// </summary>
public class CodeBagPanel
{
    public class Config
    {
        public UISkin skin;
        public PlayerPreviewSkin preview = new PlayerPreviewSkin();

        public float slotSize = 86f;
        [Tooltip("아이템 격자 열 수")]
        public int itemColumns = 2;
        public string diamondGlyph = "◆";

        /// <summary>CodeSlotView.zone 값 — 소유자가 정한다.</summary>
        public int zoneItem = 1;
        public int zoneEquipment = 2;
        public int zoneRelic = 90;

        /// <summary>
        /// 유물 칸을 <see cref="Relic.RelicManager"/>의 로드아웃으로 운영한다(장착 칸 + 보유 목록).
        /// false면 기존처럼 EquipmentInventory의 유물 장비 구간을 그린다.
        /// </summary>
        public bool useRelicLoadout = false;
        /// <summary>유물 장착/해제를 클릭·드롭으로 할 수 있는지. 지하에서는 false(표시 전용).</summary>
        public bool relicInteractable = true;

        /// <summary>장비·유물 칸을 만질 수 있는지. 지하에서는 false(장착 해제 불가).</summary>
        public bool equipmentInteractable = true;
        /// <summary>아이템 칸을 만질 수 있는지.</summary>
        public bool itemInteractable = true;
        /// <summary>
        /// <see cref="itemInteractable"/>이 false일 때도 아이템 칸을 '끌어낼' 수는 있게 할지.
        /// 지하 인벤토리가 이 조합을 쓴다 — 클릭(사용)은 막고 쓰레기통으로 버리는 드래그만 허용.
        /// </summary>
        public bool itemDraggable = false;

        public Sprite headPlaceholder, clothesPlaceholder, shoesPlaceholder, relicPlaceholder;
        /// <summary>빈 아이템 칸에 흐리게 깔 플레이스홀더. 비우면 기존처럼 빈 칸.</summary>
        public Sprite itemPlaceholder;

        /// <summary>프리뷰가 최소한 확보할 가로 폭.</summary>
        public float minPreviewWidth = 240f;

        /// <summary>
        /// 지정 시 페이퍼돌 대신 실제 <c>UiPlayer</c> 프리팹을 렌더링하는 라이브 프리뷰를 쓴다(장비·모션 실시간 반영).
        /// 비우면 <see cref="CodePlayerPreview"/> 페이퍼돌로 폴백.
        /// </summary>
        public GameObject uiPlayerPreviewPrefab;
    }

    // 소유자가 채우는 콜백
    public System.Action<CodeSlotView> onLeftClick;
    public System.Action<CodeSlotView> onRightClick;
    public System.Action<CodeSlotView, CodeSlotView> onDropReceived;

    public RectTransform Root { get; }

    /// <summary>페이퍼돌 프리뷰(프리팹 미지정 시). 라이브 프리뷰를 쓰면 null.</summary>
    public CodePlayerPreview Preview { get; }
    /// <summary>라이브 UiPlayer 프리뷰(프리팹 지정 시). 페이퍼돌을 쓰면 null.</summary>
    public CodeLivePlayerPreview LivePreview { get; }

    private readonly Config _cfg;
    private readonly LocTextBinder _loc;
    private readonly RectTransform _itemGrid, _equipGrid, _relicGrid;
    private readonly List<CodeSlotView> _itemSlots = new List<CodeSlotView>();
    private readonly List<CodeSlotView> _equipSlots = new List<CodeSlotView>();
    private readonly List<CodeSlotView> _relicSlots = new List<CodeSlotView>();

    // 유물 로드아웃 모드 전용 (useRelicLoadout = true)
    private Relic.RelicManager _relicMgr;

    /// <summary>유물 칸이 물고 있는 매니저 (로드아웃 모드에서 RefreshRelics 후 유효).</summary>
    public Relic.RelicManager RelicMgr => _relicMgr;

    public CodeBagPanel(Transform parent, Config config, LocTextBinder loc)
    {
        _cfg = config ?? new Config();
        _loc = loc;

        // 칸 묶음은 내용만큼만, 남는 폭은 전부 프리뷰에게.
        // (칸 쪽에 flexibleWidth를 주면 칸들이 벌어지면서 프리뷰가 좁아진다)
        Root = CodeUI.CreateRow(parent, "BagArea", 0f, 14f, TextAnchor.UpperLeft);

        var column = CodeUI.CreateColumn(Root, "Slots", 6f, expandWidth: false);

        AddSectionLabel(column, "ui_inv_equip", "장비", CodeUI.EquipColor);
        _equipGrid = CodeUI.CreateRect(column, "EquipGrid");
        AddGrid(_equipGrid, EquipmentInventory.SlotRelicFirst); // 머리·옷·신발

        AddSectionLabel(column, "ui_inv_item", "아이템", CodeUI.ItemColor);
        _itemGrid = CodeUI.CreateRect(column, "ItemGrid");
        AddGrid(_itemGrid, _cfg.itemColumns);

        AddSectionLabel(column, "ui_inv_relic", "유물", CodeUI.RelicColor);
        _relicGrid = CodeUI.CreateRect(column, "RelicGrid");
        AddGrid(_relicGrid, EquipmentInventory.RelicSlotCount);

        // 프리팹이 지정되면 실제 UiPlayer를 렌더하는 라이브 프리뷰, 아니면 페이퍼돌.
        RectTransform previewRoot;
        if (_cfg.uiPlayerPreviewPrefab != null)
        {
            LivePreview = new CodeLivePlayerPreview(Root, _cfg.skin, _cfg.uiPlayerPreviewPrefab, _cfg.preview, _loc);
            previewRoot = LivePreview.Root;
        }
        else
        {
            Preview = new CodePlayerPreview(Root, _cfg.skin, _cfg.preview, _loc);
            previewRoot = Preview.Root;
        }

        var previewLe = previewRoot.gameObject.AddComponent<LayoutElement>();
        previewLe.minWidth = _cfg.minPreviewWidth;
        previewLe.flexibleWidth = 1f;
        previewLe.flexibleHeight = 1f;
    }

    /// <summary>
    /// 지금 켜져 있는 가방 칸(장비 → 아이템 → 유물)을 넘긴 목록에 담는다.
    /// <see cref="CodeSlotNavigator"/>가 WASD 이동 후보를 모을 때 쓴다.
    /// </summary>
    public void CollectSlots(List<ICodeNavItem> into)
    {
        if (into == null) return;
        AddActive(_equipSlots, into);
        AddActive(_itemSlots, into);
        AddActive(_relicSlots, into);
    }

    private static void AddActive(List<CodeSlotView> pool, List<ICodeNavItem> into)
    {
        foreach (var view in pool)
            if (view != null && view.gameObject.activeInHierarchy) into.Add(view);
    }

    /// <summary>인벤토리 내용을 슬롯에 반영하고 프리뷰를 다시 그린다.</summary>
    public void Refresh(ItemInventory itemInv, EquipmentInventory equipInv)
    {
        // ── 아이템 ──
        int itemCapacity = itemInv != null ? Mathf.Max(0, itemInv.maxSlotCount) : 0;
        var itemList = itemInv != null ? itemInv.ReadonlyItems : null;

        EnsureSlots(_itemSlots, _itemGrid, itemCapacity, _cfg.zoneItem, _cfg.itemInteractable, _cfg.itemDraggable);
        for (int i = 0; i < _itemSlots.Count; i++)
        {
            bool used = i < itemCapacity;
            _itemSlots[i].gameObject.SetActive(used);
            if (!used) continue;

            _itemSlots[i].index = i;
            _itemSlots[i].SetPlaceholder(_cfg.itemPlaceholder, CodeUI.L("ui_inv_item", "아이템"), Tint());
            _itemSlots[i].Bind(GetAt(itemList, i), CodeUI.ItemColor);
        }

        // ── 장비(머리·옷·신발) / 유물 — 같은 EquipmentInventory의 앞·뒤 구간 ──
        var equipList = equipInv != null ? equipInv.ReadonlyItems : null;

        BindEquipmentRange(_equipSlots, _equipGrid, 0, EquipmentInventory.SlotRelicFirst,
            equipList, CodeUI.EquipColor);

        if (_cfg.useRelicLoadout)
            RefreshRelics();
        else
            BindEquipmentRange(_relicSlots, _relicGrid, EquipmentInventory.SlotRelicFirst,
                EquipmentInventory.RelicSlotCount, equipList, CodeUI.RelicColor);

        if (LivePreview != null) LivePreview.Refresh(equipInv);
        else Preview.Refresh(equipInv);
    }

    // ===================================================
    // 유물 로드아웃 (useRelicLoadout = true)
    // ===================================================

    /// <summary>
    /// 유물 칸 = 현재 장착(로드아웃). 미장착 보유분은 창고 쪽 목록이 담당한다.
    /// 장착/해제 조작은 소유자(오버레이) 콜백으로 넘긴다 — 창고와의 주고받기를 한곳에서 처리하려고.
    /// </summary>
    public void RefreshRelics()
    {
        if (!_cfg.useRelicLoadout) return;

        // 씬에 매니저가 없으면(지상 씬 등) 플레이어에 붙이고 세이브에서 보유 유물을 복원한다.
        // 파괴된 매니저는 == null 이 true라 씬이 바뀌면 자동으로 다시 잡힌다.
        if (_relicMgr == null) _relicMgr = Relic.RelicManager.EnsureInScene();

        var inv = _relicMgr != null ? _relicMgr.Inventory : null;
        var db = Relic.Data.RelicDatabase.Instance;

        // 열린 칸 수는 업그레이드가 정한다(RelicManager.BaseSlotCount = 0).
        // 아직 안 산 칸도 '잠김'으로 그려 준다 — 안 그리면 업그레이드 전엔 유물 구역이
        // 통째로 비어 UI가 깨진 것처럼 보인다.
        int unlockedCount = inv != null ? inv.SlotCount : 0;
        int slotCount = Mathf.Max(unlockedCount, Relic.RelicManager.MaxSlotCount);

        while (_relicSlots.Count < slotCount)
        {
            var view = CodeSlotView.Create(_relicGrid, _cfg.skin, _cfg.slotSize,
                $"Slot_{_cfg.zoneRelic}_{_relicSlots.Count}");
            view.zone = _cfg.zoneRelic;

            if (_cfg.relicInteractable)
            {
                view.onLeftClick = v => onLeftClick?.Invoke(v);
                view.onRightClick = v => onRightClick?.Invoke(v);
                view.onDropReceived = (t, s) => onDropReceived?.Invoke(t, s);
            }
            else
            {
                view.canDrag = _ => false; // 지하 = 표시 전용
            }

            _relicSlots.Add(view);
        }

        for (int i = 0; i < _relicSlots.Count; i++)
        {
            bool used = i < slotCount;
            _relicSlots[i].gameObject.SetActive(used);
            if (!used) continue;

            _relicSlots[i].index = i;

            // 잠긴 칸: 실루엣 없이 '잠김' 문구만. 장착 시도는 RelicInventory.Equip이
            // 범위 밖이라 어차피 실패하므로 여기선 표시만 막으면 된다.
            if (i >= unlockedCount)
            {
                _relicSlots[i].SetPlaceholder(null, CodeUI.L("ui_relic_slot_locked", "잠김"), Tint());
                _relicSlots[i].BindCustom(null, null, null, null, false);
                _relicSlots[i].SetLevelBadge(0);
                continue;
            }

            var id = inv != null ? inv.GetEquipped(i) : Relic.Data.RelicID.None;
            var so = (db != null && id != Relic.Data.RelicID.None) ? db.GetRelicByID(id) : null;

            _relicSlots[i].SetPlaceholder(_cfg.relicPlaceholder, CodeUI.L("ui_inv_relic", "유물"), Tint());
            BindRelic(_relicSlots[i], so, inv);
        }
    }

    /// <summary>유물 하나를 슬롯에 그린다. so가 null이면 빈 칸.</summary>
    public static void BindRelic(CodeSlotView view, Relic.Data.RelicSO so, Relic.RelicInventory inv)
    {
        if (so == null)
        {
            view.BindCustom(null, null, null, null, false);
            return;
        }

        string name = CodeUI.L(so.displayNameKey, so.displayNameKey);
        int level = inv != null ? inv.GetLevel(so.id) : 1;
        string kind = so.type == Relic.Data.RelicType.Active
            ? CodeUI.L("ui_relic_active", "액티브")
            : CodeUI.L("ui_relic_passive", "패시브");

        string content = $"{CodeUI.L(so.descriptionKey, so.descriptionKey)}\n{kind}  ·  Lv{Mathf.Max(1, level)}";
        view.BindCustom(so.icon, name, content, CodeUI.RelicColor, true);
        // 유물 강화 단계(+N). 유물은 내부적으로 1부터 → 표시는 0강부터(장비·강화 UI와 통일).
        view.SetLevelBadge(Mathf.Max(0, level - 1));
    }

    /// <summary>
    /// EquipmentInventory의 [first, first+count) 구간을 격자에 바인딩한다.
    /// 슬롯 index는 인벤토리 실제 인덱스라 소유자는 구간을 몰라도 된다.
    /// </summary>
    private void BindEquipmentRange(List<CodeSlotView> pool, RectTransform grid, int first, int count,
        IReadOnlyList<InventorySlot> list, Color accent)
    {
        EnsureSlots(pool, grid, count, _cfg.zoneEquipment, _cfg.equipmentInteractable);

        for (int i = 0; i < pool.Count; i++)
        {
            bool used = i < count;
            pool[i].gameObject.SetActive(used);
            if (!used) continue;

            int slotIndex = first + i;
            pool[i].index = slotIndex;
            pool[i].SetPlaceholder(PlaceholderFor(slotIndex), PlaceholderLabelFor(slotIndex), Tint());
            var slot = GetAt(list, slotIndex);
            pool[i].Bind(slot, accent);
            // 장비 강화 단계(+N)를 강화 UI와 동일하게 표기
            if (slot != null && slot.item is EquipmentSO eq)
                pool[i].SetLevelBadge(EquipmentUpgradeStore.GetLevel(eq));
        }
    }

    private void EnsureSlots(List<CodeSlotView> pool, RectTransform parent, int needed, int zone,
        bool interactable, bool draggable = false)
    {
        while (pool.Count < needed)
        {
            var view = CodeSlotView.Create(parent, _cfg.skin, _cfg.slotSize, $"Slot_{zone}_{pool.Count}");
            view.zone = zone;

            if (interactable)
            {
                view.onLeftClick = v => onLeftClick?.Invoke(v);
                view.onRightClick = v => onRightClick?.Invoke(v);
                view.onDropReceived = (t, s) => onDropReceived?.Invoke(t, s);
            }
            else if (!draggable)
            {
                // 표시 전용 — 클릭·드래그 모두 막는다 (지하의 장비·유물 칸)
                view.canDrag = _ => false;
            }
            // interactable=false + draggable=true → 콜백은 안 달고 canDrag는 기본값(아이템 있을 때만).
            // 클릭으로는 아무 일도 안 일어나고, 드래그해서 쓰레기통 같은 드롭존에만 넘길 수 있다.

            pool.Add(view);
        }
    }

    // ── 조립 헬퍼 ──
    private void AddGrid(RectTransform target, int columns)
    {
        var grid = target.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(_cfg.slotSize, _cfg.slotSize);
        grid.spacing = new Vector2(8f, 8f);
        grid.padding = new RectOffset(2, 2, 4, 4);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(1, columns);
    }

    private void AddSectionLabel(Transform parent, string key, string fallback, Color dotColor)
    {
        var row = CodeUI.CreateRow(parent, "SectionLabel", 26f, 8f);
        var dot = CodeUI.CreateText(row, "Dot", 15f, FontStyles.Normal, dotColor, TextAlignmentOptions.Center, _loc);
        dot.text = _cfg.diamondGlyph;
        var label = CodeUI.CreateText(row, "Label", 18f, FontStyles.Bold, CodeUI.LabelColor,
            TextAlignmentOptions.MidlineLeft, _loc);
        _loc?.Bind(label, key, fallback);
        CodeUI.CreateSpacer(row);
    }

    // 유물은 칸이 여러 개라 인덱스가 아니라 '슬롯이 받는 타입'으로 고른다
    private Sprite PlaceholderFor(int slotIndex)
    {
        switch (EquipmentInventory.TypeOfSlot(slotIndex))
        {
            case EquipmentType.Head: return _cfg.headPlaceholder;
            case EquipmentType.Clothes: return _cfg.clothesPlaceholder;
            case EquipmentType.Shoes: return _cfg.shoesPlaceholder;
            case EquipmentType.Relic: return _cfg.relicPlaceholder;
            default: return null;
        }
    }

    /// <summary>
    /// 빈 칸 실루엣 색 — 부위·카테고리와 무관하게 전부 같은 톤.
    /// UI 전체(다크 네이비)에 묻히도록 짙은 남색(NeutralBg)을 흐린 알파로.
    /// </summary>
    private static Color Tint(Color _ = default) =>
        new Color(CodeUI.NeutralBg.r, CodeUI.NeutralBg.g, CodeUI.NeutralBg.b, 0.55f);

    private static string PlaceholderLabelFor(int slotIndex)
    {
        switch (EquipmentInventory.TypeOfSlot(slotIndex))
        {
            case EquipmentType.Head: return CodeUI.L("ui_inv_head", "머리");
            case EquipmentType.Clothes: return CodeUI.L("ui_inv_clothes", "옷");
            case EquipmentType.Shoes: return CodeUI.L("ui_inv_shoes", "신발");
            case EquipmentType.Relic: return CodeUI.L("ui_inv_relic", "유물");
            default: return string.Empty;
        }
    }

    private static InventorySlot GetAt(IReadOnlyList<InventorySlot> list, int index) =>
        (list != null && index >= 0 && index < list.Count) ? list[index] : null;
}
