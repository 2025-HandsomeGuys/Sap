// @tags: relic, drop, settings, dto, json, tier, layer, exploration
using System;
using System.Collections.Generic;

/// <summary>
/// StreamingAssets/relicDropSettings.json 의 C# 표현.
///
/// 유물은 상점이 아니라 <b>탐험</b>으로 얻는다 — 돌을 완파했을 때의 확률 드롭과 던전 상자다.
/// "어느 지층에서 어떤 유물이 나오는가"를 코드가 아니라 이 파일 하나로 답할 수 있게
/// 티어별 유물 목록과 지층별 티어 가중치를 전부 JSON에 둔다.
///
/// 티어 배정 근거: <c>Assets/Docs/economy/equipment-relic-price-design.md</c> §6.1.
/// 설계 배경: <c>Assets/Docs/relic-exploration-drop.md</c>
/// </summary>
[Serializable]
public class RelicDropSettingsData
{
    /// <summary>false면 유물 드롭이 통째로 꺼진다(밸런스 실험·QA용).</summary>
    public bool enabled = true;

    public RockSection    rock         = new RockSection();
    public ChestSection   dungeonChest = new ChestSection();
    public LayerEntry[]   layers;
    public TierEntry[]    tiers;

    // ── 돌 드롭 ─────────────────────────────────────────────────────────
    [Serializable]
    public class RockSection
    {
        /// <summary>일반 돌 1개 완파당 유물 등장 확률.</summary>
        public float baseChance = 0.0035f;

        /// <summary>광물돌(MineralRock)에 곱하는 배율. 캘 만한 돌을 노린 보상.</summary>
        public float mineralRockMultiplier = 2.0f;

        /// <summary>
        /// 이만큼 돌을 깨는 동안 유물이 한 번도 안 나오면 다음 돌에서 확정 지급(피티).
        /// 0 이하면 피티 없음 — 순수 확률이라 운이 나쁘면 유물 0개로 게임이 끝날 수 있다.
        /// </summary>
        public int pityRocks = 400;

        /// <summary>바닥에 떨어진 유물의 sortingOrder. 지형(0)보다 위에 둬서 파묻혀 안 보이는 일을 막는다.</summary>
        public int sortingOrder = 1;
    }

    // ── 던전 상자 ───────────────────────────────────────────────────────
    [Serializable]
    public class ChestSection
    {
        /// <summary>유물 보상이 켜진 상자를 열었을 때 실제로 유물이 들어 있을 확률.</summary>
        public float chance = 0.35f;

        /// <summary>연속으로 이만큼 빈손이면 다음 상자는 확정. 0 이하면 피티 없음.</summary>
        public int pityChests = 6;
    }

    // ── 지층별 티어 가중치 ──────────────────────────────────────────────
    [Serializable]
    public class LayerEntry
    {
        /// <summary>tileData.json의 tileType 문자열(Dirt/Ice/MagmaRock/MeteoriteRock).</summary>
        public string tileType;

        /// <summary>
        /// 티어 1·2·3의 추첨 가중치. 해당 지층의 티어를 가장 크게 두고
        /// 아래 티어는 낮은 값으로 남겨 "놓친 유물을 깊은 층에서 회수"할 수 있게 한다.
        /// 0이면 그 티어는 이 지층에서 안 나온다. 길이가 모자라면 나머지는 0으로 본다.
        /// </summary>
        public int[] tierWeights;
    }

    // ── 티어별 유물 풀 ──────────────────────────────────────────────────
    [Serializable]
    public class TierEntry
    {
        public int tier;                // 1..3
        public string[] relics;         // RelicID enum 이름
    }

    // ====================================================================
    //  조회
    // ====================================================================

    /// <summary>지층 tileType에 걸린 가중치. 없으면 null(= 이 지층엔 유물이 안 나온다).</summary>
    public int[] ResolveTierWeights(string tileType)
    {
        if (layers == null || string.IsNullOrEmpty(tileType)) return null;
        for (int i = 0; i < layers.Length; i++)
            if (layers[i] != null && layers[i].tileType == tileType)
                return layers[i].tierWeights;
        return null;
    }

    /// <summary>티어 번호(1-based)에 속한 유물 이름 목록. 없으면 빈 배열.</summary>
    public string[] ResolveTierPool(int tier)
    {
        if (tiers == null) return Array.Empty<string>();
        for (int i = 0; i < tiers.Length; i++)
            if (tiers[i] != null && tiers[i].tier == tier)
                return tiers[i].relics ?? Array.Empty<string>();
        return Array.Empty<string>();
    }

    /// <summary>가중치 배열에서 가장 큰 티어 번호. 표시·로그용.</summary>
    public int HighestTier()
    {
        int max = 0;
        if (tiers != null)
            for (int i = 0; i < tiers.Length; i++)
                if (tiers[i] != null && tiers[i].tier > max) max = tiers[i].tier;
        return max;
    }

    /// <summary>이 설정이 다루는 모든 유물 이름(중복 제거 없이 티어 순).</summary>
    public IEnumerable<string> AllRelicNames()
    {
        if (tiers == null) yield break;
        for (int i = 0; i < tiers.Length; i++)
        {
            var t = tiers[i];
            if (t?.relics == null) continue;
            for (int j = 0; j < t.relics.Length; j++) yield return t.relics[j];
        }
    }
}
