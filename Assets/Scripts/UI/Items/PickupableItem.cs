using UnityEngine;

/// <summary>
/// SRP: 자신을 인벤토리에 넣고 월드에서 제거하는 것만 담당한다.
/// LSP: IInteractable 구현 → PlayerInteractor가 타입 무관하게 처리 가능.
/// DIP: 플레이어 구체 클래스에 의존하지 않고
///      interactor.GetComponent 로 인벤토리를 느슨하게 참조한다.
/// ISP: 인벤토리 데이터 제공(InterfaceInventoryItem)과
///      상호작용(IInteractable)을 별도 인터페이스로 분리해서 사용한다.
/// </summary>
[RequireComponent(typeof(Mineable))]
public class PickupableItem : MonoBehaviour, IInteractable, IHighlightable, IInteractionPrompt
{

    private Mineable _mineable;
    private MineralItemController _mineralController;
    private MineralPickupGlow _glow;

    public string InteractionPrompt => "줍기";
    public int InteractionPriority => 0; // 엘리베이터(10)보다 낮음

    /// <summary>
    /// 땅에서 떨어져 나왔고(IsEmbedded=false) 지형 밖으로 드러난(IsExposed) 광물만 주울 수 있다.
    /// 두 조건이 모두 필요하다 — 지지 상실로 깨어났지만 지형에 갇혀 화면에 안 보이는 광물이 있기 때문.
    /// 후보 단계에서 빠지므로 하이라이트도, "줍기" 문구도 뜨지 않는다.
    /// 드롭 아이템처럼 컨트롤러가 없는 오브젝트는 항상 주울 수 있다.
    /// </summary>
    public bool CanInteract
        => _mineralController == null
           || (!_mineralController.IsEmbedded && _mineralController.IsExposed);

    // 근접 안내 문구 (Localization 키로 언어 전환 대응)
    public InteractionPromptInfo GetInteractionPrompt()
        => InteractionPromptInfo.Ok("interact_pickup", "줍기");

    private void Awake()
    {
        _mineable          = GetComponent<Mineable>();
        _mineralController = GetComponent<MineralItemController>();
        _glow              = GetComponent<MineralPickupGlow>();
    }

    // IHighlightable — PlayerInteractor가 타깃 변경 시 호출
    public void SetHighlighted(bool highlighted)
    {
        _glow?.SetHighlighted(highlighted);
    }

    /// <summary>
    /// 같은 우선순위 후보들 사이에서 "비싼 것부터" 고르기 위한 정렬값(광물 전용).
    ///
    /// 기저 가격(<see cref="MineralPriceDatabase.GetBasePrice"/>)을 쓴다 —
    /// GetPrice를 쓰면 판매가 업그레이드를 산 광물이 먼저 주워지는 되먹임이 생긴다.
    ///
    /// 광물이 아니거나(드롭 아이템) 가격표가 없으면 false를 돌려주고,
    /// 호출측(PlayerInteractor)은 기존대로 거리 기준으로 판정한다.
    /// </summary>
    public bool TryGetPickupValue(out int value)
    {
        value = 0;

        if (ResolveItemData() is not MineralSO mineralSO) return false;

        var db = PriceDataLoader.Instance != null ? PriceDataLoader.Instance.MineralPrices : null;
        if (db == null) return false;

        value = db.GetBasePrice(mineralSO.mineralID);
        return true;
    }

    /// <summary>
    /// itemData 우선순위:
    ///   1. ObjectPooler가 런타임에 할당한 Mineable.itemData
    ///   2. 프리팹에 직접 설정된 MineralItemController.mineralData (폴백)
    /// </summary>
    private InterfaceInventoryItem ResolveItemData()
    {
        if (_mineable.itemData != null)
            return _mineable.itemData;

        if (_mineralController != null && _mineralController.mineralData != null)
        {
            // 폴백: MineralItemController.mineralData 사용
            _mineable.itemData = _mineralController.mineralData;

            // mineralID도 동기화 — ReturnToPool이 올바른 풀을 찾을 수 있도록
            if (_mineable.mineralID == MineralID.None)
                _mineable.mineralID = _mineralController.mineralData.mineralID;

            return _mineable.itemData;
        }

        return null;
    }

