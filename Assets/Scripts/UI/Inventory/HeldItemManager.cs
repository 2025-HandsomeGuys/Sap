using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;

/// <summary>
/// 집은 아이템 상태를 관리하는 싱글톤 매니저
/// 좌클릭으로 아이템을 집은 상태로 만들고, 마우스 휠로 수량을 조절하며, 우클릭으로 되돌리는 기능 제공
/// </summary>
public class HeldItemManager : MonoBehaviour
{
    private static bool _isQuitting = false;
    private static HeldItemManager _instance;
    public static HeldItemManager Instance
    {
        get
        {
            if (_isQuitting) return null;

            if (_instance == null)
            {
                _instance = FindFirstObjectByType<HeldItemManager>();
                if (_instance == null && !_isQuitting)
                {
                    GameObject go = new GameObject("HeldItemManager");
                    _instance = go.AddComponent<HeldItemManager>();
                }
            }
            return _instance;
        }
    }

    [Header("집은 아이템 UI")]
    public GameObject heldItemObject; // 마우스를 따라다니는 아이템 아이콘
    public Image heldItemIcon;
    public TextMeshProUGUI heldItemQuantityText;
    public RectTransform heldItemRectTransform;
    public Canvas heldItemCanvas;

    [Header("상태")]
    public bool IsHoldingItem => currentHeldItem != null && currentHeldQuantity > 0;

    // 현재 집은 아이템 정보
    private InterfaceInventoryItem currentHeldItem;
    private int currentHeldQuantity;
    private int originalSlotIndex;
    private InventorySlotDragHandler.InventoryType originalInventoryType;
    private InventoryUI originalInventoryUI;
    private WarehouseUI originalWarehouseUI;

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            CreateHeldItemUI();
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        // UI 객체 안전장치: 파괴되었으면 재생성
        if (heldItemObject == null)
        {
            CreateHeldItemUI();
        }

