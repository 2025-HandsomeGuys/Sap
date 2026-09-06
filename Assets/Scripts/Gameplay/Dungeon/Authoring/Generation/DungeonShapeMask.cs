// @tags: dungeon, generation, shape, mask, data
namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>
    /// 던전 격자의 형태 1종. Cells는 [row, col], row 0이 맨 위.
    /// 가로형·세로형·ㄱ자를 전부 이 한 장으로 표현한다 — 형태를 늘려도 코드는 안 바뀐다.
    /// </summary>
    public class DungeonShapeMask
    {
        public const char DungeonCell = '.';
        public const char RockCell = '#';
        public const char StartCell = 'S';
        public const char EndCell = 'X';

        public string Name = "";
        public int Weight = 1;

        public int Width;
        public int Height;
        public char[,] Cells;

        public int StartRow, StartCol;
        public int EndRow, EndCol;

        /// <summary>방이 놓이는 칸인가. 암반(#)과 격자 밖은 false.</summary>
        public bool IsDungeon(int row, int col)
        {
            if (row < 0 || row >= Height || col < 0 || col >= Width) return false;
            char c = Cells[row, col];
            return c == DungeonCell || c == StartCell || c == EndCell;
        }
    }
}
