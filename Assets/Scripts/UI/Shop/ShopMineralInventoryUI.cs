// using UnityEngine;
// using UnityEngine.UI;
// using System.Collections.Generic;
// using TMPro;

// /// <summary>
// /// [더 이상 사용하지 않음 - DEPRECATED]
// /// 
// /// 과거 상점 전용 광물 인벤토리 UI였으나, 이제 시스템이 변경되었습니다:
// /// - 지상: WarehouseUI (창고 - 광물/아이템/도구 통합) + ToolInventory + ItemInventory
// /// - 지하: MineralInventory + ToolInventory + ItemInventory
// /// 
// /// 이 클래스는 호환성을 위해 남겨두었지만 새로운 코드에서는 사용하지 마세요.
// /// 대신 WarehouseUI와 InventoryUI를 사용하세요.
// /// </summary>
// [System.Obsolete("ShopMineralInventoryUI는 더 이상 사용되지 않습니다. WarehouseUI와 InventoryUI를 사용하세요.", false)]
// public class ShopMineralInventoryUI : MonoBehaviour
// {
//     [Header("인벤토리 참조")]
//     public MineralInventory mineralInventory;

//     [Header("슬롯 프리팹")]
//     public GameObject inventorySlotPrefab;

//     [Header("UI 요소")]
//     public GameObject shopContentPanel; // 상점 콘텐츠 패널 (구매 아이템 표시)
//     public GameObject mineralPanel; // 광물 패널 (광물 인벤토리 표시)
//     public Transform mineralSlotContainer; // 광물 슬롯 컨테이너
//     public TextMeshProUGUI mineralWeightText; // 무게 표시 (선택사항)

//     [Header("상점 참조")]
//     public ShopUI shopUI; // 탭 전환 확인용

//     [Header("테스트 버튼 (개발용)")]
//     public Button testAddMineralButton;  // 광물 추가 테스트 버튼

//     private List<GameObject> mineralSlotObjects = new List<GameObject>();
//     private bool isInitialized = false;

//     void Start()
//     {
//         // 자동 참조
//         if (mineralInventory == null)
//             mineralInventory = FindFirstObjectByType<MineralInventory>();
//         if (shopUI == null)
//             shopUI = ShopUI.Instance;

//         if (mineralInventory == null)
//         {
//             Debug.LogWarning("[ShopMineralInventoryUI] MineralInventory를 찾을 수 없습니다!");
//             return;
//         }

//         if (mineralSlotContainer == null)
//             Debug.LogWarning("[ShopMineralInventoryUI] mineralSlotContainer가 설정되지 않았습니다!");
//         if (inventorySlotPrefab == null)
//             Debug.LogWarning("[ShopMineralInventoryUI] inventorySlotPrefab이 설정되지 않았습니다!");

//         // shopContentPanel과 mineralPanel 자동 참조 (Inspector에서 설정되지 않은 경우)
//         if (shopContentPanel == null)
//         {
//             Transform content = transform.Find("ShopContentPanel");
//             if (content != null)
//             {
//                 shopContentPanel = content.gameObject;
//                 Debug.Log("[ShopMineralInventoryUI] shopContentPanel 자동 참조 성공");
//             }
//             else
//             {
//                 Debug.LogError("[ShopMineralInventoryUI] shopContentPanel을 찾을 수 없습니다! 자식 객체에 'ShopContentPanel' 이름의 GameObject가 있는지 확인하거나 Inspector에서 수동으로 할당하세요.");
//             }
//         }
        
//         if (mineralPanel == null)
//         {
//             Transform mineral = transform.Find("MineralPanel");
//             if (mineral != null)
//             {
//                 mineralPanel = mineral.gameObject;
//                 Debug.Log("[ShopMineralInventoryUI] mineralPanel 자동 참조 성공");
//             }
//             else
//             {
//                 Debug.LogError("[ShopMineralInventoryUI] mineralPanel을 찾을 수 없습니다! 자식 객체에 'MineralPanel' 이름의 GameObject가 있는지 확인하거나 Inspector에서 수동으로 할당하세요.");
//             }
//         }

//         // CRITICAL: ShopMineralInventoryUI GameObject 자체는 항상 활성화 상태 유지
//         // (자식 패널들만 켜고 끄면 됨)
//         if (!gameObject.activeSelf)
//         {
//             Debug.LogWarning("[ShopMineralInventoryUI] GameObject가 비활성화되어 있어 강제로 활성화합니다.");
//             gameObject.SetActive(true);
//         }

//         // 초기에는 콘텐츠 패널들 비활성화
//         if (shopContentPanel != null)
//         {
//             shopContentPanel.SetActive(false);
//         }
//         if (mineralPanel != null)
//         {
//             mineralPanel.SetActive(false);
//         }

