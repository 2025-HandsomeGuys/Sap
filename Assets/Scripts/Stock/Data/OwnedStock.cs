using System;

namespace Stock.Data
{
    /// <summary>
    /// 플레이어가 보유한 단일 종목의 런타임/세이브 데이터.
    /// </summary>
    [Serializable]
    public class OwnedStock
    {
        public string CompanyId;
        public int Shares;        // 보유 수량
        public int AvgBuyPrice;   // 평균 매입 단가
        public int PurchasedTick; // 마지막 매수 시점 틱 (당일 매도 잠금 + 인내심 강제매각 판별용)

        public OwnedStock() { }

        public OwnedStock(string companyId, int shares, int avgBuyPrice, int purchasedTick)
        {
            CompanyId = companyId;
            Shares = shares;
            AvgBuyPrice = avgBuyPrice;
            PurchasedTick = purchasedTick;
        }

        /// <summary>이 보유분의 매입 원가 총합.</summary>
        public long GetCostBasis() => (long)AvgBuyPrice * Shares;
    }
}
