using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using UnityEngine.SceneManagement;

/// <summary>
/// 창고 격자에서 '미장착 유물'을 일반 창고 슬롯처럼 다루기 위한 어댑터.
/// 실물(소유·레벨·로드아웃)은 여전히 <see cref="Relic.RelicInventory"/>가 갖고 있고,
/// 이 래퍼는 <b>창고 오버레이가 열려 있는 동안에만</b> <see cref="WarehouseManager.AllSlots"/>에 얹혀
/// 스왑·정렬·용량 계산이 다른 아이템과 완전히 동일하게 동작하도록 한다(세션 전용).
/// 닫힐 때 <see cref="WarehouseManager.StripRelicSlots"/>로 걷어내며, 저장(<see cref="WarehouseManager.ToData"/>)에서도
/// 제외된다 — 유물은 RelicSaveData로만 저장되므로 이중 저장을 막는다.
/// </summary>
public sealed class WarehouseRelicItem : InterfaceInventoryItem
{
    public readonly Relic.Data.RelicSO so;
    public readonly int level; // 표시용(툴팁·강화 배지). 상점 강화는 창고 밖에서 일어나므로 세션 중엔 안정적.

    public WarehouseRelicItem(Relic.Data.RelicSO so, int level)
    {
        this.so = so;
        this.level = Mathf.Max(1, level);
    }

    public Relic.Data.RelicID RelicId => so != null ? so.id : Relic.Data.RelicID.None;

    // 다른 계열 Id와 충돌하지 않도록 접두사. 비스택이라 Id 동일성으로 병합되는 일은 없다.
    public string Id => "RELIC_" + (so != null ? so.id.ToString() : "None");
    public string DisplayName => so != null ? CodeUI.L(so.displayNameKey, so.displayNameKey) : string.Empty;
    public Sprite Icon => so != null ? so.icon : null;
    public float Weight => 0f;
    public bool Stackable => false;
    public int MaxStackSize => 1;

    public string Description
    {
        get
        {
            if (so == null) return string.Empty;
            string desc = CodeUI.L(so.descriptionKey, so.descriptionKey);
            string kind = so.type == Relic.Data.RelicType.Active
                ? CodeUI.L("ui_relic_active", "액티브")
                : CodeUI.L("ui_relic_passive", "패시브");
            return $"{desc}\n{kind}  ·  Lv{level}";
        }
    }
}

/// <summary>
/// 창고 시스템을 관리하는 매니저.
/// 지상 씬에서 광물, 아이템, 도구를 통합 보관합니다.
/// </summary>
public class WarehouseManager : MonoBehaviour
{
    private static bool _isQuitting = false;
    private static WarehouseManager _instance;
    public static WarehouseManager Instance
    {
        get
        {
            if (_isQuitting) return null;

            if (_instance == null)
            {
                _instance = FindFirstObjectByType<WarehouseManager>();
                if (_instance == null && !_isQuitting)
                {
                    GameObject go = new GameObject("WarehouseManager");
                    _instance = go.AddComponent<WarehouseManager>();
                }
            }
            return _instance;
        }
    }

    [Header("슬롯 설정")]
    public int totalSlots = 20; // 전체 창고 슬롯 수
    public bool canWithdrawToInventory = false; // 창고 -> 인벤토리 이동 가능 여부 (토글용)

    [Header("저장된 데이터")]
    [SerializeField] private List<InventorySlot> warehouseSlots = new List<InventorySlot>();

    // 필터링된 뷰 제공 (호환성 유지)
    public IReadOnlyList<InventorySlot> StoredItems => warehouseSlots.Where(s => s != null && s.item is ItemSO).ToList();
    public IReadOnlyList<InventorySlot> StoredMinerals => warehouseSlots.Where(s => s != null && s.item is MineralSO).ToList();
    public IReadOnlyList<InventorySlot> StoredEquipments => warehouseSlots.Where(s => s != null && s.item is EquipmentSO).ToList();
    public List<InventorySlot> AllSlots => warehouseSlots;

