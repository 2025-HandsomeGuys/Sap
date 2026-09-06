using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using System;
using TMPro;
using UnityEngine.EventSystems;

public class WarehouseUI : MonoBehaviour, IDropHandler
{
    public static WarehouseUI Instance { get; private set; }

    [Header("UI 연결")]
    public GameObject warehousePanel;
    // public Button closeButton; // 닫기 버튼은 여전히 제거 상태 유지 (InventoryUI 종속)
    
    [Header("탭 버튼")]
    public Button allTabButton;
    public Button mineralTabButton;
    public Button itemTabButton;
    [UnityEngine.Serialization.FormerlySerializedAs("toolTabButton")]
    public Button equipmentTabButton;

    [Header("슬롯 컨테이너")]
    public Transform slotContainer;
    public ScrollRect scrollRect; // 스크롤 뷰 참조
    public GameObject slotPrefab;

    [Header("정보 표시")]
    public TextMeshProUGUI infoText; // 예: "아이템 인벤토리: 1/3"

    [Header("Localization")]
    public string labelBagKey = "ui_warehouse_bag";

    [Header("탭 스프라이트")]
    public Sprite tabSelectedSprite;
    public Sprite tabNormalSprite;

    private WarehouseManager warehouseManager;
    private ItemInventory itemInventory;
    private MineralInventory mineralInventory;
    private EquipmentInventory equipmentInventory;

    private enum Tab { All, Minerals, Items, Equipments }
    private Tab currentTab = Tab.All;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[WarehouseUI] 씬에 WarehouseUI가 2개 이상 있습니다. 이 인스턴스는 Instance로 등록되지 않습니다.", this);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        warehouseManager = WarehouseManager.Instance;
        itemInventory = FindFirstObjectByType<ItemInventory>();
        mineralInventory = FindFirstObjectByType<MineralInventory>();
        equipmentInventory = FindFirstObjectByType<EquipmentInventory>();

        if (warehousePanel != null)
        {
            // InventoryUI가 이미 활성화되어 있고 현재 씬이 지상이라면 패널을 닫지 않음
            bool isAlreadyOpenInInventory = InventoryUI.Instance != null && InventoryUI.Instance.IsOpen();
            if (!isAlreadyOpenInInventory)
            {
                warehousePanel.SetActive(false); // 기본적으로는 닫힘
            }
        }

        // 탭 버튼 리스너 연결
        if (allTabButton != null) allTabButton.onClick.AddListener(() => SwitchTab(Tab.All));
        if (mineralTabButton != null) mineralTabButton.onClick.AddListener(() => SwitchTab(Tab.Minerals));
        if (itemTabButton != null) itemTabButton.onClick.AddListener(() => SwitchTab(Tab.Items));
        if (equipmentTabButton != null) equipmentTabButton.onClick.AddListener(() => SwitchTab(Tab.Equipments));

        if (warehouseManager != null)
        {
            warehouseManager.OnWarehouseChanged += RefreshUI;
            RefreshUI(); // 초기 UI 업데이트
        }

        if (itemInventory != null) itemInventory.OnInventoryChanged += RefreshInfo;

