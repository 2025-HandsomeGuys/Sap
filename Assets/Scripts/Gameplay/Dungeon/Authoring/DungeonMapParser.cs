using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring
{
    /// <summary>
    /// 던전 텍스트 맵을 파싱한다. 형식:
    ///   # key: value   메타(주석). dungeon, cell 인식.
    ///   [TILES] / [OBJECTS] / [LINKS] 블록. 각 블록은 '.'=빈칸인 문자 격자.
    /// 규칙: 세 블록의 행 수(Height)·열 수(Width)가 같아야 한다(짧은 행은 '.' 패딩).
    /// TILES, OBJECTS 는 필수. LINKS 는 선택(없으면 전부 '.').
    /// </summary>
    public static class DungeonMapParser
    {
        private enum Section { None, Tiles, Objects, Links }

        public static DungeonMapParseResult Parse(string text)
        {
            var result = new DungeonMapParseResult();
            if (string.IsNullOrEmpty(text))
            {
                result.Errors.Add("빈 입력");
                return result;
            }

            string name = "dungeon";
            float cell = 1f;
            var tiles = new List<string>();
            var objects = new List<string>();
            var links = new List<string>();
            List<string> current = null;

            string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            foreach (string raw in lines)
            {
                string line = raw;
                if (line.StartsWith("#"))
                {
                    ParseMeta(line, ref name, ref cell);
                    continue;
                }
                string trimmed = line.Trim();
                if (trimmed == "[TILES]") { current = tiles; continue; }
                if (trimmed == "[OBJECTS]") { current = objects; continue; }
                if (trimmed == "[LINKS]") { current = links; continue; }
                if (trimmed.Length == 0) continue; // 블록 사이 빈 줄 무시
                if (current == null)
                {
                    result.Errors.Add($"섹션 헤더 전에 내용이 나옴: '{line}'");
                    return result;
                }
                current.Add(line.TrimEnd());
            }

            if (tiles.Count == 0) { result.Errors.Add("[TILES] 섹션이 없거나 비어있음"); }
            if (objects.Count == 0) { result.Errors.Add("[OBJECTS] 섹션이 없거나 비어있음"); }
            if (result.Errors.Count > 0) return result;

            int height = tiles.Count;
            int width = MaxWidth(tiles);

            if (objects.Count != height)
            {
                result.Errors.Add($"[OBJECTS] 행 수({objects.Count})가 [TILES]({height})와 다름");
                return result;
            }
            if (MaxWidth(objects) != width)
            {
                result.Errors.Add($"[OBJECTS] 열 수({MaxWidth(objects)})가 [TILES]({width})와 다름");
                return result;
            }
            bool hasLinks = links.Count > 0;
            if (hasLinks && (links.Count != height || MaxWidth(links) != width))
            {
                result.Errors.Add($"[LINKS] 크기가 [TILES]({width}x{height})와 다름");
                return result;
            }

            var data = new DungeonMapData
            {
                Name = name, CellSize = cell, Width = width, Height = height,
                Tiles = ToGrid(tiles, height, width),
                Objects = ToGrid(objects, height, width),
                Links = hasLinks ? ToGrid(links, height, width) : FilledDots(height, width),
            };
            result.Data = data;
            result.Success = true;
            return result;
        }

        private static void ParseMeta(string line, ref string name, ref float cell)
        {
            // "# dungeon: cave_01" / "# cell: 1"
            string body = line.TrimStart('#').Trim();
            int colon = body.IndexOf(':');
            if (colon < 0) return;
            string key = body.Substring(0, colon).Trim().ToLowerInvariant();
            string val = body.Substring(colon + 1).Trim();
            if (key == "dungeon") name = val;
            else if (key == "cell" && float.TryParse(val, out float c)) cell = c;
        }

        private static int MaxWidth(List<string> rows)
        {
            int w = 0;
            foreach (var r in rows) if (r.Length > w) w = r.Length;
            return w;
        }

        private static char[,] ToGrid(List<string> rows, int height, int width)
        {
            var g = new char[height, width];
            for (int r = 0; r < height; r++)
                for (int c = 0; c < width; c++)
                    g[r, c] = c < rows[r].Length ? rows[r][c] : '.';
            return g;
        }

        private static char[,] FilledDots(int height, int width)
        {
            var g = new char[height, width];
            for (int r = 0; r < height; r++)
                for (int c = 0; c < width; c++)
                    g[r, c] = '.';
            return g;
        }
    }
}
