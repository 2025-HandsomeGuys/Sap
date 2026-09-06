// @tags: cauldron, special-chunk, interactable, mineral, upgrade
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 도깨비 가마솥 — 광물 1개를 제련해 확률 결과를 산출하는 1회성(최대 maxUses회) 특수청크.
/// 상호작용·횟수관리·연출을 담당하고, 규칙은 CauldronResolver(순수 C#)에 위임한다.
/// 스폰 본문은 partial DokkaebiCauldron.Spawn.cs, 연출은 DokkaebiCauldron.Anim.cs 참고.
/// </summary>
public partial class DokkaebiCauldron : InteractableBlockBase, IChunkInitializer
{
    [Header("Cauldron Refs")]
    [SerializeField] private CauldronUI cauldronUI;       // 씬/프리팹 내 UI (없으면 런타임 탐색)
    [SerializeField] private Transform spawnPoint;         // 결과가 튀어나오는 위치 (없으면 transform)
    [SerializeField] private SpriteRenderer bodyRenderer;  // 부글부글/붉은글로우/소진 색 표현
    [SerializeField] private Color spentColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    private Vector2Int _coord;
    private int _remainingUses;
    private bool _busy;
    private bool _initialized;
    private CauldronResolver _resolver;

    public int RemainingUses => _remainingUses;
    public int InitializationOrder => 0;

    public void Initialize(Transform parent)
    {
        _coord = ResolveCoord(parent);

        int maxUses = SettingsMaxUses();
        _remainingUses = CauldronStateStore.GetRemainingUses(_coord, maxUses);

        _resolver = BuildResolver();
        _initialized = true;

        if (_remainingUses <= 0) ApplySpentVisual();
    }

    protected override void Start()
    {
        base.Start();
        // 특수청크 스폰 경로를 타지 않고 씬에 직접 배치된 경우(테스트 등) 폴백 초기화.
        if (!_initialized) Initialize(transform.parent);
    }

    // 연출(부글부글)·소진 중에는 InteractableBlockBase의 근접 색상 자동변경을 억제한다.
    // (base.UpdateVisuals가 매 프레임 spriteRenderer.color를 덮어쓰면 brew/spent 색이 지워짐)
    protected override void UpdateVisuals()
    {
        if (_busy || _remainingUses <= 0) return;
        base.UpdateVisuals();
    }

    protected override void HandleInteraction(GameObject interactor)
    {
        if (_busy || _remainingUses <= 0) return;

        // 정답 인벤토리는 InventoryUI.Instance.mineralInventory (PickupableItem과 동일 경로).
        // FindObjectOfType는 창고 등 빈 인스턴스를 잡을 수 있으므로 후순위 폴백.
        MineralInventory inv = InventoryUI.Instance != null ? InventoryUI.Instance.mineralInventory : null;
        if (inv == null) inv = interactor.GetComponentInChildren<MineralInventory>();
        if (inv == null) inv = UnityEngine.Object.FindFirstObjectByType<MineralInventory>();
        if (inv == null) { Debug.LogWarning("[Cauldron] MineralInventory 없음"); return; }

        var ui = cauldronUI ?? UnityEngine.Object.FindFirstObjectByType<CauldronUI>(FindObjectsInactive.Include);
        if (ui != null)
        {
            ui.Open(inv, OnMineralChosen);
            return;
        }

        // UI 미구현 단계: 인벤토리에서 광물 1개를 랜덤으로 뽑아 즉시 투입(테스트용 폴백).
        InsertRandomMineral(inv);
    }

    // 인벤토리 광물 중 1개를 랜덤으로 골라 1개 차감 후 제련 시작.
    private void InsertRandomMineral(MineralInventory inv)
    {
        var candidates = new List<MineralSO>();
        foreach (var slot in inv.ReadonlyItems)
            if (slot != null && slot.item is MineralSO so)
                candidates.Add(so);

        if (candidates.Count == 0) { Debug.LogWarning("[Cauldron] 인벤토리에 투입할 광물이 없음"); return; }

        var picked = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        if (!inv.RemoveItem(picked, 1)) { Debug.LogWarning("[Cauldron] 광물 차감 실패"); return; }

        Debug.Log($"[Cauldron] 랜덤 투입: {picked.mineralID}");
        OnMineralChosen(picked.mineralID);
    }

    private void OnMineralChosen(MineralID input)
    {
        if (_busy || _remainingUses <= 0 || input == MineralID.None) return;
        if (_resolver == null) { Debug.LogWarning("[Cauldron] resolver 미초기화"); return; }
        StartCoroutine(BrewRoutine(input));
    }

    private IEnumerator BrewRoutine(MineralID input)
    {
        _busy = true;

        CauldronResult result = _resolver.Resolve(input, new System.Random(Environment.TickCount));

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(SfxKeys.CauldronBrew, transform.position);

        yield return PlayBrewAnimation(result); // partial(Task 7) — 연출

        SpawnReward(result); // partial(Task 6) — 스폰

        _remainingUses--;
        CauldronStateStore.SetRemainingUses(_coord, _remainingUses);
        if (_remainingUses <= 0) ApplySpentVisual();

        _busy = false;
    }

    private void ApplySpentVisual()
    {
        if (bodyRenderer != null) bodyRenderer.color = spentColor;
        promptText = "불이 꺼진 가마솥"; // 상호작용은 _remainingUses<=0 가드로 차단됨
    }

    private int SettingsMaxUses()
    {
        var s = SpecialChunkSettingsLoader.Instance?.Settings?.cauldron;
        return s != null ? s.maxUses : 3;
    }

    // 좌표 해석: 자신이 속한 특수청크(TerrainChunk)의 그리드 좌표.
    // 주의: Initialize(parent)의 parent는 청크들의 부모 컨테이너이므로 좌표 해석에 쓰지 않는다.
    // 가마솥은 TerrainChunk 프리팹의 자식이므로 자기 자신 기준으로 상위 TerrainChunk를 찾는다.
    private Vector2Int ResolveCoord(Transform parent)
    {
        var tc = GetComponentInParent<TerrainChunk>();
        return tc != null ? tc.Coord : Vector2Int.zero;
    }

    private CauldronResolver BuildResolver()
    {
        var loader = SpecialChunkSettingsLoader.Instance;
        var settings = loader != null ? loader.Settings.cauldron : new SpecialChunkSettingsData.CauldronSection();

        // tileData.json의 층(얕은→깊은 순서)을 얻어 승급 사다리 구축.
        List<TileDataJson> tiles = TileDataManager.Instance != null
            ? TileDataManager.Instance.GetTilesShallowToDeep()
            : new List<TileDataJson>();

        var ladder = new MineralUpgradeLadder(tiles);
        return new CauldronResolver(ladder, settings.ToProbabilities(), settings.explosiveMin, settings.explosiveMax);
    }
}
