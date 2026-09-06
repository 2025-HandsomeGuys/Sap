// @tags: dungeon, generation, shape, mask, parser
using System.Collections.Generic;

namespace Gameplay.Dungeon.Authoring.Generation
{
    public class ShapeParseResult
    {
        public bool Success;
        public readonly List<DungeonShapeMask> Masks = new List<DungeonShapeMask>();
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// 형태 마스크 파일을 파싱한다. 파일 하나에 형태 여러 개, '---' 한 줄로 구분.
    ///   # shape: &lt;이름&gt;      경고 메시지용
    ///   # weight: N            형태 추첨 가중치 (생략 시 1)
    ///   격자: S=시작 X=끝 .=던전 칸 #=암반
    ///
    /// '#'은 주석 시작 문자이면서 암반 칸 문자이기도 하다. **'#' 뒤에 공백이 오면 주석**으로 본다 —
    /// 격자 행에는 공백이 들어갈 일이 없어 충돌하지 않는다.
    /// </summary>
    public static class DungeonShapeParser
    {
        public static ShapeParseResult Parse(string text, string sourceName)
        {
            var result = new ShapeParseResult();
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
                    var m = ParseBlock(block, sourceName, blockIndex, blockStartLine, result);
                    if (m != null) result.Masks.Add(m);
                    blockIndex++;
                }
                block.Clear();
                blockStartLine = i + 2;
            }

            if (result.Masks.Count == 0 && result.Errors.Count == 0)
                result.Errors.Add($"[{sourceName}] 형태를 하나도 찾지 못했습니다.");

            result.Success = result.Errors.Count == 0;
            return result;
        }

        private static bool HasContent(List<string> block)
        {
            foreach (string l in block)
                if (l.Trim().Length > 0) return true;
            return false;
        }

        private static bool IsComment(string line)
        {
            string t = line.TrimStart();
            if (t.Length == 0 || t[0] != '#') return false;
            return t.Length == 1 || char.IsWhiteSpace(t[1]);
        }

        private static DungeonShapeMask ParseBlock(
            List<string> block, string sourceName, int blockIndex, int startLine, ShapeParseResult result)
        {
            string tag = $"[{sourceName} #{blockIndex} (line {startLine})]";

            var mask = new DungeonShapeMask();
            var rows = new List<string>();

            foreach (string raw in block)
            {
                string line = raw.TrimEnd();
                if (line.Trim().Length == 0) continue;

                if (IsComment(line))
                {
                    string body = line.TrimStart().TrimStart('#').Trim();
                    if (body.StartsWith("shape:"))
                        mask.Name = body.Substring("shape:".Length).Trim();
                    else if (body.StartsWith("weight:"))
                    {
                        if (int.TryParse(body.Substring("weight:".Length).Trim(), out int w) && w > 0)
                            mask.Weight = w;
                        else
                            result.Warnings.Add($"{tag} weight 값을 읽지 못해 1로 둡니다.");
                    }
                    continue;
                }

                rows.Add(line);
            }

            if (rows.Count == 0)
            {
                result.Errors.Add($"{tag} 격자가 비어 있습니다.");
                return null;
            }

            int width = rows[0].Length;
            for (int r = 0; r < rows.Count; r++)
                if (rows[r].Length != width)
                {
                    result.Errors.Add($"{tag} {r}행 길이가 {rows[r].Length}입니다 — 모든 행이 {width}이어야 합니다.");
                    return null;
                }

            mask.Width = width;
            mask.Height = rows.Count;
            mask.Cells = new char[mask.Height, mask.Width];

            int starts = 0, ends = 0;
            for (int r = 0; r < mask.Height; r++)
                for (int c = 0; c < mask.Width; c++)
                {
                    char ch = rows[r][c];
                    switch (ch)
                    {
                        case DungeonShapeMask.DungeonCell:
                        case DungeonShapeMask.RockCell:
                            break;
                        case DungeonShapeMask.StartCell:
                            starts++; mask.StartRow = r; mask.StartCol = c; break;
                        case DungeonShapeMask.EndCell:
                            ends++; mask.EndRow = r; mask.EndCol = c; break;
                        default:
                            result.Errors.Add($"{tag} ({r},{c})에 알 수 없는 문자 '{ch}' — S/X/./# 만 쓸 수 있습니다.");
                            return null;
                    }
                    mask.Cells[r, c] = ch;
                }

            if (starts != 1) { result.Errors.Add($"{tag} S가 {starts}개입니다 — 정확히 1개여야 합니다."); return null; }
            if (ends != 1) { result.Errors.Add($"{tag} X가 {ends}개입니다 — 정확히 1개여야 합니다."); return null; }

            if (!DemoteDisconnected(mask, tag, result)) return null;
            return mask;
        }

        /// <summary>
        /// S에서 4방향으로 닿는 칸만 남기고 나머지 던전 칸은 암반으로 강등한다.
        /// X에 못 닿으면 형태 자체가 틀린 것이므로 에러.
        /// </summary>
        private static bool DemoteDisconnected(DungeonShapeMask mask, string tag, ShapeParseResult result)
        {
            var reached = new bool[mask.Height, mask.Width];
            var queue = new Queue<(int row, int col)>();

            reached[mask.StartRow, mask.StartCol] = true;
            queue.Enqueue((mask.StartRow, mask.StartCol));

            int[] dr = { 0, 0, -1, 1 };
            int[] dc = { -1, 1, 0, 0 };

            while (queue.Count > 0)
            {
                var (r, c) = queue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    int nr = r + dr[i], nc = c + dc[i];
                    if (!mask.IsDungeon(nr, nc) || reached[nr, nc]) continue;
                    reached[nr, nc] = true;
                    queue.Enqueue((nr, nc));
                }
            }

            if (!reached[mask.EndRow, mask.EndCol])
            {
                result.Errors.Add($"{tag} S에서 X까지 이어지지 않습니다.");
                return false;
            }

            int demoted = 0;
            for (int r = 0; r < mask.Height; r++)
                for (int c = 0; c < mask.Width; c++)
                    if (mask.IsDungeon(r, c) && !reached[r, c])
                    {
                        mask.Cells[r, c] = DungeonShapeMask.RockCell;
                        demoted++;
                    }

            if (demoted > 0)
                result.Warnings.Add($"{tag} S와 이어지지 않은 칸 {demoted}개를 암반으로 바꿨습니다.");

            return true;
        }
    }
}
