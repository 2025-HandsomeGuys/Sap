// @tags: dungeon, generation, compose, room, template, stamp, carve
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>조립된 맵 격자. 인덱싱은 [row, col], row 0이 맨 위.</summary>
    public class ComposeResult
    {
        public int Width;
        public int Height;
        public char[,] Tiles;
        public char[,] Objects;
        public char[,] Links;
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// 방 타입 격자에 실제 템플릿을 찍어 하나의 큰 격자로 만든다.
    /// 템플릿 선택은 Opens 정확 일치 → Opens 상위집합 → 빈 방 순으로 폴백한다.
    /// 상위집합 폴백에서 생기는 여분의 개구부는 막힌 방으로 이어지는 벽감이 되므로 치명적이지 않다.
    /// </summary>
    public static class DungeonRoomComposer
    {
        public static ComposeResult Compose(
            RoomPlan[,] plan,
            IReadOnlyList<DungeonRoomTemplate> templates,
            DungeonGenPresetSO preset,
            System.Random rng)
        {
            if (plan == null) throw new System.ArgumentNullException(nameof(plan));
            if (templates == null) throw new System.ArgumentNullException(nameof(templates));
            if (preset == null) throw new System.ArgumentNullException(nameof(preset));
            if (rng == null) throw new System.ArgumentNullException(nameof(rng));

            int gridH = plan.GetLength(0);
            int gridW = plan.GetLength(1);
            int rw = preset.roomWidth;
            int rh = preset.roomHeight;

            var res = new ComposeResult
            {
                Width = gridW * rw + 2,
                Height = gridH * rh + 2,
            };
            res.Tiles = Filled(res.Height, res.Width, preset.Wall);
            res.Objects = Filled(res.Height, res.Width, '.');
            res.Links = Filled(res.Height, res.Width, '.');

            for (int gr = 0; gr < gridH; gr++)
            for (int gc = 0; gc < gridW; gc++)
            {
                var cell = plan[gr, gc];

                // 암반 칸은 아무것도 안 한다 — 최종 격자가 이미 벽으로 초기화돼 있어 통짜로 남는다.
                if (cell.Role == RoomRole.Rock) continue;

                var template = Pick(cell.Opens, templates, rng, res.Warnings);

                if (template == null)
                    StampBlank(res, gr, gc, preset);
                else
                    StampTemplate(res, template, gr, gc, preset, rng);
            }

            if (preset.forceCarveBoundaries)
                CarveBoundaries(res, plan, preset);

            return res;
        }

        // 개구부 조합 정확 일치 → 상위집합 → null(빈 방)
        private static DungeonRoomTemplate Pick(
            RoomOpen opens, IReadOnlyList<DungeonRoomTemplate> templates,
            System.Random rng, List<string> warnings)
        {
            var exact = new List<DungeonRoomTemplate>();
            var superset = new List<DungeonRoomTemplate>();

            foreach (var t in templates)
            {
                if (t == null) continue;
                if (t.Opens == opens) exact.Add(t);
                else if ((t.Opens & opens) == opens) superset.Add(t);
            }

            if (exact.Count > 0) return WeightedPick(exact, rng);

            string label = RoomTypeUtil.FormatOpens(opens);
            if (label.Length == 0) label = "(닫힌 방)";

            if (superset.Count > 0)
            {
                warnings.Add($"'{label}' 템플릿이 없어 상위집합 템플릿으로 대체했습니다.");
                return WeightedPick(superset, rng);
            }

            warnings.Add($"'{label}' 템플릿이 없어 빈 방으로 생성했습니다.");
            return null;
        }

        private static DungeonRoomTemplate WeightedPick(List<DungeonRoomTemplate> list, System.Random rng)
        {
            int total = 0;
            foreach (var t in list) total += t.Weight > 0 ? t.Weight : 1;

            int roll = rng.Next(total);
            foreach (var t in list)
            {
                roll -= t.Weight > 0 ? t.Weight : 1;
                if (roll < 0) return t;
            }
            return list[list.Count - 1];
        }

        private static void StampTemplate(
            ComposeResult res, DungeonRoomTemplate t, int gr, int gc,
            DungeonGenPresetSO preset, System.Random rng)
        {
            int rw = preset.roomWidth;
            int rh = preset.roomHeight;

            for (int r = 0; r < rh; r++)
            for (int c = 0; c < rw; c++)
            {
                int gy = gr * rh + r + 1;
                int gx = gc * rw + c + 1;

                res.Tiles[gy, gx] = ResolveChanceTile(t.Tiles[r, c], preset, rng);
                res.Objects[gy, gx] = t.Objects[r, c];
                res.Links[gy, gx] = t.Links[r, c];
            }
        }

        // 템플릿이 없을 때의 폴백 방: 테두리만 벽, 내부는 빈칸. 경계 개통이 통로를 뚫는다.
        private static void StampBlank(ComposeResult res, int gr, int gc, DungeonGenPresetSO preset)
        {
            int rw = preset.roomWidth;
            int rh = preset.roomHeight;

            for (int r = 0; r < rh; r++)
            for (int c = 0; c < rw; c++)
            {
                int gy = gr * rh + r + 1;
                int gx = gc * rw + c + 1;

                bool border = r == 0 || r == rh - 1 || c == 0 || c == rw - 1;
                res.Tiles[gy, gx] = border ? preset.Wall : preset.Empty;
                res.Objects[gy, gx] = '.';
                res.Links[gy, gx] = '.';
            }
        }

        // 확률 심볼이면 굴려서 치환, 아니면 그대로.
        private static char ResolveChanceTile(char symbol, DungeonGenPresetSO preset, System.Random rng)
        {
            if (!preset.TryGetChanceTile(symbol, out var ct)) return symbol;

            string outcome = rng.NextDouble() < ct.chance ? ct.onHit : ct.onMiss;
            return string.IsNullOrEmpty(outcome) ? preset.Empty : outcome[0];
        }

        /// <summary>
        /// 인접한 경로 방 사이 경계에 통로를 강제로 뚫는다(설계 §4 개구부 위치 규약).
        /// 좌우는 방 하단 기준 위쪽 3행, 상하는 방 가운데 3열. 경계 양쪽 각 1칸씩 뚫는다.
        /// </summary>
        private static void CarveBoundaries(ComposeResult res, RoomPlan[,] plan, DungeonGenPresetSO preset)
        {
            int gridH = plan.GetLength(0);
            int gridW = plan.GetLength(1);
            int rw = preset.roomWidth;
            int rh = preset.roomHeight;
            char empty = preset.Empty;

            // 하한이 1인 이유: roomHeight가 6보다 작으면 rh-4가 0 이하로 내려가 방의 천장 행(0)까지
            // 뚫린다 — "닫힌 면은 확정 벽" 불변식이 조립 단계에서 조용히 깨져 위층으로 샌다.
            int rowFrom = System.Math.Max(1, rh - 4);
            int rowTo = System.Math.Max(0, rh - 2);
            int colFrom = System.Math.Max(0, rw / 2 - 1);
            int colTo = System.Math.Min(rw - 1, rw / 2 + 1);

            for (int gr = 0; gr < gridH; gr++)
            for (int gc = 0; gc < gridW; gc++)
            {
                var opens = plan[gr, gc].Opens;

                // 암반 칸으로는 뚫지 않는다: 통짜로 남아야 한다
                if ((opens & RoomOpen.R) != 0 && gc + 1 < gridW && plan[gr, gc + 1].Role != RoomRole.Rock)
                {
                    int boundaryCol = gc * rw + rw; // 왼쪽 방의 마지막 열(전역)
                    for (int r = rowFrom; r <= rowTo; r++)
                    {
                        int gy = gr * rh + r + 1;
                        res.Tiles[gy, boundaryCol] = empty;
                        res.Tiles[gy, boundaryCol + 1] = empty;
                    }
                }

                // 암반 칸으로는 뚫지 않는다: 통짜로 남아야 한다
                if ((opens & RoomOpen.D) != 0 && gr + 1 < gridH && plan[gr + 1, gc].Role != RoomRole.Rock)
                {
                    int boundaryRow = gr * rh + rh; // 위쪽 방의 마지막 행(전역)
                    for (int c = colFrom; c <= colTo; c++)
                    {
                        int gx = gc * rw + c + 1;
                        res.Tiles[boundaryRow, gx] = empty;
                        res.Tiles[boundaryRow + 1, gx] = empty;
                    }
                }
            }
        }

        private static char[,] Filled(int height, int width, char value)
        {
            var g = new char[height, width];
            for (int r = 0; r < height; r++)
                for (int c = 0; c < width; c++)
                    g[r, c] = value;
            return g;
        }
    }
}