        // 집은 아이템이 있으면 마우스 위치로 이동
        if (IsHoldingItem)
        {
            // [추가] 인벤토리나 창고 UI가 닫혔을 때 자동으로 반환
            CheckSourceUIStatus();

            if (heldItemRectTransform != null)
            {
                UpdateHeldItemPosition();
                
                // 전역 입력 처리 (집은 아이템이 있을 때만)
                HandleGlobalInput();
            }
        }
    }

    /// <summary>
    /// 아이템의 출처였던 UI 패널이 닫혔는지 확인하고, 닫혔다면 자동 반환
    /// </summary>
    private void CheckSourceUIStatus()
    {
        bool shouldReturn = false;

        // 1. 인벤토리 UI 체크
        if (originalInventoryUI != null)
        {
            if (originalInventoryUI.inventoryPanel != null && !originalInventoryUI.inventoryPanel.activeInHierarchy)
            {
                shouldReturn = true;
            }
        }
        // 2. 창고 UI 체크
        else if (originalWarehouseUI != null)
        {
            if (originalWarehouseUI.warehousePanel != null && !originalWarehouseUI.warehousePanel.activeInHierarchy)
            {
                shouldReturn = true;
            }
        }

        if (shouldReturn)
        {
            // Debug.Log("[HeldItemManager] UI가 닫혀 잡고 있던 아이템을 자동 반환합니다.");
            ReturnAllToOriginalSlot();
        }
    }
    
    /// <summary>
    /// 전역 입력 처리 (마우스 휠, 우클릭, 좌클릭 등)
    /// </summary>
    private void HandleGlobalInput()
    {
        if (Mouse.current == null) return;

        // 마우스 휠 처리 (전역) - New Input System
        float scrollDelta = Mouse.current.scroll.y.ReadValue(); 
        // 휠 델타 값 조정 (Legacy: +-1, New: +-120 usually, but normalized to similar logic)
        // 여기서는 그냥 값의 방향과 크기로 처리 (나누기 120 등은 필요시)
        if (Mathf.Abs(scrollDelta) > 0.01f)
        {
            // Input.mouseScrollDelta는 Vector2였으나 여기선 float로 처리
            // 기존 로직이 scrollDelta 값을 직접 사용하므로 스케일 조정 필요할 수 있음.
            // 보통 120 단위로 들어오므로 120으로 나눠서 1단위로 맞춤
            HandleMouseWheel(scrollDelta / 120f); 
        }
        
        // 우클릭 처리 (전역) - 슬롯 위에서는 InventorySlotClickHandler가 처리
        // 슬롯 외 영역(빈 공간) 클릭 시 전량 되돌리기
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            HandleRightClick();
        }

        // 좌클릭 처리 (전역) - 드랍존이 아닌 곳에서 클릭 시 원래 자리로 복구
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            HandleLeftClick();
        }
    }
    
    /// <summary>
    /// 좌클릭 처리 (전역): 유효하지 않은 영역 클릭 시 아이템 반환
    /// </summary>
    private void HandleLeftClick()
    {
        if (!IsHoldingItem) return;

        if (Mouse.current == null) return;

        // 마우스 포인터 아래에 무엇이 있는지 확인
        UnityEngine.EventSystems.PointerEventData pointerData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
        {
            position = Mouse.current.position.ReadValue()
        };
        
        var raycastResults = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointerData, raycastResults);
        }
        
        bool overValidTarget = false;
        foreach (var result in raycastResults)
        {
            // 1. 상점 드롭존
            if (result.gameObject.GetComponentInParent<ShopDropZone>() != null)
            {
                overValidTarget = true;
                break;
            }
            // 2. 인벤토리/창고 슬롯
            if (result.gameObject.GetComponentInParent<InventorySlotClickHandler>() != null || 
                result.gameObject.GetComponentInParent<InventorySlotDragHandler>() != null)
            {
                overValidTarget = true;
                break;
            }
            // 3. UI 프롬프트 (수량 입력, 확인창 등)
            if (result.gameObject.GetComponentInParent<QuantityPrompt>() != null || 
                result.gameObject.GetComponentInParent<ConfirmationPrompt>() != null)
            {
                overValidTarget = true;
                break;
            }

            // 4. 휴지통 (TrashSlotUI) — InventoryUI 패널보다 먼저 체크해야 함
            // TrashSlotUI는 InventoryUI의 자식이므로, 패널 체크보다 앞에 있어야
            // 휴지통 위 클릭 시 패널 드롭이 아닌 휴지통 버리기가 실행된다.
            var trashUI = result.gameObject.GetComponentInParent<TrashSlotUI>();
            if (trashUI != null)
            {
                overValidTarget = true;
                trashUI.TryTrashHeldItem();
                break;
            }

            // 5. 창고 패널 (빈 공간 드랍용)
            var wUI = result.gameObject.GetComponentInParent<WarehouseUI>();
            if (wUI != null && wUI.warehousePanel != null && wUI.warehousePanel.activeSelf)
            {
                overValidTarget = true;
                // 현재 집고 있는 아이템 종류에 맞는 창고 타입 결정
                InventorySlotDragHandler.InventoryType targetType = InventorySlotDragHandler.InventoryType.WarehouseItems;
                if (currentHeldItem is MineralSO) targetType = InventorySlotDragHandler.InventoryType.WarehouseMinerals;
                else if (currentHeldItem is EquipmentSO) targetType = InventorySlotDragHandler.InventoryType.WarehouseEquipments;

                TryDropItem(-1, targetType, null, wUI);
                break;
            }

            // 6. 인벤토리 패널 (빈 공간 드랍용)
            var invUI = result.gameObject.GetComponentInParent<InventoryUI>();
            if (invUI != null && invUI.inventoryPanel != null && invUI.inventoryPanel.activeSelf)
            {
                overValidTarget = true;
                // 현재 집고 있는 아이템 종류에 맞는 인벤토리 타입 결정
                InventorySlotDragHandler.InventoryType targetType = InventorySlotDragHandler.InventoryType.Items;
                if (currentHeldItem is MineralSO) targetType = InventorySlotDragHandler.InventoryType.Minerals;
                else if (currentHeldItem is EquipmentSO) targetType = InventorySlotDragHandler.InventoryType.Equipments;

                TryDropItem(-1, targetType, invUI, null);
                break;
            }
        }

        // 유효한 대상이 아니면 원래 슬롯으로 되돌리기
        if (!overValidTarget)
        {
            ReturnAllToOriginalSlot();
        }
    }

    /// <summary>
    /// 마우스 휠 처리 (전역)
    /// </summary>
    private void HandleMouseWheel(float scrollDelta)
    {
        // 원래 슬롯 정보 가져오기
        InventorySlot originalSlot = GetSlot(originalSlotIndex, originalInventoryType, originalInventoryUI, originalWarehouseUI);
        
        // 휠 아래로: 되돌리기 (슬롯이 비어있어도 가능)
        if (scrollDelta < 0)
        {
            int amount = Mathf.Max(1, Mathf.Abs((int)scrollDelta) * 1); // 수량 증감 단위를 1로 변경
            ReturnQuantityOnWheelDown(amount);
        }
        // 휠 위로: 다시 집기 (슬롯에 아이템이 있어야 가능)
        else if (scrollDelta > 0)
        {
            if (originalSlot == null) return;

            int amount = Mathf.Max(1, (int)scrollDelta * 1); // 수량 증감 단위를 1로 변경
            PickUpQuantityOnWheelUp(amount);
        }
    }
    
    /// <summary>
    /// 우클릭 처리 (전역)
    /// 슬롯 위에서는 InventorySlotClickHandler가 개별 처리하므로,
    /// 여기서는 슬롯 외 영역(빈 공간, 비호환 영역)에서만 전량 되돌리기를 수행합니다.
    /// </summary>
    private void HandleRightClick()
    {
        if (!IsHoldingItem) return;

        // ShopDropZone 위에서는 무시 (ShopDropZone이 판매 처리)
        if (IsPointerOverShopDropZone())
            return;

        // 인벤토리/창고 슬롯 위에서는 InventorySlotClickHandler가 처리하므로 스킵
        if (IsPointerOverInventorySlot())
            return;

        // 유효하지 않은 영역 → 전량 되돌리기
        ReturnAllToOriginalSlot();
    }

    /// <summary>
    /// 마우스 포인터가 인벤토리/창고 슬롯 위에 있는지 확인
    /// </summary>
    private bool IsPointerOverInventorySlot()
    {
        if (Mouse.current == null) return false;

        var pointerData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
        {
            position = Mouse.current.position.ReadValue()
        };

        var raycastResults = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointerData, raycastResults);
        }

        foreach (var result in raycastResults)
        {
            if (result.gameObject.GetComponentInParent<InventorySlotClickHandler>() != null)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 마우스 포인터가 ShopDropZone 위에 있는지 확인
    /// </summary>
    private bool IsPointerOverShopDropZone()
    {
        if (Mouse.current == null) return false;

        UnityEngine.EventSystems.PointerEventData pointerData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
        {
            position = Mouse.current.position.ReadValue()
        };
        
        var raycastResults = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointerData, raycastResults);
        }
        
        foreach (var result in raycastResults)
        {
            if (result.gameObject.GetComponentInParent<ShopDropZone>() != null)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 집은 아이템 UI 생성
    /// </summary>
    private void CreateHeldItemUI()
    {
        // 기존 캔버스 참조 확인 (이미 파괴되었을 수 있음)
        if (heldItemCanvas == null || heldItemCanvas.gameObject == null)
        {
            GameObject canvasObj = new GameObject("HeldItemCanvas_Persistent");
            heldItemCanvas = canvasObj.AddComponent<Canvas>();
            heldItemCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            heldItemCanvas.sortingOrder = 3000; // 최상위 (드래그 객체보다 위)
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            // 씬 루트 캔버스(4K 기준)와 동일 스케일 — 해상도가 달라도 슬롯 크기와 일치하게
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840f, 2160f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObj.AddComponent<GraphicRaycaster>();
            
            // 씬 전환 시 파괴되지 않도록 설정 (중요)
            DontDestroyOnLoad(canvasObj);
        }

        // 집은 아이템 객체 생성
        // 기존 객체가 있다면 파괴 (오류 방지)
        if (heldItemObject != null)
        {
            Destroy(heldItemObject);
        }

        heldItemObject = new GameObject("HeldItem");
        heldItemObject.transform.SetParent(heldItemCanvas.transform, false);
        heldItemRectTransform = heldItemObject.AddComponent<RectTransform>();
        heldItemRectTransform.sizeDelta = new Vector2(80, 80); // 크기 상향 (64 -> 80)
        heldItemRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        heldItemRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        heldItemRectTransform.pivot = new Vector2(0.5f, 0.5f);

        // 배경 이미지 (비활성화)
        Image bg = heldItemObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0f); // 투명하게 설정
        bg.enabled = false; // 이미지 컴포넌트 비활성화

        // 아이콘 이미지
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(heldItemObject.transform, false);
        heldItemIcon = iconObj.AddComponent<Image>();
        RectTransform iconRect = iconObj.GetComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.sizeDelta = Vector2.zero;
        iconRect.offsetMin = Vector2.zero; // 여백 제거
        iconRect.offsetMax = Vector2.zero; // 여백 제거

        // 수량 텍스트
        GameObject quantityObj = new GameObject("Quantity");
        quantityObj.transform.SetParent(heldItemObject.transform, false);
        heldItemQuantityText = quantityObj.AddComponent<TextMeshProUGUI>();
        heldItemQuantityText.fontSize = 16;
        heldItemQuantityText.fontStyle = FontStyles.Bold;
        heldItemQuantityText.color = Color.white;
        heldItemQuantityText.alignment = TextAlignmentOptions.BottomRight;
        RectTransform quantityRect = quantityObj.GetComponent<RectTransform>();
        quantityRect.anchorMin = new Vector2(0.5f, 0f);
        quantityRect.anchorMax = new Vector2(1f, 0.5f);
        quantityRect.sizeDelta = new Vector2(30, 20);
        quantityRect.anchoredPosition = new Vector2(-5, 5);

        // CanvasGroup 추가 - raycast 차단 방지
        CanvasGroup canvasGroup = heldItemObject.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        heldItemObject.SetActive(false);
    }

    /// <summary>
    /// 집은 아이템 위치 업데이트 (마우스 따라다니기)
    /// </summary>
    private void UpdateHeldItemPosition()
    {
        if (heldItemCanvas == null || heldItemRectTransform == null) return;

        Vector2 mousePos;
        Vector2 screenPos = (Mouse.current != null) ? Mouse.current.position.ReadValue() : Vector2.zero;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            heldItemCanvas.transform as RectTransform,
            screenPos,
            heldItemCanvas.worldCamera != null ? heldItemCanvas.worldCamera : (heldItemCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : Camera.main),
            out mousePos);

        heldItemRectTransform.anchoredPosition = mousePos;
    }

    /// <summary>
    ///아이템을 집은 상태로 만들기 (좌클릭 시 호출)
    /// </summary>
    public void PickUpItem(InventorySlot slot, int slotIndex, InventorySlotDragHandler.InventoryType inventoryType, 
        Vector2 size, InventoryUI inventoryUI = null, WarehouseUI warehouseUI = null)
    {
        if (slot == null || slot.item == null || slot.quantity <= 0)
            return;

        // 이미 집은 아이템이 있으면 먼저 되돌리기
        if (IsHoldingItem)
        {
            ReturnAllToOriginalSlot();
        }

        // 집은 아이템 정보 저장
        currentHeldItem = slot.item;
        currentHeldQuantity = slot.quantity;
        originalSlotIndex = slotIndex;
        originalInventoryType = inventoryType;
        originalInventoryUI = inventoryUI;
        originalWarehouseUI = warehouseUI;

        // 마우스 포인터 이미지 크기 동기화 (원래 슬롯 크기)
        if (heldItemRectTransform != null)
        {
            heldItemRectTransform.sizeDelta = size;
        }

        // --- 원본 슬롯에서 아이템을 제거하지 않습니다. (임시로 보관) ---
        // 드래그나 드랍 완료 시점에 실제 제거를 수행합니다.

        // UI에 원본 슬롯 반투명 효과 적용
        SetOriginalSlotAlpha(0.5f);

        // UI 업데이트
        UpdateHeldItemUI();
        heldItemObject.SetActive(true);

        // 장비 픽업 시 하이라이트 표시
        if (currentHeldItem is EquipmentSO eq)
        {
            if (InventoryUI.Instance != null)
                InventoryUI.Instance.HighlightEquipmentSlot(eq);
        }

        // 아이템 보유 중 스크롤 비활성화 (휠 입력이 인벤토리 스크롤에 전달되지 않도록)
        SetInventoryScrollEnabled(false);
    }

    /// <summary>
    /// 마우스 휠 아래로: 원래 슬롯에 수량 되돌리기
    /// </summary>
    public void ReturnQuantityOnWheelDown(int amount = 1)
    {
        if (!IsHoldingItem || amount <= 0) return;

        int returnAmount = Mathf.Min(amount, currentHeldQuantity);
        if (returnAmount <= 0) return;

        // 원본 슬롯에 이미 아이템이 있으므로 수량만 감소시킵니다.
        currentHeldQuantity -= returnAmount;
        if (currentHeldQuantity <= 0)
        {
            ClearHeldItem();
        }
        else
        {
            UpdateHeldItemUI();
        }
    }

    /// <summary>
    /// 마우스 휠 위로: 다시 집기 (원래 슬롯에서 수량 가져오기)
    /// </summary>
    public void PickUpQuantityOnWheelUp(int amount = 1)
    {
        if (!IsHoldingItem || amount <= 0) return;

        // 원래 슬롯에 있는 전체 수량 퍼센트 가져오기
        int totalQuantity = GetSlotQuantity(originalSlotIndex, originalInventoryType, originalInventoryUI, originalWarehouseUI);
        
        // 현재 들고있는 수량을 뺀 나머지가 가져올 수 있는 수량
        int availableQuantity = totalQuantity - currentHeldQuantity;
        if (availableQuantity <= 0) return;

        int takeAmount = Mathf.Min(amount, availableQuantity);
        if (takeAmount <= 0) return;

        // 슬롯에서 실제로 빼지 않으므로 수량만 증가시킵니다.
        currentHeldQuantity += takeAmount;
        UpdateHeldItemUI();
    }

    /// <summary>
    ///  우클릭: 마우스 아래 슬롯에 1개씩 내려놓기
    /// </summary>
    public void ReturnOneOnRightClick()
    {
        if (!IsHoldingItem) return;

        // 마우스 포인터 아래의 슬롯 찾기
        InventorySlotClickHandler hoverSlot = GetHoveredSlot();
        
        if (hoverSlot != null)
        {
            // 같은 슬롯에 1개 되돌리기
            if (hoverSlot.slotIndex == originalSlotIndex && hoverSlot.inventoryType == originalInventoryType)
            {
                currentHeldQuantity--;
                if (currentHeldQuantity <= 0)
                    ClearHeldItem();
                else
                    UpdateHeldItemUI();
                return;
            }

            // [중요] 타겟 슬롯에 넣기 전에 원래 슬롯에서 아이템 1개 제거
            RemoveItemFromSlot(originalSlotIndex, originalInventoryType, originalInventoryUI, originalWarehouseUI, 1);

            // 해당 슬롯에 1개 드롭 시도
            bool success = AddItemToSlot(hoverSlot.slotIndex, hoverSlot.inventoryType, 
                hoverSlot.inventoryUI, hoverSlot.warehouseUI, currentHeldItem, 1);

            if (success)
            {
                currentHeldQuantity--;
                if (currentHeldQuantity <= 0)
                {
                    ClearHeldItem();
                }
                else
                {
                    UpdateHeldItemUI();
                }
            }
            else
            {
                // 실패 시 제거했던 1개를 원래 슬롯 복구
                AddItemToSlot(originalSlotIndex, originalInventoryType, originalInventoryUI, originalWarehouseUI, currentHeldItem, 1);
            }
        }
    }

    /// <summary>
    /// 현재 마우스 포인터 아래에 있는 슬롯 핸들러 찾기
    /// </summary>
    private InventorySlotClickHandler GetHoveredSlot()
    {
        if (Mouse.current == null) return null;

        var pointerData = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
        {
            position = Mouse.current.position.ReadValue()
        };

        var raycastResults = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        if (UnityEngine.EventSystems.EventSystem.current != null)
        {
            UnityEngine.EventSystems.EventSystem.current.RaycastAll(pointerData, raycastResults);
        }

        foreach (var result in raycastResults)
        {
            var handler = result.gameObject.GetComponentInParent<InventorySlotClickHandler>();
            if (handler != null) return handler;
        }
        return null;
    }

    /// <summary>
    /// 다른 슬롯에 아이템 드랍 시도
    /// </summary>
    public void TryDropItem(int targetSlotIndex, InventorySlotDragHandler.InventoryType targetInventoryType,
        InventoryUI targetInventoryUI, WarehouseUI targetWarehouseUI)
    {
        if (!IsHoldingItem) return;

        // 같은 인벤토리 타입인지 확인
        // 단, 창고 <-> 일반 인벤토리 간 이동은 아이템 종류가 맞으면 허용
        if (!IsCompatibleInventoryType(originalInventoryType, targetInventoryType))
        {
            // 호환되지 않는 타입이면 원래 슬롯에 되돌리기
            ReturnAllToOriginalSlot();
            return;
        }

        // 타겟 슬롯 정보 가져오기
        InventorySlot targetSlot = GetSlot(targetSlotIndex, targetInventoryType, targetInventoryUI, targetWarehouseUI);

        // 같은 슬롯이면 원래 상태로 되돌리기
        if (targetSlotIndex == originalSlotIndex && targetInventoryType == originalInventoryType)
        {
            ReturnAllToOriginalSlot();
            return;
        }

        // 타겟 슬롯이 비어있거나, 같은 아이템이거나, 또는 타겟이 창고인 경우 드랍 허용
        bool isWarehouseTarget = targetInventoryType == InventorySlotDragHandler.InventoryType.WarehouseItems ||
                                 targetInventoryType == InventorySlotDragHandler.InventoryType.WarehouseMinerals ||
                                 targetInventoryType == InventorySlotDragHandler.InventoryType.WarehouseEquipments ||
                                 targetInventoryType == InventorySlotDragHandler.InventoryType.WarehouseEmpty;

        if (targetSlot == null || targetSlot.item == null || targetSlot.item.Id == currentHeldItem.Id || isWarehouseTarget)
        {
            // [중요] 타겟 슬롯에 넣기 전에 원래 슬롯에서 아이템 제거
            RemoveItemFromSlot(originalSlotIndex, originalInventoryType, originalInventoryUI, originalWarehouseUI, currentHeldQuantity);

            // 타겟 슬롯에 아이템 추가
            bool success = AddItemToSlot(targetSlotIndex, targetInventoryType, targetInventoryUI, targetWarehouseUI, currentHeldItem, currentHeldQuantity);
            
            if (success)
            {
                // 드랍 성공 시 집은 아이템 초기화
                ClearHeldItem();
            }
            else
            {
                // 드랍 실패(슬롯 다 참 등) 시 빼냈던 아이템을 원래 자리에 다시 넣기
                AddItemToSlot(originalSlotIndex, originalInventoryType, originalInventoryUI, originalWarehouseUI, currentHeldItem, currentHeldQuantity);
                ReturnAllToOriginalSlot();
            }
        }
        else
        {
            // 다른 아이템이면 원래 슬롯에 되돌리기
            ReturnAllToOriginalSlot();
        }
    }

    /// <summary>
    /// 두 인벤토리 타입이 호환 가능한지 확인
    /// 창고 <-> 일반 인벤토리 간 아이템 종류가 맞으면 이동 허용
    /// </summary>
    private bool IsCompatibleInventoryType(InventorySlotDragHandler.InventoryType type1, InventorySlotDragHandler.InventoryType type2)
    {
        // 빈 슬롯(WarehouseEmpty)은 모든 창고 관련 전송과 호환됨
        if (type1 == InventorySlotDragHandler.InventoryType.WarehouseEmpty || 
            type2 == InventorySlotDragHandler.InventoryType.WarehouseEmpty)
            return true;

        // 완전히 같은 타입이면 허용
        if (type1 == type2) return true;

        // 창고 광물 <-> 일반 광물
        if ((type1 == InventorySlotDragHandler.InventoryType.WarehouseMinerals && type2 == InventorySlotDragHandler.InventoryType.Minerals) ||
            (type1 == InventorySlotDragHandler.InventoryType.Minerals && type2 == InventorySlotDragHandler.InventoryType.WarehouseMinerals))
            return true;

        // 창고 아이템 <-> 일반 아이템
        if ((type1 == InventorySlotDragHandler.InventoryType.WarehouseItems && type2 == InventorySlotDragHandler.InventoryType.Items) ||
            (type1 == InventorySlotDragHandler.InventoryType.Items && type2 == InventorySlotDragHandler.InventoryType.WarehouseItems))
            return true;

        // 창고 장비 <-> 일반 장비 (장착 슬롯)
        if ((type1 == InventorySlotDragHandler.InventoryType.WarehouseEquipments && type2 == InventorySlotDragHandler.InventoryType.Equipments) ||
            (type1 == InventorySlotDragHandler.InventoryType.Equipments && type2 == InventorySlotDragHandler.InventoryType.WarehouseEquipments))
            return true;

        // 그 외 창고 내부간 이동 허용
        bool isW1 = (type1 == InventorySlotDragHandler.InventoryType.WarehouseItems || 
                     type1 == InventorySlotDragHandler.InventoryType.WarehouseMinerals || 
                     type1 == InventorySlotDragHandler.InventoryType.WarehouseEquipments);
        bool isW2 = (type2 == InventorySlotDragHandler.InventoryType.WarehouseItems || 
                     type2 == InventorySlotDragHandler.InventoryType.WarehouseMinerals || 
                     type2 == InventorySlotDragHandler.InventoryType.WarehouseEquipments);
        if (isW1 && isW2) return true;

        return false;
    }

    /// <summary>
    /// 모든 아이템을 원래 슬롯에 되돌리기
    /// </summary>
    public void ReturnAllToOriginalSlot()
    {
        if (!IsHoldingItem) return;

        // 아이템을 미리 제거하지 않았으므로, 추가할 필요 없이 상태만 초기화합니다.
        ClearHeldItem();
    }

    /// <summary>
    /// 휴지통에 아이템을 버릴 때 호출 (삭제)
    /// </summary>
    public void TrashHeldItem()
    {
        Debug.Log($"[HeldItemManager] TrashHeldItem called. IsHoldingItem: {IsHoldingItem}");
        if (!IsHoldingItem) return;

        Debug.Log($"[HeldItemManager] Removing item from slot {originalSlotIndex}, Type: {originalInventoryType}, Qty: {currentHeldQuantity}");
        // 타겟 슬롯 없이 원본 슬롯에서 완전히 아이템 제거 (버림 처리)
        RemoveItemFromSlot(originalSlotIndex, originalInventoryType, originalInventoryUI, originalWarehouseUI, currentHeldQuantity);

        Debug.Log("[HeldItemManager] Clearing Held Item.");
        // UI 초기화
        ClearHeldItem();
    }

    /// <summary>
    /// 집은 아이템 초기화
    /// </summary>
    public void ClearHeldItem()
    {
        // 원래 슬롯 UI 반투명 복구
        SetOriginalSlotAlpha(1f);

        currentHeldItem = null;
        currentHeldQuantity = 0;
        originalSlotIndex = -1;
        originalInventoryUI = null;
        originalWarehouseUI = null;
        
        if (heldItemObject != null)
            heldItemObject.SetActive(false);

        // 하이라이트 제거
        if (InventoryUI.Instance != null)
            InventoryUI.Instance.ClearEquipmentSlotHighlights();

        // 스크롤 복원
        SetInventoryScrollEnabled(true);
    }

    /// <summary>
    /// 인벤토리 UI의 ScrollRect 스크롤을 활성화/비활성화
    /// 아이템을 들고 있을 때 휠 입력이 스크롤에 전달되지 않도록 차단
    /// </summary>
    private void SetInventoryScrollEnabled(bool enabled)
    {
        if (InventoryUI.Instance != null && InventoryUI.Instance.mineralScrollRect != null)
        {
            InventoryUI.Instance.mineralScrollRect.vertical = enabled;
        }
        if (WarehouseUI.Instance != null && WarehouseUI.Instance.scrollRect != null)
        {
            WarehouseUI.Instance.scrollRect.vertical = enabled;
        }
    }

    /// <summary>
    /// 집은 아이템 UI 업데이트
    /// </summary>
    private void UpdateHeldItemUI()
    {
        if (currentHeldItem == null) return;

        if (heldItemIcon != null)
        {
            heldItemIcon.sprite = currentHeldItem.Icon;
            heldItemIcon.enabled = (heldItemIcon.sprite != null);
        }

        if (heldItemQuantityText != null)
        {
            heldItemQuantityText.text = (currentHeldItem.Stackable && currentHeldQuantity > 1)
                ? currentHeldQuantity.ToString()
                : string.Empty;
        }
    }

    /// <summary>
    /// 원본 슬롯의 UI 투명도를 설정합니다.
    /// </summary>
    private void SetOriginalSlotAlpha(float alpha)
    {
        if (originalSlotIndex < 0) return;

        Transform container = null;
        if (originalWarehouseUI != null && IsCompatibleInventoryType(originalInventoryType, InventorySlotDragHandler.InventoryType.WarehouseEmpty))
        {
            container = originalWarehouseUI.slotContainer;
        }
        else if (originalInventoryUI != null)
        {
            switch (originalInventoryType)
            {
                case InventorySlotDragHandler.InventoryType.Items:
                    container = originalInventoryUI.itemSlotContainer;
                    break;
                case InventorySlotDragHandler.InventoryType.Minerals:
                    container = originalInventoryUI.mineralSlotContainer;
                    break;
                case InventorySlotDragHandler.InventoryType.Equipments:
                    container = originalInventoryUI.equipmentSlotContainer;
                    break;
            }
        }

        if (container != null && originalSlotIndex < container.childCount)
        {
            Transform slotObj = container.GetChild(originalSlotIndex);
            if (slotObj != null)
            {
                CanvasGroup cg = slotObj.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = alpha;
                }
            }
        }
    }

    /// <summary>
    /// 슬롯에서 아이템 제거
    /// </summary>
    private void RemoveItemFromSlot(int slotIndex, InventorySlotDragHandler.InventoryType inventoryType,
        InventoryUI inventoryUI, WarehouseUI warehouseUI, int quantity)
    {
        // 폴백 자가 참조
        if (inventoryUI == null) inventoryUI = InventoryUI.Instance;
        if (warehouseUI == null) warehouseUI = WarehouseUI.Instance;

        switch (inventoryType)
        {
            case InventorySlotDragHandler.InventoryType.Items:
                if (inventoryUI != null && inventoryUI.itemInventory != null)
                {
                    // [수정] 인덱스 기반으로 정확한 슬롯의 아이템 삭제
                    inventoryUI.itemInventory.RemoveItemAt(slotIndex, quantity);
                }
                break;
            case InventorySlotDragHandler.InventoryType.Minerals:
                if (inventoryUI != null && inventoryUI.mineralInventory != null)
                {
                    // [수정] 인덱스 기반으로 정확한 슬롯의 아이템 삭제
                    inventoryUI.mineralInventory.RemoveItemAt(slotIndex, quantity);
                }
                break;
            case InventorySlotDragHandler.InventoryType.WarehouseMinerals:
            case InventorySlotDragHandler.InventoryType.WarehouseItems:
            case InventorySlotDragHandler.InventoryType.WarehouseEquipments:
                if (WarehouseManager.Instance != null)
                {
                    WarehouseManager.Instance.RemoveItemAt(slotIndex, quantity);
                }
                break;
            case InventorySlotDragHandler.InventoryType.Equipments:
                if (inventoryUI != null && inventoryUI.equipmentInventory != null)
                {
                    var slot = GetSlot(slotIndex, inventoryType, inventoryUI, warehouseUI);
                    if (slot != null && slot.item != null && slot.item is EquipmentSO equipmentSO)
                    {
                        inventoryUI.equipmentInventory.RemoveItem(equipmentSO, quantity);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// 슬롯에 아이템 추가
    /// 슬롯이 비어있으면 인벤토리에 직접 추가하고, 있으면 해당 슬롯에 추가
    /// </summary>
    private bool AddItemToSlot(int slotIndex, InventorySlotDragHandler.InventoryType inventoryType,
        InventoryUI inventoryUI, WarehouseUI warehouseUI, InterfaceInventoryItem item, int quantity)
    {
        // 폴백 자가 참조
        if (inventoryUI == null) inventoryUI = InventoryUI.Instance;
        if (warehouseUI == null) warehouseUI = WarehouseUI.Instance;

        // 슬롯 정보 가져오기
        InventorySlot slot = GetSlot(slotIndex, inventoryType, inventoryUI, warehouseUI);
        
        // 창고 타입인지 확인 (창고는 슬롯 위치가 중요하므로 별도 처리)
        bool isWarehouse = (inventoryType == InventorySlotDragHandler.InventoryType.WarehouseMinerals || 
                            inventoryType == InventorySlotDragHandler.InventoryType.WarehouseItems ||
                            inventoryType == InventorySlotDragHandler.InventoryType.WarehouseEquipments ||
                            inventoryType == InventorySlotDragHandler.InventoryType.WarehouseEmpty);

        // 슬롯이 비어있거나, 다른 아이템이거나, 창고 타입인 경우 (창고는 내부 로직에서 스택/스왑 모두 처리)
        if (slot == null || slot.item == null || slot.item.Id != item.Id || isWarehouse)
        {
            // 인벤토리에 직접 추가 (인벤토리 시스템이 자동으로 처리)
            switch (inventoryType)
            {
                case InventorySlotDragHandler.InventoryType.Items:
                    if (inventoryUI != null && inventoryUI.itemInventory != null && item is ItemSO itemSO)
                    {
                        int addedItems = inventoryUI.itemInventory.AddItem(itemSO, quantity);
                        if (addedItems > 0 && addedItems < quantity)
                        {
                            // 부분 추가 — 나머지를 보존하여 원래 자리로 반환되도록 함
                            currentHeldQuantity = quantity - addedItems;
                            UpdateHeldItemUI();
                            return false;
                        }
                        return addedItems > 0;
                    }
                    break;
                case InventorySlotDragHandler.InventoryType.Minerals:
                    if (inventoryUI != null && inventoryUI.mineralInventory != null && item is MineralSO mineralSO2)
                    {
                        int addedMinerals = inventoryUI.mineralInventory.AddItem(mineralSO2, quantity);
                        if (addedMinerals > 0 && addedMinerals < quantity)
                        {
                            currentHeldQuantity = quantity - addedMinerals;
                            UpdateHeldItemUI();
                            return false;
                        }
                        return addedMinerals > 0;
                    }
                    break;
                case InventorySlotDragHandler.InventoryType.WarehouseMinerals:
                case InventorySlotDragHandler.InventoryType.WarehouseItems:
                case InventorySlotDragHandler.InventoryType.WarehouseEquipments:
                case InventorySlotDragHandler.InventoryType.WarehouseEmpty:
                    if (WarehouseManager.Instance != null)
                    {
                        var wm = WarehouseManager.Instance;
                        if (slotIndex >= 0 && slotIndex < wm.AllSlots.Count)
                        {
                            var targetSlot = wm.AllSlots[slotIndex];
                            int maxStack = item.MaxStackSize;
                            
                            // 1. 슬롯이 비어있으면 넣기
                            if (targetSlot == null || targetSlot.item == null)
                            {
                                int addAmount = Mathf.Min(quantity, maxStack);
                                wm.AllSlots[slotIndex] = new InventorySlot(item, addAmount);
                                
                                int remain = quantity - addAmount;
                                if (remain > 0) {
                                    currentHeldQuantity = remain;
                                    UpdateHeldItemUI();
                                    wm.NotifyWarehouseChanged();
                                    return false; // 일부 남아있음
                                }
                                
                                wm.NotifyWarehouseChanged();
                                return true;
                            }
                            // 2. 같은 아이템이면 합치기 (중첩 가능한 경우)
                            else if (targetSlot.item.Id == item.Id && item.Stackable)
                            {
                                int canAdd = maxStack - targetSlot.quantity;
                                if (canAdd > 0)
                                {
                                    int toAdd = Mathf.Min(quantity, canAdd);
                                    targetSlot.quantity += toAdd;
                                    
                                    int remain = quantity - toAdd;
                                    if (remain > 0) {
                                        currentHeldQuantity = remain;
                                        UpdateHeldItemUI();
                                        wm.NotifyWarehouseChanged();
                                        return false; // 일부 남아있음
                                    }
                                    
                                    wm.NotifyWarehouseChanged();
                                    return true;
                                }
                                else {
                                    // 이미 풀스택이면 스왑? 아니면 그냥 유지? 
                                    // 여기서는 스왑 처리 (3번으로 넘어감)
                                }
                            }
                            
                            // 3. 다른 아이템이거나 스택 불가능/풀스택인 경우 교환 (Swap)
                            {
                                var previousItem = targetSlot.item;
                                int previousQty = targetSlot.quantity;
                                
                                // 현재 든 아이템을 슬롯의 MaxStackSize만큼만 넣을 수도 있지만, 
                                // 보통 스왑은 통째로 바꿈.
                                wm.AllSlots[slotIndex] = new InventorySlot(item, quantity);
                                
                                // 기존 아이템을 손에 들기
                                currentHeldItem = previousItem;
                                currentHeldQuantity = previousQty;
                                UpdateHeldItemUI();
                                
                                wm.NotifyWarehouseChanged();
                                return false; // 교환됨을 알림
                            }
                        }
                    }
                    break;
                case InventorySlotDragHandler.InventoryType.Equipments:
                    if (inventoryUI != null && inventoryUI.equipmentInventory != null && item is EquipmentSO equipmentSO2)
                    {
                        // EquipAt을 사용하여 장비 장착 (기존 장비가 있으면 교환)
                        if (inventoryUI.equipmentInventory.EquipAt(slotIndex, equipmentSO2, out EquipmentSO previous))
                        {
                            if (previous != null)
                            {
                                // 기존 장비는 항상 창고로 반환 (장비는 Warehouse ↔ EquipmentInventory만 가능)
                                if (WarehouseManager.Instance != null)
                                {
                                    WarehouseManager.Instance.AddEquipment(previous, 1);
                                }
                            }
                            return true;
                        }
                    }
                    break;
            }
            return false;
        }
        
        // 같은 아이템이면 슬롯에 직접 추가 (스택 가능한 경우)
        if (item.Stackable)
        {
            // 인벤토리 시스템을 통해 추가 (스택 제한 고려)
            switch (inventoryType)
            {
                case InventorySlotDragHandler.InventoryType.Items:
                    if (inventoryUI != null && inventoryUI.itemInventory != null && item is ItemSO itemSO)
                    {
                        return inventoryUI.itemInventory.AddItem(itemSO, quantity) > 0;
                    }
                    break;
                case InventorySlotDragHandler.InventoryType.Minerals:
                    if (inventoryUI != null && inventoryUI.mineralInventory != null && item is MineralSO mineralSO2)
                    {
                        return inventoryUI.mineralInventory.AddItem(mineralSO2, quantity) > 0;
                    }
                    break;
                case InventorySlotDragHandler.InventoryType.WarehouseMinerals:
                    if (WarehouseManager.Instance != null && item is MineralSO mineralSOW)
                    {
                        WarehouseManager.Instance.AddMineral(mineralSOW, quantity);
                        return true;
                    }
                    break;
                case InventorySlotDragHandler.InventoryType.WarehouseItems:
                    if (WarehouseManager.Instance != null && item is ItemSO itemSOW)
                    {
                        WarehouseManager.Instance.AddItem(itemSOW, quantity);
                        return true;
                    }
                    break;
                case InventorySlotDragHandler.InventoryType.WarehouseEquipments:
                    if (WarehouseManager.Instance != null && item is EquipmentSO equipmentSOW)
                    {
                        WarehouseManager.Instance.AddEquipment(equipmentSOW, quantity);
                        return true;
                    }
                    break;
                case InventorySlotDragHandler.InventoryType.Equipments:
                    if (inventoryUI != null && inventoryUI.equipmentInventory != null && item is EquipmentSO equipmentSO)
                    {
                        return inventoryUI.equipmentInventory.AddItem(equipmentSO, quantity) > 0;
                    }
                    break;
            }
        }
        
        return false;
    }

    /// <summary>
    /// 슬롯의 수량 가져오기
    /// </summary>
    private int GetSlotQuantity(int slotIndex, InventorySlotDragHandler.InventoryType inventoryType,
        InventoryUI inventoryUI, WarehouseUI warehouseUI)
    {
        var slot = GetSlot(slotIndex, inventoryType, inventoryUI, warehouseUI);
        return slot != null ? slot.quantity : 0;
    }

    /// <summary>
    /// 슬롯 가져오기
    /// </summary>
    private InventorySlot GetSlot(int slotIndex, InventorySlotDragHandler.InventoryType inventoryType,
        InventoryUI inventoryUI, WarehouseUI warehouseUI)
    {
        if (slotIndex < 0 || inventoryType == InventorySlotDragHandler.InventoryType.WarehouseEmpty) 
            return null;
        
        switch (inventoryType)
        {
            case InventorySlotDragHandler.InventoryType.Items:
                if (inventoryUI != null && inventoryUI.itemInventory != null && 
                    slotIndex < inventoryUI.itemInventory.ReadonlyItems.Count)
                    return inventoryUI.itemInventory.ReadonlyItems[slotIndex];
                break;
            case InventorySlotDragHandler.InventoryType.Minerals:
                if (inventoryUI != null && inventoryUI.mineralInventory != null &&
                    slotIndex < inventoryUI.mineralInventory.ReadonlyItems.Count)
                    return inventoryUI.mineralInventory.ReadonlyItems[slotIndex];
                break;
            case InventorySlotDragHandler.InventoryType.WarehouseMinerals:
            case InventorySlotDragHandler.InventoryType.WarehouseItems:
            case InventorySlotDragHandler.InventoryType.WarehouseEquipments:
                if (WarehouseManager.Instance != null && slotIndex < WarehouseManager.Instance.AllSlots.Count)
                    return WarehouseManager.Instance.AllSlots[slotIndex];
                break;
            case InventorySlotDragHandler.InventoryType.Equipments:
                if (inventoryUI != null && inventoryUI.equipmentInventory != null &&
                    slotIndex < inventoryUI.equipmentInventory.ReadonlyItems.Count)
                    return inventoryUI.equipmentInventory.ReadonlyItems[slotIndex];
                break;
        }
        return null;
    }

    /// <summary>
    /// 현재 집은 아이템 정보 가져오기
    /// </summary>
    public InterfaceInventoryItem GetHeldItem()
    {
        return currentHeldItem;
    }

    /// <summary>
    /// 현재 집은 수량 가져오기
    /// </summary>
    public int GetHeldQuantity()
    {
        return currentHeldQuantity;
    }

    /// <summary>
    /// 원래 슬롯 인덱스 가져오기
    /// </summary>
    public int GetOriginalSlotIndex()
    {
        return originalSlotIndex;
    }

    /// <summary>
    /// 원래 인벤토리 타입 가져오기
    /// </summary>
    public InventorySlotDragHandler.InventoryType GetOriginalInventoryType()
    {
        return originalInventoryType;
    }

    private void OnApplicationQuit()
    {
        _isQuitting = true;
    }
}

