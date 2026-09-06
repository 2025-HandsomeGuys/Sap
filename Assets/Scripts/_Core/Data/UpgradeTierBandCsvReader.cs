// @tags: upgrade, tree, tier, band, csv, reader, data
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// UpgradeTierBands.csv를 읽어 "지층 띠의 윗변 uiY"를 돌려준다.
///
/// 왜 별도 파일인가
/// ----------------
/// 지층을 가르는 선은 원래 <see cref="UpgradeOverlayUI"/>가 **자동으로** 잡았다 —
/// "앞 지층의 맨 위 노드와 다음 지층의 맨 아래 노드의 중간". 두 지층의 uiY가
/// 안 겹칠 때는 맞는 규칙이지만, 겹치는 순간(한 노드만 엇갈려도) 선이 양쪽
/// 노드를 가로지른다. 그래서 손으로 정할 길을 하나 열어 둔다.
///
/// 값이 비어 있으면 예전 그대로 자동 계산이다. 파일이 통째로 없어도 마찬가지 —
/// 이 파일은 **덮어쓰기 창구**일 뿐 트리의 원본이 아니다.
/// UpgradeTree.csv에 열로 붙이지 않은 이유: 그건 한 줄이 한 노드인데
/// 이 값은 지층당 하나라, 붙이면 62줄에 같은 값을 베껴 쓰게 된다.
///
/// 쓰는 쪽: Tools/upgrade_tree_editor.html (경계선을 끌어 옮기면 여기 적힌다)
/// 읽는 쪽: UpgradeTreeGenerator → TierInfo.bandTop → UpgradeOverlayUI
/// </summary>
public static class UpgradeTierBandCsvReader
{
    public const string DefaultPath = "Assets/GameData/UpgradeData/UpgradeTierBands.csv";

    /// <summary>지층 -> 띠 윗변 uiY. 비어 있거나 못 읽은 지층은 아예 안 담는다.</summary>
    public static Dictionary<int, float> Read(string filePath, List<string> errors = null)
    {
        var map = new Dictionary<int, float>();

        List<Dictionary<string, string>> rows;
        try
        {
            rows = CsvParser.Parse(filePath);
        }
        catch (System.Exception e)
        {
            // 파일이 없는 건 잘못이 아니다 — 전부 자동으로 돌면 된다.
            errors?.Add($"{filePath}: {e.Message}");
            return map;
        }

        foreach (var row in rows)
        {
            if (!row.TryGetValue("tier", out string tierText) ||
                !int.TryParse((tierText ?? "").Trim(), out int tier))
                continue;

            row.TryGetValue("bandTop", out string topText);
            topText = (topText ?? "").Trim();
            if (topText.Length == 0) continue;              // 빈 칸 = 자동

            if (!float.TryParse(topText, NumberStyles.Float, CultureInfo.InvariantCulture,
                                out float top))
            {
                errors?.Add($"지층 {tier}: bandTop이 숫자가 아니다 ('{topText}')");
                continue;
            }
            map[tier] = top;
        }
        return map;
    }
}
