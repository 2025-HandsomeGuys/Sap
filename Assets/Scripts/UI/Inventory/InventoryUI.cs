using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using UnityEngine.InputSystem;
using System.Linq;
using UnityEngine.EventSystems;

public class InventoryUI : MonoBehaviour, IDropHandler
{
    public static InventoryUI Instance { get; private set; }

    /// <summary>
    /// 이 씬에서 인벤토리가 어떤 구성으로 동작하는지.
    /// Underground = 광물 가방(MineralPanel) 사용, Ground = 창고(WarehousePanel) 사용.
    /// </summary>
    public enum SceneKind { Underground, Ground }

    [Header("씬 설정")]
    [Tooltip("Underground: 광물 가방(MineralPanel) 사용 / Ground: 창고(WarehousePanel) 사용. 씬마다 직접 지정한다.")]
    public SceneKind sceneKind = SceneKind.Underground;

    [Header("UI 패널 연결")]
    public GameObject inventoryPanel; // 전체 인벤토리 패널
    public GameObject itemPanel;      // Items 패널 (위쪽)
    public GameObject mineralPanel;   // Minerals 패널 (가운데) — 지하씬 전용, 지상씬은 비워도 됨
    [UnityEngine.Serialization.FormerlySerializedAs("toolPanel")]
    public GameObject equipmentPanel;      // Equipments 패널 (왼쪽)

    [Header("Warehouse Integration")]
    public WarehouseUI warehouseUI; // 창고 UI 참조 — 지상씬 전용, 지하씬은 비워도 됨

    [Header("인벤토리 참조")]
    public ItemInventory itemInventory;
    public MineralInventory mineralInventory;

    [UnityEngine.Serialization.FormerlySerializedAs("toolInventory")]
    public EquipmentInventory equipmentInventory;
    
    // Equipment에 대한 별칭 (toolInventory와 동일한 객체를 참조)
    // Equipment에 대한 별칭 (호환성 유지)
    // public EquipmentInventory equipmentInventory => equipmentInventory; // Field is now named equipmentInventory

    [Header("슬롯 프리팹")]
    public GameObject inventorySlotPrefab;

    [Header("Items 패널 UI")]
    public Transform itemSlotContainer;

    [Header("Minerals 패널 UI")]
    public Transform mineralSlotContainer;
    public ScrollRect mineralScrollRect; // 광물 목록 스크롤 뷰
    public TextMeshProUGUI mineralWeightText; // 무게 표시

    [Header("Equipments 패널 UI")]
    [UnityEngine.Serialization.FormerlySerializedAs("toolSlotContainer")]
    public Transform equipmentSlotContainer;

    [Header("기타")]
    public QuantityPrompt quantityPrompt;

    [Header("장비 슬롯 플레이스홀더")]
    public Sprite headPlaceholder;
    public Sprite clothesPlaceholder;
    public Sprite shoesPlaceholder;
    public Sprite relicPlaceholder;

    [Header("슬롯 하이라이트")]
    public Sprite highlightedSlotSprite;
    private Dictionary<int, Sprite> originalSlotSprites = new Dictionary<int, Sprite>();
    private Dictionary<int, Color> originalSlotColors = new Dictionary<int, Color>();

    [Header("Localization")]
    public string labelItemKey = "ui_inv_item";
    public string labelWeightKey = "ui_inv_weight";
    public string labelEquipmentKey = "ui_inv_equip";
    public string labelDropKey = "ui_inv_drop";
    public string labelCurrentOwnedKey = "ui_inv_owned";
    public string labelCountUnitKey = "ui_inv_count_unit";
    public string labelEnterDropCountKey = "ui_inv_enter_drop_count";
    public string labelHeadKey = "ui_inv_head";
    public string labelClothesKey = "ui_inv_clothes";
    public string labelShoesKey = "ui_inv_shoes";
    public string labelRelicKey = "ui_inv_relic";

    private List<GameObject> itemSlotObjects = new List<GameObject>();
    private List<GameObject> mineralSlotObjects = new List<GameObject>();
    private List<GameObject> equipmentSlotObjects = new List<GameObject>();

