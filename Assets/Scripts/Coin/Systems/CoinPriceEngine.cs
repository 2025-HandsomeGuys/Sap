// @tags: coin, engine, price, simulation, gambling, win-rate
using System.Collections.Generic;
using UnityEngine;
using Coin.Data;

namespace Coin.Systems
{
    /// <summary>
    /// 코인 가격 시뮬레이션 + 라운드 정산 (설계 §7.5).
    /// 승률 50% 고정(부호 50/50) + 변동폭 비례 손익 + ±100% 극단(상장폐지/떡상).
    /// 절대 가격은 차트 연출용일 뿐, 손익은 stake × |변동폭|로 계산된다.
    /// 순수 C# — MonoBehaviour 아님(단위 테스트 용이).
    /// </summary>
    public class CoinPriceEngine
    {
        private const int MaxHistory = 60;
        private const int InitialPoints = 24;
        private const float PriceFloor = 0.01f;
        private const float MinSwing = 0.005f; // 무승부 방지 최소 변동

        private readonly List<float> _history = new List<float>();
        private CoinData _slot;
        private float _basePrice;

        public IReadOnlyList<float> History => _history;
        public float CurrentPrice => _history.Count > 0 ? _history[_history.Count - 1] : _basePrice;
        public bool HasData => _history.Count > 0;

        /// <summary>슬롯(티어)과 표시가로 진입 추세선을 새로 생성한다. 교체 시에도 호출.</summary>
        public void StartCoin(CoinData slot, float basePrice)
        {
            _slot = slot;
            _basePrice = Mathf.Max(PriceFloor, basePrice);
            _history.Clear();
            if (_slot == null) return;

            // 진입 추세선(장식): 랜덤워크. 결과와 무관.
            float mid = (_slot.minSwing + _slot.maxSwing) * 0.5f;
            float p = _basePrice;
            _history.Add(p);
            for (int i = 1; i < InitialPoints; i++)
            {
                float step = Random.Range(-1f, 1f) * mid * 0.6f;
                p = Mathf.Max(PriceFloor, p * (1f + step));
                _history.Add(p);
            }
        }

        /// <summary>
        /// 코스메틱 한 틱. 승패·손익과 무관하게 추세선에 점 하나를 더한다(난이도 선택 미리보기 라이브용).
        /// 슬롯 변동폭으로 랜덤워크하므로 코인별로 출렁임 정도가 다르게 보인다.
        /// </summary>
        public float StepCosmetic()
        {
            if (_slot == null || _history.Count == 0) return CurrentPrice;
            float mid = (_slot.minSwing + _slot.maxSwing) * 0.5f;
            float step = Random.Range(-1f, 1f) * mid;
            float p = Mathf.Max(PriceFloor, CurrentPrice * (1f + step));
            _history.Add(p);
            if (_history.Count > MaxHistory)
                _history.RemoveRange(0, _history.Count - MaxHistory);
            return p;
        }

        /// <summary>
        /// 한 라운드 정산. 승률 50% 유지, 위험은 (1) 하우스 엣지: 승리 보상 차감,
        /// (2) 극단 추가 엣지: 떡상 보상 더 차감, (3) 연승 욕심: streak↑ 시 극단(폭락/상폐) 확률↑.
        /// delta는 fee 미반영 기본값(매니저가 fee 레이크 별도 적용).
        /// </summary>
        public CoinRoundResult Resolve(BetDirection dir, int stake, int streak = 0, int leverage = 1)
        {
            leverage = Mathf.Max(1, leverage);
            var result = new CoinRoundResult { pick = dir, stake = stake, leverage = leverage };
            if (_slot == null) return result;

            // 연승 욕심 페널티 — 오래 탈수록 극단(절반은 −100% 상폐) 확률이 오른다.
            float effExtreme = Mathf.Clamp01(_slot.extremeChance + Mathf.Max(0, streak) * _slot.streakGreed);
            bool extreme = Random.value < effExtreme;
            float mag = extreme ? 1f : Random.Range(_slot.minSwing, _slot.maxSwing);
            mag = Mathf.Max(mag, MinSwing);

            int sign = Random.value < 0.5f ? +1 : -1;          // 50/50 → 승률 50% 고정
            int dirSign = dir == BetDirection.Up ? +1 : -1;
            bool win = sign == dirSign;

            result.extreme = extreme;
            result.delisted = extreme && sign < 0;             // 극단 하락 = 상장폐지
            result.win = win;
            result.swing = mag;

            float oldPrice = CurrentPrice;
            float newPrice = Mathf.Max(PriceFloor, oldPrice * (1f + sign * mag));
            result.oldPrice = oldPrice;
            result.newPrice = newPrice;

            _history.Add(newPrice);
            if (_history.Count > MaxHistory)
                _history.RemoveRange(0, _history.Count - MaxHistory);

            // 손익(레버리지 = 위험-보상 다이얼): 레버리지가 클수록 적중 상한(winCap)과
            // 빗나감 상한(lossCap, 초과청산)이 함께 커진다 → 최대 레버리지 = 큰 승리 + 깊은 청산(대칭 스릴).
            // effLossCap > effWinCap×(1−edge) 라 EV는 항상 마이너스(하우스 엣지 유지).
            // 연승할수록 적중 보상에서 streakPayoutDecay만큼 더 떼 복리 런어웨이를 누른다.
            float move = mag * leverage;
            float t = _slot.maxLeverage <= 1
                ? 0f
                : Mathf.Clamp01((leverage - 1f) / (_slot.maxLeverage - 1f)); // lev1=0, 최대=1
            float effWinCap = Mathf.Lerp(_slot.winCap * 0.5f, _slot.winCap, t);   // 저레버리지=절반 → 최대=winCap
            float effLossCap = Mathf.Lerp(1f, _slot.lossCap, t);                  // 저레버리지=마진까지 → 최대=초과청산

            if (win)
            {
                float effWin = Mathf.Min(move, effWinCap);
                float edge = Mathf.Clamp01(_slot.houseEdge
                    + (extreme ? _slot.extremeExtraEdge : 0f)
                    + Mathf.Max(0, streak) * _slot.streakPayoutDecay);
                result.delta = Mathf.RoundToInt(stake * effWin * (1f - edge));
            }
            else
            {
                float effLoss = Mathf.Min(move, effLossCap);
                result.delta = -Mathf.RoundToInt(stake * effLoss);
                result.liquidated = move >= 1f; // 변동이 마진을 가득 채움 이상 = 청산
            }

            return result;
        }
    }
}