//         // 인벤토리 변경 이벤트 구독
//         SubscribeToEvents();

//         // 테스트 버튼 이벤트 연결
//         SetupTestButtons();

//         // 초기 UI 업데이트 (데이터 로드 전이므로 무게만 업데이트)
//         UpdateMineralWeight();
//         isInitialized = true;
//     }

//     void OnEnable()
//     {
//         // 컴포넌트가 활성화될 때 인벤토리 참조 재확인 및 업데이트
//         if (isInitialized)
//         {
//             if (mineralInventory == null)
//             {
//                 mineralInventory = FindFirstObjectByType<MineralInventory>();
//             }
//             if (shopUI == null)
//             {
//                 shopUI = ShopUI.Instance;
//             }
            
//             if (mineralInventory != null)
//             {
//                 UpdateMineralWeight();
//                 // 상점이 열려있으면 슬롯도 업데이트
//                 if (IsOpen())
//                 {
//                     UpdateMineralSlots();
//                 }
//             }
//         }
//     }

//     void OnDestroy()
//     {
//         UnsubscribeFromEvents();
//     }

//     private void SubscribeToEvents()
//     {
//         if (mineralInventory != null)
//         {
//             mineralInventory.OnInventoryChanged -= OnMineralInventoryChanged;
//             mineralInventory.OnInventoryChanged += OnMineralInventoryChanged;
//         }
//     }

//     private void UnsubscribeFromEvents()
//     {
//         if (mineralInventory != null)
//             mineralInventory.OnInventoryChanged -= OnMineralInventoryChanged;
//     }

//     private void SetupTestButtons()
//     {
//         // 광물 추가 테스트 버튼
//         if (testAddMineralButton != null)
//         {
//             testAddMineralButton.onClick.RemoveAllListeners();
//             testAddMineralButton.onClick.AddListener(() => TestAddMineral());
//         }
//     }

//     // 테스트 메서드
//     public void TestAddMineral()
//     {
//         if (mineralInventory == null)
//         {
//             Debug.LogWarning("[ShopMineralInventoryUI] MineralInventory를 찾을 수 없습니다!");
//             return;
//         }

//         if (MineralDatabase.Instance == null || MineralDatabase.Instance.allMinerals == null || MineralDatabase.Instance.allMinerals.Count == 0)
//         {
//             Debug.LogWarning("[ShopMineralInventoryUI] MineralDatabase에 광물이 없습니다!");
//             return;
//         }

//         // 첫 번째 광물 추가 (None이 아닌 것)
//         MineralSO mineralToAdd = null;
//         foreach (var mineral in MineralDatabase.Instance.allMinerals)
//         {
//             if (mineral != null && mineral.mineralID != MineralID.None)
//             {
//                 mineralToAdd = mineral;
//                 break;
//             }
//         }

//         if (mineralToAdd != null)
//         {
//             int addedCount = mineralInventory.AddItem(mineralToAdd, 1);
//             if (addedCount > 0)
//             {
//                 Debug.Log($"[Test] 광물 추가 성공: {mineralToAdd.mineralName}, 수량: {addedCount}");
//             }
//             else
//             {
//                 Debug.LogWarning($"[Test] 광물 추가 실패: {mineralToAdd.mineralName} (인벤토리 가득 참 또는 무게 초과)");
//             }
//         }
//         else
//         {
//             Debug.LogWarning("[ShopMineralInventoryUI] 추가할 수 있는 광물을 찾을 수 없습니다!");
//         }
//     }

//     private void OnMineralInventoryChanged()
//     {
//         UpdateMineralWeight();
//         // 상점이 열려있을 때만 슬롯 업데이트
//         if (isInitialized && IsOpen())
//         {
//             UpdateMineralSlots();
//         }
//     }

//     /// <summary>
//     /// ShopMineralInventoryUI 활성화 (ShopContentPanel + MineralPanel 동시 제어)
//     /// </summary>
//     public void SetActive(bool active)
//     {
//         Debug.Log($"[ShopMineralInventoryUI] SetActive 호출됨 - active: {active}");
        
//         // CRITICAL: ShopMineralInventoryUI GameObject 자체가 활성화되어 있는지 확인
//         if (!gameObject.activeSelf)
//         {
//             Debug.LogError($"[ShopMineralInventoryUI] 경고! ShopMineralInventoryUI GameObject가 비활성화되어 있습니다! " +
//                           $"자식 패널({nameof(shopContentPanel)}, {nameof(mineralPanel)})을 활성화하려면 " +
//                           $"ShopMineralInventoryUI GameObject 자체가 먼저 활성화되어야 합니다.");
//         }
        
