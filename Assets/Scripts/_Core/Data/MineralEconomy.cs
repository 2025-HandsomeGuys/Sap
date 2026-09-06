// @tags: mineral, economy, balance, price, weight, pure-logic
using System.Collections.Generic;

/// <summary>
/// 밸런스 계산 한 항목. 한 층에 속한 광물 1종의 가격·무게·기대 산출량.
/// perChunk는 tileData.json의 [min,max] 중앙값을 쓴다.
/// </summary>
public struct MineralEconomyEntry
{
    public MineralID id;
    public float price;
    public float weight;
    public float perChunk;

    public MineralEconomyEntry(MineralID id, float price, float weight, float perChunk)
    {
        this.id = id;
        this.price = price;
        this.weight = weight;
        this.perChunk = perChunk;
    }

    /// <summary>무게 1당 가치. 무게가 0이면 0(선별 판정에서 제외).</summary>
    public float ValuePerWeight => weight > 0f ? price / weight : 0f;
}

/// <summary>
/// 광물 경제 밸런스 계산. 순수 로직 — Unity 타입에 의존하지 않는다.
///
/// 회당 매출 = (가방 용량 / 가중평균 무게) x 가중평균 가격.
/// 가중치는 perChunk(청크당 기대 산출량)다 — 플레이어가 마주치는 비율이기 때문이다.
///
/// 설계 근거: Assets/Docs/economy/mineral-price-design.md
/// </summary>
public static class MineralEconomy
{
    public static float TotalPerChunk(IReadOnlyList<MineralEconomyEntry> entries)
    {
        if (entries == null) return 0f;
        float sum = 0f;
        for (int i = 0; i < entries.Count; i++) sum += entries[i].perChunk;
        return sum;
    }

    public static float WeightedAveragePrice(IReadOnlyList<MineralEconomyEntry> entries)
    {
        float total = TotalPerChunk(entries);
        if (total <= 0f) return 0f;
        float acc = 0f;
        for (int i = 0; i < entries.Count; i++) acc += entries[i].price * entries[i].perChunk;
        return acc / total;
    }

    public static float WeightedAverageWeight(IReadOnlyList<MineralEconomyEntry> entries)
    {
        float total = TotalPerChunk(entries);
        if (total <= 0f) return 0f;
        float acc = 0f;
        for (int i = 0; i < entries.Count; i++) acc += entries[i].weight * entries[i].perChunk;
        return acc / total;
    }

    /// <summary>층 전체를 나오는 대로 담았을 때의 무게 1당 가치.</summary>
    public static float AverageValuePerWeight(IReadOnlyList<MineralEconomyEntry> entries)
    {
        float avgWeight = WeightedAverageWeight(entries);
        if (avgWeight <= 0f) return 0f;
        return WeightedAveragePrice(entries) / avgWeight;
    }

    /// <summary>그 층에서 가장 효율 좋은 광물의 무게 1당 가치.</summary>
    public static float MaxValuePerWeight(IReadOnlyList<MineralEconomyEntry> entries)
    {
        if (entries == null) return 0f;
        float max = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            float v = entries[i].ValuePerWeight;
            if (v > max) max = v;
        }
        return max;
    }

    /// <summary>
    /// 선별 편향. 최고 효율 광물만 골라 담았을 때 평균 대비 몇 배를 버는가.
    /// 1.2를 넘으면 "골라 담기"가 최적해가 되어 가중평균이 실효값 구실을 못 한다.
    /// </summary>
    public static float PickBias(IReadOnlyList<MineralEconomyEntry> entries)
    {
        float avg = AverageValuePerWeight(entries);
        if (avg <= 0f) return 0f;
        return MaxValuePerWeight(entries) / avg;
    }

    public static float ItemsPerTrip(float bagCapacity, IReadOnlyList<MineralEconomyEntry> entries)
    {
        float avgWeight = WeightedAverageWeight(entries);
        if (avgWeight <= 0f) return 0f;
        return bagCapacity / avgWeight;
    }

    public static float RevenuePerTrip(float bagCapacity, IReadOnlyList<MineralEconomyEntry> entries)
    {
        return ItemsPerTrip(bagCapacity, entries) * WeightedAveragePrice(entries);
    }
}
