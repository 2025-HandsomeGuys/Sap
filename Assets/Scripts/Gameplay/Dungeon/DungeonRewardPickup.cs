// @tags: dungeon, reward, pickup, save, persistence, treasure, mineral, relic
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 던전 안의 1회성 보상 상자. 플레이어가 **F키로 열면** 골드 + 광물 + (확률로) 유물을 주고
/// 인스턴스별로 수집 처리한다.
///  - Start: 이 인스턴스에서 이미 열었으면 **열린 상태로 시작**(빈 상자가 "여긴 이미 털었다"를 알려준다).
///  - Interact(E): 골드/광물 지급 + 유물 판정 + 수집 기록 + 열림 연출.
/// 근접 탐지용 Collider2D(isTrigger=true)가 필요하다 — PlayerInteractor가 이걸로 후보를 찾는다.
/// 광물은 destination에 따라 플레이어 광물 인벤토리 또는 창고(Warehouse)로 지급된다.
/// rewardId는 한 던전 프리팹 안에서 유일해야 한다(임포터가 자동 부여).
///
/// 접촉 수집(OnTriggerEnter2D)은 쓰지 않는다. F키 입력을 직접 폴링하지도 않는다 —
/// <see cref="IInteractable"/>로 붙으면 PlayerInteractor가 거리·우선순위·UI 차단을 이미 처리한다.
///
/// ⚠ 수집 기록은 <see cref="DungeonStateStore.CurrentInstance"/>(던전 문 좌표)에 남는다.
/// 던전 밖(특수 청크 등)에 놓으면 "마지막에 들어간 던전"에 기록되므로 지금은 던전 전용이다.
/// </summary>
public class DungeonRewardPickup : MonoBehaviour, IInteractable, IInteractionPrompt
{
    public enum RewardDestination { Inventory, Warehouse }

    [Serializable]
    public class MineralReward
    {
        public MineralSO mineral;
        [Min(1)] public int quantity = 1;
    }

    [Tooltip("이 던전 프리팹 안에서 유일한 보상 식별자")]
    [SerializeField] private string rewardId = "reward_1";

    [Header("Gold")]
    [Tooltip("수집 시 지급할 골드 (0이면 골드 없음)")]
    [SerializeField] private int rewardGold = 0;

    [Header("Treasure — Minerals")]
    [Tooltip("수집 시 지급할 광물 목록 (보물상자)")]
    [SerializeField] private List<MineralReward> mineralRewards = new List<MineralReward>();

    [Tooltip("광물 지급처: Inventory(플레이어 광물 인벤토리) 또는 Warehouse(창고)")]
    [SerializeField] private RewardDestination destination = RewardDestination.Inventory;

    [Header("Treasure — Relic")]
    [Tooltip("탐험 보상으로 유물이 나올 수 있는 상자인가. 실제 등장 여부·종류는 " +
             "relicDropSettings.json의 확률과 던전 문이 있던 지층으로 정해진다.")]
    [SerializeField] private bool canDropRelic = true;

    [Header("열림 연출")]
    [Tooltip("상자 열림 애니메이터. 비우면 자신·자식에서 찾는다. 아예 없으면 수집 시 오브젝트가 사라진다.")]
    [SerializeField] private Animator chestAnimator;

    [Tooltip("열림 애니메이션 상태 이름")]
    [SerializeField] private string openStateName = "Open";

    private const string PromptKey = "interact_chest_open";
    private const string PromptFallback = "상자 열기";

    private bool _collected;
    private Animator _animator;

    private void Awake()
    {
        _animator = chestAnimator != null ? chestAnimator : GetComponentInChildren<Animator>(true);
    }

    private void Start()
    {
        if (!DungeonStateStore.IsRewardCollected(DungeonStateStore.CurrentInstance, rewardId)) return;

        // 이미 연 상자 — 지급 없이 열린 모습으로만 남긴다.
        _collected = true;
        PlayOpen(instant: true);
    }

    // --- IInteractable / IInteractionPrompt ---

    public string InteractionPrompt => PromptFallback;

    /// <summary>연 상자는 후보에서 빠진다 → 하이라이트·문구도 안 뜬다.</summary>
    public bool CanInteract => !_collected;

