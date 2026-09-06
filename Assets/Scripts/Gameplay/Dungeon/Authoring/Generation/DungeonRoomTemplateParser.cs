// @tags: dungeon, generation, room, template, parser
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    public class RoomTemplateParseResult
    {
        public bool Success;
        public readonly List<DungeonRoomTemplate> Templates = new List<DungeonRoomTemplate>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// 방 템플릿 파일을 파싱한다. 파일 하나에 방 여러 개, '---' 한 줄로 구분.
    ///   # room: &lt;OPENS?&gt;   OPENS = L/R/U/D 조합. START/END/FILL 토큰은 폐지됐다(경고 후 무시).
    ///   # weight: N                        같은 타입끼리의 추첨 가중치 (생략 시 1)
    ///   [TILES] / [OBJECTS] 필수, [LINKS] 선택
    /// 모든 방은 roomWidth x roomHeight와 **정확히** 일치해야 한다(짧은 행도 에러).
    /// 짧은 행을 '.'로 패딩하면 방 가장자리가 조용히 뚫려 의도치 않은 개구부가 생긴다.
    /// </summary>
    public static class DungeonRoomTemplateParser
    {
        public static RoomTemplateParseResult Parse(string text, int roomWidth, int roomHeight, string sourceName)
        {
            var result = new RoomTemplateParseResult();
            if (string.IsNullOrWhiteSpace(text))
            {
                result.Errors.Add($"[{sourceName}] 빈 입력");
                return result;
            }

            string[] lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            var block = new List<string>();
            int blockStartLine = 1;
            int blockIndex = 0;

            for (int i = 0; i <= lines.Length; i++)
            {
                bool isSeparator = i == lines.Length || lines[i].TrimEnd().StartsWith("---");
                if (!isSeparator) { block.Add(lines[i]); continue; }

                if (HasContent(block))
                {
                    var t = ParseBlock(block, roomWidth, roomHeight, sourceName, blockIndex, blockStartLine, result);
                    if (t != null) result.Templates.Add(t);
                    blockIndex++;
                }
                block.Clear();
                blockStartLine = i + 2;
            }

            if (result.Templates.Count == 0 && result.Errors.Count == 0)
                result.Errors.Add($"[{sourceName}] 방 템플릿을 하나도 찾지 못했습니다.");

            result.Success = result.Errors.Count == 0;
            return result;
        }

        private static bool HasContent(List<string> block)
        {
            foreach (string l in block)
                if (l.Trim().Length > 0) return true;
            return false;
        }

        // 방 하나(블록) 파싱. 실패 시 result.Errors에 쌓고 null 반환.
        private static DungeonRoomTemplate ParseBlock(
            List<string> block, int roomWidth, int roomHeight,
            string sourceName, int blockIndex, int startLine, RoomTemplateParseResult result)
        {
            string tag = $"[{sourceName} #{blockIndex} (line {startLine})]";

            var template = new DungeonRoomTemplate();
            bool sawRoomHeader = false;

            var tiles = new List<string>();
            var objects = new List<string>();
            var links = new List<string>();
            List<string> current = null;

            foreach (string raw in block)
            {
                string trimmed = raw.Trim();
                if (trimmed.Length == 0) continue;

                if (trimmed.StartsWith("#"))
                {
                    if (!ParseMeta(trimmed, template, ref sawRoomHeader, tag, result)) return null;
                    continue;
                }
                if (trimmed == "[TILES]") { current = tiles; continue; }
                if (trimmed == "[OBJECTS]") { current = objects; continue; }
                if (trimmed == "[LINKS]") { current = links; continue; }

                if (current == null)
                {
                    result.Errors.Add($"{tag} 섹션 헤더 전에 내용이 나옴: '{raw}'");
                    return null;
                }
                current.Add(raw.TrimEnd());
            }

            if (!sawRoomHeader) { result.Errors.Add($"{tag} '# room:' 헤더가 없습니다."); return null; }
            if (tiles.Count == 0) { result.Errors.Add($"{tag} [TILES] 섹션이 없거나 비었습니다."); return null; }
            if (objects.Count == 0) { result.Errors.Add($"{tag} [OBJECTS] 섹션이 없거나 비었습니다."); return null; }

            if (!CheckSize(tiles, "[TILES]", roomWidth, roomHeight, tag, result)) return null;
            if (!CheckSize(objects, "[OBJECTS]", roomWidth, roomHeight, tag, result)) return null;
            if (links.Count > 0 && !CheckSize(links, "[LINKS]", roomWidth, roomHeight, tag, result)) return null;

            template.Width = roomWidth;
            template.Height = roomHeight;
            template.Tiles = ToGrid(tiles, roomHeight, roomWidth);
            template.Objects = ToGrid(objects, roomHeight, roomWidth);
            template.Links = links.Count > 0 ? ToGrid(links, roomHeight, roomWidth) : FilledDots(roomHeight, roomWidth);
            return template;
        }

        // "# room: LR" / "# weight: 3" 처리. 알 수 없는 키는 주석으로 무시.
        private static bool ParseMeta(
            string line, DungeonRoomTemplate template, ref bool sawRoomHeader,
            string tag, RoomTemplateParseResult result)
        {
            string body = line.TrimStart('#').Trim();
            int colon = body.IndexOf(':');
            if (colon < 0) return true; // 순수 주석

            string key = body.Substring(0, colon).Trim().ToLowerInvariant();
            string val = body.Substring(colon + 1).Trim();

            if (key == "room")
            {
                sawRoomHeader = true;
                return ParseRoomHeader(val, template, tag, result);
            }
            if (key == "weight")
            {
                if (!int.TryParse(val, out int w) || w <= 0)
                {
                    result.Errors.Add($"{tag} weight는 1 이상의 정수여야 합니다: '{val}'");
                    return false;
                }
                template.Weight = w;
            }
            return true;
        }

        // "LR" / "LRUD" → Opens (구 ROLE 토큰 START/END/FILL은 폐지, 남아 있으면 무시)
        private static bool ParseRoomHeader(string val, DungeonRoomTemplate template, string tag, RoomTemplateParseResult result)
        {
            string[] parts = val.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            string opensText = "";

            foreach (string p in parts)
            {
                string token = p.ToUpperInvariant();

                // ROLE 표기는 폐지됐다(START/END는 후처리 배치, FILL은 암반). 남아 있으면 무시한다.
                if (token == "START" || token == "END" || token == "FILL")
                {
                    result.Warnings.Add($"{tag} '# room:'의 '{token}' 표기는 더 이상 쓰이지 않아 무시합니다.");
                    continue;
                }
                if (token == "NORMAL") continue;

                opensText += p;
            }

            if (!RoomTypeUtil.TryParseOpens(opensText, out var opens))
            {
                result.Errors.Add($"{tag} 알 수 없는 방 타입 표기: '{val}' (OPENS=L/R/U/D 조합)");
                return false;
            }
            template.Opens = opens;
            return true;
        }

        private static bool CheckSize(
            List<string> rows, string section, int roomWidth, int roomHeight,
            string tag, RoomTemplateParseResult result)
        {
            if (rows.Count != roomHeight)
            {
                result.Errors.Add($"{tag} {section} 행 수가 {rows.Count}인데 방 높이는 {roomHeight}입니다.");
                return false;
            }
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r].Length != roomWidth)
                {
                    result.Errors.Add($"{tag} {section} {r}행 길이가 {rows[r].Length}인데 방 너비는 {roomWidth}입니다.");
                    return false;
                }
            }
            return true;
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