//         // ShopContentPanel 활성화/비활성화
//         if (shopContentPanel != null)
//         {
//             shopContentPanel.SetActive(active);
//             Debug.Log($"[ShopMineralInventoryUI] shopContentPanel 설정: {active}, 실제 상태: {shopContentPanel.activeSelf}");
//         }
//         else
//         {
//             Debug.LogWarning("[ShopMineralInventoryUI] shopContentPanel이 null입니다! Inspector에서 할당해주세요.");
//         }
        
//         // MineralPanel 활성화/비활성화
//         if (mineralPanel != null)
//         {
//             mineralPanel.SetActive(active);
//             Debug.Log($"[ShopMineralInventoryUI] mineralPanel 설정: {active}, 실제 상태: {mineralPanel.activeSelf}");
//         }
//         else
//         {
//             Debug.LogWarning("[ShopMineralInventoryUI] mineralPanel이 null입니다! Inspector에서 할당해주세요.");
//         }
        
//         // 활성화될 때만 UI 업데이트
//         if (active)
//         {
//             // 인벤토리 참조 재확인
//             if (mineralInventory == null)
//             {
//                 mineralInventory = FindFirstObjectByType<MineralInventory>();
//             }
//             if (shopUI == null)
//             {
//                 shopUI = ShopUI.Instance;
//             }
            
//             // 무게와 슬롯 업데이트
//             UpdateMineralWeight();
//             UpdateMineralSlots();
//         }
        
//         Debug.Log($"[ShopMineralInventoryUI] SetActive 완료 - " +
//                  $"ShopContent활성: {(shopContentPanel != null ? shopContentPanel.activeSelf.ToString() : "null")}, " +
//                  $"Mineral활성: {(mineralPanel != null ? mineralPanel.activeSelf.ToString() : "null")}");
//     }

//     /// <summary>
//     /// 무게 표시 업데이트
//     /// </summary>
//     public void UpdateMineralWeight()
//     {
//         if (mineralInventory != null && mineralWeightText != null)
//         {
//             mineralWeightText.text = $"무게: {mineralInventory.TotalWeight:F1} / {mineralInventory.maxWeightLimit:F1}";
//         }
//     }

//     /// <summary>
//     /// 광물 슬롯 업데이트
//     /// </summary>
//     public void UpdateMineralSlots()
//     {
//         // 기존 슬롯 제거
//         foreach (GameObject slot in mineralSlotObjects)
//         {
//             if (slot != null)
//                 Destroy(slot);
//         }
//         mineralSlotObjects.Clear();

//         if (mineralSlotContainer == null || inventorySlotPrefab == null || mineralInventory == null)
//         {
//             Debug.LogWarning("[ShopMineralInventoryUI] Mineral 슬롯 업데이트 실패: 필수 참조가 없습니다.");
//             return;
//         }

//         var list = mineralInventory.ReadonlyItems;

//         for (int i = 0; i < list.Count; i++)
//         {
//             var itemSlot = list[i];
//             if (itemSlot == null || itemSlot.item == null) continue;

//             GameObject newSlot = Instantiate(inventorySlotPrefab, mineralSlotContainer);
//             mineralSlotObjects.Add(newSlot);

//             SetupSlotUI(newSlot, itemSlot, i);
//         }
//     }

//     /// <summary>
//     /// 슬롯 UI 설정 (버리기 버튼 없이 드래그 앤 드롭만 지원)
//     /// </summary>
//     private void SetupSlotUI(GameObject newSlot, InventorySlot itemSlot, int slotIndex)
//     {
//         // 아이콘 설정
//         Image icon = newSlot.transform.Find("ItemIcon")?.GetComponent<Image>();
//         if (icon != null)
//         {
//             icon.sprite = itemSlot.item.Icon;
//             icon.enabled = (icon.sprite != null);
//         }

//         // 수량 표시
//         TextMeshProUGUI quantityText = newSlot.transform.Find("ItemQuantity")?.GetComponent<TextMeshProUGUI>();
//         if (quantityText != null)
//         {
//             quantityText.text = (itemSlot.item.Stackable && itemSlot.quantity > 1)
//                 ? itemSlot.quantity.ToString()
//                 : string.Empty;
//         }

//         // 버리기 버튼 비활성화 (상점에서는 불필요)
//         Button dropAllButton = newSlot.transform.Find("DropAllButton")?.GetComponent<Button>();
//         if (dropAllButton != null)
//         {
//             dropAllButton.gameObject.SetActive(false);
//         }

//         Button dropSingleButton = newSlot.transform.Find("DropSingleButton")?.GetComponent<Button>();
//         if (dropSingleButton != null)
//         {
//             dropSingleButton.gameObject.SetActive(false);
//         }

//         Button dropAmountButton = newSlot.transform.Find("DropAmountButton")?.GetComponent<Button>();
//         if (dropAmountButton != null)
//         {
//             dropAmountButton.gameObject.SetActive(false);
//         }

