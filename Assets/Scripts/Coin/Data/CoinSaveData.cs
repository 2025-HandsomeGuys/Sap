// @tags: coin, data, save, persistence
using System;
using System.Collections.Generic;

namespace Coin.Data
{
    /// <summary>
    /// 코인 미니게임 영속 데이터 (설계 §10). PlayerData.coinSave로 SaveManager에 엮인다.
    /// 가격 히스토리는 비영속(진입 시 재생성). 데일리 잠금·로스터·누적 통계만 보관.
    /// </summary>
    [Serializable]
    public class CoinSaveData
    {
        public bool hasData;

        // 데일리 잠금 + 라운드 제한
        public int lockedDay = -1;
        public int lockedSlotId = -1;
        public int roundsToday;
        public long dayStartGold;   // 오늘 첫 베팅 시점의 골드 — 출금 한도 시작점
        public long dayFirstStake;  // 오늘 첫 베팅 금액 — 출금 한도(첫 베팅액 × 배수) 기준

        // 동적 로스터 (슬롯별 현재 코인)
        public List<CoinSlotState> roster = new List<CoinSlotState>();

        // 누적 통계 / 기록
        public long totalStaked;
        public long totalProfit;
        public int roundsPlayed;
        public int bestSingleWin;
        public int longestStreak;
        public int delistsWitnessed;
    }
}
