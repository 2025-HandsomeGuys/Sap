using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlotDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [Header("참조")]
    public InventoryUI inventoryUI;
    public int slotIndex; // 이 슬롯의 인벤토리 내 인덱스
    public InventoryType inventoryType; // Items, Minerals, Tools 중 하나

    private Canvas canvas;
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private GameObject dragObject;
    private Vector2 dragOffset; // 마우스 위치와 슬롯 중심의 오프셋
    public static InventorySlotDragHandler currentDraggingHandler; // 현재 드래그 중인 핸들러

    // 외부에서 접근 가능하도록 public 프로퍼티 추가
    public CanvasGroup CanvasGroup => canvasGroup;

    public enum InventoryType
    {
        Items,
        Minerals,
        Tools
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
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (inventoryUI == null || !inventoryUI.IsOpen())
            return;

        // 드래그 가능한지 확인 (빈 슬롯은 드래그 불가)
        if (GetSlotItem() == null)
            return;

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
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragObject == null) return;

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
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (inventoryUI == null || !inventoryUI.IsOpen())
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

        // 같은 인벤토리 타입인지 확인 (다른 인벤토리 간 이동 불가)
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
        // 앞에 삽입
        else if (insertBefore && toIndex > fromIndex)
        {
            success = inventoryUI.MoveInventorySlot(inventoryType, fromIndex, toIndex);
        }
        // 뒤에 삽입
        else if (insertAfter && toIndex < fromIndex)
        {
            success = inventoryUI.MoveInventorySlot(inventoryType, fromIndex, toIndex + 1);
        }
        // 중간 영역이거나 삽입이 불가능한 경우 교환
        else
        {
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
        if (inventoryUI == null) return null;

        switch (inventoryType)
        {
            case InventoryType.Items:
                return inventoryUI.itemSlotContainer;
            case InventoryType.Minerals:
                return inventoryUI.mineralSlotContainer;
            case InventoryType.Tools:
                return inventoryUI.toolSlotContainer;
            default:
                return null;
        }
    }

    private InventorySlot GetSlotItem()
    {
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
            case InventoryType.Tools:
                if (inventoryUI.toolInventory != null && slotIndex < inventoryUI.toolInventory.ReadonlyItems.Count)
                    return inventoryUI.toolInventory.ReadonlyItems[slotIndex];
                break;
        }
        return null;
    }
}

