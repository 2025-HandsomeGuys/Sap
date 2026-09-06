// @tags: dungeon, generation, slot, fill, trap, reward
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>
    /// 조립된 [OBJECTS] 격자의 슬롯 심볼을 실제 오브젝트 심볼로 치환한다(설계 §7).
    /// 지형에서 확률 타일이 하는 일을 오브젝트 층에서 하는 것 — 템플릿은 "자리"만 정하고
    /// 무엇이 놓이는지는 시드가 정한다. 방 하나가 여러 판을 낳으므로 템플릿 수가 조합 수만큼 불어나지 않는다.
    ///
    /// 패스가 둘로 나뉘는 이유는 보상 때문이다. 함정은 칸마다 독립으로 굴려도 되지만
    /// 보상을 그렇게 굴리면 판마다 개수가 0~N개로 들쭉날쭉해진다. 보상은 맵 전체 후보를
    /// 모은 뒤 rewardCount개만 뽑는다.
    ///
    /// 난수 소비 순서는 (함정 row-major) → (보상 셔플)로 고정이다. 순서를 바꾸면 같은 시드가 다른 맵을 만든다.
    /// </summary>
    public static class DungeonRoomFiller
    {
        private const char None = '.'; // 오브젝트 격자의 빈 칸

        public static void Fill(ComposeResult res, DungeonGenPresetSO preset, System.Random rng)
        {
            if (res == null) throw new System.ArgumentNullException(nameof(res));
            if (preset == null) throw new System.ArgumentNullException(nameof(preset));
            if (rng == null) throw new System.ArgumentNullException(nameof(rng));

            int buried = FillTrapSlots(res, preset, rng);
            buried += FillRewardSlots(res, preset, rng);

            if (buried > 0)
                res.Warnings.Add($"슬롯 {buried}개가 벽 안에 있어 지웠습니다 — 확률 타일이 벽으로 굴렀거나 템플릿의 슬롯 위치가 잘못됐습니다.");
        }

        /// <summary>슬롯마다 독립으로 굴린다. 반환값은 벽에 묻혀 지운 슬롯 수.</summary>
        private static int FillTrapSlots(ComposeResult res, DungeonGenPresetSO preset, System.Random rng)
        {
            char rewardSlot = preset.RewardSlot;
            char wall = preset.Wall;
            int buried = 0;

            for (int r = 0; r < res.Height; r++)
            for (int c = 0; c < res.Width; c++)
            {
                char symbol = res.Objects[r, c];
                if (symbol == None || symbol == rewardSlot) continue;

                // 슬롯이 아니면 템플릿이 직접 놓은 오브젝트다(E/X, 압력판·문 같은 짝 기믹). 건드리지 않는다.
                if (!preset.TryGetObjectSlot(symbol, out var slot)) continue;

                // 벽에 묻힌 슬롯은 굴리기 전에 걸러낸다 — 굴려봐야 벽 속에 프리팹만 박힌다.
                if (res.Tiles[r, c] == wall)
                {
                    res.Objects[r, c] = None;
                    buried++;
                    continue;
                }

                res.Objects[r, c] = Roll(slot, rng);
            }
            return buried;
        }

        private static char Roll(DungeonGenPresetSO.ObjectSlot slot, System.Random rng)
        {
            if (rng.NextDouble() >= slot.fillChance) return None;

            var candidates = slot.candidates;
            if (candidates == null || candidates.Count == 0) return None;

            int total = 0;
            foreach (var w in candidates)
                if (w != null) total += Weight(w);
            if (total <= 0) return None;

            int roll = rng.Next(total);
            foreach (var w in candidates)
            {
                if (w == null) continue;
                roll -= Weight(w);
                if (roll < 0) return Symbol(w);
            }
            return None;
        }

        /// <summary>
        /// 보상 후보를 맵 전체에서 모아 rewardCount개만 확정한다. 반환값은 벽에 묻혀 지운 슬롯 수.
        /// </summary>
        private static int FillRewardSlots(ComposeResult res, DungeonGenPresetSO preset, System.Random rng)
        {
            char slotSymbol = preset.RewardSlot;
            char wall = preset.Wall;
            int buried = 0;

            var candidates = new List<(int row, int col)>();

            for (int r = 0; r < res.Height; r++)
            for (int c = 0; c < res.Width; c++)
            {
                if (res.Objects[r, c] != slotSymbol) continue;

                if (res.Tiles[r, c] == wall)
                {
                    res.Objects[r, c] = None;
                    buried++;
                    continue;
                }

                candidates.Add((r, c));
            }

            Shuffle(candidates, rng);

            int take = System.Math.Min(preset.rewardCount, candidates.Count);
            if (take < preset.rewardCount)
                res.Warnings.Add($"보상 후보가 {candidates.Count}자리뿐이라 {take}개만 배치했습니다(요청 {preset.rewardCount}개).");

            char reward = preset.Reward;
            for (int i = 0; i < candidates.Count; i++)
            {
                var (r, c) = candidates[i];
                res.Objects[r, c] = i < take ? reward : None;
            }
            return buried;
        }

        // 후보를 격자 순서대로 뽑으면 보상이 항상 왼쪽 위에 몰린다.
        private static void Shuffle(List<(int row, int col)> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static int Weight(DungeonGenPresetSO.WeightedSymbol w) => w.weight > 0 ? w.weight : 1;

        private static char Symbol(DungeonGenPresetSO.WeightedSymbol w)
            => string.IsNullOrEmpty(w.symbol) ? None : w.symbol[0];
    }
}
