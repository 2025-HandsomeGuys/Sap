using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring
{
    /// <summary>파싱된 던전 맵 한 장. 그리드는 [row, col] 인덱싱, 빈 칸은 '.'.</summary>
    public class DungeonMapData
    {
        public string Name = "dungeon";
        public float CellSize = 1f;
        public int Width;
        public int Height;
        public char[,] Tiles;   // [Height, Width]
        public char[,] Objects; // [Height, Width]
        public char[,] Links;   // [Height, Width] — LINKS 미존재 시 전부 '.'
    }

    public class DungeonMapParseResult
    {
        public bool Success;
        public DungeonMapData Data;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }
}
