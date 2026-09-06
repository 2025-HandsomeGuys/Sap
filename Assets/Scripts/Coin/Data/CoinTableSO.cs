// @tags: coin, data, table, scriptableobject, lineup, gambling
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Coin.Data
{
    /// <summary>
    /// 코인 슬롯 라인업 + 교체 이름 풀 + 전역 규칙 (설계 §7.4 / §7.7).
    /// 에디터에서 에셋으로 만들어 CoinGameManager.coinTable에 할당한다.
    /// slots/replacementNames가 비어있으면 코드 기본값(Default*)이 폴백으로 쓰인다.
    /// </summary>
    [CreateAssetMenu(fileName = "CoinTable", menuName = "Market/Coin Table")]
    public class CoinTableSO : ScriptableObject
    {
        [Tooltip("위험 티어 슬롯 (낮은 위험 → 높은 위험 순). 비우면 코드 기본 라인업 사용")]
        public List<CoinData> slots = new List<CoinData>();

        [Tooltip("슬롯에 rotationNames가 없을 때만 쓰는 예비 이름 풀(랜덤). 슬롯별 순환이 우선")]
        public List<string> replacementNames = new List<string>();

        [Tooltip("하루 베팅 가능 라운드 수")]
        public int dailyRoundLimit = 5;

        [Tooltip("양방향 베팅세(레이크). 승패 무관 매 베팅마다 stake×fee 차감(money sink). 0=없음")]
        [Range(0f, 0.2f)] public float fee = 0.02f;

        [Tooltip("하루 출금 한도 배수. 코인 이익으로 골드가 (오늘 시작 골드 × 이 값)을 못 넘는다. 거래소 정책상 하루 출금 가능액 상한 — 복리 폭주 방지")]
        [FormerlySerializedAs("dailyWinMultiplier")]
        [Range(1, 20)] public int withdrawLimitMultiplier = 5;

        /// <summary>코드 기본 라인업 (설계 §7.4). 인스펙터 slots가 비었을 때 폴백.</summary>
        public static List<CoinData> DefaultSlots()
        {
            return new List<CoinData>
            {
                //              id  name        tier  basePrice    minSwing maxSwing extreme  minBet     maxBet            houseEdge / extremeExtraEdge / streakGreed / maxLeverage(≈1/minSwing) / winCap(적중상한↓) / lossCap(초과청산↑) / streakPayoutDecay(연승감쇠↑) — 고티어일수록 가혹
                new CoinData(1, "GRAVEL",  1,          40f, 0.01f, 0.04f, 0.000f,        50,       1500, houseEdge: 0.02f, extremeExtraEdge: 0.00f, streakGreed: 0.000f, maxLeverage: 100, winCap: 0.97f, lossCap: 1.05f, streakPayoutDecay: 0.03f, rotation: new[]{"PEBBLE","GRIT"}),
                new CoinData(2, "MOLE",    1,         350f, 0.03f, 0.09f, 0.000f,       200,       3000, houseEdge: 0.03f, extremeExtraEdge: 0.00f, streakGreed: 0.000f, maxLeverage: 30,  winCap: 0.95f, lossCap: 1.07f, streakPayoutDecay: 0.04f, rotation: new[]{"WORM","GOPHER"}),
                new CoinData(3, "LUCKY",   2,        1800f, 0.05f, 0.13f, 0.010f,       800,      12000, houseEdge: 0.05f, extremeExtraEdge: 0.10f, streakGreed: 0.010f, maxLeverage: 20,  winCap: 0.93f, lossCap: 1.10f, streakPayoutDecay: 0.05f, rotation: new[]{"CLOVER","WISH"}),
                new CoinData(4, "VEIN",    2,        9000f, 0.07f, 0.18f, 0.020f,      2000,      30000, houseEdge: 0.06f, extremeExtraEdge: 0.12f, streakGreed: 0.012f, maxLeverage: 14,  winCap: 0.91f, lossCap: 1.12f, streakPayoutDecay: 0.06f, rotation: new[]{"NUGGET","SEAM"}),
                new CoinData(5, "QUARTZ",  3,       45000f, 0.10f, 0.24f, 0.035f,      6000,      90000, houseEdge: 0.08f, extremeExtraEdge: 0.15f, streakGreed: 0.018f, maxLeverage: 10,  winCap: 0.89f, lossCap: 1.15f, streakPayoutDecay: 0.07f, rotation: new[]{"GEODE","PRISM"}),
                new CoinData(6, "GOBLIN",  3,      220000f, 0.13f, 0.32f, 0.050f,     15000,     250000, houseEdge: 0.10f, extremeExtraEdge: 0.18f, streakGreed: 0.022f, maxLeverage: 8,   winCap: 0.87f, lossCap: 1.17f, streakPayoutDecay: 0.08f, rotation: new[]{"GREMLIN","IMP"}),
                new CoinData(7, "MAGMA",   4,     1100000f, 0.16f, 0.42f, 0.075f,     40000,     700000, houseEdge: 0.13f, extremeExtraEdge: 0.22f, streakGreed: 0.030f, maxLeverage: 6,   winCap: 0.85f, lossCap: 1.20f, streakPayoutDecay: 0.09f, rotation: new[]{"EMBER","CINDER"}),
                new CoinData(8, "BOOM",    4,     6000000f, 0.20f, 0.55f, 0.110f,    100000,    1500000, houseEdge: 0.15f, extremeExtraEdge: 0.25f, streakGreed: 0.035f, maxLeverage: 5,   winCap: 0.83f, lossCap: 1.22f, streakPayoutDecay: 0.10f, rotation: new[]{"BLAST","FUSE"}),
                new CoinData(9, "JACKPOT", 5,    38000000f, 0.26f, 0.70f, 0.160f,    250000,    3000000, houseEdge: 0.18f, extremeExtraEdge: 0.30f, streakGreed: 0.045f, maxLeverage: 4,   winCap: 0.80f, lossCap: 1.25f, streakPayoutDecay: 0.11f, rotation: new[]{"BONANZA","MOONSHOT"}),
                new CoinData(10,"ABYSS",   5,   260000000f, 0.32f, 0.95f, 0.250f,    500000,    5000000, unlimitedMax: true, houseEdge: 0.22f, extremeExtraEdge: 0.35f, streakGreed: 0.055f, maxLeverage: 3, winCap: 0.78f, lossCap: 1.30f, streakPayoutDecay: 0.12f, rotation: new[]{"VOID","OBLIVION"}),
            };
        }

        /// <summary>코드 기본 교체 이름 풀 (설계 §7.7). 인스펙터 풀이 비었을 때 폴백.</summary>
        public static List<string> DefaultReplacementNames()
        {
            return new List<string>
            {
                "RUBBLE","NUGGET","GEODE","SHALE","EMBER","CINDER","RELIC","FOSSIL",
                "SLUDGE","GUSHER","PEBBLE","OBSIDIAN","BASALT","COMET","BLAZE","DRIFT",
                "SPARK","HOARD","TROVE","RUIN","GHOST","ZOMBIE","PHOENIX","VOID",
            };
        }
    }
}
