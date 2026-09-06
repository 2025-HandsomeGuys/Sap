// @tags: telemetry, events, constants, names

/// <summary>텔레메트리 이벤트 이름 상수(설계 §3). 호출부에 문자열 리터럴을 흩뿌리지 않는다.</summary>
public static class TelemetryEvents
{
    // 세션·이탈
    public const string SessionStart = "session_start";
    public const string SessionEnd   = "session_end";
    public const string Heartbeat    = "heartbeat";

    // 안정성
    public const string Error = "error";

    // 지역 진행
    public const string DiveStart        = "dive_start";
    public const string DiveEnd          = "dive_end";
    public const string RegionFirstEnter = "region_first_enter";
    public const string DepthMilestone   = "depth_milestone";

    // 스태미나·실패
    public const string StaminaDepleted = "stamina_depleted";
    public const string EmergencyEscape = "emergency_escape";
    public const string EncumberedEnter = "encumbered_enter";

    // 경제
    public const string DaySettled       = "day_settled";
    public const string ShopTransaction  = "shop_transaction";
    public const string UpgradePurchased = "upgrade_purchased";
    public const string UpgradeBlocked   = "upgrade_blocked";

    // 마켓
    public const string CoinBet        = "coin_bet";
    public const string CoinDaySummary = "coin_day_summary";
    public const string StockTrade     = "stock_trade";
    public const string MarketVisit    = "market_visit";

    // 가이드
    public const string GuideShown   = "guide_shown";
    public const string GuideSkipped = "guide_skipped";
}
