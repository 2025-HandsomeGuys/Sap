// @tags: dungeon, generation, writer, serialize, txt
using System.Text;

namespace Gameplay.Dungeon.Authoring.Generation
{
    /// <summary>
    /// DungeonMapData를 기존 DungeonMapParser가 읽을 수 있는 txt로 직렬화한다.
    /// preset/seed 메타는 파서가 무시하지만, "이 맵 어디서 나왔지"를 추적하려고 남긴다.
    /// </summary>
    public static class DungeonMapWriter
    {
        public static string Write(DungeonMapData data, string presetName, int seed)
        {
            if (data == null) return string.Empty;

            var sb = new StringBuilder();
            sb.Append("# dungeon: ").Append(data.Name).Append('\n');
            sb.Append("# cell: ").Append(data.CellSize.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("# size: ").Append(data.Width).Append('x').Append(data.Height).Append('\n');
            sb.Append("# preset: ").Append(presetName ?? "").Append('\n');
            sb.Append("# seed: ").Append(seed).Append('\n');

            AppendBlock(sb, "[TILES]", data.Tiles, data.Width, data.Height);
            AppendBlock(sb, "[OBJECTS]", data.Objects, data.Width, data.Height);
            AppendBlock(sb, "[LINKS]", data.Links, data.Width, data.Height);
            return sb.ToString();
        }

        private static void AppendBlock(StringBuilder sb, string header, char[,] grid, int width, int height)
        {
            sb.Append(header).Append('\n');
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                    sb.Append(grid[r, c]);
                sb.Append('\n');
            }
        }
    }
}