    // 데이터 변경 이벤트
    public event Action OnWarehouseChanged;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        
        InitializeSlots();
    }

    private void InitializeSlots()
    {
        if (warehouseSlots == null || warehouseSlots.Count == 0)
        {
            warehouseSlots = new List<InventorySlot>();
            for (int i = 0; i < totalSlots; i++)
            {
                warehouseSlots.Add(new InventorySlot(null, 0));
            }
        }
        // 슬롯 수 확장을 고려하여 개수 맞춤
        while (warehouseSlots.Count < totalSlots)
        {
            warehouseSlots.Add(new InventorySlot(null, 0));
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 씬 로드 시 데이터 통합은 이제 GameManager가 호출하는 SaveManager.Load()에서 통합 관리됩니다.
    }

    /// <summary>
    /// 플레이어 인벤토리의 모든 내용을 창고로 이동 (지상 진입 시 호출)
    /// </summary>
    public void DepositAllFromInventory(ItemInventory itemInv, MineralInventory mineralInv, EquipmentInventory equipmentInv)
    {
        bool changed = false;

        if (itemInv != null)
        {
            var items = itemInv.ReadonlyItems.ToList();
            for (int i = items.Count - 1; i >= 0; i--)
            {
                var slot = items[i];
                if (slot.item is ItemSO item)
                {
                    // 실제로 들어간 만큼만 가방에서 뺀다 — 창고가 가득 찼을 때 아이템이 사라지지 않게
                    int moved = AddGenericItem(item, slot.quantity, false);
                    if (moved <= 0) continue;
                    itemInv.RemoveItem(item, moved);
                    changed = true;
                }
            }
        }

        if (mineralInv != null)
        {
            var minerals = mineralInv.ReadonlyItems.ToList();
            for (int i = minerals.Count - 1; i >= 0; i--)
            {
                var slot = minerals[i];
                if (slot.item is MineralSO mineral)
                {
                    int moved = AddGenericItem(mineral, slot.quantity, false);
                    if (moved <= 0) continue;
                    mineralInv.RemoveItemAt(i, moved);
                    changed = true;
                }
            }
        }

        if (equipmentInv != null)
        {
            var equipments = equipmentInv.ReadonlyItems.ToList();
            for (int i = equipments.Count - 1; i >= 0; i--)
            {
                var slot = equipments[i];
                if (slot.item is EquipmentSO equipment)
                {
                    // 유물은 스택형일 수 있으므로 수량 전부 옮긴다 (1로 고정하면 남은 수량이 가방에 갇힌다)
                    int qty = Math.Max(1, slot.quantity);
                    int moved = AddGenericItem(equipment, qty, false);
                    if (moved <= 0) continue; // 창고가 가득 찼으면 벗기지 않고 그대로 착용 유지
                    equipmentInv.RemoveItemAt(i, moved);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            OnWarehouseChanged?.Invoke();
            SaveWarehouse();
        }
    }

    /// <summary>창고에 넣는다. 반환값 = 실제로 들어간 수량 (가득 차면 넣은 만큼만).</summary>
    private int AddGenericItem(InterfaceInventoryItem item, int quantity, bool notify = true)
    {
        if (item == null || quantity <= 0) return 0;

        int remaining = quantity;

        // 1. 스택 가능한 경우 기존 슬롯에 합치기
        if (item.Stackable)
        {
            foreach (var slot in warehouseSlots)
            {
                if (slot.item != null && slot.item.Id == item.Id)
                {
                    int canAdd = item.MaxStackSize - slot.quantity;
                    if (canAdd > 0)
                    {
                        int toAdd = Math.Min(remaining, canAdd);
                        slot.AddQuantity(toAdd);
                        remaining -= toAdd;
                        if (remaining <= 0) break;
                    }
                }
            }
        }

        // 2. 남은 수량 빈 슬롯에 추가
        while (remaining > 0)
        {
            int emptyIndex = warehouseSlots.FindIndex(s => s.item == null);
            if (emptyIndex != -1)
            {
                int toAdd = item.Stackable ? Math.Min(remaining, item.MaxStackSize) : 1;
                warehouseSlots[emptyIndex] = new InventorySlot(item, toAdd);
                remaining -= toAdd;
            }
            else
            {
                Debug.LogWarning($"[Warehouse] 창고가 가득 찼습니다! 버려짐: {item.DisplayName} x{remaining}");
                break;
            }
        }

        if (notify)
        {
            OnWarehouseChanged?.Invoke();
            SaveWarehouse();
        }

        return quantity - remaining;
    }

    public void AddItem(ItemSO item, int quantity) => AddGenericItem(item, quantity);
    public void AddMineral(MineralSO mineral, int quantity) => AddGenericItem(mineral, quantity);
    public void AddEquipment(EquipmentSO equipment, int quantity) => AddGenericItem(equipment, quantity);

    /// <summary>
    /// 외부에서 창고 변경 이벤트를 발생시키기 위한 공개 메서드
    /// </summary>
    public void NotifyWarehouseChanged()
    {
        OnWarehouseChanged?.Invoke();
        SaveWarehouse();
    }

    // --- 삭제 로직 ---

    public void RemoveItemAt(int index, int quantity)
    {
        if (index < 0 || index >= warehouseSlots.Count) return;
        
        var slot = warehouseSlots[index];
        if (slot == null || slot.item == null) return;

        if (slot.quantity <= quantity)
        {
            warehouseSlots[index] = new InventorySlot(null, 0); // 빈 슬롯으로 교체
        }
        else
        {
            slot.quantity -= quantity;
        }

        OnWarehouseChanged?.Invoke();
        SaveWarehouse();
    }

    public void RemoveMineralAt(int index, int quantity) => RemoveItemAt(index, quantity);
    public void RemoveEquipmentAt(int index, int quantity) => RemoveItemAt(index, quantity);

    // --- 종류별 삭제 (상점 판매 등에서 사용) ---
    public bool RemoveItem(ItemSO item, int quantity) => RemoveGenericInternal(item, quantity);
    public bool RemoveMineral(MineralSO mineral, int quantity) => RemoveGenericInternal(mineral, quantity);
    public bool RemoveEquipment(EquipmentSO equipment, int quantity) => RemoveGenericInternal(equipment, 1);

    private bool RemoveGenericInternal(InterfaceInventoryItem target, int quantity)
    {
        if (target == null || quantity <= 0) return false;

        // 전체 보유량 확인
        int total = warehouseSlots.Where(s => s.item != null && s.item.Id == target.Id).Sum(s => s.quantity);
        if (total < quantity) return false;

        int remain = quantity;
        for (int i = 0; i < warehouseSlots.Count; i++)
        {
            if (remain <= 0) break;
            var slot = warehouseSlots[i];
            if (slot.item != null && slot.item.Id == target.Id)
            {
                int take = Math.Min(slot.quantity, remain);
                slot.quantity -= take;
                remain -= take;
                if (slot.quantity <= 0) warehouseSlots[i] = new InventorySlot(null, 0);
            }
        }

        OnWarehouseChanged?.Invoke();
        SaveWarehouse();
        return true;
    }

    // 인벤토리로 꺼내기 (Withdraw)
    public bool WithdrawToInventory(ItemInventory targetInv, ItemSO item, int quantity) => WithdrawInternal(targetInv, item, quantity);
    public bool WithdrawToMineralInventory(MineralInventory targetInv, MineralSO mineral, int quantity) => WithdrawInternal(targetInv, mineral, quantity);
    public bool WithdrawToEquipmentInventory(EquipmentInventory targetInv, EquipmentSO equipment, int quantity) => WithdrawInternal(targetInv, equipment, quantity);

    private bool WithdrawInternal(MonoBehaviour targetInv, InterfaceInventoryItem item, int quantity)
    {
        // 해당 아이템이 있는 첫 번째 슬롯 찾기
        int index = warehouseSlots.FindIndex(s => s.item != null && s.item.Id == item.Id);
        if (index == -1) return false;

        var slot = warehouseSlots[index];
        int moveAmount = Math.Min(slot.quantity, quantity);
        int added = 0;

        if (targetInv is ItemInventory ii) added = ii.AddItem(item as ItemSO, moveAmount);
        else if (targetInv is MineralInventory mi) added = mi.AddItem(item as MineralSO, moveAmount);
        else if (targetInv is EquipmentInventory ei) added = ei.AddItem(item as EquipmentSO, moveAmount);

        if (added > 0)
        {
            RemoveItemAt(index, added);
            return true;
        }
        return false;
    }

    public int GetMineralCount(MineralSO mineral)
    {
        if (mineral == null) return 0;
        return warehouseSlots.Where(s => s.item != null && s.item.Id == mineral.Id).Sum(s => s.quantity);
    }

    // --- 유물 세션 편입 (창고 오버레이 전용) ---

    /// <summary>해당 슬롯이 '미장착 유물' 어댑터 칸인지.</summary>
    public static bool IsRelicSlot(InventorySlot s) => s != null && s.item is WarehouseRelicItem;

    /// <summary>
    /// 미장착 보유 유물을 창고 슬롯으로 편입/갱신한다(세션 전용). 창고 오버레이가 매 갱신마다 호출.
    /// 이미 얹혀 있는 유물은 <b>자리를 유지</b>해 세션 중 수동 배치가 보존되고,
    /// 장착돼 사라진 유물 칸은 비우며, 새로 미장착이 된 유물만 빈 칸(없으면 격자를 늘려)에 추가한다.
    /// 유물 손실을 막기 위해 용량을 넘겨서라도 담는다(용량 초과분은 닫을 때 <see cref="StripRelicSlots"/>가 정리).
    /// <b>OnWarehouseChanged를 발생시키지 않는다</b> — 이미 refresh 흐름 안에서 불리므로 재귀 갱신을 막기 위함.
    /// </summary>
    /// <param name="unequipped">현재 미장착인 보유 유물 (SO, 레벨) 목록.</param>
    public void SyncRelicSlots(IReadOnlyList<(Relic.Data.RelicSO so, int level)> unequipped)
    {
        var desired = new Dictionary<Relic.Data.RelicID, int>(); // id -> level
        if (unequipped != null)
            foreach (var r in unequipped)
                if (r.so != null) desired[r.so.id] = r.level;

        var present = new HashSet<Relic.Data.RelicID>();

        // 1. 기존 유물 칸 정리 — 더 이상 미장착이 아니면 비우고, 레벨이 바뀌었으면 자리 유지한 채 갱신
        for (int i = 0; i < warehouseSlots.Count; i++)
        {
            var it = warehouseSlots[i]?.item as WarehouseRelicItem;
            if (it == null) continue;

            if (!desired.TryGetValue(it.RelicId, out int lv))
            {
                warehouseSlots[i] = new InventorySlot(null, 0); // 장착됨/미보유 → 칸 비움
            }
            else
            {
                if (it.level != lv)
                    warehouseSlots[i] = new InventorySlot(new WarehouseRelicItem(it.so, lv), 1);
                present.Add(it.RelicId);
            }
        }

        // 2. 아직 안 얹힌 미장착 유물을 빈 칸(없으면 새 칸)에 추가
        if (unequipped != null)
        {
            foreach (var r in unequipped)
            {
                if (r.so == null || present.Contains(r.so.id)) continue;

                var slot = new InventorySlot(new WarehouseRelicItem(r.so, r.level), 1);
                int idx = warehouseSlots.FindIndex(s => s == null || s.item == null);
                if (idx >= 0) warehouseSlots[idx] = slot;
                else warehouseSlots.Add(slot); // 용량 초과 — 유물 손실 방지용 임시 확장
                present.Add(r.so.id);
            }
        }
    }

    /// <summary>편입했던 유물 칸을 전부 걷어내고 용량 초과로 늘어난 뒤쪽 빈 칸을 되돌린다(오버레이 닫을 때).</summary>
    public void StripRelicSlots()
    {
        bool changed = false;

        for (int i = 0; i < warehouseSlots.Count; i++)
        {
            if (warehouseSlots[i]?.item is WarehouseRelicItem)
            {
                warehouseSlots[i] = new InventorySlot(null, 0);
                changed = true;
            }
        }

        // 유물 편입으로 totalSlots를 넘겨 늘어난 뒤쪽 빈 칸 제거
        while (warehouseSlots.Count > totalSlots)
        {
            var last = warehouseSlots[warehouseSlots.Count - 1];
            if (last != null && last.item != null) break; // 실제 아이템이면 멈춤(정상적으론 없음)
            warehouseSlots.RemoveAt(warehouseSlots.Count - 1);
            changed = true;
        }

        if (changed)
        {
            OnWarehouseChanged?.Invoke();
            SaveWarehouse();
        }
    }

    // --- 스왑 및 정렬 ---

    public bool SwapSlots(int index1, int index2)
    {
        if (index1 < 0 || index1 >= warehouseSlots.Count || index2 < 0 || index2 >= warehouseSlots.Count)
            return false;
        
        var temp = warehouseSlots[index1];
        warehouseSlots[index1] = warehouseSlots[index2];
        warehouseSlots[index2] = temp;

        OnWarehouseChanged?.Invoke();
        SaveWarehouse();
        return true;
    }

    /// <summary>
    /// from 슬롯을 to 슬롯 위에 놓았을 때의 처리: 같은 스택형 아이템이면 <b>병합</b>(넘치면 남는 만큼 from에 유지),
    /// 그 외(다른 아이템·비스택·유물)는 <b>자리 교환</b>. 드래그 앤 드롭 한 방에서 이 판정을 담당한다.
    /// </summary>
    public bool MergeOrSwapSlots(int from, int to)
    {
        if (from == to) return false;
        if (from < 0 || from >= warehouseSlots.Count || to < 0 || to >= warehouseSlots.Count) return false;

        var fromSlot = warehouseSlots[from];
        var toSlot = warehouseSlots[to];

        // 같은 스택형 아이템 → 병합 (유물 등 비스택은 조건에서 걸러져 스왑으로 간다)
        if (fromSlot?.item != null && toSlot?.item != null &&
            fromSlot.item.Stackable && fromSlot.item.Id == toSlot.item.Id &&
            toSlot.quantity < toSlot.item.MaxStackSize)
        {
            int canAdd = toSlot.item.MaxStackSize - toSlot.quantity;
            int move = Math.Min(fromSlot.quantity, canAdd);
            toSlot.quantity += move;
            fromSlot.quantity -= move;
            if (fromSlot.quantity <= 0)
                warehouseSlots[from] = new InventorySlot(null, 0);

            OnWarehouseChanged?.Invoke();
            SaveWarehouse();
            return true;
        }

        // 병합 불가 → 자리 교환
        return SwapSlots(from, to);
    }

    /// <summary>
    /// from 슬롯에서 <paramref name="amount"/>개만 to 슬롯으로 옮긴다(손에 집기 '부분 내려놓기'용).
    /// - to가 비었으면 그만큼(비스택은 1개) 새로 놓는다.
    /// - to가 같은 스택형 아이템이면 남는 공간만큼 합친다.
    /// - to가 다른 아이템이면 부분 이동이 불가능하므로 옮기지 않는다(0 반환) — 전량일 때만 <see cref="MergeOrSwapSlots"/>가 스왑한다.
    /// 반환값 = 실제로 옮긴 개수.
    /// </summary>
    public int MovePartial(int from, int to, int amount)
    {
        if (from == to || amount <= 0) return 0;
        if (from < 0 || from >= warehouseSlots.Count || to < 0 || to >= warehouseSlots.Count) return 0;

        var fromSlot = warehouseSlots[from];
        if (fromSlot?.item == null) return 0;

        int move = Math.Min(amount, fromSlot.quantity);
        var toSlot = warehouseSlots[to];

        // to 비어 있음 → 그만큼 새로 놓기(비스택은 1개까지)
        if (toSlot == null || toSlot.item == null)
        {
            int cap = fromSlot.item.Stackable ? Math.Max(1, fromSlot.item.MaxStackSize) : 1;
            move = Math.Min(move, cap);
            warehouseSlots[to] = new InventorySlot(fromSlot.item, move);
        }
        // to 같은 스택형 아이템 → 남는 공간만큼 합치기
        else if (toSlot.item.Id == fromSlot.item.Id && fromSlot.item.Stackable)
        {
            int canAdd = toSlot.item.MaxStackSize - toSlot.quantity;
            if (canAdd <= 0) return 0;
            move = Math.Min(move, canAdd);
            toSlot.quantity += move;
        }
        // to 다른 아이템 → 부분 이동 불가
        else
        {
            return 0;
        }

        fromSlot.quantity -= move;
        if (fromSlot.quantity <= 0) warehouseSlots[from] = new InventorySlot(null, 0);

        OnWarehouseChanged?.Invoke();
        SaveWarehouse();
        return move;
    }

    // 호환성을 위한 구식 메서드들 매핑
    public bool SwapItemSlots(int i1, int i2) => SwapSlots(i1, i2);
    public bool SwapMineralSlots(int i1, int i2) => SwapSlots(i1, i2);
    public bool SwapEquipmentSlots(int i1, int i2) => SwapSlots(i1, i2);
    public bool SwapCrossTypeSlots(InventorySlotDragHandler.InventoryType t1, int i1, InventorySlotDragHandler.InventoryType t2, int i2)
    {
        // 팁: 이제 종류에 상관없이 전체 창고 슬롯 리스트의 절대 인덱스를 사용하거나, 
        // WarehouseUI에서 넘겨준 인덱스를 그대로 사용하여 스왑합니다.
        // 여기서는 WarehouseUI가 전달하는 인덱스가 AllSlots 기준이라고 가정합니다.
        return SwapSlots(i1, i2);
    }

    /// <summary>
    /// 창고 정렬 (동일 아이템 스택 병합 + 종류별/ID별 정렬)
    /// </summary>
    public void SortWarehouse()
    {
        // 1단계: 동일 아이템 스택 병합 (MaxStackSize 범위 내)
        MergeStacks();

        // 2단계: 아이템이 있는 슬롯만 추출
        var activeSlots = warehouseSlots.Where(s => s != null && s.item != null).ToList();
        
        // 3단계: 정렬 - 종류(Mineral → Item → Equipment) → 장비는 착용 부위(모자 → 몸통 → 신발 → 유물)
        //         → 같은 아이템 ID → 수량(내림차순)
        activeSlots.Sort((a, b) => {
            int typeOrderA = GetTypeSortOrder(a.item);
            int typeOrderB = GetTypeSortOrder(b.item);
            if (typeOrderA != typeOrderB) return typeOrderA.CompareTo(typeOrderB);

            int partOrderA = GetEquipmentPartSortOrder(a.item);
            int partOrderB = GetEquipmentPartSortOrder(b.item);
            if (partOrderA != partOrderB) return partOrderA.CompareTo(partOrderB);

            int idCompare = string.Compare(a.item.Id, b.item.Id, StringComparison.Ordinal);
            if (idCompare != 0) return idCompare;
            
            return b.quantity.CompareTo(a.quantity);
        });

        // 4단계: 다시 채우기
        warehouseSlots.Clear();
        foreach (var slot in activeSlots) warehouseSlots.Add(slot);
        while (warehouseSlots.Count < totalSlots) warehouseSlots.Add(new InventorySlot(null, 0));

        OnWarehouseChanged?.Invoke();
        SaveWarehouse();
    }

    /// <summary>
    /// 동일 아이템 슬롯들을 MaxStackSize 범위 내에서 병합
    /// </summary>
    private void MergeStacks()
    {
        for (int i = 0; i < warehouseSlots.Count; i++)
        {
            var targetSlot = warehouseSlots[i];
            if (targetSlot == null || targetSlot.item == null || !targetSlot.item.Stackable) continue;
            
            int maxStack = targetSlot.item.MaxStackSize;
            if (targetSlot.quantity >= maxStack) continue;

            for (int j = i + 1; j < warehouseSlots.Count; j++)
            {
                var sourceSlot = warehouseSlots[j];
                if (sourceSlot == null || sourceSlot.item == null) continue;
                if (sourceSlot.item.Id != targetSlot.item.Id) continue;

                int spaceLeft = maxStack - targetSlot.quantity;
                if (spaceLeft <= 0) break;

                int toMove = Math.Min(sourceSlot.quantity, spaceLeft);
                targetSlot.quantity += toMove;
                sourceSlot.quantity -= toMove;

                if (sourceSlot.quantity <= 0)
                {
                    warehouseSlots[j] = new InventorySlot(null, 0);
                }

                if (targetSlot.quantity >= maxStack) break;
            }
        }
    }

    private int GetTypeSortOrder(InterfaceInventoryItem item)
    {
        if (item is MineralSO) return 0;
        if (item is ItemSO) return 1;
        if (item is EquipmentSO) return 2;
        if (item is WarehouseRelicItem) return 3; // 미장착 유물은 장비 뒤에 한데 모은다
        return 4;
    }

    /// <summary>
    /// 장비끼리의 정렬 순서 — 착용 부위 기준(모자 → 몸통 → 신발 → 유물).
    /// EquipmentType 값이 이미 Head(1) → Clothes(2) → Shoes(3) → Relic(4) 순서라 그대로 쓰고,
    /// None만 뒤로 밀어낸다. 장비가 아니면 0(비교에 영향 없음).
    /// </summary>
    private int GetEquipmentPartSortOrder(InterfaceInventoryItem item)
    {
        var equipment = item as EquipmentSO;
        if (equipment == null) return 0;
        return equipment.equipmentType == EquipmentType.None ? int.MaxValue : (int)equipment.equipmentType;
    }

    /// <summary>
    /// 지정된 슬롯의 아이템과 같은 종류의 아이템을 다른 슬롯에서 모두 끌어모음 (MaxStackSize 제한)
    /// </summary>
    /// <param name="targetIndex">모을 대상 슬롯 인덱스</param>
    public void GatherItems(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= warehouseSlots.Count) return;

        var targetSlot = warehouseSlots[targetIndex];
        // 타겟 슬롯이 비어있거나 스택 불가능한 아이템이면 무시
        if (targetSlot == null || targetSlot.item == null || !targetSlot.item.Stackable)
            return;

        int maxStack = targetSlot.item.MaxStackSize;
        // 이미 꽉 찼으면 더 모을 수 없음
        if (targetSlot.quantity >= maxStack) return;

        bool changed = false;

        // 다른 모든 슬롯 확인
        for (int i = 0; i < warehouseSlots.Count; i++)
        {
            if (i == targetIndex) continue; // 자기 자신 제외

            var sourceSlot = warehouseSlots[i];
            // 같은 아이템 ID를 가진 슬롯 찾기
            if (sourceSlot != null && sourceSlot.item != null && sourceSlot.item.Id == targetSlot.item.Id)
            {
                // [수정] 이미 MaxStackSize만큼 꽉 찬 슬롯은 가져오지 않음
                if (sourceSlot.quantity >= maxStack) continue;

                // 가져올 수 있는 수량 계산
                int spaceLeft = maxStack - targetSlot.quantity;
                if (spaceLeft <= 0) break; // 공간 없음 (이미 꽉 참)

                int takeAmount = Math.Min(sourceSlot.quantity, spaceLeft);
                
                // 이동 실행
                targetSlot.quantity += takeAmount;
                sourceSlot.quantity -= takeAmount;

                // 소스 슬롯이 비었으면 초기화
                if (sourceSlot.quantity <= 0)
                {
                    warehouseSlots[i] = new InventorySlot(null, 0);
                }

                changed = true;
                
                // 만약 타겟 슬롯이 꽉 찼으면 종료
                if (targetSlot.quantity >= maxStack) break;
            }
        }

        if (changed)
        {
            OnWarehouseChanged?.Invoke();
            SaveWarehouse();
        }
    }

    // --- 데이터 저장/로드 (호환성 유지) ---

    public WarehouseData ToData()
    {
        var data = new WarehouseData();
        foreach (var slot in warehouseSlots)
        {
            if (slot.item == null) continue;
            // 유물은 세션 중에만 창고 슬롯으로 얹혀 있는 어댑터다 — 저장은 RelicSaveData가 담당하므로 제외.
            if (slot.item is WarehouseRelicItem) continue;

            if (slot.item is ItemSO item)
                data.itemInventory.slots.Add(new ItemSlotData { itemId = item.itemID.ToString(), quantity = slot.quantity });
            else if (slot.item is MineralSO mineral)
                data.mineralInventory.slots.Add(new MineralSlotData { itemId = mineral.mineralID.ToString(), quantity = slot.quantity });
            else if (slot.item is EquipmentSO equipment)
                data.equipmentInventory.slots.Add(new EquipmentSlotData { itemId = equipment.equipmentID.ToString(), quantity = slot.quantity });
        }
        return data;
    }

    public void FromData(WarehouseData data)
    {
        warehouseSlots.Clear();
        for (int i = 0; i < totalSlots; i++) warehouseSlots.Add(new InventorySlot(null, 0));

        if (data == null) return;

        // 로드 시에는 고정된 순서 없이 빈 자리부터 채워넣음 (또는 저장 시 순서를 보존하려면 WarehouseData도 수정 필요)
        // 여기서는 기존 데이터와의 호환성을 위해 순차적으로 추가함
        LoadItems(data.itemInventory?.slots);
        LoadMinerals(data.mineralInventory?.slots);
        LoadEquipments(data.equipmentInventory?.slots);

        OnWarehouseChanged?.Invoke();
    }

    private void LoadItems(List<ItemSlotData> slots)
    {
        if (slots == null) return;
        if (ItemDatabase.Instance == null)
        {
            Debug.LogError("[WarehouseManager] ItemDatabase.Instance가 null입니다.");
            return;
        }

        foreach (var s in slots)
        {
            if (string.IsNullOrEmpty(s.itemId) || s.itemId == "None") continue;

            if (Enum.TryParse<ItemID>(s.itemId, true, out var id))
            {
                var item = ItemDatabase.Instance.GetItemByID(id);
                if (item != null) 
                {
                    if (item.Icon == null) Debug.LogWarning($"[WarehouseManager] 아이템 {item.DisplayName} ({s.itemId})의 아이콘이 없습니다.");
                    AddGenericItem(item, s.quantity, false);
                }
                else Debug.LogWarning($"[WarehouseManager] ItemID '{s.itemId}'를 데이터베이스에서 찾을 수 없습니다.");
            }
            else Debug.LogError($"[WarehouseManager] ItemID 파싱 실패: '{s.itemId}'");
        }
    }

    private void LoadMinerals(List<MineralSlotData> slots)
    {
        if (slots == null) return;
        if (MineralDatabase.Instance == null)
        {
            Debug.LogError("[WarehouseManager] MineralDatabase.Instance가 null입니다.");
            return;
        }

        foreach (var s in slots)
        {
            if (string.IsNullOrEmpty(s.itemId) || s.itemId == "None") continue;

            if (Enum.TryParse<MineralID>(s.itemId, true, out var id))
            {
                var mineral = MineralDatabase.Instance.GetMineralByID(id);
                if (mineral != null) 
                {
                    if (mineral.Icon == null) Debug.LogWarning($"[WarehouseManager] 광물 {mineral.DisplayName} ({s.itemId})의 아이콘이 없습니다.");
                    AddGenericItem(mineral, s.quantity, false);
                }
                else Debug.LogWarning($"[WarehouseManager] MineralID '{s.itemId}'를 데이터베이스에서 찾을 수 없습니다.");
            }
            else Debug.LogError($"[WarehouseManager] MineralID 파싱 실패: '{s.itemId}'");
        }
    }

    private void LoadEquipments(List<EquipmentSlotData> slots)
    {
        if (slots == null) return;
        if (EquipmentDatabase.Instance == null)
        {
            Debug.LogError("[WarehouseManager] EquipmentDatabase.Instance가 null입니다.");
            return;
        }

        foreach (var s in slots)
        {
            if (string.IsNullOrEmpty(s.itemId) || s.itemId == "None") continue;

            if (Enum.TryParse<EquipmentID>(s.itemId, true, out var id))
            {
                var eq = EquipmentDatabase.Instance.GetEquipmentByID(id);
                if (eq != null) 
                {
                    if (eq.Icon == null) Debug.LogWarning($"[WarehouseManager] 장비 {eq.DisplayName} ({s.itemId})의 아이콘이 없습니다.");
                    AddGenericItem(eq, s.quantity, false);
                }
                else Debug.LogWarning($"[WarehouseManager] EquipmentID '{s.itemId}'를 데이터베이스에서 찾을 수 없습니다.");
            }
            else Debug.LogError($"[WarehouseManager] EquipmentID 파싱 실패: '{s.itemId}'");
        }
    }

    private void SaveWarehouse()
    {
        // GameManager 등을 통해 전체 저장이 호출되도록 함
        // SaveManager.Instance?.Save(); 
    }

    private void OnApplicationQuit() => _isQuitting = true;
}
