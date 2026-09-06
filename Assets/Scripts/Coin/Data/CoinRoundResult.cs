// @tags: coin, data, round-result, betting
namespace Coin.Data
{
    /// <summary>한 라운드 베팅 정산 결과 (설계 §7.3 / §7.7).</summary>
    public struct CoinRoundResult
    {
        public BetDirection pick;       // 플레이어 선택
        public bool win;                // 방향 적중?
        public bool extreme;            // ±100% 극단 이벤트?
        public bool delisted;           // 극단 −100%(상장폐지)?
        public int stake;               // 베팅(마진) 금액
        public int leverage;            // 적용 배율 (1=일반)
        public int fee;                 // 이번 라운드 양방향 베팅세(레이크) — 승패 무관 차감액
        public bool liquidated;         // 레버리지 손실이 마진을 초과해 청산됨?
        public int delta;               // 골드 증감 (+이익 / −손실)
        public float swing;             // |변동폭| (0~1)
        public float oldPrice;
        public float newPrice;
        public string replacementName;  // delisted일 때 새 코인명 (매니저가 채움)
    }
}
