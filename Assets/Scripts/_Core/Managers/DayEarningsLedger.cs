// @tags: earnings, ledger, gold, daysummary, settlement, static, manager

public enum DayEarningsCategory
{
    MineralSale,   // 광물 판매 (ShopManager.SellItem)
    Stock,         // 주식 매수/매도 (PortfolioManager)
    Coin,          // 코인 베팅 손익 (CoinGameManager.ApplyDelta)
    ShopPurchase,  // 상점 아이템 구매 (ShopManager.BuyItem)
    Upgrade,       // 업그레이드 비용 (UpgradeManager.UnlockNode)
}

/// <summary>하루 정산 연출(DaySummaryUI)에 넘기는 스냅샷.</summary>
public struct DayEarningsReport
{
    public int day;          // 방금 끝난 날 (N일차)
    public int mineralSale;
    public int stock;
    public int coin;
    public int shopPurchase;
    public int upgrade;
    public int other;        // Report()로 추적되지 않은 골드 변동 (퀘스트 보상, 디버그 등)
    public int total;        // 현재 골드 - dayStartGold (하루 실제 순변화)
    public int goldAfter;    // 정산 시점 보유 골드
}

/// <summary>
/// 하루 동안의 골드 증감을 카테고리별로 기록하는 정적 장부 (CauldronStateStore 패턴).
/// 골드가 움직이는 지점(상점·주식·코인·업그레이드)에서 Report()로 기록하고,
/// 침대 수면 시 BuildReport() → ResetForNewDay() 순으로 정산한다.
/// 최종 손익은 dayStartGold 대비 실제 골드 차이로 계산하므로,
/// Report가 누락된 골드 변동도 합계가 어긋나지 않고 '기타(other)'로 흡수된다.
/// 세이브 연동은 SaveManager가 Capture()/Apply()로 수행 (PlayerData.dayEarnings).
/// </summary>
public static class DayEarningsLedger
{
    private static DayEarningsSaveData _data = new DayEarningsSaveData();

    // ▼▼▼ 여기에 CoinToday 프로퍼티가 추가되었습니다 ▼▼▼
    /// <summary>오늘 하루 동안의 코인 총 손익을 반환합니다.</summary>
    public static int CoinToday => _data.coin;
    // ▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲

    /// <summary>골드 증감 기록. 수입은 +, 지출은 -.</summary>
    public static void Report(DayEarningsCategory category, int delta)
    {
        if (delta == 0) return;
        switch (category)
        {
            case DayEarningsCategory.MineralSale: _data.mineralSale += delta; break;
            case DayEarningsCategory.Stock: _data.stock += delta; break;
            case DayEarningsCategory.Coin: _data.coin += delta; break;
            case DayEarningsCategory.ShopPurchase: _data.shopPurchase += delta; break;
            case DayEarningsCategory.Upgrade: _data.upgrade += delta; break;
        }
        _data.hasData = true;
    }

    /// <summary>추적된 증감으로 추정한 현재 골드. PlayerStat을 못 찾았을 때의 폴백.</summary>
    public static int EstimateCurrentGold()
    {
        return _data.dayStartGold + TrackedSum();
    }

    /// <summary>정산 스냅샷 생성. endedDay = 방금 끝난 날, currentGold = 정산 시점 보유 골드.</summary>
    public static DayEarningsReport BuildReport(int endedDay, int currentGold)
    {
        int total = currentGold - _data.dayStartGold;
        return new DayEarningsReport
        {
            day = endedDay,
            mineralSale = _data.mineralSale,
            stock = _data.stock,
            coin = _data.coin,
            shopPurchase = _data.shopPurchase,
            upgrade = _data.upgrade,
            other = total - TrackedSum(),
            total = total,
            goldAfter = currentGold,
        };
    }

    /// <summary>새 하루 시작 — 장부를 비우고 하루 시작 골드를 기록한다.</summary>
    public static void ResetForNewDay(int currentGold)
    {
        _data = new DayEarningsSaveData { hasData = true, dayStartGold = currentGold };
    }

    // ===================================================
    // 세이브 연동 (SaveManager)
    // ===================================================
    public static DayEarningsSaveData Capture()
    {
        return new DayEarningsSaveData
        {
            hasData = _data.hasData,
            dayStartGold = _data.dayStartGold,
            mineralSale = _data.mineralSale,
            stock = _data.stock,
            coin = _data.coin,
            shopPurchase = _data.shopPurchase,
            upgrade = _data.upgrade,
        };
    }

    /// <summary>세이브 복원. 데이터가 없으면(구버전 세이브 등) 현재 골드를 하루 시작값으로 초기화.</summary>
    public static void Apply(DayEarningsSaveData data, int fallbackGold)
    {
        if (data == null || !data.hasData)
        {
            ResetForNewDay(fallbackGold);
            return;
        }

        _data = new DayEarningsSaveData
        {
            hasData = true,
            dayStartGold = data.dayStartGold,
            mineralSale = data.mineralSale,
            stock = data.stock,
            coin = data.coin,
            shopPurchase = data.shopPurchase,
            upgrade = data.upgrade,
        };
    }

    private static int TrackedSum()
    {
        return _data.mineralSale + _data.stock + _data.coin + _data.shopPurchase + _data.upgrade;
    }
}