// @tags: dungeon, generation, cavity, flood, connectivity
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>
    /// 입구(E)에서 빈 타일을 4방향 플러드필해, 닿지 않은 빈 타일을 전부 벽으로 메운다.
    ///
    /// 이 프로젝트의 던전 타일맵은 파괴할 수 없다. 그래서 벽으로 둘러싸인 공동은 영원히 못 가는
    /// 장식이 되고, 화면에서는 "암반 한가운데 뚫린 주머니"로 보인다. 확률 타일이 방 안에서 굴러
    /// 만든 공동까지 여기서 같이 사라진다.
    ///
    /// 부수 효과로 연결성 검증을 겸한다 — 출구에 닿지 않으면 그 맵은 실패다.
    ///
    /// 한계: 4방향 연결만 본다. "뚫려는 있지만 점프로 못 닿는" 공간은 잡지 못한다.
    /// </summary>
    public static class DungeonCavityFiller
    {
        /// <summary>메운 칸 수를 반환한다.</summary>
        public static int Fill(ComposeResult res, DungeonGenPresetSO preset, out bool exitReachable)
        {
            if (res == null) throw new System.ArgumentNullException(nameof(res));
            if (preset == null) throw new System.ArgumentNullException(nameof(preset));

            exitReachable = false;

            if (!TryFindObject(res, DungeonEntryPlacer.Entry, out int startRow, out int startCol))
                return 0;

            char wall = preset.Wall;
            var reached = new bool[res.Height, res.Width];
            var queue = new Queue<(int row, int col)>();

            reached[startRow, startCol] = true;
            queue.Enqueue((startRow, startCol));

            int[] dr = { 0, 0, -1, 1 };
            int[] dc = { -1, 1, 0, 0 };

            while (queue.Count > 0)
            {
                var (r, c) = queue.Dequeue();
                if (res.Objects[r, c] == DungeonEntryPlacer.Exit) exitReachable = true;

                for (int i = 0; i < 4; i++)
                {
                    int nr = r + dr[i], nc = c + dc[i];
                    if (nr < 0 || nr >= res.Height || nc < 0 || nc >= res.Width) continue;
                    if (reached[nr, nc] || res.Tiles[nr, nc] == wall) continue;

                    reached[nr, nc] = true;
                    queue.Enqueue((nr, nc));
                }
            }

            int filled = 0;
            for (int r = 0; r < res.Height; r++)
                for (int c = 0; c < res.Width; c++)
                {
                    if (res.Tiles[r, c] == wall || reached[r, c]) continue;

                    res.Tiles[r, c] = wall;
                    res.Objects[r, c] = '.'; // 벽 속에 프리팹이 박히지 않도록
                    res.Links[r, c] = '.';   // 암반 속 좌표로 압력판·문 배선이 걸리지 않도록
                    filled++;
                }

            return filled;
        }

        private static bool TryFindObject(ComposeResult res, char symbol, out int row, out int col)
        {
            for (int r = 0; r < res.Height; r++)
                for (int c = 0; c < res.Width; c++)
                    if (res.Objects[r, c] == symbol) { row = r; col = c; return true; }

            row = col = -1;
            return false;
        }
    }
}
