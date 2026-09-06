// @tags: upgrade, tree, csv, reader, data, generator
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// UpgradeTree.csv를 읽어 UpgradeNodeData 목록으로 만든다.
///
/// 이 CSV가 업그레이드 트리의 단일 원본이다. 에디터 생성기·가격 익스포터·
/// 파이썬 도구가 모두 여기서 읽는다.
/// 설계: Assets/Docs/superpowers/specs/2026-08-21-upgrade-tree-autogen-design.md §4
///
/// 잘못된 행은 버리고 errors에 사유를 남긴다 — 한 행이 깨졌다고 트리 전체를
/// 못 읽으면 생성기가 아무것도 못 하고, 무엇이 잘못됐는지도 안 보인다.
/// </summary>
public static class UpgradeTreeCsvReader
{
    public const string DefaultPath = "Assets/GameData/UpgradeData/UpgradeTree.csv";

    public static List<UpgradeNodeData> Read(string filePath, List<string> errors = null)
        => FromRows(CsvParser.Parse(filePath), errors);

    public static List<UpgradeNodeData> ReadText(string csvText, List<string> errors = null)
        => FromRows(CsvParser.ParseText(csvText), errors);

    private static List<UpgradeNodeData> FromRows(
        List<Dictionary<string, string>> rows, List<string> errors)
    {
        var list = new List<UpgradeNodeData>();
        var seen = new HashSet<string>();

        foreach (var row in rows)
        {
            string id = Get(row, "nodeId").Trim();
            if (string.IsNullOrEmpty(id)) continue;

            if (!seen.Add(id))
            {
                Err(errors, $"{id}: nodeId 중복 — 뒤의 행을 버린다");
                continue;
            }

            string typeText = Get(row, "effectType").Trim();
            if (!System.Enum.TryParse(typeText, out UpgradeEffectType effType))
            {
                Err(errors, $"{id}: 모르는 effectType '{typeText}'");
                continue;
            }

            if (!TryInt(row, "tier", out int tier) ||
                !TryInt(row, "cost", out int cost) ||
                !TryFloat(row, "effectValue", out float effVal) ||
                !TryFloat(row, "uiX", out float uiX) ||
                !TryFloat(row, "uiY", out float uiY))
            {
                Err(errors, $"{id}: 숫자 칸을 읽을 수 없다 (tier/cost/effectValue/uiX/uiY)");
                continue;
            }

            var node = new UpgradeNodeData(
                id,
                Get(row, "displayNameKey"),
                Get(row, "descriptionKey"),
                tier,
                cost,
                new Vector2(uiX, uiY),
                ParseParents(Get(row, "parentIds")),
                effType,
                effVal,
                ParseBool(Get(row, "isPercentage")),
                ParseBool(Get(row, "locked")),
                ParseMineral(Get(row, "targetMineral"), id, errors),
                ParseExtraMinerals(Get(row, "targetMineral"), id, errors));

            node.lineBends = ParseLineBends(Get(row, "lineBends"), id, errors);
            node.iconPath = Get(row, "icon").Trim();
            list.Add(node);
        }

        return list;
    }

