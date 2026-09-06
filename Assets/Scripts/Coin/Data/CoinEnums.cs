// @tags: coin, enum, betting, gambling
namespace Coin.Data
{
    /// <summary>코인 베팅 방향.</summary>
    public enum BetDirection { Up, Down }

    /// <summary>베팅 라운드의 진행 단계.</summary>
    public enum CoinRoundPhase { Idle, Revealing, Resolved }
}
