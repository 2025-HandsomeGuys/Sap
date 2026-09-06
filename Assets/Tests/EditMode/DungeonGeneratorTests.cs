using NUnit.Framework;
using UnityEngine;
using Gameplay.Dungeon.Authoring;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonGeneratorTests
{
    // 4x4 방 3종 — R / LR / L
    private const string Rooms =
        "# room: R\n" +
        "[TILES]\nWWWW\nW..W\nW...\nWWWW\n" +
        "[OBJECTS]\n....\n....\n....\n....\n" +
        "---\n" +
        "# room: LR\n" +
        "[TILES]\nWWWW\nW00W\n....\nWWWW\n" +
        "[OBJECTS]\n....\n....\n....\n....\n" +
        "---\n" +
        "# room: L\n" +
        "[TILES]\nWWWW\nW..W\n...W\nWWWW\n" +
        "[OBJECTS]\n....\n....\n....\n....\n";

    private DungeonGenPresetSO _preset;
    private TextAsset _roomAsset;
    private TextAsset _shapeAsset;

    [SetUp]
    public void SetUp()
    {
        _roomAsset = new TextAsset(Rooms);
        _shapeAsset = new TextAsset("# shape: 테스트\nS.X\n");

        _preset = ScriptableObject.CreateInstance<DungeonGenPresetSO>();
        _preset.name = "TestPreset";
        _preset.roomWidth = 4;
        _preset.roomHeight = 4;
        _preset.extraConnectionChance = 0f;
        _preset.roomTemplateFiles.Add(_roomAsset);
        _preset.shapeFiles.Add(_shapeAsset);
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_preset);
        Object.DestroyImmediate(_roomAsset);
        Object.DestroyImmediate(_shapeAsset);
    }

    [Test]
    public void Generate_NullPreset_Fails()
    {
        var r = DungeonGenerator.Generate(null, 1);
        Assert.IsFalse(r.Success);
        Assert.IsTrue(r.Errors.Count > 0);
    }

    [Test]
    public void Generate_NoTemplateFiles_Fails()
    {
        _preset.roomTemplateFiles.Clear();
        var r = DungeonGenerator.Generate(_preset, 1);
        Assert.IsFalse(r.Success);
        Assert.IsTrue(r.Errors.Count > 0);
    }

    [Test]
    public void Generate_NoShapeFiles_Fails()
    {
        _preset.shapeFiles.Clear();
        var r = DungeonGenerator.Generate(_preset, 1);
        Assert.IsFalse(r.Success);
        Assert.IsTrue(r.Errors.Count > 0);
    }

    [Test]
    public void Generate_BadTemplateSize_Fails()
    {
        _preset.roomWidth = 5; // 템플릿은 4칸
        var r = DungeonGenerator.Generate(_preset, 1);
        Assert.IsFalse(r.Success);
    }

    [Test]
    public void Generate_ProducesMapWithBorderSizeAndName()
    {
        var r = DungeonGenerator.Generate(_preset, 12345);
        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));
        Assert.AreEqual(3 * 4 + 2, r.Data.Width);
        Assert.AreEqual(1 * 4 + 2, r.Data.Height);
        Assert.AreEqual("gen_12345", r.Data.Name);
        Assert.AreEqual(12345, r.Seed);
    }

    [Test]
    public void Generate_PlacesExactlyOneEntryAndOneExit()
    {
        var r = DungeonGenerator.Generate(_preset, 7);
        Assert.IsTrue(r.Success, string.Join("; ", r.Errors));

        int e = 0, x = 0;
        for (int row = 0; row < r.Data.Height; row++)
            for (int col = 0; col < r.Data.Width; col++)
            {
                if (r.Data.Objects[row, col] == 'E') e++;
                if (r.Data.Objects[row, col] == 'X') x++;
            }
        Assert.AreEqual(1, e, "E는 정확히 1개");
        Assert.AreEqual(1, x, "X는 정확히 1개");
    }

    [Test]
    public void Generate_SameSeed_SameMap()
    {
        var a = DungeonGenerator.Generate(_preset, 999);
        var b = DungeonGenerator.Generate(_preset, 999);
        Assert.AreEqual(DungeonMapWriter.Write(a.Data, "p", 999),
                        DungeonMapWriter.Write(b.Data, "p", 999));
    }

    [Test]
    public void Write_RoundTripsThroughExistingParser()
    {
        var gen = DungeonGenerator.Generate(_preset, 55);
        Assert.IsTrue(gen.Success, string.Join("; ", gen.Errors));

        string text = DungeonMapWriter.Write(gen.Data, "TestPreset", 55);
        var parsed = DungeonMapParser.Parse(text);

        Assert.IsTrue(parsed.Success, string.Join("; ", parsed.Errors));
        Assert.AreEqual(gen.Data.Width, parsed.Data.Width);
        Assert.AreEqual(gen.Data.Height, parsed.Data.Height);
        Assert.AreEqual("gen_55", parsed.Data.Name);

        for (int r = 0; r < gen.Data.Height; r++)
            for (int c = 0; c < gen.Data.Width; c++)
            {
                Assert.AreEqual(gen.Data.Tiles[r, c], parsed.Data.Tiles[r, c], $"Tiles({r},{c})");
                Assert.AreEqual(gen.Data.Objects[r, c], parsed.Data.Objects[r, c], $"Objects({r},{c})");
                Assert.AreEqual(gen.Data.Links[r, c], parsed.Data.Links[r, c], $"Links({r},{c})");
            }
    }

    [Test]
    public void Write_IncludesSeedAndPresetMeta()
    {
        var gen = DungeonGenerator.Generate(_preset, 55);
        string text = DungeonMapWriter.Write(gen.Data, "TestPreset", 55);
        StringAssert.Contains("# seed: 55", text);
        StringAssert.Contains("# preset: TestPreset", text);
        StringAssert.Contains("[TILES]", text);
        StringAssert.Contains("[OBJECTS]", text);
        StringAssert.Contains("[LINKS]", text);
    }
}
