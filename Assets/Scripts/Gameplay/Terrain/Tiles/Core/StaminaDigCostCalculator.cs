// @tags: stamina, digging, cost, player-stat, upgrade, tile-data
using UnityEngine;

/// <summary>
/// 스태미나 기반 채굴 비용 계산기
/// </summary>
public class StaminaDigCostCalculator : IDigCostCalculator
{
    private PlayerStat _playerStats;

    // 현재 도구 인덱스를 매번 새로 물어보기 위한 델리게이트.
    // 값을 생성 시점에 캐시하면 도구를 바꿔도 배율이 따라오지 않는다.
    // null이면 도구 배율 없이(1배) 동작 — 기존 호출부 호환.
    private readonly System.Func<int> _toolIndexProvider;

    public StaminaDigCostCalculator(PlayerStat playerStats, System.Func<int> toolIndexProvider = null)
    {
        _playerStats = playerStats;
        _toolIndexProvider = toolIndexProvider;
    }

    private int CurrentToolIndex => _toolIndexProvider != null ? _toolIndexProvider() : -1;

    // TileDataManager 없는 테스트 씬용 기본 비용 (Dirt 기준, radius=1 기준값)
    private const float FallbackStaminaCost = 0.1f;
    private const float BaseRadius          = 1.0f;

    public bool PayCost(TileType tileType, float radius)
    {
        if (_playerStats == null) return false;

        float reduction;

        if (TileDataManager.Instance != null)
        {
            TileDataJson data = TileDataManager.Instance.GetData(tileType);
            if (data == null || data.maxStaminaReduction <= 0)
                return true; // 비용이 없는 타일은 무료

            reduction = data.maxStaminaReduction;

            if (UpgradeManager.Instance != null)
                reduction = UpgradeManager.Instance.GetStatValue(UpgradeEffectType.StaminaCostMultiplier, reduction);

            // 합연산판('효율적인 호흡')은 기준 1 배율 스탯이라 여기서 따로 곱한다.
            // 타일 비용(0.1~2.0)에 고정값을 빼면 싼 타일이 공짜가 되므로 배율로 받는다.
            reduction *= _playerStats.StaminaCostScale;
        }
        else
        {
            // TileDataManager가 없는 테스트 씬에서도 스테미나 차감
            reduction = FallbackStaminaCost;
        }

        // 반경에 비례하여 스태미나 소모 (기준 반경 BaseRadius=1.0f)
        reduction *= Mathf.Max(radius / BaseRadius, 0f);

        int toolIndex = CurrentToolIndex;
        // 지형 배율을 쓴다. Digger 안의 '돌' PayCost 호출부는 맨손(0)·드릴(3)에서만 도달하고
        // (Update가 tool 1·2·3을 걸러내고, 돌 분기는 tool 1을 또 걸러낸다) 그 둘은 1배로 통과하므로
        // 여기서 돌/지형을 더 구분할 필요가 없다. 실제 돌 비용은 전략이 직접 UseStamina로 낸다.
        reduction *= MiningStaminaTuning.GetTerrainCostMultiplier(toolIndex);

        _playerStats.UseStamina(reduction);
        MiningStaminaTuning.ReportCost(toolIndex, reduction);
        return true;
    }

    /// <summary>
    /// 파기 지점 월드 위치 기반 비용 지불. 블렌딩 구역 안이면 저항값 선형 보간.
    /// </summary>
    public bool PayCost(Vector2 worldPos, float radius)
    {
        if (_playerStats == null) return false;

        float reduction;

        if (TileDataManager.Instance != null && InfinityMapManager.Instance != null)
        {
            reduction = TileDataManager.Instance.GetStaminaReductionAtWorldY(
                worldPos.y, InfinityMapManager.Instance.chunkHeightWorld);

            if (reduction <= 0f) return true;

            if (UpgradeManager.Instance != null)
                reduction = UpgradeManager.Instance.GetStatValue(UpgradeEffectType.StaminaCostMultiplier, reduction);

            // 합연산판('효율적인 호흡')은 기준 1 배율 스탯이라 여기서 따로 곱한다.
            // 타일 비용(0.1~2.0)에 고정값을 빼면 싼 타일이 공짜가 되므로 배율로 받는다.
            reduction *= _playerStats.StaminaCostScale;
        }
        else
        {
            reduction = FallbackStaminaCost;
        }

        reduction *= Mathf.Max(radius / BaseRadius, 0f);

        int toolIndex = CurrentToolIndex;
        // 지형 배율을 쓴다. Digger 안의 '돌' PayCost 호출부는 맨손(0)·드릴(3)에서만 도달하고
        // (Update가 tool 1·2·3을 걸러내고, 돌 분기는 tool 1을 또 걸러낸다) 그 둘은 1배로 통과하므로
        // 여기서 돌/지형을 더 구분할 필요가 없다. 실제 돌 비용은 전략이 직접 UseStamina로 낸다.
        reduction *= MiningStaminaTuning.GetTerrainCostMultiplier(toolIndex);

        _playerStats.UseStamina(reduction);
        MiningStaminaTuning.ReportCost(toolIndex, reduction);
        return true;
    }
}
