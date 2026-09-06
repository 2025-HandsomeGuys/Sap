// @tags: coin, data, slot, tier, gambling
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Coin.Data
{
    /// <summary>
    /// 코인 "슬롯(위험 티어)" 정의 (설계 §7.4 / §7.7). 고정값이며 CoinTableSO에 보관된다.
    /// 슬롯에 앉는 코인 인스턴스(현재 이름/표시가)는 CoinSlotState가 들고 있다(동적 로스터).
    /// 승률은 전 슬롯 50% 고정 — 위험은 변동폭(minSwing~maxSwing)과 극단(extremeChance)에서만 나온다.
    /// </summary>
    [Serializable]
    public class CoinData
    {
        public int id;                  // 슬롯 id (1~10). 코인이 갈려도 변하지 않음
        public string defaultName;      // 슬롯 최초 코인 영문명 (GRAVEL …). 번역 안 함
        public string flavorKey;        // 슬롯 최초 코인의 플레이버 키 (COIN_FLAVOR_GRAVEL). 현재 코인 기준 키는 FlavorKeyFor 사용
        public int riskTier = 1;        // 1~5 위험 게이지 표시용

        /// <summary>
        /// 이 슬롯(스테이지)에서 돌려 쓰는 코인 이름 순환 목록. [0] = defaultName.
        /// 상장폐지될 때마다 다음 이름으로 넘어가고 끝에 닿으면 [0]으로 되돌아온다 (A→B→C→A).
        /// 비어 있으면 CoinTableSO.replacementNames 전역 풀이 폴백으로 쓰인다.
        /// </summary>
        public List<string> rotationNames = new List<string>();

        public float basePrice = 100f;  // 표시가 기준(시각용). 손익엔 직접 안 씀
        [Range(0f, 1f)] public float minSwing = 0.02f;     // 일반 |변동폭| 하한
        [Range(0f, 1f)] public float maxSwing = 0.08f;     // 일반 |변동폭| 상한
        [Range(0f, 1f)] public float extremeChance = 0f;   // ±100% 발생 확률 (절반이 −100% 상장폐지)

        // 위험/하우스 엣지 — 승률은 50% 유지하되 손익 비대칭으로 위험을 부여한다(고티어↑).
        [Range(0f, 0.5f)] public float houseEdge = 0f;        // 승리 보상 차감 비율(EV 하락)
        [Range(0f, 0.5f)] public float extremeExtraEdge = 0f; // 극단(떡상) 보상 추가 차감
        [Range(0f, 0.2f)] public float streakGreed = 0f;      // 연승 1당 극단확률 가산(욕심 페널티)
        public int maxLeverage = 1;     // 이 티어 최대 배율(고티어↑). 1=레버리지 없음

        // 비대칭 페이아웃(도파민 싱크) — 승률 50%는 유지, 손익 '크기'로 EV를 음수로 만든다.
        [Range(0f, 1f)] public float winCap = 1f;             // 적중 effMove 상한. <1이면 고배율 적중이 마진의 winCap까지만(비대칭 엣지)
        [Range(1f, 2f)] public float lossCap = 1f;            // 빗나감 effMove 상한. >1이면 소프트 초과청산(손실>마진, 지갑서 클램프 — 빚 없음)
        [Range(0f, 0.3f)] public float streakPayoutDecay = 0f;// 연승 1당 적중 보상에서 추가 차감(연승 욕심 → 보상 감쇠, 복리 런어웨이 차단)

        public int minBet = 10;         // 소지금 기반 자연 해금 게이트
        public int maxBet = 50000;
        public bool unlimitedMax = false;

        public CoinData() { }

        public CoinData(int id, string defaultName, int riskTier, float basePrice,
                        float minSwing, float maxSwing, float extremeChance,
                        int minBet, int maxBet, bool unlimitedMax = false,
                        float houseEdge = 0f, float extremeExtraEdge = 0f, float streakGreed = 0f,
                        int maxLeverage = 1,
                        float winCap = 1f, float lossCap = 1f, float streakPayoutDecay = 0f,
                        string[] rotation = null)
        {
            this.id = id;
            this.defaultName = defaultName;
            this.flavorKey = "COIN_FLAVOR_" + defaultName;
            this.riskTier = riskTier;
            this.basePrice = basePrice;
            this.minSwing = minSwing;
            this.maxSwing = maxSwing;
            this.extremeChance = extremeChance;
            this.minBet = minBet;
            this.maxBet = maxBet;
            this.unlimitedMax = unlimitedMax;
            this.houseEdge = houseEdge;
            this.extremeExtraEdge = extremeExtraEdge;
            this.streakGreed = streakGreed;
            this.maxLeverage = Mathf.Max(1, maxLeverage);
            this.winCap = Mathf.Clamp01(winCap);
            this.lossCap = Mathf.Max(1f, lossCap);
            this.streakPayoutDecay = Mathf.Max(0f, streakPayoutDecay);

            rotationNames = new List<string> { defaultName };
            if (rotation != null)
                foreach (var n in rotation)
                    if (!string.IsNullOrEmpty(n) && !rotationNames.Contains(n)) rotationNames.Add(n);
        }

        /// <summary>코인 이름 → 플레이버 로컬라이제이션 키. 순환 코인도 각자 플레이버를 갖는다.</summary>
        public static string FlavorKeyFor(string coinName)
            => string.IsNullOrEmpty(coinName) ? "" : "COIN_FLAVOR_" + coinName;

        /// <summary>순환 목록에서 index번째 이름(모듈로 순환). 목록이 비었으면 null.</summary>
        public string RotationNameAt(int index)
        {
            if (rotationNames == null || rotationNames.Count == 0) return null;
            int i = index % rotationNames.Count;
            if (i < 0) i += rotationNames.Count;
            return rotationNames[i];
        }
    }
}
