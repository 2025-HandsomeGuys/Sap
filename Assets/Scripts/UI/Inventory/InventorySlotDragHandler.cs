using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [Header("참조")]
    public InventoryUI inventoryUI;

    public WarehouseUI warehouseUI; // 창고 UI 참조
    public int slotIndex; // 이 슬롯의 인벤토리 내 인덱스
    public InventoryType inventoryType; // Items, Minerals, Tools, WarehouseItems, WarehouseMinerals, WarehouseTools 중 하나

    private Canvas canvas;
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private GameObject dragObject;
    private Vector2 dragOffset; // 마우스 위치와 슬롯 중심의 오프셋
    public static InventorySlotDragHandler currentDraggingHandler; // 현재 드래그 중인 핸들러
    
    // 스크롤 뷰 지원을 위한 참조
    private ScrollRect parentScrollRect;

    // 외부에서 접근 가능하도록 public 프로퍼티 추가
    public CanvasGroup CanvasGroup => canvasGroup;

    public enum InventoryType
    {
        Items,
        Minerals,
        Equipments,
        WarehouseItems,
        WarehouseMinerals,
        WarehouseEquipments,
        WarehouseEmpty // 빈 창고 슬롯 (모든 종류 수용 가능)
    }

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        // Canvas 찾기
        canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            canvas = FindFirstObjectByType<Canvas>();
        }

        // 부모 스크롤 뷰 찾기 (스크롤 지원)
        parentScrollRect = GetComponentInParent<ScrollRect>();
    }

    void OnDisable()
    {
        // 비활성화될 때 드래그 중이었다면 정리 (잔여 객체 방지)
        if (currentDraggingHandler == this)
        {
            CleanupDragObject();
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // InventoryUI 또는 ShopMineralInventoryUI가 열려있는지 확인
        bool isOpen = false;
        if (inventoryUI != null)
            isOpen = inventoryUI.IsOpen();
        else if (warehouseUI != null)
            isOpen = warehouseUI.warehousePanel.activeSelf; 
        
        if (!isOpen)
            return;

        // [수정] 광물 인벤토리도 휴지통 드래그를 위해 드래그를 허용합니다. (Swap은 MineralInventory에서 차단됨)
        /*
        if (inventoryType == InventoryType.Minerals)
        {
            if (parentScrollRect != null) parentScrollRect.OnBeginDrag(eventData);
            return;
        }
        */

        // [추가] 지하씬에서의 드래그 제한
        bool isUnderground = inventoryUI != null && inventoryUI.IsUndergroundScene();
        if (isUnderground && (inventoryType == InventoryType.Equipments || inventoryType == InventoryType.Items))
        {
            // 지하씬에서는 장비 해제(드래그) 및 아이템 드래그(클릭 시 사용되므로)를 차단
            return;
        }

        // 빈 슬롯(WarehouseEmpty) 또는 아이템이 없으면 드래그 불가
        if (inventoryType == InventoryType.WarehouseEmpty || GetSlotItem() == null)
        {
            if (parentScrollRect != null) parentScrollRect.OnBeginDrag(eventData);
            return;
        }

        // 현재 드래그 중인 핸들러로 설정
        currentDraggingHandler = this;

        // 드래그 중인 객체 생성 (시각적 피드백)
        dragObject = Instantiate(gameObject, canvas.transform);
        dragObject.name = "DragObject";
        RectTransform dragRect = dragObject.GetComponent<RectTransform>();
        
        // 원본 슬롯의 모든 RectTransform 속성 복사
        dragRect.sizeDelta = rectTransform.sizeDelta;
        dragRect.anchorMin = new Vector2(0.5f, 0.5f); // 중앙 앵커로 설정
        dragRect.anchorMax = new Vector2(0.5f, 0.5f);
        dragRect.pivot = new Vector2(0.5f, 0.5f); // 중앙 피벗으로 설정
        dragRect.rotation = rectTransform.rotation;
        dragRect.localScale = rectTransform.localScale;
        
        // 마우스 위치를 Canvas 로컬 좌표로 변환
        Vector2 mouseLocalPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            eventData.position,
            canvas.worldCamera != null ? canvas.worldCamera : (canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main),
            out mouseLocalPos);
        
        // 원본 슬롯의 중심 위치를 Canvas 로컬 좌표로 변환
        Vector2 slotCenterPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.transform as RectTransform,
            RectTransformUtility.WorldToScreenPoint(
                canvas.worldCamera != null ? canvas.worldCamera : (canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main),
                rectTransform.position),
            canvas.worldCamera != null ? canvas.worldCamera : (canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main),
            out slotCenterPos);
        
        // 마우스 위치와 슬롯 중심의 오프셋 계산
        dragOffset = slotCenterPos - mouseLocalPos;
        
        // 드래그 객체를 마우스 위치 + 오프셋에 배치
        dragRect.anchoredPosition = mouseLocalPos + dragOffset;

        // 원본 슬롯 반투명하게
        canvasGroup.alpha = 0.6f;
        canvasGroup.blocksRaycasts = false;

        // 드래그 객체 설정
        CanvasGroup dragCanvasGroup = dragObject.GetComponent<CanvasGroup>();
        if (dragCanvasGroup == null)
            dragCanvasGroup = dragObject.AddComponent<CanvasGroup>();
        dragCanvasGroup.alpha = 0.8f;
        dragCanvasGroup.blocksRaycasts = false;

        // 드래그 객체의 배경 이미지 비활성화
        Image dragBg = dragObject.GetComponent<Image>();
        if (dragBg != null) dragBg.enabled = false;

        // 드래그 객체의 버튼들 비활성화
        Button[] buttons = dragObject.GetComponentsInChildren<Button>();
        foreach (var btn in buttons)
        {
            btn.enabled = false;
        }

        // 드래그 핸들러 제거 (무한 루프 방지)
        InventorySlotDragHandler dragHandler = dragObject.GetComponent<InventorySlotDragHandler>();
        if (dragHandler != null)
            Destroy(dragHandler);

        // 장비 드래그 시 하이라이트 표시
        InventorySlot draggedItemSlot = GetSlotItem();
        if (draggedItemSlot != null && draggedItemSlot.item is EquipmentSO eq)
        {
            if (InventoryUI.Instance != null)
                InventoryUI.Instance.HighlightEquipmentSlot(eq);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        // 드래그 객체가 없으면 스크롤 뷰로 이벤트 전달
        if (dragObject == null)
        {
            if (parentScrollRect != null) parentScrollRect.OnDrag(eventData);
            return;
        }

        RectTransform dragRect = dragObject.GetComponent<RectTransform>();
        if (dragRect != null && canvas != null)
        {
            // 현재 마우스 위치를 Canvas 로컬 좌표로 변환
            Vector2 mouseLocalPos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.transform as RectTransform,
                eventData.position,
                canvas.worldCamera != null ? canvas.worldCamera : (canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main),
                out mouseLocalPos);
            
            // 마우스 위치 + 오프셋으로 드래그 객체 배치
            dragRect.anchoredPosition = mouseLocalPos + dragOffset;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // 스크롤 뷰로 이벤트 전달
        if (parentScrollRect != null) parentScrollRect.OnEndDrag(eventData);

        // 드래그 객체 제거
        CleanupDragObject();

        // 원본 슬롯 복원
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
        }

        // 현재 드래그 중인 핸들러 초기화
        if (currentDraggingHandler == this)
        {
            currentDraggingHandler = null;
        }
    }

    public void CleanupDragObject()
    {
        if (dragObject != null)
        {
            Destroy(dragObject);
            dragObject = null;
        }

        // 원본 슬롯 복원
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
        }

        // 현재 드래그 중인 핸들러 초기화
        if (currentDraggingHandler == this)
        {
            currentDraggingHandler = null;
        }

        // 드래그 종료 시 하이라이트 제거
        if (InventoryUI.Instance != null)
            InventoryUI.Instance.ClearEquipmentSlotHighlights();
    }

    public void OnDrop(PointerEventData eventData)
    {
        // InventoryUI 또는 ShopMineralInventoryUI가 열려있는지 확인
        bool isOpen = false;
        if (inventoryUI != null)
            isOpen = inventoryUI.IsOpen();
        else if (warehouseUI != null)
            isOpen = warehouseUI.warehousePanel.activeSelf;
        
        if (!isOpen)
            return;

        // 드래그 중인 슬롯 찾기
        InventorySlotDragHandler draggedHandler = currentDraggingHandler;
        
        // 찾지 못했으면 다른 방법으로 찾기
        if (draggedHandler == null)
        {
            if (eventData.pointerDrag != null)
            {
                draggedHandler = eventData.pointerDrag.GetComponent<InventorySlotDragHandler>();
            }
            if (draggedHandler == null)
            {
                draggedHandler = FindDraggedSlot();
            }
        }

        if (draggedHandler == null || draggedHandler == this)
            return;

        // 드래그된 아이템 가져오기
        InventorySlot draggedSlot = GetDraggedSlotItem(draggedHandler);
        if (draggedSlot == null || draggedSlot.item == null) return;

        // [핵심] 호환성 체크 - 동일한 계열인지 확인
        bool isCompatible = false;
        
        // 빈 슬롯(WarehouseEmpty)은 모든 종류를 받을 수 있음
        if (this.inventoryType == InventoryType.WarehouseEmpty)
        {
            // 창고 계열 또는 플레이어 인벤토리 계열 모두 허용
            if (draggedHandler.inventoryType == InventoryType.WarehouseItems ||
                draggedHandler.inventoryType == InventoryType.WarehouseMinerals ||
                draggedHandler.inventoryType == InventoryType.WarehouseEquipments ||
                draggedHandler.inventoryType == InventoryType.Items ||
                draggedHandler.inventoryType == InventoryType.Minerals ||
                draggedHandler.inventoryType == InventoryType.Equipments)
            {
                isCompatible = true;
            }
        }
        // 창고 내부에서는 모든 종류 간 이동 허용 (전체 탭 지원)
        else if ((this.inventoryType == InventoryType.WarehouseItems || 
                  this.inventoryType == InventoryType.WarehouseMinerals || 
                  this.inventoryType == InventoryType.WarehouseEquipments) &&
                 (draggedHandler.inventoryType == InventoryType.WarehouseItems || 
                  draggedHandler.inventoryType == InventoryType.WarehouseMinerals || 
                  draggedHandler.inventoryType == InventoryType.WarehouseEquipments ||
                  draggedHandler.inventoryType == InventoryType.WarehouseEmpty ||
                  draggedHandler.inventoryType == InventoryType.Items ||
                  draggedHandler.inventoryType == InventoryType.Minerals ||
                  draggedHandler.inventoryType == InventoryType.Equipments))
        {
            isCompatible = true;
        }
        // 1. 아이템 계열
        else if ((this.inventoryType == InventoryType.Items || this.inventoryType == InventoryType.WarehouseItems) &&
            (draggedHandler.inventoryType == InventoryType.Items || draggedHandler.inventoryType == InventoryType.WarehouseItems))
            isCompatible = true;
        // 2. 광물 계열
        else if ((this.inventoryType == InventoryType.Minerals || this.inventoryType == InventoryType.WarehouseMinerals) &&
                 (draggedHandler.inventoryType == InventoryType.Minerals || draggedHandler.inventoryType == InventoryType.WarehouseMinerals))
            isCompatible = true;
        // 3. 장비 계열 (장착 슬롯 <-> 창고 장비)
        else if ((this.inventoryType == InventoryType.Equipments || this.inventoryType == InventoryType.WarehouseEquipments) &&
                 (draggedHandler.inventoryType == InventoryType.Equipments || draggedHandler.inventoryType == InventoryType.WarehouseEquipments))
            isCompatible = true;

        if (!isCompatible)
        {
            Debug.Log($"[Inventory] {draggedHandler.inventoryType}에서 {this.inventoryType}으로는 아이템을 이동할 수 없습니다.");
            return;
        }

        // --- 장비 장착/교체 특수 처리 (Equipments 타겟일 때) ---
        if (this.inventoryType == InventoryType.Equipments)
        {
            if (draggedSlot.item is EquipmentSO eq)
            {
                if (inventoryUI != null && inventoryUI.equipmentInventory != null)
                {
                    EquipmentSO previous = null;
                    // EquipAt 내부에서 타입 검증(머리/옷/신발)도 수행함
                    if (inventoryUI.equipmentInventory.EquipAt(this.slotIndex, eq, out previous))
                    {
                        // 원본 위치에서 제거
                        if (draggedHandler.inventoryType == InventoryType.WarehouseEquipments)
                            WarehouseManager.Instance.RemoveEquipmentAt(draggedHandler.slotIndex, 1);
                        else if (draggedHandler.inventoryType == InventoryType.Equipments)
                            inventoryUI.equipmentInventory.RemoveItemAt(draggedHandler.slotIndex, 1);
                        else if (draggedHandler.inventoryType == InventoryType.Items)
                        {
                            var itemInv = draggedHandler.inventoryUI != null ? draggedHandler.inventoryUI.itemInventory : FindFirstObjectByType<ItemInventory>();
                            if (itemInv != null) itemInv.RemoveItem(eq, 1);
                        }

                        // 교체된 기존 장비가 있으면 창고로 반환 (장비는 Warehouse ↔ EquipmentInventory만 가능)
                        if (previous != null)
                        {
                            if (WarehouseManager.Instance != null)
                                WarehouseManager.Instance.AddEquipment(previous, 1);
                        }
                        draggedHandler.CleanupDragObject();
                    }
                }
                return;
            }
        }

        // --- [추가] 창고 내부 이동 전용 처리 (같은 창고 내에서는 타입 상관없이 스왑/스택) ---
        if (IsWarehouseType(draggedHandler.inventoryType) && IsWarehouseType(this.inventoryType))
        {
            if (draggedHandler.slotIndex == this.slotIndex) return; // 같은 슬롯

            var wm = WarehouseManager.Instance;
            if (wm != null && warehouseUI != null)
            {
                var fromSlot = wm.AllSlots[draggedHandler.slotIndex];
                var toSlot = wm.AllSlots[this.slotIndex];

                // 같은 아이템이고 스택 가능한 경우 합치기 시도
                if (fromSlot != null && fromSlot.item != null && toSlot != null && toSlot.item != null &&
                    fromSlot.item.Id == toSlot.item.Id && fromSlot.item.Stackable)
                {
                    int maxStack = fromSlot.item.MaxStackSize;
                    int canAdd = maxStack - toSlot.quantity;
                    
                    if (canAdd > 0)
                    {
                        int toAdd = Mathf.Min(fromSlot.quantity, canAdd);
                        toSlot.quantity += toAdd;
                        fromSlot.quantity -= toAdd;
                        
                        if (fromSlot.quantity <= 0) {
                            wm.AllSlots[draggedHandler.slotIndex] = new InventorySlot(null, 0);
                        }
                        
                        wm.NotifyWarehouseChanged();
                        draggedHandler.CleanupDragObject();
                        return;
                    }
                }

                // 합칠 수 없으면 그냥 스왑
                if (warehouseUI.SwapWarehouseSlots(draggedHandler.slotIndex, this.slotIndex))
                {
                    draggedHandler.CleanupDragObject();
                    return;
                }
            }
        }

        // --- [수정] 인벤토리 <-> 창고 간 전송 처리 (서로 다른 타입인 경우) ---
        if (draggedHandler.inventoryType != this.inventoryType)
        {
            bool isTransferSuccess = false;

            // 목적지가 창고 슬롯인 경우 (가방 -> 창고)
            if (IsWarehouseType(this.inventoryType))
            {
                var wm = WarehouseManager.Instance;
                var targetSlot = (this.slotIndex >= 0 && this.slotIndex < wm.AllSlots.Count) ? wm.AllSlots[this.slotIndex] : null;
                
                // 특정 슬롯이 비어있거나 같은 아이템인 경우 (Stacking 지원)
                bool canStackAtSlot = (targetSlot != null && targetSlot.item != null && 
                                       draggedSlot.item != null && targetSlot.item.Id == draggedSlot.item.Id && 
                                       targetSlot.item.Stackable && targetSlot.quantity < targetSlot.item.MaxStackSize);
                bool canInsertAtSlot = (targetSlot != null && targetSlot.item == null);

                // 드래그 주체가 플레이어 인벤토리인 경우
                if (draggedHandler.inventoryType == InventoryType.Items && draggedSlot.item is ItemSO item)
                {
                    var itemInv = draggedHandler.inventoryUI != null ? draggedHandler.inventoryUI.itemInventory : FindFirstObjectByType<ItemInventory>();
                    if (itemInv != null)
                    {
                        if (canStackAtSlot) {
                            int toAdd = Mathf.Min(draggedSlot.quantity, targetSlot.item.MaxStackSize - targetSlot.quantity);
                            targetSlot.quantity += toAdd;
                            itemInv.RemoveItem(item, toAdd);
                            wm.NotifyWarehouseChanged();
                            isTransferSuccess = (toAdd == draggedSlot.quantity); // 전체 다 옮겨졌을 때만 성공 처리 (부분 이동 시 드래그 객체 유지 위해)
                        } else if (canInsertAtSlot) {
                            wm.AllSlots[this.slotIndex] = new InventorySlot(item, draggedSlot.quantity);
                            itemInv.RemoveItem(item, draggedSlot.quantity);
                            wm.NotifyWarehouseChanged();
                            isTransferSuccess = true;
                        } else {
                            wm.AddItem(item, draggedSlot.quantity);
                            itemInv.RemoveItem(item, draggedSlot.quantity);
                            isTransferSuccess = true;
                        }
                    }
                }
                else if (draggedHandler.inventoryType == InventoryType.Minerals && draggedSlot.item is MineralSO mineral)
                {
                    var mineralInv = draggedHandler.inventoryUI != null ? draggedHandler.inventoryUI.mineralInventory : FindFirstObjectByType<MineralInventory>();
                    if (mineralInv != null)
                    {
                        if (canStackAtSlot) {
                            int toAdd = Mathf.Min(draggedSlot.quantity, targetSlot.item.MaxStackSize - targetSlot.quantity);
                            targetSlot.quantity += toAdd;
                            mineralInv.RemoveItemAt(draggedHandler.slotIndex, toAdd);
                            wm.NotifyWarehouseChanged();
                            isTransferSuccess = (toAdd == draggedSlot.quantity);
                        } else if (canInsertAtSlot) {
                            wm.AllSlots[this.slotIndex] = new InventorySlot(mineral, draggedSlot.quantity);
                            mineralInv.RemoveItemAt(draggedHandler.slotIndex, draggedSlot.quantity);
                            wm.NotifyWarehouseChanged();
                            isTransferSuccess = true;
                        } else {
                            wm.AddMineral(mineral, draggedSlot.quantity);
                            mineralInv.RemoveItemAt(draggedHandler.slotIndex, draggedSlot.quantity);
                            isTransferSuccess = true;
                        }
                    }
                }
                else if (draggedHandler.inventoryType == InventoryType.Equipments && draggedSlot.item is EquipmentSO eq)
                {
                    var equipInv = draggedHandler.inventoryUI != null ? draggedHandler.inventoryUI.equipmentInventory : FindFirstObjectByType<EquipmentInventory>();
                    if (equipInv != null)
                    {
                        if (canInsertAtSlot) {
                            wm.AllSlots[this.slotIndex] = new InventorySlot(eq, 1);
                            wm.NotifyWarehouseChanged();
                        } else {
                            wm.AddEquipment(eq, 1);
                        }
                        equipInv.RemoveItemAt(draggedHandler.slotIndex, 1);
                        isTransferSuccess = true;
                    }
                }
            }
            // 목적지가 플레이어 인벤토리인 경우 (창고 -> 가방)
            else if (IsWarehouseType(draggedHandler.inventoryType))
            {
                // [수정] 이동 가능 여부 체크 — 광물만 제한 (장비/아이템은 항상 이동 가능)
                if (WarehouseManager.Instance != null && !WarehouseManager.Instance.canWithdrawToInventory
                    && draggedSlot.item is MineralSO)
                {
                    Debug.Log("창고에서 광물을 꺼낼 수 없는 상태입니다 (토글 꺼짐).");
                    return;
                }

                var targetInvUI = this.inventoryUI != null ? this.inventoryUI : FindFirstObjectByType<InventoryUI>();
                if (targetInvUI != null)
                {
                    if (draggedSlot.item is ItemSO item && this.inventoryType == InventoryType.Items)
                    {
                        int added = targetInvUI.itemInventory.AddItem(item, draggedSlot.quantity);
                        if (added > 0)
                        {
                            WarehouseManager.Instance.RemoveItemAt(draggedHandler.slotIndex, added);
                            isTransferSuccess = true;
                        }
                    }
                    else if (draggedSlot.item is MineralSO mineral && this.inventoryType == InventoryType.Minerals)
                    {
                        int added = targetInvUI.mineralInventory.AddItem(mineral, draggedSlot.quantity);
                        if (added > 0)
                        {
                            WarehouseManager.Instance.RemoveMineralAt(draggedHandler.slotIndex, added);
                            isTransferSuccess = true;
                        }
                    }
                    else if (draggedSlot.item is EquipmentSO eq && this.inventoryType == InventoryType.Equipments)
                    {
                        EquipmentSO previous = null;
                        if (targetInvUI.equipmentInventory.EquipAt(this.slotIndex, eq, out previous))
                        {
                            WarehouseManager.Instance.RemoveEquipmentAt(draggedHandler.slotIndex, 1);
                            if (previous != null) WarehouseManager.Instance.AddEquipment(previous, 1);
                            isTransferSuccess = true;
                        }
                    }
                }
            }

            if (isTransferSuccess)
            {
                draggedHandler.CleanupDragObject();
                return;
            }
        }

        // 일반적인 인벤토리 내의 정렬 처리
        if (draggedHandler.inventoryType != this.inventoryType)
            return;

        // 마우스 위치에 따라 삽입 또는 교환 결정
        Vector2 mousePos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform,
            eventData.position,
            canvas != null && canvas.worldCamera != null ? canvas.worldCamera : null,
            out mousePos);

        int fromIndex = draggedHandler.slotIndex;
        int toIndex = this.slotIndex;

        // 슬롯의 높이를 기준으로 상단/하단 절반 판단
        float slotHeight = rectTransform.rect.height;
        bool insertBefore = mousePos.y > slotHeight * 0.25f; // 상단 25% 영역에 있으면 앞에 삽입
        bool insertAfter = mousePos.y < -slotHeight * 0.25f; // 하단 25% 영역에 있으면 뒤에 삽입

        bool success = false;
        
        // 같은 슬롯이면 아무것도 하지 않음
        if (fromIndex == toIndex)
        {
            success = true; // 이미 같은 위치
        }
        // 창고 슬롯 간 스왑 (자유로운 위치 교환 지원)
        else if (IsWarehouseType(inventoryType) && IsWarehouseType(draggedHandler.inventoryType))
        {
            if (warehouseUI != null)
            {
                success = warehouseUI.SwapWarehouseSlots(fromIndex, toIndex);
            }
        }
        // 앞에 삽입
        else if (insertBefore && toIndex > fromIndex)
        {
            if (inventoryUI != null)
                success = inventoryUI.MoveInventorySlot(inventoryType, fromIndex, toIndex);

        }
        // 뒤에 삽입
        else if (insertAfter && toIndex < fromIndex)
        {
            if (inventoryUI != null)
                success = inventoryUI.MoveInventorySlot(inventoryType, fromIndex, toIndex + 1);

        }
        // 중간 영역이거나 삽입이 불가능한 경우 교환
        else
        {
            if (inventoryUI != null)
                success = inventoryUI.SwapInventorySlots(inventoryType, fromIndex, toIndex);

        }

        if (success)
        {
            // 드래그 객체 제거
            draggedHandler.CleanupDragObject();
            // UI 업데이트는 인벤토리 변경 이벤트로 자동 처리됨
        }
    }

    private InventorySlotDragHandler FindDraggedSlot()
    {
        // 드래그 시작한 슬롯 찾기 (alpha가 낮은 슬롯)
        Transform container = GetContainer();
        if (container == null) return null;

        for (int i = 0; i < container.childCount; i++)
        {
            Transform child = container.GetChild(i);
            InventorySlotDragHandler handler = child.GetComponent<InventorySlotDragHandler>();
            if (handler != null && handler.inventoryType == inventoryType)
            {
                CanvasGroup cg = child.GetComponent<CanvasGroup>();
                if (cg != null && cg.alpha < 1f)
                {
                    return handler;
                }
            }
        }
        return null;
    }

    private Transform GetContainer()
    {

        
        // WarehouseUI 사용 시
        if (warehouseUI != null)
        {
            return warehouseUI.slotContainer;
        }

        // InventoryUI 사용 시
        if (inventoryUI == null) return null;

        switch (inventoryType)
        {
            case InventoryType.Items:
                return inventoryUI.itemSlotContainer;
            case InventoryType.Minerals:
                return inventoryUI.mineralSlotContainer;
            case InventoryType.Equipments:
                return inventoryUI.equipmentSlotContainer;
            default:
                return null;
        }
    }

    public InventorySlot GetSlotItem()
    {


        // WarehouseUI 사용 시 (통합 슬롯 리스트 사용)
        if (warehouseUI != null && WarehouseManager.Instance != null && IsWarehouseType(inventoryType))
        {
            var wm = WarehouseManager.Instance;
            if (slotIndex >= 0 && slotIndex < wm.AllSlots.Count)
            {
                return wm.AllSlots[slotIndex];
            }
            return null;
        }

        // InventoryUI 사용 시
        if (inventoryUI == null) return null;

        switch (inventoryType)
        {
            case InventoryType.Items:
                if (inventoryUI.itemInventory != null && slotIndex < inventoryUI.itemInventory.ReadonlyItems.Count)
                    return inventoryUI.itemInventory.ReadonlyItems[slotIndex];
                break;
            case InventoryType.Minerals:
                if (inventoryUI.mineralInventory != null && slotIndex < inventoryUI.mineralInventory.ReadonlyItems.Count)
                    return inventoryUI.mineralInventory.ReadonlyItems[slotIndex];
                break;
            case InventoryType.Equipments:
                if (inventoryUI.equipmentInventory != null && slotIndex < inventoryUI.equipmentInventory.ReadonlyItems.Count)
                    return inventoryUI.equipmentInventory.ReadonlyItems[slotIndex];
                break;
        }
        return null;
    }
    private InventorySlot GetDraggedSlotItem(InventorySlotDragHandler handler)
    {
        if (handler == null) return null;

        // WarehouseUI 사용 시 (통합 슬롯 리스트 사용)
        if (WarehouseManager.Instance != null && IsWarehouseType(handler.inventoryType))
        {
            var wm = WarehouseManager.Instance;
            if (handler.slotIndex >= 0 && handler.slotIndex < wm.AllSlots.Count)
            {
                return wm.AllSlots[handler.slotIndex];
            }
            return null;
        }

        // InventoryUI 사용 시
        if (handler.inventoryUI == null) return null;

        switch (handler.inventoryType)
        {
            case InventoryType.Items:
                if (handler.inventoryUI.itemInventory != null && handler.slotIndex < handler.inventoryUI.itemInventory.ReadonlyItems.Count)
                    return handler.inventoryUI.itemInventory.ReadonlyItems[handler.slotIndex];
                break;
            case InventoryType.Minerals:
                if (handler.inventoryUI.mineralInventory != null && handler.slotIndex < handler.inventoryUI.mineralInventory.ReadonlyItems.Count)
                    return handler.inventoryUI.mineralInventory.ReadonlyItems[handler.slotIndex];
                break;
            case InventoryType.Equipments:
                if (handler.inventoryUI.equipmentInventory != null && handler.slotIndex < handler.inventoryUI.equipmentInventory.ReadonlyItems.Count)
                    return handler.inventoryUI.equipmentInventory.ReadonlyItems[handler.slotIndex];
                break;
        }
        return null;
    }
    private bool IsWarehouseType(InventoryType type)
    {
        return type == InventoryType.WarehouseItems || 
               type == InventoryType.WarehouseMinerals || 
               type == InventoryType.WarehouseEquipments ||
               type == InventoryType.WarehouseEmpty;
    }
}