    /// <summary>
    /// PlayerInteractor가 호출한다.
    /// InventoryUI.Instance를 통해 인벤토리 컴포넌트를 참조하므로,
    /// 비활성 UI 패널에 있는 인벤토리도 안전하게 접근 가능하다.
    /// </summary>
    public void Interact(GameObject interactor)
    {
        Debug.Log($"[PickupableItem] Interact 호출됨 — {gameObject.name}");

        // 탐지 단계(PlayerInteractor)에서 이미 걸러지지만, 자석 등 다른 호출 경로도 있으므로 여기서도 막는다.
        if (!CanInteract) return;

        if (_mineable == null)
        {
            Debug.LogError("[PickupableItem] Mineable 컴포넌트가 없습니다.");
            return;
        }

        InterfaceInventoryItem itemData = ResolveItemData();
        if (itemData == null)
        {
            Debug.LogError("[PickupableItem] itemData를 찾을 수 없습니다. " +
                           "MineralItemController.mineralData 또는 ObjectPooler의 MineralDatabase 연결을 확인하세요.");
            return;
        }

        bool added = false;

        if (itemData is MineralSO mineralSO)
        {
            var mineralInventory = ResolveMineralInventory(interactor);
            if (mineralInventory == null)
            {
                Debug.LogError("[PickupableItem] MineralInventory를 찾지 못했습니다. " +
                               "플레이어에 MineralInventory 컴포넌트가 있는지 확인하세요.");
                return;
            }
            int qty = CalculatePickupQuantity();
            // AddItem 안에서 CollectionCodex.Discover가 불리므로, 첫 발견 여부는 반드시 그 전에 확인한다.
            bool wasKnown = CollectionCodex.IsDiscovered(CodexCategory.Mineral, mineralSO.mineralID.ToString());
            int addedCount = mineralInventory.AddItem(mineralSO, qty);
            added = addedCount > 0;
            if (added)
            {
                AcquisitionNotifier.NotifyMineral(mineralSO, addedCount, isNew: !wasKnown);
                Debug.Log($"[PickupableItem] {mineralSO.DisplayName} x{addedCount} 획득");
            }
            else
                Debug.LogWarning($"[PickupableItem] {mineralSO.DisplayName} 추가 실패 — 무게 한계 초과 가능성");
        }
        else if (itemData is ItemSO itemSO)
        {
            var itemInventory = ResolveItemInventory(interactor);
            if (itemInventory == null)
            {
                Debug.LogError("[PickupableItem] ItemInventory를 찾지 못했습니다. " +
                               "플레이어에 ItemInventory 컴포넌트가 있는지 확인하세요.");
                return;
            }
            int addedCount = itemInventory.AddItem(itemSO, 1);
            added = addedCount > 0;
            if (added)
            {
                AcquisitionNotifier.NotifyItem(itemSO, addedCount);
                Debug.Log($"[PickupableItem] {itemSO.DisplayName} x1 획득");
            }
            else
                Debug.LogWarning($"[PickupableItem] {itemSO.DisplayName} 추가 실패 — 개수 한계 초과 가능성");
        }

        if (added)
        {
            NotifyMineralCollected();
            PlayPickupEffect();
            RemoveFromWorld();
        }
    }

