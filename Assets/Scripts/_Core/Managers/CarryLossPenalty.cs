// @tags: penalty, mineral, emergency, escape, death, game-over, settlement, static
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 지하에서 '판이 실패로 끝났을 때' 짐에 매기는 페널티 — 긴급 탈출과 사망이 **같은 규칙**을 쓴다.
///
/// 규칙: 캔 광물 중 **가장 비싼 것 1~2개만 챙기고 나머지는 전부 잃는다.**
/// (예전 규칙인 '무작위 60% 삭제'는 많이 캘수록 많이 남아서, 실패해도 한 탕이 되는 구간이 있었다.
///  지금은 성과의 크기와 무관하게 남는 게 1~2개로 고정되므로 "제때 걸어 올라오는 것"이 항상 이득이다.)
///
/// '비싸다'의 기준은 <see cref="MineralPriceDatabase.GetBasePrice"/>(업그레이드 보너스를 뺀 기저 가격).
/// <c>GetPrice</c>를 쓰면 판매가 업그레이드를 산 광물이 더 자주 살아남는 되먹임이 생긴다.
///
/// 호출 지점:
///   • <c>SaveManager.MergeInventoriesToWarehouse</c> — 긴급 탈출
///   • <c>GameOverHandler</c>                        — 스태미나 고갈 사망
/// 두 경로 모두 여기서 <see cref="EmergencyEscapeReport"/>에 잃은/챙긴 내역을 기록하고,
/// 지상 도착 후 <c>EmergencyEscapeOverlayUI</c>가 그 장부를 읽어 정산창을 띄운다.
/// </summary>
public static class CarryLossPenalty
{
    /// <summary>챙길 수 있는 최소 개수(가장 비싼 것부터).</summary>
    public const int KeepMin = 1;
    /// <summary>챙길 수 있는 최대 개수. 매판 [KeepMin, KeepMax]에서 뽑는다.</summary>
    public const int KeepMax = 2;

    /// <summary>
    /// 광물 인벤토리에 페널티를 적용한다(가장 비싼 1~2개만 남기고 제거).
    /// 결과는 <see cref="EmergencyEscapeReport.Record"/>로 기록되어 지상 정산창에 쓰인다.
    /// </summary>
    /// <param name="inventory">플레이어의 광물 인벤토리. null이면 아무 일도 하지 않는다.</param>
    /// <param name="reason">정산창 문구를 가르는 사유(탈출 / 사망).</param>
    public static void Apply(MineralInventory inventory, CarryLossReason reason)
    {
        if (inventory == null) return;

        // 스택을 낱개 단위로 펼친다 — '개수'로 세는 규칙이라 스택 경계는 의미가 없다.
        var units = new List<MineralSO>();
        foreach (var slot in inventory.ReadonlyItems)
        {
            if (slot.item is MineralSO mineral && slot.quantity > 0)
                for (int i = 0; i < slot.quantity; i++) units.Add(mineral);
        }

        int total = units.Count;
        var lost = new Dictionary<MineralID, int>();
        var kept = new Dictionary<MineralID, int>();

        if (total == 0)
        {
            EmergencyEscapeReport.Record(lost, kept, reason);
            return;
        }

        var priceDb = (PriceDataLoader.Instance != null) ? PriceDataLoader.Instance.MineralPrices : null;

        // 비싼 순으로 정렬. 가격표가 없으면(로더 미초기화 등) 정렬 없이 앞에서 남긴다 —
        // 페널티 자체는 어떤 경우에도 적용되어야 한다.
        if (priceDb != null)
            units.Sort((a, b) => priceDb.GetBasePrice(b.mineralID).CompareTo(priceDb.GetBasePrice(a.mineralID)));

        int keepCount = Mathf.Min(total, Random.Range(KeepMin, KeepMax + 1));

        for (int i = 0; i < total; i++)
        {
            var id = units[i].mineralID;
            var bucket = (i < keepCount) ? kept : lost;
            bucket[id] = bucket.TryGetValue(id, out int c) ? c + 1 : 1;
        }

        // 남길 것 뒤쪽(=잃는 것)만 인벤토리에서 제거한다.
        for (int i = keepCount; i < total; i++)
            inventory.RemoveItem(units[i], 1);

        EmergencyEscapeReport.Record(lost, kept, reason);

        Debug.Log($"[CarryLossPenalty] {reason}: 광물 {total}개 중 {keepCount}개만 챙김 ({total - keepCount}개 유실)");
    }
}