    private bool isInventoryOpen = false;
    private InventorySlot selectedItemSlot;
    private InventorySlot selectedMineralSlot;
    private InventorySlot selectedEquipmentSlot;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
        }

        // 인벤토리 자동 참조 시도
        if (itemInventory == null)
            itemInventory = FindFirstObjectByType<ItemInventory>();
        if (mineralInventory == null)
            mineralInventory = FindFirstObjectByType<MineralInventory>();
        if (equipmentInventory == null)
            equipmentInventory = FindFirstObjectByType<EquipmentInventory>();

        // 참조 확인 로그 (공통 필수 참조)
        if (itemInventory == null)
            Debug.LogWarning("[InventoryUI] ItemInventory를 찾을 수 없습니다!");
        if (mineralInventory == null)
            Debug.LogWarning("[InventoryUI] MineralInventory를 찾을 수 없습니다!");
        if (equipmentInventory == null)
            Debug.LogWarning("[InventoryUI] EquipmentInventory를 찾을 수 없습니다!");

        if (itemSlotContainer == null)
            Debug.LogWarning("[InventoryUI] itemSlotContainer가 설정되지 않았습니다!");
        if (equipmentSlotContainer == null)
            Debug.LogWarning("[InventoryUI] equipmentSlotContainer가 설정되지 않았습니다!");
        if (inventorySlotPrefab == null)
            Debug.LogWarning("[InventoryUI] inventorySlotPrefab이 설정되지 않았습니다!");

        // 씬 종류별 필수 참조 검사 — 반대쪽 씬에서는 해당 요소가 아예 없어도 정상
        if (sceneKind == SceneKind.Underground)
        {
            if (mineralSlotContainer == null)
                Debug.LogWarning("[InventoryUI] 지하씬(Underground)인데 mineralSlotContainer가 설정되지 않았습니다!");
        }
        else
        {
            if (warehouseUI == null)
                warehouseUI = FindFirstObjectByType<WarehouseUI>();
            if (warehouseUI == null)
                Debug.LogWarning("[InventoryUI] 지상씬(Ground)인데 WarehouseUI를 찾을 수 없습니다!");
        }

        SubscribeToEvents();

        // 초기 UI 동기화
        UpdateMineralWeight();
        // Start에서는 인벤토리가 닫혀있으므로 슬롯 업데이트는 하지 않음
        // UpdateAllSlots();
        UpdateDescription(null, null, null);

        if (warehouseUI != null) warehouseUI.CloseWarehouse();

        SetupGoldUI();
    }

    void Update()
    {
        // 'i' 키 입력 처리는 UIStateManager로 이관되었습니다.
    }

    void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    private void SubscribeToEvents()
    {
        if (itemInventory != null)
        {
            itemInventory.OnInventoryChanged -= OnItemInventoryChanged;
            itemInventory.OnInventoryChanged += OnItemInventoryChanged;
        }
        if (mineralInventory != null)
        {
            mineralInventory.OnInventoryChanged -= OnMineralInventoryChanged;
            mineralInventory.OnInventoryChanged += OnMineralInventoryChanged;
        }
        if (equipmentInventory != null)
        {
            equipmentInventory.OnInventoryChanged -= OnEquipmentInventoryChanged;
            equipmentInventory.OnInventoryChanged += OnEquipmentInventoryChanged;
        }

        // 언어 변경 이벤트 구독
        if (LanguageManager.Instance != null)
        {
            LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
            LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
        }
    }

    private PlayerStat playerStats;

    // ...



    // ...

    private void UnsubscribeFromEvents()
    {
        if (itemInventory != null)
            itemInventory.OnInventoryChanged -= OnItemInventoryChanged;
        if (mineralInventory != null)
            mineralInventory.OnInventoryChanged -= OnMineralInventoryChanged;
        if (equipmentInventory != null)
            equipmentInventory.OnInventoryChanged -= OnEquipmentInventoryChanged;
            
        if (playerStats != null)
            playerStats.OnGoldChanged -= UpdateGoldUI;

        if (LanguageManager.Instance != null)
            LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(LanguageType newLanguage)
    {
        UpdateMineralWeight();
        if (isInventoryOpen)
            UpdateAllSlots();
    }

    private void OnItemInventoryChanged()
    {
        if (isInventoryOpen)
        {
            UpdateItemSlots();
            if (selectedItemSlot != null && selectedItemSlot.item != null)
                UpdateDescription(selectedItemSlot, null, null);
        }
    }

    private void OnMineralInventoryChanged()
    {
        UpdateMineralWeight();
        if (isInventoryOpen)
        {
            UpdateMineralSlots();
            if (selectedMineralSlot != null && selectedMineralSlot.item != null)
                UpdateDescription(null, selectedMineralSlot, null);
        }
    }

    private void OnEquipmentInventoryChanged()
    {
        if (isInventoryOpen)
        {
            UpdateEquipmentSlots();
            if (selectedEquipmentSlot != null && selectedEquipmentSlot.item != null)
                UpdateDescription(null, null, selectedEquipmentSlot);
        }
    }

    public void OnOpenInventory(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            ToggleInventory();
        }
    }

    public void ToggleInventory()
    {
        if (isInventoryOpen) CloseInventory();
        else OpenInventory();
    }

    public void OpenInventory()
    {
        if (inventoryPanel == null) return;

        isInventoryOpen = true;
        inventoryPanel.SetActive(true);
        
        // 모든 공통 패널 명시적 활성화
        if (itemPanel != null) itemPanel.SetActive(true);
        if (equipmentPanel != null) equipmentPanel.SetActive(true);
        ToggleGoldPanel(true);

        UpdateSceneLayout(); 
        UpdateAllSlots();
        UpdateDescription(null, null, null);

        if (mineralScrollRect != null) mineralScrollRect.verticalNormalizedPosition = 1f;
    }

    public void CloseInventory()
    {
        if (inventoryPanel == null) return;

        isInventoryOpen = false;
        inventoryPanel.SetActive(false);
        ToggleGoldPanel(false);
        
        // 공통 패널 비활성화 (필요한 경우)
        if (itemPanel != null) itemPanel.SetActive(false);
        if (equipmentPanel != null) equipmentPanel.SetActive(false);

        if (warehouseUI != null) warehouseUI.CloseWarehouse();
        if (mineralPanel != null) mineralPanel.SetActive(false);
    }

    public bool IsOpen()
    {
        return isInventoryOpen;
    }

    // Minerals 패널 업데이트
    public void UpdateMineralWeight()
    {
        if (mineralInventory != null && mineralWeightText != null)
        {
            mineralWeightText.text = $"{(LanguageManager.Instance?.L(labelWeightKey) ?? labelWeightKey)}: {mineralInventory.TotalWeight:F1} / {mineralInventory.maxWeightLimit:F1}";
        }
    }

    // 설명 업데이트 - 툴팁으로 대체되어 더 이상 사용하지 않음
    // 슬롯 선택 상태만 업데이트 (필요한 경우를 위해 유지)
    public void UpdateDescription(InventorySlot itemSlot, InventorySlot mineralSlot, InventorySlot equipmentSlot)
    {
        selectedItemSlot = itemSlot;
        selectedMineralSlot = mineralSlot;
        selectedEquipmentSlot = equipmentSlot;
        // 모든 상세 설명은 툴팁으로 표시됨
    }

    public void UpdateAllSlots()
    {
        // 인벤토리가 열려있을 때만 슬롯 업데이트
        if (!isInventoryOpen) return;
        UpdateItemSlots();
        UpdateMineralSlots();
        UpdateEquipmentSlots();
    }

    // Items 슬롯 업데이트
    private void UpdateItemSlots()
    {
        // 인벤토리가 열려있지 않으면 업데이트하지 않음
        if (!isInventoryOpen) return;

        if (itemSlotContainer == null || inventorySlotPrefab == null || itemInventory == null)
        {
            Debug.LogWarning("[InventoryUI] Item 슬롯 업데이트 실패: 필수 참조가 없습니다.");
            return;
        }

        // 1. 슬롯 개수 맞추기 (없으면 생성, 많으면 삭제 - 풀링 효과)
        int maxSlots = itemInventory.maxSlotCount;
        int currentChildCount = itemSlotContainer.childCount;
        
        // 부족하면 추가
        while (currentChildCount < maxSlots)
        {
            GameObject newSlot = Instantiate(inventorySlotPrefab, itemSlotContainer);
            itemSlotObjects.Add(newSlot);
            currentChildCount++;
        }
        
        // 많으면 삭제 (뒤에서부터)
        while (currentChildCount > maxSlots)
        {
            GameObject lastSlot = itemSlotContainer.GetChild(currentChildCount - 1).gameObject;
            itemSlotObjects.Remove(lastSlot);
            Destroy(lastSlot);
            currentChildCount--;
        }

        // 2. 각 슬롯 업데이트
        var list = itemInventory.ReadonlyItems;

        for (int i = 0; i < maxSlots; i++)
        {
            GameObject slotObj = itemSlotContainer.GetChild(i).gameObject;
            InventorySlot itemSlot = (i < list.Count) ? list[i] : new InventorySlot(null, 0);

            SetupSlotUI(slotObj, itemSlot, i, InventorySlotDragHandler.InventoryType.Items, (slot) =>
            {
                UpdateDescription(slot, null, null);
            }, (item) =>
            {
                if (item is ItemSO itemSO)
                {
                    int removed = itemInventory.RemoveAllOf(itemSO);
                    if (removed > 0) UpdateItemSlots();
                }
            }, (item) =>
            {
                if (item is ItemSO itemSO)
                {
                    bool ok = itemInventory.RemoveItem(itemSO, 1);
                    if (ok) UpdateItemSlots();
                }
            }, (item) =>
            {
                if (item is ItemSO itemSO)
                {
                    int total = itemInventory.CountOf(itemSO);
                    if (total <= 0) return;

                    string title = $"{itemSO.DisplayName} {(LanguageManager.Instance?.L(labelDropKey) ?? labelDropKey)}";
                    string unit = LanguageManager.Instance?.L(labelCountUnitKey) ?? labelCountUnitKey;
                    string info = $"{(LanguageManager.Instance?.L(labelCurrentOwnedKey) ?? labelCurrentOwnedKey)}: {total}{unit}\n{(LanguageManager.Instance?.L(labelEnterDropCountKey) ?? labelEnterDropCountKey)}";

                    if (quantityPrompt != null)
                    {
                        quantityPrompt.Show(title, 1, total, (value) =>
                        {
                            bool ok = itemInventory.RemoveItem(itemSO, value);
                            if (ok) UpdateItemSlots();
                        }, info);
                    }
                }
            });

            // 아이템이 없는 빈 슬롯의 시각적 처리
            Image icon = slotObj.transform.Find("ItemIcon")?.GetComponent<Image>();
            TextMeshProUGUI quantityText = slotObj.transform.Find("ItemQuantity")?.GetComponent<TextMeshProUGUI>();

            if (itemSlot.item == null)
            {
                if (icon != null) icon.enabled = false;
                if (quantityText != null) quantityText.text = string.Empty;
            }
            else
            {
                // [추가] 슬롯당 1개만 들어가므로 아이템이 있어도 수량 텍스트는 표시하지 않음
                if (quantityText != null) quantityText.text = string.Empty;
            }
        }

        // 첫 번째 유효 아이템 설명 표시
        if (selectedItemSlot == null)
        {
            InventorySlot firstValid = list.FirstOrDefault(s => s != null && s.item != null);
            if (firstValid != null)
                UpdateDescription(firstValid, null, null);
        }
    }

    // Minerals 슬롯 업데이트
    private void UpdateMineralSlots()
    {
        // 인벤토리가 열려있지 않으면 업데이트하지 않음
        if (!isInventoryOpen) return;

        // 지상씬은 광물 가방 UI를 쓰지 않음 (창고가 대신함) — MineralPanel이 없어도 정상
        if (sceneKind != SceneKind.Underground) return;

        foreach (GameObject slot in mineralSlotObjects)
        {
            Destroy(slot);
        }
        mineralSlotObjects.Clear();

        if (mineralSlotContainer == null || inventorySlotPrefab == null || mineralInventory == null)
        {
            Debug.LogWarning("[InventoryUI] Mineral 슬롯 업데이트 실패: 필수 참조가 없습니다.");
            return;
        }

        var list = mineralInventory.ReadonlyItems;
        // Debug.Log($"[InventoryUI] UpdateMineralSlots: {list.Count}개의 광물 슬롯 생성 중...");

        for (int i = 0; i < list.Count; i++)
        {
            var itemSlot = list[i];
            if (itemSlot == null || itemSlot.item == null) continue;

            GameObject newSlot = Instantiate(inventorySlotPrefab, mineralSlotContainer);
            mineralSlotObjects.Add(newSlot);

            SetupSlotUI(newSlot, itemSlot, i, InventorySlotDragHandler.InventoryType.Minerals, (slot) =>
            {
                UpdateDescription(null, slot, null);
            }, (item) =>
            {
                if (item is MineralSO mineralSO)
                {
                    int removed = mineralInventory.RemoveAllOf(mineralSO);
                    if (removed > 0) UpdateMineralSlots();
                }
            }, (item) =>
            {
                if (item is MineralSO mineralSO)
                {
                    bool ok = mineralInventory.RemoveItem(mineralSO, 1);
                    if (ok) UpdateMineralSlots();
                }
            }, (item) =>
            {
                if (item is MineralSO mineralSO)
                {
                    int total = mineralInventory.CountOf(mineralSO);
                    if (total <= 0) return;

                    string title = $"{mineralSO.DisplayName} {(LanguageManager.Instance?.L(labelDropKey) ?? labelDropKey)}";
                    string unit = LanguageManager.Instance?.L(labelCountUnitKey) ?? labelCountUnitKey;
                    string info = $"{(LanguageManager.Instance?.L(labelCurrentOwnedKey) ?? labelCurrentOwnedKey)}: {total}{unit}\n{(LanguageManager.Instance?.L(labelEnterDropCountKey) ?? labelEnterDropCountKey)}";

                    if (quantityPrompt != null)
                    {
                        quantityPrompt.Show(title, 1, total, (value) =>
                        {
                            bool ok = mineralInventory.RemoveItem(mineralSO, value);
                            if (ok) UpdateMineralSlots();
                        }, info);
                    }
                }
            });
        }

        // 첫 번째 광물 설명 표시
        if (list.Count > 0)
        {
            InventorySlot firstValid = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].item != null)
                {
                    firstValid = list[i];
                    break;
                }
            }
            UpdateDescription(null, firstValid, null);
        }
        else
            UpdateDescription(null, null, null);



        // 레이아웃 재빌드 (한 프레임 대기 후 실행)
        if (gameObject.activeInHierarchy)
        {
            StartCoroutine(UpdateMineralScrollLayout());
        }
    }

    private System.Collections.IEnumerator UpdateMineralScrollLayout()
    {
        yield return null;

        if (mineralScrollRect != null && mineralScrollRect.content != null)
        {
            // 스크롤 방향 및 타입 설정
            mineralScrollRect.horizontal = false;
            mineralScrollRect.vertical = true;
            mineralScrollRect.movementType = ScrollRect.MovementType.Clamped; // 범위를 벗어나지 않도록 Clamped로 변경

            // 직접 높이 계산 및 적용
            ApplyGridHeight(mineralScrollRect);
            
            // 스크롤 감도 설정
            mineralScrollRect.scrollSensitivity = 20f;
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

    // Equipments 슬롯 업데이트
    private void UpdateEquipmentSlots()
    {
        // 인벤토리가 열려있지 않으면 업데이트하지 않음
        if (!isInventoryOpen) return;

        if (equipmentSlotContainer == null || inventorySlotPrefab == null || equipmentInventory == null)
        {
            Debug.LogWarning("[InventoryUI] Equipment 슬롯 업데이트 실패: 필수 참조가 없습니다.");
            return;
        }

        // 1. 슬롯 개수 맞추기 (없으면 생성, 많으면 삭제 - 풀링 효과)
        // 사용자가 에디터에서 미리 만들어둔 슬롯이 있다면 그것을 사용함
        int currentChildCount = equipmentSlotContainer.childCount;
        int slotCount = EquipmentInventory.SlotCount; // 머리·옷·신발 + 유물 2칸

        // 부족하면 추가
        while (currentChildCount < slotCount)
        {
            Instantiate(inventorySlotPrefab, equipmentSlotContainer);
            currentChildCount++;
        }

        // 많으면 삭제 (뒤에서부터)
        while (currentChildCount > slotCount)
        {
            DestroyImmediate(equipmentSlotContainer.GetChild(currentChildCount - 1).gameObject);
            currentChildCount--;
        }

        // 2. 각 슬롯 업데이트
        var list = equipmentInventory.ReadonlyItems;

        for (int i = 0; i < slotCount; i++)
        {
            // UI 객체 가져오기 (0: 머리, 1: 옷, 2: 신발, 3: 유물)
            GameObject slotObj = equipmentSlotContainer.GetChild(i).gameObject;
            
            // 데이터 가져오기 (없으면 빈 슬롯으로 생성)
            InventorySlot itemSlot = (i < list.Count) ? list[i] : new InventorySlot(null, 0);

            // 아이콘 및 텍스트 컴포넌트 찾기
            Image icon = slotObj.transform.Find("ItemIcon")?.GetComponent<Image>();
            TextMeshProUGUI quantityText = slotObj.transform.Find("ItemQuantity")?.GetComponent<TextMeshProUGUI>();

            if (itemSlot.item != null)
            {
                // 아이템이 있는 경우: 아이콘 표시
                if (icon != null)
                {
                    icon.sprite = itemSlot.item.Icon;
                    icon.enabled = true;
                    icon.color = Color.white;
                }
                if (quantityText != null)
                {
                    quantityText.text = string.Empty; // 장비는 개수 표시 안함
                }
            }
            else
            {
                // 아이템이 없는 경우: 플레이스홀더 처리
                if (icon != null)
                {
                    // 유물은 슬롯이 여러 개라 인덱스가 아니라 슬롯 타입으로 플레이스홀더를 고른다
                    Sprite placeholder = null;
                    switch (EquipmentInventory.TypeOfSlot(i))
                    {
                        case EquipmentType.Head: placeholder = headPlaceholder; break;
                        case EquipmentType.Clothes: placeholder = clothesPlaceholder; break;
                        case EquipmentType.Shoes: placeholder = shoesPlaceholder; break;
                        case EquipmentType.Relic: placeholder = relicPlaceholder; break;
                    }

                    if (placeholder != null)
                    {
                        icon.sprite = placeholder;
                        icon.enabled = true;
                        icon.color = new Color(1, 1, 1, 1f); 
                    }
                    else
                    {
                        icon.enabled = false;
                    }
                }

                if (quantityText != null)
                {
                    // Placeholder 이미지가 있으면 텍스트 숨김
                    var slotType = EquipmentInventory.TypeOfSlot(i);
                    bool hasPlaceholder = (slotType == EquipmentType.Head && headPlaceholder != null) ||
                                          (slotType == EquipmentType.Clothes && clothesPlaceholder != null) ||
                                          (slotType == EquipmentType.Shoes && shoesPlaceholder != null) ||
                                          (slotType == EquipmentType.Relic && relicPlaceholder != null);

                    if (hasPlaceholder)
                    {
                        quantityText.text = string.Empty;
                    }
                    else
                    {
                        string partName = "";
                        var lm = LanguageManager.Instance;
                        switch (slotType)
                        {
                            case EquipmentType.Head: partName = lm?.L(labelHeadKey) ?? labelHeadKey; break;
                            case EquipmentType.Clothes: partName = lm?.L(labelClothesKey) ?? labelClothesKey; break;
                            case EquipmentType.Shoes: partName = lm?.L(labelShoesKey) ?? labelShoesKey; break;
                            case EquipmentType.Relic: partName = lm?.L(labelRelicKey) ?? labelRelicKey; break;
                        }
                        quantityText.text = partName;
                        quantityText.alignment = TextAlignmentOptions.Center;
                        quantityText.fontSize = 12;
                        quantityText.color = new Color(1, 1, 1, 1f);
                    }
                }
            }

            // 이벤트 바인딩
            SetupSlotUI(slotObj, itemSlot, i, InventorySlotDragHandler.InventoryType.Equipments, (slot) =>
            {
                UpdateDescription(null, null, slot);
            }, (item) =>
            {
                if (item is EquipmentSO equipmentSO)
                {
                    bool ok = equipmentInventory.RemoveItem(equipmentSO, 1);
                    if (ok) UpdateEquipmentSlots();
                }
            }, (item) =>
            {
                if (item is EquipmentSO equipmentSO)
                {
                    bool ok = equipmentInventory.RemoveItem(equipmentSO, 1);
                    if (ok) UpdateEquipmentSlots();
                }
            }, null);
        }

        // 첫 번째 유효 장비 설명 표시 (선택사항)
        if (selectedEquipmentSlot == null && list != null && list.Count > 0)
        {
            InventorySlot firstValid = list.FirstOrDefault(s => s != null && s.item != null);
            if (firstValid != null)
                UpdateDescription(null, null, firstValid);
        }
    }

    public void HighlightEquipmentSlot(EquipmentSO equipment)
    {
        if (equipment == null || equipmentSlotContainer == null) return;
        
        // 유물은 슬롯이 2개라 '넣을 자리'를 인벤토리에 물어본다 (빈 칸 우선)
        int slotIndex = equipmentInventory != null
            ? equipmentInventory.FindSlotForAdd(equipment)
            : -1;

        if (slotIndex >= 0 && slotIndex < equipmentSlotContainer.childCount)
        {
            Transform slotObj = equipmentSlotContainer.GetChild(slotIndex);
            Image bgImage = slotObj.GetComponent<Image>();
            if (bgImage != null)
            {
                if (!originalSlotSprites.ContainsKey(slotIndex))
                {
                    originalSlotSprites[slotIndex] = bgImage.sprite;
                    originalSlotColors[slotIndex] = bgImage.color;
                }

                if (highlightedSlotSprite != null)
                {
                    bgImage.sprite = highlightedSlotSprite;
                }
                else
                {
                    bgImage.color = new Color(1f, 0.9f, 0.5f, 1f); // 황금색 톤으로 하이라이트 (스프라이트 미지정 시 대비)
                }
            }
        }
    }

    public void ClearEquipmentSlotHighlights()
    {
        if (equipmentSlotContainer == null) return;
        
        for (int i = 0; i < equipmentSlotContainer.childCount; i++)
        {
            Transform slotObj = equipmentSlotContainer.GetChild(i);
            Image bgImage = slotObj.GetComponent<Image>();
            if (bgImage != null)
            {
                if (originalSlotSprites.ContainsKey(i))
                {
                    bgImage.sprite = originalSlotSprites[i];
                    bgImage.color = originalSlotColors[i];
                }
                else
                {
                    // Fallback
                    bgImage.color = Color.white; 
                }
            }
        }
    }

    // 슬롯 UI 설정 헬퍼 메서드
    private void SetupSlotUI(GameObject newSlot, InventorySlot itemSlot, int slotIndex, InventorySlotDragHandler.InventoryType inventoryType,
        System.Action<InventorySlot> onSlotClick,
        System.Action<InterfaceInventoryItem> onDropAll,
        System.Action<InterfaceInventoryItem> onDropSingle,
        System.Action<InterfaceInventoryItem> onDropAmount)
    {
        Image icon = newSlot.transform.Find("ItemIcon")?.GetComponent<Image>();
        TextMeshProUGUI quantityText = newSlot.transform.Find("ItemQuantity")?.GetComponent<TextMeshProUGUI>();

        if (icon != null)
        {
            if (itemSlot.item != null)
            {
                icon.sprite = itemSlot.item.Icon;
                icon.enabled = (icon.sprite != null);
            }
            else
            {
                // 아이템이 없을 때
                // 장비 슬롯은 플레이스홀더를 유지해야 하므로 건드리지 않음
                // 그 외(Items, Minerals)는 아이콘 숨김
                if (inventoryType != InventorySlotDragHandler.InventoryType.Equipments)
                {
                    icon.enabled = false;
                }
            }
        }

        if (quantityText != null)
        {
            if (itemSlot.item != null)
            {
                quantityText.text = (itemSlot.item.Stackable && itemSlot.quantity > 1)
                    ? itemSlot.quantity.ToString()
                    : string.Empty;
            }
            else
            {
                // 아이템 없음: 텍스트 비움 
                // 단, 장비 슬롯(Equipments)의 경우 UpdateEquipmentSlots에서 이미 플레이스홀더 텍스트를 설정했을 수 있으므로
                // 여기서는 굳이 건드리지 않거나, 확실히 비워야 할 경우만 비움.
                // 일반 아이템 슬롯이라면 비우는 게 맞음.
                if (inventoryType != InventorySlotDragHandler.InventoryType.Equipments)
                {
                    quantityText.text = string.Empty;
                }
            }
        }

        Button slotButton = newSlot.GetComponent<Button>();
        if (slotButton != null)
        {
            slotButton.onClick.RemoveAllListeners();
            slotButton.onClick.AddListener(() => onSlotClick(itemSlot));
        }

        Button dropAllButton = newSlot.transform.Find("DropAllButton")?.GetComponent<Button>();
        if (dropAllButton != null && onDropAll != null)
        {
            dropAllButton.onClick.RemoveAllListeners();
            dropAllButton.onClick.AddListener(() => onDropAll(itemSlot.item));
        }

        Button dropSingleButton = newSlot.transform.Find("DropSingleButton")?.GetComponent<Button>();
        if (dropSingleButton != null && onDropSingle != null)
        {
            bool shouldBeActive = itemSlot.item.Stackable ? itemSlot.quantity > 0 : true;
            dropSingleButton.gameObject.SetActive(shouldBeActive);
            if (shouldBeActive)
            {
                dropSingleButton.onClick.RemoveAllListeners();
                dropSingleButton.onClick.AddListener(() => onDropSingle(itemSlot.item));
            }
        }

        Button dropAmountButton = newSlot.transform.Find("DropAmountButton")?.GetComponent<Button>();
        if (dropAmountButton != null && onDropAmount != null)
        {
            dropAmountButton.gameObject.SetActive(true);
            dropAmountButton.onClick.RemoveAllListeners();
            dropAmountButton.onClick.AddListener(() => onDropAmount(itemSlot.item));
        }

        // 드래그 앤 드롭 핸들러 설정
        InventorySlotDragHandler dragHandler = newSlot.GetComponent<InventorySlotDragHandler>();
        if (dragHandler == null)
        {
            dragHandler = newSlot.AddComponent<InventorySlotDragHandler>();
        }
        dragHandler.inventoryUI = this;
        dragHandler.slotIndex = slotIndex;
        dragHandler.inventoryType = inventoryType;

        // 툴팁 트리거 추가
        TooltipTrigger tooltipTrigger = newSlot.GetComponent<TooltipTrigger>();
        if (tooltipTrigger == null)
        {
            tooltipTrigger = newSlot.AddComponent<TooltipTrigger>();
        }

        // 툴팁 제공자 추가
        InventorySlotTooltipProvider tooltipProvider = newSlot.GetComponent<InventorySlotTooltipProvider>();
        if (tooltipProvider == null)
        {
            tooltipProvider = newSlot.AddComponent<InventorySlotTooltipProvider>();
        }
        tooltipProvider.Initialize(itemSlot, this);
        tooltipTrigger.tooltipProvider = tooltipProvider;

        // 클릭 핸들러 추가 (좌클릭/우클릭/휠 입력 처리)
        InventorySlotClickHandler clickHandler = newSlot.GetComponent<InventorySlotClickHandler>();
        if (clickHandler == null)
        {
            clickHandler = newSlot.AddComponent<InventorySlotClickHandler>();
        }
        clickHandler.inventoryUI = this;
        clickHandler.warehouseUI = null;
        clickHandler.slotIndex = slotIndex;
        clickHandler.inventoryType = inventoryType;
    }

    // 인벤토리 슬롯 교환 메서드 (드래그 앤 드롭용)
    public bool SwapInventorySlots(InventorySlotDragHandler.InventoryType type, int index1, int index2)
    {
        // OnInventoryChanged 이벤트가 자동으로 슬롯을 업데이트하므로 여기서는 호출하지 않음
        switch (type)
        {
            case InventorySlotDragHandler.InventoryType.Items:
                if (itemInventory != null)
                {
                    return itemInventory.SwapSlots(index1, index2);
                }
                break;
            case InventorySlotDragHandler.InventoryType.Minerals:
                if (mineralInventory != null)
                {
                    return mineralInventory.SwapSlots(index1, index2);
                }
                break;
            case InventorySlotDragHandler.InventoryType.Equipments:
                if (equipmentInventory != null)
                {
                    return equipmentInventory.SwapSlots(index1, index2);
                }
                break;
        }
        return false;
    }

    // 인벤토리 슬롯 이동 메서드 (드래그 앤 드롭용 - 삽입)
    public bool MoveInventorySlot(InventorySlotDragHandler.InventoryType type, int fromIndex, int toIndex)
    {
        // OnInventoryChanged 이벤트가 자동으로 슬롯을 업데이트하므로 여기서는 호출하지 않음
        switch (type)
        {
            case InventorySlotDragHandler.InventoryType.Items:
                if (itemInventory != null)
                {
                    return itemInventory.MoveSlot(fromIndex, toIndex);
                }
                break;
            case InventorySlotDragHandler.InventoryType.Minerals:
                if (mineralInventory != null)
                {
                    return mineralInventory.MoveSlot(fromIndex, toIndex);
                }
                break;
            case InventorySlotDragHandler.InventoryType.Equipments:
                if (equipmentInventory != null)
                {
                    return equipmentInventory.MoveSlot(fromIndex, toIndex);
                }
                break;
        }
        return false;
    }

    // 씬 종류(sceneKind)에 따른 UI 레이아웃 적용 — 없는 패널은 건너뜀
    private void UpdateSceneLayout()
    {
        if (sceneKind == SceneKind.Ground)
        {
            // 지상: Warehouse ON, Mineral OFF
            if (mineralPanel != null) mineralPanel.SetActive(false);
            if (warehouseUI != null) warehouseUI.OpenWarehouse();
        }
        else
        {
            // 지하: Warehouse OFF, Mineral ON
            if (warehouseUI != null) warehouseUI.CloseWarehouse();
            if (mineralPanel != null) mineralPanel.SetActive(true);
        }
    }

    /// <summary>
    /// 현재 씬이 지하씬인지 판별 (인스펙터의 sceneKind 설정 기준)
    /// </summary>
    public bool IsUndergroundScene()
    {
        return sceneKind == SceneKind.Underground;
    }

    // ================================================================================================
    //  Gold UI Setup
    // ================================================================================================
    [Header("Gold UI")]
    public GameObject goldPanel;
    public TextMeshProUGUI goldText;


    /// <summary>
    /// PlayerStat 연결 및 이벤트 구독
    /// </summary>
    private void SetupGoldUI()
    {
        if (playerStats == null)
            playerStats = FindFirstObjectByType<PlayerStat>();

        if (playerStats != null)
        {
            playerStats.OnGoldChanged -= UpdateGoldUI;
            playerStats.OnGoldChanged += UpdateGoldUI;
        }

        // 초기 상태: 꺼짐
        if (goldPanel != null) goldPanel.SetActive(false);
    }

    private void UpdateGoldUI(int goldAmount)
    {
        if (goldText != null)
        {
            goldText.text = $"{goldAmount:N0} $";
        }
    }

    private void ToggleGoldPanel(bool isOpen)
    {
        if (goldPanel != null)
        {
            goldPanel.SetActive(isOpen);
            if (isOpen && playerStats != null)
            {
                UpdateGoldUI(playerStats.Gold);
            }
        }
    }

    // --- [추가] 인벤토리 배경 드롭 처리 (창고 -> 가방) ---
    public void OnDrop(PointerEventData eventData)
    {
        InventorySlotDragHandler draggedHandler = eventData.pointerDrag?.GetComponent<InventorySlotDragHandler>();
        if (draggedHandler == null) return;

        // 드래그된 아이템 가져오기
        InventorySlot draggedSlot = draggedHandler.GetSlotItem();
        if (draggedSlot == null || draggedSlot.item == null) return;

        bool success = false;

        // 출처가 창고인 경우만 처리 (가방 내 정렬은 슬롯 단위로 처리됨)
        if (draggedHandler.inventoryType == InventorySlotDragHandler.InventoryType.WarehouseItems)
        {
            if (draggedSlot.item is ItemSO item && itemInventory != null)
            {
                int added = itemInventory.AddItem(item, draggedSlot.quantity);
                if (added > 0)
                {
                    WarehouseManager.Instance.RemoveItemAt(draggedHandler.slotIndex, added);
                    success = true;
                }
            }
        }
        else if (draggedHandler.inventoryType == InventorySlotDragHandler.InventoryType.WarehouseMinerals)
        {
            if (draggedSlot.item is MineralSO mineral && mineralInventory != null)
            {
                int added = mineralInventory.AddItem(mineral, draggedSlot.quantity);
                if (added > 0)
                {
                    WarehouseManager.Instance.RemoveMineralAt(draggedHandler.slotIndex, added);
                    success = true;
                }
            }
        }
        else if (draggedHandler.inventoryType == InventorySlotDragHandler.InventoryType.WarehouseEquipments)
        {
            if (draggedSlot.item is EquipmentSO eq && equipmentInventory != null)
            {
                // 장비는 빈 슬롯에 자동 장착 시도 (AddItem)
                int added = equipmentInventory.AddItem(eq, 1);
                if (added > 0)
                {
                    WarehouseManager.Instance.RemoveEquipmentAt(draggedHandler.slotIndex, 1);
                    success = true;
                }
            }
        }

        if (success)
        {
            draggedHandler.CleanupDragObject();
        }
    }
}
