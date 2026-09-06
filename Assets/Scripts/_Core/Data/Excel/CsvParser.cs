// @tags: csv, parser, excel, data, localization, utility
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 범용 CSV 파서. RFC 4180 준수.
/// 따옴표 내부 쉼표/줄바꿈 처리, BOM 자동 제거.
/// </summary>
public static class CsvParser
{
    /// <summary>
    /// CSV 파일을 파싱하여 행 단위 Dictionary 리스트로 반환.
    /// 첫 행은 헤더로 사용됩니다.
    /// </summary>
    public static List<Dictionary<string, string>> Parse(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"[CsvParser] 파일을 찾을 수 없습니다: {filePath}");
            return new List<Dictionary<string, string>>();
        }

        string text = File.ReadAllText(filePath, Encoding.UTF8);
        return ParseText(text);
    }

    /// <summary>
    /// CSV 텍스트 문자열을 직접 파싱
    /// </summary>
    public static List<Dictionary<string, string>> ParseText(string csvText)
    {
        var result = new List<Dictionary<string, string>>();
        if (string.IsNullOrEmpty(csvText)) return result;

        // BOM 제거
        if (csvText.Length > 0 && csvText[0] == '\uFEFF')
            csvText = csvText.Substring(1);

        var rows = ParseRows(csvText);
        if (rows.Count < 2) return result; // 헤더만 있거나 빈 파일

        // 첫 행을 헤더로 사용
        var headers = rows[0];
        for (int i = 0; i < headers.Count; i++)
            headers[i] = headers[i].Trim();

        // 데이터 행 처리
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r];

            // 빈 행 스킵 (모든 필드가 비어있는 경우)
            bool allEmpty = true;
            for (int c = 0; c < row.Count; c++)
            {
                if (!string.IsNullOrWhiteSpace(row[c]))
                {
                    allEmpty = false;
                    break;
                }
            }
            if (allEmpty) continue;

            var dict = new Dictionary<string, string>();
            for (int c = 0; c < headers.Count; c++)
            {
                string key = headers[c];
                string value = c < row.Count ? row[c] : "";
                if (!dict.ContainsKey(key))
                    dict[key] = value;
            }
            result.Add(dict);
        }

        return result;
    }

    /// <summary>
    /// CSV 텍스트를 행/열 단위로 파싱 (RFC 4180 준수)
    /// </summary>
    private static List<List<string>> ParseRows(string text)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // 다음 문자도 따옴표면 이스케이프된 따옴표
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                    }
                    else
                    {
                        // 따옴표 종료
                        inQuotes = false;
                        i++;
                    }
                }
                else
                {
                    field.Append(c);
                    i++;
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                    i++;
                }
                else if (c == ',')
                {
                    currentRow.Add(field.ToString());
                    field.Clear();
                    i++;
                }
                else if (c == '\r')
                {
                    // \r\n 또는 \r 단독 줄바꿈
                    currentRow.Add(field.ToString());
                    field.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();

                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i += 2;
                    else
                        i++;
                }
                else if (c == '\n')
                {
                    currentRow.Add(field.ToString());
                    field.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();
                    i++;
                }
                else
                {
                    field.Append(c);
                    i++;
                }
            }
        }

        // 마지막 필드/행 처리
        if (field.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(field.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }
}