    // ================================================================
    //  인벤토리 참조 해석
    //  InventoryUI(프리팹 인벤토리 화면)가 없는 씬에서도 동작해야 하므로
    //  UI에 의존하지 않는다. 코드 생성 오버레이(InventoryOverlayUI)만 쓰는 씬이 그렇다.
    //
    //  탐색 순서는 DokkaebiCauldron과 동일하게 맞췄다:
    //   1) InventoryUI (있으면 기존 동작 그대로)
    //   2) 상호작용한 플레이어 본인 — 가장 확실하다
    //   3) 씬 전체 (창고 등 엉뚱한 인스턴스를 잡을 수 있어 최후순위)
    // ================================================================
    //  includeInactive를 켜는 이유: 인벤토리 컴포넌트가 꺼진 UI 패널에 붙어 있을 수 있다
    //  (EquipmentInventory는 실제로 EquipmentsPanel에 있다).
    private static MineralInventory ResolveMineralInventory(GameObject interactor)
    {
        var ui = InventoryUI.Instance;
        if (ui != null && ui.mineralInventory != null) return ui.mineralInventory;

        if (interactor != null)
        {
            var own = interactor.GetComponentInChildren<MineralInventory>(true);
            if (own != null) return own;
        }

        return FindFirstObjectByType<MineralInventory>(FindObjectsInactive.Include);
    }

    private static ItemInventory ResolveItemInventory(GameObject interactor)
    {
        var ui = InventoryUI.Instance;
        if (ui != null && ui.itemInventory != null) return ui.itemInventory;

        if (interactor != null)
        {
            var own = interactor.GetComponentInChildren<ItemInventory>(true);
            if (own != null) return own;
        }

        return FindFirstObjectByType<ItemInventory>(FindObjectsInactive.Include);
    }

    // 내장 지형 광물(TerrainChunk 자식)인 경우에만 PixelInfo를 수집 완료 마킹.
    // 이름 규격: MINERAL_{SOName}_{x}_{y} — 마지막 두 토큰이 픽셀 좌표.
    private void NotifyMineralCollected()
    {
        TerrainChunk parentChunk = GetComponentInParent<TerrainChunk>();
        if (parentChunk == null) return; // 드롭 광물 등 청크 외부 오브젝트는 스킵

        string[] parts = gameObject.name.Split('_');
        if (parts.Length < 3) return;
        if (!int.TryParse(parts[parts.Length - 2], out int x)) return;
        if (!int.TryParse(parts[parts.Length - 1], out int y)) return;

        MineralGenerator.MarkMineralCollected(parentChunk, x, y);
    }

    private void PlayPickupEffect()
    {
        // 획득이 실제로 성사됐을 때만 불린다(무게 초과로 실패하면 호출되지 않음).
        // 3D 재생 — 여러 광물을 연달아 주울 때 위치감이 살아난다.
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(SfxKeys.MineralPickup, transform.position);

        // 파티클 생성 로직 제거됨

        CameraShakeManager.Instance?.Shake(0.06f, 0.015f);
    }

    /// <summary>
    /// 광물은 MineralGenerator 풀로 반납(풀 소속이 아니면 ReturnToPool이 알아서 파괴한다),
    /// 그 외 드롭 아이템은 Destroy.
    ///
    /// 예전엔 ObjectPooler로 반납했는데 SpawnFromPool 호출부가 프로젝트에 하나도 없어 꺼내는 쪽이
    /// 없었고, 씬의 stratumPools도 비어 있어 HasPool이 항상 false → 사실상 매번 Destroy로 폴백됐다.
    /// 그래서 60초 만료 광물(MineralLifetime)만 풀로 돌아가고 '주운' 광물은 전부 파괴 → 다음 청크
    /// 로드에서 재생성되는 비대칭이 있었다. 두 퇴장 경로를 같은 풀로 합친다.
    /// </summary>
    private void RemoveFromWorld()
    {
        if (_mineralController != null)
            MineralGenerator.ReturnToPool(gameObject);
        else
            Destroy(gameObject);
    }

    /// <summary>
    /// 업그레이드 효과에 따라 추가 드랍 수량을 계산한다.
    /// </summary>
    private int CalculatePickupQuantity()
    {
        int qty = 1;
        UpgradeManager upgradeManager = UpgradeManager.Instance;
        if (upgradeManager != null)
        {
            float extraChance = upgradeManager.GetStatValue(UpgradeEffectType.MineralExtraDropChance, 0f);
            if (extraChance > 0 && Random.Range(0f, 100f) < extraChance)
                qty++;
        }
        return qty;
    }
}