    public InteractionPromptInfo GetInteractionPrompt()
        => _collected ? InteractionPromptInfo.None : InteractionPromptInfo.Ok(PromptKey, PromptFallback);

    public void Interact(GameObject interactor)
    {
        if (_collected) return;
        _collected = true;

        GrantGold();
        GrantMinerals(interactor);
        GrantRelic();

        DungeonStateStore.MarkRewardCollected(rewardId);
        PlayOpen(instant: false);
    }

    /// <summary>
    /// 열림 표현. 애니메이터가 없는 프리팹은 보여줄 게 없으므로 그대로 사라진다(구 접촉형 픽업과 같은 결과).
    /// </summary>
    private void PlayOpen(bool instant)
    {
        if (_animator == null)
        {
            Destroy(gameObject);
            return;
        }

        if (instant) _animator.Play(openStateName, 0, 1f); // 끝 프레임으로 바로
        else _animator.Play(openStateName);
    }

    private void GrantGold()
    {
        if (rewardGold == 0) return;
        var sm = GameManager.Instance != null ? GameManager.Instance.saveManager : null;
        if (sm != null && sm.playerData != null)
        {
            sm.playerData.gold += rewardGold;
            Debug.Log($"[DungeonReward] '{rewardId}' → +{rewardGold} 골드 (현재: {sm.playerData.gold})");
        }
    }

    /// <summary>
    /// 유물 보상. 골드·광물과 달리 <b>인벤토리에 바로 넣지 않고 상자 앞에 떨어뜨린다</b> —
    /// 돌에서 나오는 유물과 획득 방식(F키로 줍기)을 맞추기 위해서다.
    /// 등장 확률·지층별 후보는 <c>RelicDropRoller</c>가 판정한다.
    /// </summary>
    private void GrantRelic()
    {
        if (!canDropRelic) return;

        Vector3 dropPos = transform.position + new Vector3(0f, 0.5f, 0f);
        if (Relic.Drop.RelicDropRoller.TryDropFromChest(dropPos))
            Debug.Log($"[DungeonReward] '{rewardId}' → 유물 등장");
    }

    private void GrantMinerals(GameObject interactor)
    {
        if (mineralRewards == null || mineralRewards.Count == 0) return;

        if (destination == RewardDestination.Warehouse)
        {
            var wh = WarehouseManager.Instance;
            if (wh == null)
            {
                Debug.LogWarning($"[DungeonReward] '{rewardId}' 창고(WarehouseManager)를 찾지 못해 광물 지급 실패");
                return;
            }
            foreach (var r in mineralRewards)
            {
                if (r == null || r.mineral == null || r.quantity <= 0) continue;
                wh.AddMineral(r.mineral, r.quantity);
                Debug.Log($"[DungeonReward] '{rewardId}' → 창고 {r.mineral.name} x{r.quantity}");
            }
        }
        else
        {
            var inv = ResolveMineralInventory(interactor);
            if (inv == null)
            {
                Debug.LogWarning($"[DungeonReward] '{rewardId}' 광물 인벤토리를 찾지 못해 지급 실패");
                return;
            }
            foreach (var r in mineralRewards)
            {
                if (r == null || r.mineral == null || r.quantity <= 0) continue;
                // AddItem 안에서 발견 기록이 되므로 첫 발견 여부는 그 전에 확인한다(도감 '새 광물!' 뱃지).
                bool wasKnown = CollectionCodex.IsDiscovered(CodexCategory.Mineral, r.mineral.mineralID.ToString());
                inv.AddItem(r.mineral, r.quantity);
                AcquisitionNotifier.NotifyMineral(r.mineral, r.quantity, isNew: !wasKnown);
                Debug.Log($"[DungeonReward] '{rewardId}' → 인벤토리 {r.mineral.name} x{r.quantity}");
            }
        }
    }

    // PickupableItem.ResolveMineralInventory와 동일한 해석 순서.
    private static MineralInventory ResolveMineralInventory(GameObject interactor)
    {
        var ui = InventoryUI.Instance;
        if (ui != null && ui.mineralInventory != null) return ui.mineralInventory;

        if (interactor != null)
        {
            var own = interactor.GetComponentInChildren<MineralInventory>(true);
            if (own != null) return own;
        }

        return UnityEngine.Object.FindFirstObjectByType<MineralInventory>(FindObjectsInactive.Include);
    }
}