//         // 드래그 앤 드롭 핸들러 설정
//         InventorySlotDragHandler dragHandler = newSlot.GetComponent<InventorySlotDragHandler>();
//         if (dragHandler == null)
//         {
//             dragHandler = newSlot.AddComponent<InventorySlotDragHandler>();
//         }
        
//         // InventorySlotDragHandler에 ShopMineralInventoryUI 참조 설정
//         // InventorySlotDragHandler가 ShopMineralInventoryUI도 지원하도록 수정 필요
//         dragHandler.shopMineralInventoryUI = this; // ShopMineralInventoryUI 참조 추가
//         dragHandler.inventoryUI = null; // InventoryUI는 null로 설정
//         dragHandler.slotIndex = slotIndex;
//         dragHandler.inventoryType = InventorySlotDragHandler.InventoryType.Minerals;

//         // 툴팁 트리거 추가
//         TooltipTrigger tooltipTrigger = newSlot.GetComponent<TooltipTrigger>();
//         if (tooltipTrigger == null)
//         {
//             tooltipTrigger = newSlot.AddComponent<TooltipTrigger>();
//         }

//         // 툴팁 제공자 추가
//         InventorySlotTooltipProvider tooltipProvider = newSlot.GetComponent<InventorySlotTooltipProvider>();
//         if (tooltipProvider == null)
//         {
//             tooltipProvider = newSlot.AddComponent<InventorySlotTooltipProvider>();
//         }
        
//         // InventorySlotTooltipProvider 초기화
//         // InventorySlotTooltipProvider가 ShopMineralInventoryUI도 지원하도록 수정 필요
//         tooltipProvider.InitializeForShop(itemSlot, this); // ShopMineralInventoryUI 전용 초기화 메서드
//         tooltipTrigger.tooltipProvider = tooltipProvider;

//         // 클릭 핸들러 추가 (좌클릭/우클릭/휠 입력 처리)
//         InventorySlotClickHandler clickHandler = newSlot.GetComponent<InventorySlotClickHandler>();
//         if (clickHandler == null)
//         {
//             clickHandler = newSlot.AddComponent<InventorySlotClickHandler>();
//         }
//         clickHandler.inventoryUI = null;
//         clickHandler.warehouseUI = null; // ShopMineralInventoryUI는 더 이상 사용하지 않으므로 null
//         clickHandler.slotIndex = slotIndex;
//         clickHandler.inventoryType = InventorySlotDragHandler.InventoryType.Minerals;
//     }

//     /// <summary>
//     /// 인벤토리 슬롯 교환 메서드 (드래그 앤 드롭용)
//     /// InventorySlotDragHandler에서 호출됨
//     /// </summary>
//     public bool SwapInventorySlots(int index1, int index2)
//     {
//         if (mineralInventory != null)
//         {
//             bool result = mineralInventory.SwapSlots(index1, index2);
//             // OnInventoryChanged 이벤트가 자동으로 슬롯을 업데이트함
//             return result;
//         }
//         return false;
//     }

//     /// <summary>
//     /// 인벤토리 슬롯 이동 메서드 (드래그 앤 드롭용 - 삽입)
//     /// InventorySlotDragHandler에서 호출됨
//     /// </summary>
//     public bool MoveInventorySlot(int fromIndex, int toIndex)
//     {
//         if (mineralInventory != null)
//         {
//             bool result = mineralInventory.MoveSlot(fromIndex, toIndex);
//             // OnInventoryChanged 이벤트가 자동으로 슬롯을 업데이트함
//             return result;
//         }
//         return false;
//     }

//     /// <summary>
//     /// InventorySlotDragHandler에서 사용할 수 있도록 mineralSlotContainer 반환
//     /// </summary>
//     public Transform GetMineralSlotContainer()
//     {
//         return mineralSlotContainer;
//     }

//     /// <summary>
//     /// 상점이 열려있는지 확인 (InventorySlotDragHandler에서 사용)
//     /// </summary>
//     public bool IsOpen()
//     {
//         return shopUI != null && shopUI.IsOpen();
//     }

//     /// <summary>
//     /// InventorySlotDragHandler 호환성을 위한 메서드들
//     /// </summary>
//     public bool SwapInventorySlots(InventorySlotDragHandler.InventoryType type, int index1, int index2)
//     {
//         if (type == InventorySlotDragHandler.InventoryType.Minerals)
//         {
//             return SwapInventorySlots(index1, index2);
//         }
//         return false;
//     }

//     public bool MoveInventorySlot(InventorySlotDragHandler.InventoryType type, int fromIndex, int toIndex)
//     {
//         if (type == InventorySlotDragHandler.InventoryType.Minerals)
//         {
//             return MoveInventorySlot(fromIndex, toIndex);
//         }
//         return false;
//     }
// }

