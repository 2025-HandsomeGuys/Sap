// @tags: dungeon, generation, path, maze, grid, room
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>격자 한 칸의 계획. 어떤 역할의 칸이며 어느 면이 열려야 하는가.</summary>
    public struct RoomPlan
    {
        public RoomRole Role;
        public RoomOpen Opens;
    }

    /// <summary>
    /// 형태 마스크 위에 미로를 뚫는다.
    ///
    /// 1) 랜덤 DFS 스패닝 트리 — S에서 출발해 모든 던전 칸을 한 번씩 방문하며 지나온 면을 연다.
    ///    트리가 완성되면 **모든 칸이 반드시 S에서 도달 가능**하다. 뚫고 나서 검사하고 재시도하는
    ///    구조가 필요 없는 이유가 이것이다.
    /// 2) 추가 연결 — 남은 면을 extraConnectionChance로 더 뚫어 순환로와 갈림길을 만든다.
    ///
    /// 반환 배열은 [row, col] 인덱싱이며 row 0이 맨 위. 암반 칸은 Role=Rock, Opens=None으로 남는다.
    /// </summary>
    public static class DungeonPathBuilder
    {
        private static readonly int[] DirRow = { 0, 0, -1, 1 };
        private static readonly int[] DirCol = { -1, 1, 0, 0 };
        private static readonly RoomOpen[] DirOpen = { RoomOpen.L, RoomOpen.R, RoomOpen.U, RoomOpen.D };

        public static RoomPlan[,] Build(DungeonShapeMask mask, float extraConnectionChance, System.Random rng)
        {
            if (mask == null) throw new System.ArgumentNullException(nameof(mask));
            if (rng == null) throw new System.ArgumentNullException(nameof(rng));

            int h = mask.Height, w = mask.Width;
            var plan = new RoomPlan[h, w];

            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    RoomRole role = RoomRole.Rock;
                    if (mask.IsDungeon(r, c))
                    {
                        role = RoomRole.Normal;
                        if (r == mask.StartRow && c == mask.StartCol) role = RoomRole.Start;
                        else if (r == mask.EndRow && c == mask.EndCol) role = RoomRole.End;
                    }
                    plan[r, c] = new RoomPlan { Role = role, Opens = RoomOpen.None };
                }

            CarveSpanningTree(plan, mask, rng);
            AddExtraConnections(plan, mask, extraConnectionChance, rng);
            return plan;
        }

        // 반복 DFS. 재귀로 짜면 큰 마스크에서 스택이 깊어진다.
        private static void CarveSpanningTree(RoomPlan[,] plan, DungeonShapeMask mask, System.Random rng)
        {
            var visited = new bool[mask.Height, mask.Width];
            var stack = new Stack<(int row, int col)>();
            var candidates = new List<int>(4);

            visited[mask.StartRow, mask.StartCol] = true;
            stack.Push((mask.StartRow, mask.StartCol));

            while (stack.Count > 0)
            {
                var (r, c) = stack.Peek();

                candidates.Clear();
                for (int i = 0; i < 4; i++)
                {
                    int nr = r + DirRow[i], nc = c + DirCol[i];
                    if (mask.IsDungeon(nr, nc) && !visited[nr, nc]) candidates.Add(i);
                }

                if (candidates.Count == 0) { stack.Pop(); continue; }

                int dir = candidates[rng.Next(candidates.Count)];
                int tr = r + DirRow[dir], tc = c + DirCol[dir];

                Open(plan, r, c, tr, tc, DirOpen[dir]);
                visited[tr, tc] = true;
                stack.Push((tr, tc));
            }
        }

        // 오른쪽·아래 방향만 훑는다 — 왼쪽·위까지 보면 같은 면을 두 번 센다.
        private static void AddExtraConnections(
            RoomPlan[,] plan, DungeonShapeMask mask, float chance, System.Random rng)
        {
            if (chance <= 0f) return;

            var closed = new List<(int row, int col, int dir)>();
            for (int r = 0; r < mask.Height; r++)
                for (int c = 0; c < mask.Width; c++)
                {
                    if (!mask.IsDungeon(r, c)) continue;

                    if (mask.IsDungeon(r, c + 1) && (plan[r, c].Opens & RoomOpen.R) == 0)
                        closed.Add((r, c, 1));
                    if (mask.IsDungeon(r + 1, c) && (plan[r, c].Opens & RoomOpen.D) == 0)
                        closed.Add((r, c, 3));
                }

            Shuffle(closed, rng);

            foreach (var (r, c, dir) in closed)
            {
                if (rng.NextDouble() >= chance) continue;
                Open(plan, r, c, r + DirRow[dir], c + DirCol[dir], DirOpen[dir]);
            }
        }

        private static void Open(RoomPlan[,] plan, int r, int c, int tr, int tc, RoomOpen dir)
        {
            plan[r, c].Opens |= dir;
            plan[tr, tc].Opens |= RoomTypeUtil.Opposite(dir);
        }

        // 격자 순서대로 뚫으면 순환로가 항상 왼쪽 위에 몰린다.
        private static void Shuffle(List<(int row, int col, int dir)> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