        // 언어 변경 이벤트 구독
        if (LanguageManager.Instance != null)
        {
            LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
            LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (warehouseManager != null) warehouseManager.OnWarehouseChanged -= RefreshUI;
        if (itemInventory != null) itemInventory.OnInventoryChanged -= RefreshInfo;

        if (LanguageManager.Instance != null)
            LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(LanguageType newLanguage)
    {
        RefreshInfo();
    }

    void Update()
    {
        if (warehousePanel != null && warehousePanel.activeInHierarchy)
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                int newIndex = (int)currentTab - 1;
                if (newIndex < 0) newIndex = 3;
                SwitchTab((Tab)newIndex);
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                int newIndex = (int)currentTab + 1;
                if (newIndex > 3) newIndex = 0;
                SwitchTab((Tab)newIndex);
            }
        }
    }

    public void OpenWarehouse()
    {
        if (warehousePanel != null) warehousePanel.SetActive(true);
        SwitchTab(Tab.All); // 열릴 때 기본은 '전체' 탭
    }

    public void CloseWarehouse()
    {
        if (warehousePanel != null) warehousePanel.SetActive(false);
    }

    private void SwitchTab(Tab tab)
    {
        currentTab = tab;
        UpdateTabButtons();
        RefreshUI();
        
        // 탭 변경 시 스크롤 위로 초기화
        if (scrollRect != null)
        {
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }

    private void UpdateTabButtons()
    {
        SetTabState(allTabButton, currentTab == Tab.All);
        SetTabState(mineralTabButton, currentTab == Tab.Minerals);
        SetTabState(itemTabButton, currentTab == Tab.Items);
        SetTabState(equipmentTabButton, currentTab == Tab.Equipments);
    }

    private void SetTabState(Button btn, bool selected)
    {
        if (btn == null) return;
        Image img = btn.GetComponent<Image>();
        if (img != null)
        {
            if (tabSelectedSprite != null && tabNormalSprite != null)
                img.sprite = selected ? tabSelectedSprite : tabNormalSprite;
            
            // 누른 것(selected)을 어둡게, 누르지 않은 것을 밝게 변경
            img.color = selected ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
        }
    }

    public void OnClickAllTab() => SwitchTab(Tab.All);
    public void OnClickMineralsTab() => SwitchTab(Tab.Minerals);
    public void OnClickItemsTab() => SwitchTab(Tab.Items);
    public void OnClickEquipmentsTab() => SwitchTab(Tab.Equipments);

    /// <summary>
    /// 정렬 버튼 클릭 시 호출
    /// </summary>
    public void OnClickSort()
    {
        if (warehouseManager != null)
        {
            warehouseManager.SortWarehouse();
        }
    }

    private void RefreshUI()
    {
        if (warehousePanel == null || !warehousePanel.activeSelf) return;

        RefreshInfo();
        if (warehouseManager == null) return;

        // 1. 필요한 데이터 리스트 구성
        var displayList = new List<(InventorySlot slot, InventorySlotDragHandler.InventoryType type, int index)>();

        if (currentTab == Tab.All)
        {
            var allSlots = warehouseManager.AllSlots;
            for (int i = 0; i < allSlots.Count; i++)
            {
                var slot = allSlots[i];
                var type = InventorySlotDragHandler.InventoryType.WarehouseEmpty;
                
                if (slot != null && slot.item != null)
                {
                     if (slot.item is MineralSO) type = InventorySlotDragHandler.InventoryType.WarehouseMinerals;
                     else if (slot.item is EquipmentSO) type = InventorySlotDragHandler.InventoryType.WarehouseEquipments;
                     else type = InventorySlotDragHandler.InventoryType.WarehouseItems;
                }
                displayList.Add((slot, type, i));
            }
        }
        else
        {
            var allSlots = warehouseManager.AllSlots;
            for(int i=0; i<allSlots.Count; i++)
            {
                var slot = allSlots[i];
                // 탭 모드에서는 빈 슬롯 제외
                if (slot == null || slot.item == null) continue;

                if (currentTab == Tab.Minerals && slot.item is MineralSO)
                    displayList.Add((slot, InventorySlotDragHandler.InventoryType.WarehouseMinerals, i));
                else if (currentTab == Tab.Items && slot.item is ItemSO)
                    displayList.Add((slot, InventorySlotDragHandler.InventoryType.WarehouseItems, i));
                else if (currentTab == Tab.Equipments && slot.item is EquipmentSO)
                    displayList.Add((slot, InventorySlotDragHandler.InventoryType.WarehouseEquipments, i));
            }
        }

        // 2. 오브젝트 풀링 (기존 자식 재사용, 모자르면 생성, 남으면 비활성)
        int needed = displayList.Count;
        int current = slotContainer.childCount;

        // 모자란 만큼 생성
        for (int i = current; i < needed; i++)
        {
            Instantiate(slotPrefab, slotContainer);
        }

        // 갱신
        for (int i = 0; i < slotContainer.childCount; i++)
        {
            GameObject child = slotContainer.GetChild(i).gameObject;
            if (i < needed)
            {
                child.SetActive(true);
                var data = displayList[i];
                UpdateSlot(child, data.slot, data.type, data.index);
            }
            else
            {
                child.SetActive(false);
            }
        }

        // 컨텐츠 크기 갱신을 위해 레이아웃 재빌드 (한 프레임 대기 후 실행)
        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(UpdateScrollLayout());
        }
    }

    private System.Collections.IEnumerator UpdateScrollLayout()
    {
        yield return null; // 한 프레임 대기

        if (scrollRect != null && scrollRect.content != null)
        {
            // 스크롤 방향 고정 (가로 비활성, 세로 활성)
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic; // 탄성 스크롤 복구

            // 직접 높이 계산 및 적용
            ApplyGridHeight(scrollRect);
            
            // 스크롤 감도 설정
            scrollRect.scrollSensitivity = 20f;
        }
    }

    private void ApplyGridHeight(ScrollRect sRect)
    {
        if (sRect == null || sRect.content == null) return;
        
        RectTransform content = sRect.content;
        GridLayoutGroup grid = content.GetComponent<GridLayoutGroup>();
        if (grid == null) return;

        // ContentSizeFitter가 있다면 끈다
        ContentSizeFitter csf = content.GetComponent<ContentSizeFitter>();
        if (csf != null) csf.enabled = false;

        // 0. Anchor 및 Pivot 강제 설정 (Top-Stretch)
        // Anchor를 (0,1) ~ (1,1)로 잡아야 가로는 꽉 차고 세로는 sizeDelta.y로 제어됨
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        
        // 가로 길이는 0으로 만들어 Viewport 너비를 따르게 함 (Offset 0)
        Vector2 size = content.sizeDelta;
        size.x = 0;
        
        // 1. 실제 사용 가능한 너비 가져오기 (Viewport 기준)
        float width = 0f;
        if (sRect.viewport != null) 
            width = sRect.viewport.rect.width;
        else 
            width = content.parent is RectTransform p ? p.rect.width : content.rect.width; // Fallback

        // 2. 열(Column) 개수 계산
        int columns = 1;
        if (grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount)
        {
            columns = grid.constraintCount;
        }
        else
        {
            float cellWidth = grid.cellSize.x;
            float spacingX = grid.spacing.x;
            float paddingLeft = grid.padding.left;
            float paddingRight = grid.padding.right;

            float availableWidth = width - paddingLeft - paddingRight;
            if (availableWidth <= 0) availableWidth = cellWidth; 

            columns = Mathf.Max(1, Mathf.FloorToInt((availableWidth + spacingX) / (cellWidth + spacingX)));
        }

        // 3. 행(Row) 개수 계산
        int childCount = content.childCount;
        int rows = Mathf.CeilToInt((float)childCount / columns);
        if (rows < 0) rows = 0;

        // 4. 높이 계산
        float cellHeight = grid.cellSize.y;
        float spacingY = grid.spacing.y;
        float paddingTop = grid.padding.top;
        float paddingBottom = grid.padding.bottom;

        float requiredHeight = paddingTop + paddingBottom + (rows * cellHeight) + (Mathf.Max(0, rows - 1) * spacingY);

        // 5. 높이 적용
        size.y = requiredHeight;
        content.sizeDelta = size;
    }

    private void RefreshInfo()
    {
        if (infoText == null) return;
        
        // 카테고리별 슬롯 점유 수 계산
        int mineralSlots = warehouseManager?.StoredMinerals?.Count ?? 0;
        int itemSlots = warehouseManager?.StoredItems?.Count ?? 0;
        int equipmentSlots = warehouseManager?.StoredEquipments?.Count ?? 0;

        // 현재 점유 중인 총 슬롯 계산
        int totalUsedSlots = mineralSlots + itemSlots + equipmentSlots;
        int maxSlots = warehouseManager?.totalSlots ?? 0;

        // 모든 탭에서 동일하게 전체 상태 표시
        infoText.text = $"{(LanguageManager.Instance?.L(labelBagKey) ?? labelBagKey)} [ {totalUsedSlots} / {maxSlots} ]";
    }

    private void UpdateSlot(GameObject go, InventorySlot slot, InventorySlotDragHandler.InventoryType type, int index)
    {
        // 1. 아이콘 및 수량 설정
        Image icon = go.transform.Find("ItemIcon")?.GetComponent<Image>();
        TextMeshProUGUI qty = go.transform.Find("ItemQuantity")?.GetComponent<TextMeshProUGUI>();

        bool hasItem = (slot != null && slot.item != null);

        if (icon != null)
        {
            if (hasItem)
            {
                icon.sprite = slot.item.Icon;
                icon.enabled = true;
                icon.color = Color.white;
            }
            else
            {
                icon.enabled = false;
            }
        }

        if (qty != null)
        {
            qty.text = (hasItem && slot.item.Stackable && slot.quantity > 1) ? slot.quantity.ToString() : "";
        }
        
        // 2. 드래그 핸들러 갱신
        InventorySlotDragHandler dragHandler = go.GetComponent<InventorySlotDragHandler>();
        if (dragHandler == null) dragHandler = go.AddComponent<InventorySlotDragHandler>();
        
        dragHandler.warehouseUI = this;
        dragHandler.inventoryUI = null;
        dragHandler.inventoryType = type;
        dragHandler.slotIndex = index;

        // 3. 클릭 핸들러 갱신
        InventorySlotClickHandler clickHandler = go.GetComponent<InventorySlotClickHandler>();
        if (clickHandler == null)
        {
            clickHandler = go.AddComponent<InventorySlotClickHandler>();
        }
        clickHandler.inventoryUI = null;
        clickHandler.warehouseUI = this;
        clickHandler.slotIndex = index;
        clickHandler.inventoryType = type;

        // 4. 버튼 이벤트 제거 (충돌 방지)
        Button btn = go.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners();
        }

        // 5. 툴팁 핸들러 갱신
        InventorySlotTooltipProvider tooltipProvider = go.GetComponent<InventorySlotTooltipProvider>();
        if (tooltipProvider == null) tooltipProvider = go.AddComponent<InventorySlotTooltipProvider>();
        
        TooltipTrigger tooltipTrigger = go.GetComponent<TooltipTrigger>();
        if (tooltipTrigger == null) tooltipTrigger = go.AddComponent<TooltipTrigger>();

        tooltipProvider.InitializeForWarehouse(slot, this);
        tooltipTrigger.tooltipProvider = tooltipProvider;
    }

    private void OnSlotClicked(InventorySlot slot)
    {
        if (warehouseManager == null) return;

        bool success = false;
        
        if (slot.item is ItemSO itemSO && itemInventory != null)
        {
            // 아이템은 1개씩 이동 시도? 
            // "Select 3 items" implies picking specific amount. 
            // 1 click = 1 item move logic is safe.
            success = warehouseManager.WithdrawToInventory(itemInventory, itemSO, 1);
            if (!success) Debug.Log("가방이 가득 찼거나 이동할 수 없습니다.");
        }
        else if (slot.item is EquipmentSO equipmentSO && equipmentInventory != null)
        {
            success = warehouseManager.WithdrawToEquipmentInventory(equipmentInventory, equipmentSO, 1);
            if (!success) Debug.Log("장비함이 가득 찼거나 이동할 수 없습니다.");
        }
        else if (slot.item is MineralSO mineralSO && mineralInventory != null)
        {
             success = warehouseManager.WithdrawToMineralInventory(mineralInventory, mineralSO, 1);
             if (!success) Debug.Log("광물 가방이 가득 찼거나 이동할 수 없습니다.");
        }
    }

    public void OnDrop(PointerEventData eventData)
    {
        InventorySlotDragHandler draggedHandler = eventData.pointerDrag?.GetComponent<InventorySlotDragHandler>();
        if (draggedHandler == null) return;

        // 드래그된 아이템 가져오기
        InventorySlot draggedSlot = draggedHandler.GetSlotItem();

        if (draggedSlot == null || draggedSlot.item == null) return;

        bool success = false;

        // 계열에 따른 처리
        if (draggedSlot.item is EquipmentSO eq && draggedHandler.inventoryType == InventorySlotDragHandler.InventoryType.Equipments)
        {
            if (warehouseManager != null)
            {
                warehouseManager.AddEquipment(eq, 1);
                equipmentInventory.RemoveItemAt(draggedHandler.slotIndex, 1);
                success = true;
            }
        }
        else if (draggedSlot.item is ItemSO item && draggedHandler.inventoryType == InventorySlotDragHandler.InventoryType.Items)
        {
            if (warehouseManager != null)
            {
                warehouseManager.AddItem(item, draggedSlot.quantity);
                itemInventory.RemoveItem(item, draggedSlot.quantity);
                success = true;
            }
        }
        else if (draggedSlot.item is MineralSO mineral && draggedHandler.inventoryType == InventorySlotDragHandler.InventoryType.Minerals)
        {
            if (warehouseManager != null)
            {
                warehouseManager.AddMineral(mineral, draggedSlot.quantity);
                mineralInventory.RemoveItemAt(draggedHandler.slotIndex, draggedSlot.quantity);
                success = true;
            }
        }

        if (success)
        {
            draggedHandler.CleanupDragObject();
        }
    }

    /// <summary>
    /// 창고 슬롯 간 위치 교환 (드래그 앤 드롭용)
    /// </summary>
    public bool SwapWarehouseSlots(int index1, int index2)
    {
        if (warehouseManager == null) return false;
        return warehouseManager.SwapSlots(index1, index2);
    }
}
