using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using Gameplay.Dungeon.Authoring;
using Gameplay.Dungeon.Authoring.Generation;

/// <summary>
/// 실제 에셋(DungeonGenPreset.asset → basic_shapes.txt + cave_rooms.txt)으로 생성을 끝까지 돌린다.
///
/// 다른 테스트들은 파일을 '파싱'만 해서 계약을 본다. 그 조합으로 실제 맵을 만들어 보지 않으면
/// 템플릿 세트의 구멍(개구부 조합 누락 → 상위집합 폴백, 바닥 없는 방 → E/X 강제 배치 같은 것)이
/// 조용히 통과한다. 생성기가 남기는 경고가 그 구멍의 신호라 여기서 0건을 요구한다.
///
/// UnityEditor(AssetDatabase)를 쓰므로 EditMode 테스트다.
/// </summary>
public class DungeonGenerationEndToEndTests
{
    private const string PresetPath = "Assets/DungeonMaps/DungeonGenPreset.asset";
    private const string TilesetPath = "Assets/GameData/Dungeon/DefaultDungeonTileset.asset";

    private const int FirstSeed = 1;
    private const int LastSeed = 50;

    // 이 문구가 뜨면 템플릿 세트에 구멍이 있다는 뜻이다.
    //   "템플릿이 없어"      — DungeonRoomComposer: 개구부 조합 누락 → 상위집합 폴백/빈 방
    //   "강제로 만들었습니다" — DungeonEntryPlacer: 방에 바닥이 없어 E/X 자리를 뚫음
    private static readonly string[] FatalWarningFragments =
    {
        "템플릿이 없어",
        "강제로 만들었습니다",
    };

    private static DungeonGenPresetSO LoadPreset()
    {
        var preset = AssetDatabase.LoadAssetAtPath<DungeonGenPresetSO>(PresetPath);
        Assert.IsNotNull(preset, $"{PresetPath} 를 찾지 못했습니다.");
        preset.BuildLookup();
        return preset;
    }

    private static DungeonTilesetSO LoadTileset()
    {
        var tileset = AssetDatabase.LoadAssetAtPath<DungeonTilesetSO>(TilesetPath);
        Assert.IsNotNull(tileset, $"{TilesetPath} 를 찾지 못했습니다.");
        tileset.BuildLookup();
        return tileset;
    }

    [Test]
    public void RealAssets_AllSeedsGenerateSuccessfully()
    {
        var preset = LoadPreset();
        var failures = new List<string>();

        for (int seed = FirstSeed; seed <= LastSeed; seed++)
        {
            var r = DungeonGenerator.Generate(preset, seed);
            if (!r.Success)
                failures.Add($"seed {seed}: {string.Join(" / ", r.Errors)}");
            else if (r.Data == null)
                failures.Add($"seed {seed}: Success인데 Data가 null");
        }

        Assert.IsEmpty(failures, "생성 실패:\n" + string.Join("\n", failures));
    }

    [Test]
    public void RealAssets_NoTemplateGapWarnings()
    {
        var preset = LoadPreset();
        var offenders = new List<string>();

        for (int seed = FirstSeed; seed <= LastSeed; seed++)
        {
            var r = DungeonGenerator.Generate(preset, seed);

            foreach (var w in r.Warnings)
                foreach (var fragment in FatalWarningFragments)
                    if (w.Contains(fragment))
                        offenders.Add($"seed {seed}: {w}");
        }

        Assert.IsEmpty(offenders,
            "템플릿 세트에 구멍이 있습니다 — 개구부 조합이 빠졌거나 바닥 없는 방이 있습니다:\n"
            + string.Join("\n", offenders));
    }

    [Test]
    public void RealAssets_AllObjectSymbolsAreMappedInTileset()
    {
        var preset = LoadPreset();
        var tileset = LoadTileset();
        var unmapped = new Dictionary<char, string>();

        for (int seed = FirstSeed; seed <= LastSeed; seed++)
        {
            var r = DungeonGenerator.Generate(preset, seed);
            if (!r.Success || r.Data == null) continue;

            var objects = r.Data.Objects;
            for (int row = 0; row < r.Data.Height; row++)
            for (int col = 0; col < r.Data.Width; col++)
            {
                char o = objects[row, col];
                if (o == '.') continue;
                if (tileset.TryGetObject(o, out _)) continue;
                if (!unmapped.ContainsKey(o))
                    unmapped[o] = $"'{o}' (seed {seed}, ({row},{col}))";
            }
        }

        if (unmapped.Count > 0)
        {
            var sb = new StringBuilder("타일셋에 없는 오브젝트 심볼이 최종 격자에 남았습니다 — 임포터가 경고만 남기고 조용히 빠뜨립니다:\n");
            foreach (var kv in unmapped) sb.AppendLine("  " + kv.Value);
            Assert.Fail(sb.ToString());
        }
    }

    [Test]
    public void RealAssets_ExactlyOneEntryAndOneExit()
    {
        var preset = LoadPreset();
        var offenders = new List<string>();

        for (int seed = FirstSeed; seed <= LastSeed; seed++)
        {
            var r = DungeonGenerator.Generate(preset, seed);
            if (!r.Success || r.Data == null) continue;

            int entries = 0, exits = 0;
            for (int row = 0; row < r.Data.Height; row++)
            for (int col = 0; col < r.Data.Width; col++)
            {
                char o = r.Data.Objects[row, col];
                if (o == 'E') entries++;
                else if (o == 'X') exits++;
            }

            if (entries != 1 || exits != 1)
                offenders.Add($"seed {seed}: E {entries}개 / X {exits}개");
        }

        Assert.IsEmpty(offenders, "입구·출구 개수가 1개가 아닙니다:\n" + string.Join("\n", offenders));
    }
}
