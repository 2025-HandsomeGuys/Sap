// @tags: coin, testing, debug, win-rate, ev, simulation
using UnityEngine;
using Coin.Data;
using Coin.Systems;

namespace Coin.Testing
{
    /// <summary>
    /// 코인 엔진 밸런스 검증 (설계 §8.11). 인스펙터 우클릭 → Run Simulation.
    /// 다수 라운드를 돌려 실측 승률(≈0.5), 평균 손익률(≈0 또는 −fee), 극단/상장폐지 빈도를 로그로 확인한다.
    /// </summary>
    public class CoinTestRunner : MonoBehaviour
    {
        [Tooltip("DefaultSlots 인덱스 (0=GRAVEL … 9=ABYSS)")]
        [SerializeField] private int slotIndex = 9;
        [SerializeField] private int iterations = 10000;
        [SerializeField] private int stake = 1000;

        [ContextMenu("Run Simulation")]
        public void Run()
        {
            var slots = CoinTableSO.DefaultSlots();
            int idx = Mathf.Clamp(slotIndex, 0, slots.Count - 1);
            var slot = slots[idx];

            var engine = new CoinPriceEngine();
            engine.StartCoin(slot, slot.basePrice);

            int wins = 0, extremes = 0, delists = 0;
            long pnl = 0;

            for (int i = 0; i < iterations; i++)
            {
                var dir = Random.value < 0.5f ? BetDirection.Up : BetDirection.Down;
                var r = engine.Resolve(dir, stake);
                if (r.win) wins++;
                if (r.extreme) extremes++;
                if (r.delisted) delists++;
                pnl += r.delta;
                if (r.delisted) engine.StartCoin(slot, slot.basePrice); // 교체 시 재시작
            }

            double winRate = (double)wins / iterations;
            double avgPnlRatio = (double)pnl / (iterations * (double)stake);
            double extremeRate = (double)extremes / iterations;
            double delistRate = (double)delists / iterations;

            Debug.Log($"[CoinTest] {slot.defaultName} n={iterations} lev=1 | 승률={winRate:P2} (기대 50%) | " +
                      $"평균손익률={avgPnlRatio:P2} (기대 음수: 비대칭 winCap {slot.winCap:P0}/lossCap {slot.lossCap:P0} + 엣지 {slot.houseEdge:P0}, fee 미반영) | " +
                      $"극단={extremeRate:P2} (설정 {slot.extremeChance:P2}) | 상장폐지={delistRate:P2} (기대 {slot.extremeChance * 0.5f:P2})");
        }
    }
}
