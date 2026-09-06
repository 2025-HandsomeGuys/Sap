using NUnit.Framework;
using UnityEngine;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonCavityFillerTests
{
    private DungeonGenPresetSO _preset;

    [SetUp]
    public void SetUp() => _preset = ScriptableObject.CreateInstance<DungeonGenPresetSO>();

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_preset);

    private static ComposeResult Make(string[] tileRows, string[] objectRows)
    {
        int h = tileRows.Length, w = tileRows[0].Length;
        var res = new ComposeResult
        {
            Width = w, Height = h,
            Tiles = new char[h, w], Objects = new char[h, w], Links = new char[h, w],
        };
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                res.Tiles[r, c] = tileRows[r][c];
                res.Objects[r, c] = objectRows[r][c];
                res.Links[r, c] = '.';
            }
        return res;
    }

    [Test]
    public void Fill_SealedCavity_BecomesWall()
    {
        var res = Make(
            new[] { "WWWWWW",
                    "W..WWW",
                    "W.WW.W",   // (2,4)는 벽으로 둘러싸인 공동
                    "WWWWWW" },
            new[] { "......",
                    "......",
                    ".E....",
                    "......" });

        int filled = DungeonCavityFiller.Fill(res, _preset, out _);

        Assert.AreEqual('W', res.Tiles[2, 4], "밀봉 공동이 메워지지 않았다");
        Assert.AreEqual(1, filled);
    }

    [Test]
    public void Fill_ReachableSpace_Untouched()
    {
        var res = Make(
            new[] { "WWWWWW",
                    "W....W",
                    "W....W",
                    "WWWWWW" },
            new[] { "......",
                    "......",
                    ".E....",
                    "......" });

        int filled = DungeonCavityFiller.Fill(res, _preset, out _);

        Assert.AreEqual(0, filled);
        for (int c = 1; c <= 4; c++)
            Assert.AreEqual('.', res.Tiles[1, c]);
    }

    [Test]
    public void Fill_ExitReachable_ReportsTrue()
    {
        var res = Make(
            new[] { "WWWWWW",
                    "W....W",
                    "WWWWWW" },
            new[] { "......",
                    ".E..X.",
                    "......" });

        DungeonCavityFiller.Fill(res, _preset, out bool reachable);
        Assert.IsTrue(reachable);
    }

    [Test]
    public void Fill_ExitSealedOff_ReportsFalse()
    {
        var res = Make(
            new[] { "WWWWWW",
                    "W.W..W",   // 가운데 벽으로 갈라짐
                    "WWWWWW" },
            new[] { "......",
                    ".E..X.",
                    "......" });

        DungeonCavityFiller.Fill(res, _preset, out bool reachable);
        Assert.IsFalse(reachable);
    }

    // 메운 칸에 오브젝트가 남아 있으면 벽 속에 프리팹이 박힌다.
    [Test]
    public void Fill_ClearsObjectsInFilledCells()
    {
        var res = Make(
            new[] { "WWWWWW",
                    "W.WW.W",
                    "W.WW.W",
                    "WWWWWW" },
            new[] { "......",
                    "......",
                    ".E..^.",
                    "......" });

        DungeonCavityFiller.Fill(res, _preset, out _);

        Assert.AreEqual('.', res.Objects[2, 4], "메운 칸의 오브젝트가 남아 있다");
    }
}
