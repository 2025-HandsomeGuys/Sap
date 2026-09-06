// @tags: dungeon, generation, entry, exit, placement
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>
    /// 시작·끝 칸의 바닥에 E(입구)와 X(출구)를 찍는다.
    ///
    /// 전용 START/END 템플릿을 두지 않는 이유: 미로에서는 시작 칸의 개구부 조합을 미리 알 수 없어
    /// 'START LRUD'까지 15종 × 2를 그려야 한다. 대신 조립이 끝난 격자에서 바닥을 찾아 찍는다.
    /// </summary>
    public static class DungeonEntryPlacer
    {
        private const char None = '.';
        public const char Entry = 'E';
        public const char Exit = 'X';

        public static void Place(
            ComposeResult res, RoomPlan[,] plan, DungeonGenPresetSO preset, System.Random rng)
        {
            if (res == null) throw new System.ArgumentNullException(nameof(res));
            if (plan == null) throw new System.ArgumentNullException(nameof(plan));
            if (preset == null) throw new System.ArgumentNullException(nameof(preset));
            if (rng == null) throw new System.ArgumentNullException(nameof(rng));

            for (int gr = 0; gr < plan.GetLength(0); gr++)
            for (int gc = 0; gc < plan.GetLength(1); gc++)
            {
                if (plan[gr, gc].Role == RoomRole.Start) PlaceOne(res, preset, rng, gr, gc, Entry);
                else if (plan[gr, gc].Role == RoomRole.End) PlaceOne(res, preset, rng, gr, gc, Exit);
            }
        }

        private static void PlaceOne(
            ComposeResult res, DungeonGenPresetSO preset, System.Random rng, int gr, int gc, char symbol)
        {
            int rowFrom = gr * preset.roomHeight + 1;
            int rowTo = rowFrom + preset.roomHeight - 1;
            int colFrom = gc * preset.roomWidth + 1;
            int colTo = colFrom + preset.roomWidth - 1;

            var free = new List<(int row, int col)>();     // 슬롯도 없는 자리
            var occupied = new List<(int row, int col)>(); // 바닥이지만 슬롯이 있는 자리

            for (int r = rowFrom; r <= rowTo; r++)
            for (int c = colFrom; c <= colTo; c++)
            {
                if (!IsFloorSpot(res, preset, r, c)) continue;
                if (res.Objects[r, c] == None) free.Add((r, c));
                else occupied.Add((r, c));
            }

            var pool = free.Count > 0 ? free : occupied;
            if (pool.Count > 0)
            {
                var (pr, pc) = pool[rng.Next(pool.Count)];
                res.Objects[pr, pc] = symbol;
                return;
            }

            // 바닥이 하나도 없다. 방 맨 아랫줄 가운데를 바닥으로 만들고 그 위에 놓는다.
            int floorRow = rowTo;
            int mid = colFrom + preset.roomWidth / 2;

            // CarveBoundaries가 아래로 뚫어둔 통로를 되메우지 않도록 이미 빈 칸인 열은 피한다.
            // 탐색은 방의 '내부' 열만 본다 — 가장자리 열을 집으면 floorRow-1을 뚫을 때 옆벽에 구멍이 난다.
            // 방 폭이 3 미만이면 내부 열이 없으므로 기본값(가운데)을 그대로 쓴다.
            for (int c = colFrom + 1; c <= colTo - 1; c++)
            {
                if (res.Tiles[floorRow, c] != preset.Empty) { mid = c; break; }
            }

            res.Tiles[floorRow, mid] = preset.Wall;
            res.Tiles[floorRow - 1, mid] = preset.Empty;
            res.Objects[floorRow - 1, mid] = symbol;
            res.Warnings.Add($"방 ({gr},{gc})에 바닥이 없어 '{symbol}' 자리를 강제로 만들었습니다 — 템플릿을 확인하세요.");
        }

        private static bool IsFloorSpot(ComposeResult res, DungeonGenPresetSO preset, int r, int c)
        {
            if (res.Tiles[r, c] != preset.Empty) return false;
            if (r + 1 >= res.Height) return false;
            return res.Tiles[r + 1, c] == preset.Wall;
        }
    }
}