    /// <summary>
    /// `lineBends` 칸 — 사람이 지정한 연결선 모양. `부모ID=x:y|x:y;부모ID=...` 형태다.
    /// x는 건너가서 탈 레인(uiX), y는 건너가는 높이(uiY).
    ///
    /// 이 칸이 아예 없는 구버전 CSV도 그대로 읽힌다(Get이 빈 문자열을 돌려준다) —
    /// 열 이름으로 찾으므로 열 순서가 바뀌어도 안전하다.
    /// 깨진 항목만 버리고 나머지는 살린다. 선 하나가 자동으로 돌아갈 뿐이라
    /// 행 전체를 버리는 것보다 낫다.
    /// </summary>
    private static UpgradeLineBend[] ParseLineBends(string text, string nodeId, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text)) return System.Array.Empty<UpgradeLineBend>();

        var list = new List<UpgradeLineBend>();
        foreach (string entry in text.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = entry.IndexOf('=');
            if (eq <= 0)
            {
                Err(errors, $"{nodeId}: lineBends '{entry}' — '부모ID=x:y|x:y' 형태여야 한다");
                continue;
            }

            string parent = entry.Substring(0, eq).Trim();
            if (parent.Length == 0) { Err(errors, $"{nodeId}: lineBends에 부모 id가 비었다"); continue; }

            var points = new List<Vector2>();
            bool ok = true;
            foreach (string pt in entry.Substring(eq + 1)
                         .Split(new[] { '|' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = pt.IndexOf(':');
                if (colon <= 0 ||
                    !float.TryParse(pt.Substring(0, colon), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(pt.Substring(colon + 1), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out float y))
                {
                    Err(errors, $"{nodeId}: lineBends 꺾임점 '{pt}'를 읽을 수 없다 ('x:y' 형태)");
                    ok = false;
                    break;
                }
                points.Add(new Vector2(x, y));
            }

            if (ok && points.Count > 0) list.Add(new UpgradeLineBend(parent, points));
        }
        return list.ToArray();
    }

    private static string Get(Dictionary<string, string> row, string key)
        => row.TryGetValue(key, out string v) ? v : "";

    private static void Err(List<string> errors, string message)
    {
        errors?.Add(message);
        Debug.LogWarning($"[UpgradeTreeCsvReader] {message}");
    }

    /// <summary>
    /// 선행 노드는 세미콜론으로 나눈다 — 쉼표는 CSV 구분자라 쓸 수 없다.
    /// 빈 칸은 null이 아니라 빈 배열이다. 생성기가 Length로 루트 노드를 판정하므로
    /// null을 돌려주면 NullReference 대신 조용한 분기 차이가 생긴다.
    /// </summary>
    private static string[] ParseParents(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new string[0];

        var parts = text.Split(';');
        var list = new List<string>(parts.Length);
        foreach (string p in parts)
        {
            string t = p.Trim();
            if (t.Length > 0) list.Add(t);
        }
        return list.ToArray();
    }

    /// <summary>
    /// targetMineral 칸. 비어 있으면 None(대부분의 노드가 그렇다).
    /// 오타는 조용히 넘기지 않는다 — None이 되면 그 노드가 아무 일도 안 하는데
    /// 에러도 안 나서 추적이 어렵다.
    /// </summary>
    private static MineralID ParseMineral(string text, string nodeId, List<string> errors)
    {
        string[] parts = SplitMinerals(text);
        return parts.Length == 0 ? MineralID.None : ParseOne(parts[0], nodeId, errors);
    }

    /// <summary>두 번째 이후 대상. 한 노드가 광물 여러 개의 값을 함께 올릴 때 쓴다.</summary>
    private static MineralID[] ParseExtraMinerals(string text, string nodeId, List<string> errors)
    {
        string[] parts = SplitMinerals(text);
        if (parts.Length <= 1) return System.Array.Empty<MineralID>();

        var list = new List<MineralID>(parts.Length - 1);
        for (int i = 1; i < parts.Length; i++)
        {
            MineralID id = ParseOne(parts[i], nodeId, errors);
            if (id != MineralID.None) list.Add(id);
        }
        return list.ToArray();
    }

    private static string[] SplitMinerals(string text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return System.Array.Empty<string>();
        return text.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
    }

    private static MineralID ParseOne(string text, string nodeId, List<string> errors)
    {
        text = text.Trim();
        if (text.Length == 0) return MineralID.None;
        if (System.Enum.TryParse(text, out MineralID id)) return id;

        Err(errors, $"{nodeId}: 모르는 targetMineral '{text}'");
        return MineralID.None;
    }

    private static bool ParseBool(string text)
        => !string.IsNullOrWhiteSpace(text) &&
           text.Trim().Equals("true", System.StringComparison.OrdinalIgnoreCase);

    // 숫자는 반드시 InvariantCulture로 읽는다. CurrentCulture로 읽으면
    // 소수점 규칙이 다른 로케일에서 1.12가 112가 되거나 파싱이 실패한다.
    private static bool TryInt(Dictionary<string, string> row, string key, out int value)
        => int.TryParse(Get(row, key).Trim(), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out value);

    private static bool TryFloat(Dictionary<string, string> row, string key, out float value)
        => float.TryParse(Get(row, key).Trim(), NumberStyles.Float,
                          CultureInfo.InvariantCulture, out value);
}
