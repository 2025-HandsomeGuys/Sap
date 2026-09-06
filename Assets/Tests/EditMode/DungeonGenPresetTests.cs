using NUnit.Framework;
using UnityEngine;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonGenPresetTests
{
    private DungeonGenPresetSO _preset;

    [SetUp]
    public void SetUp() => _preset = ScriptableObject.CreateInstance<DungeonGenPresetSO>();

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_preset);

    [Test]
    public void Defaults_AreHorizontalDungeonSized()
    {
        Assert.AreEqual(10, _preset.roomWidth);
        Assert.AreEqual(6, _preset.roomHeight);
        Assert.IsTrue(_preset.forceCarveBoundaries);
    }

    [Test]
    public void SymbolProperties_ReadFirstCharOfString()
    {
        Assert.AreEqual('W', _preset.Wall);
        Assert.AreEqual('.', _preset.Empty);
    }

    [Test]
    public void SymbolProperties_EmptyStringFallsBack()
    {
        _preset.wallSymbol = "";
        _preset.emptySymbol = null;
        Assert.AreEqual('W', _preset.Wall);
        Assert.AreEqual('.', _preset.Empty);
    }

    [Test]
    public void TryGetChanceTile_DefaultTableHasZeroOneTwo()
    {
        Assert.IsTrue(_preset.TryGetChanceTile('0', out var t0));
        Assert.AreEqual(0.50f, t0.chance, 0.0001f);
        Assert.AreEqual("W", t0.onHit);
        Assert.AreEqual(".", t0.onMiss);

        Assert.IsTrue(_preset.TryGetChanceTile('1', out var t1));
        Assert.AreEqual(0.25f, t1.chance, 0.0001f);

        Assert.IsTrue(_preset.TryGetChanceTile('2', out var t2));
        Assert.AreEqual(0.75f, t2.chance, 0.0001f);
    }

    [Test]
    public void TryGetChanceTile_UnregisteredSymbol_ReturnsFalse()
    {
        Assert.IsFalse(_preset.TryGetChanceTile('W', out _));
        Assert.IsFalse(_preset.TryGetChanceTile('.', out _));
    }

    [Test]
    public void BuildLookup_PicksUpRuntimeEdits()
    {
        _preset.chanceTiles.Add(new DungeonGenPresetSO.ChanceTile
        {
            symbol = "9", chance = 0.1f, onHit = "W", onMiss = "."
        });
        _preset.BuildLookup();
        Assert.IsTrue(_preset.TryGetChanceTile('9', out var t));
        Assert.AreEqual(0.1f, t.chance, 0.0001f);
    }

    [Test]
    public void TryGetObjectSlot_DefaultTableHasFloorAndWallSlots()
    {
        Assert.IsTrue(_preset.TryGetObjectSlot('?', out var floor), "바닥 위험 슬롯");
        Assert.AreEqual(0.60f, floor.fillChance, 0.0001f);

        // 가시·화염·무너지는발판·압쇄·바람
        var symbols = floor.candidates.ConvertAll(c => c.symbol);
        CollectionAssert.AreEquivalent(new[] { "!", "f", "_", "C", "w" }, symbols);

        // 다트는 발사 방향이 심볼에 박혀 있어 좌우 슬롯이 따로 있어야 한다.
        Assert.IsTrue(_preset.TryGetObjectSlot('D', out var firesRight), "왼쪽 벽 슬롯(오른쪽 발사)");
        Assert.AreEqual(">", firesRight.candidates[0].symbol);

        Assert.IsTrue(_preset.TryGetObjectSlot('d', out var firesLeft), "오른쪽 벽 슬롯(왼쪽 발사)");
        Assert.AreEqual("<", firesLeft.candidates[0].symbol);
    }

    [Test]
    public void TryGetObjectSlot_UnregisteredSymbol_ReturnsFalse()
    {
        Assert.IsFalse(_preset.TryGetObjectSlot('E', out _));
        Assert.IsFalse(_preset.TryGetObjectSlot(_preset.RewardSlot, out _),
            "보상은 objectSlots가 아니라 전용 경로로 처리된다");

        // '^'는 타일셋에서 점프대(JumpTrap)다. 슬롯으로 잡으면 템플릿이 직접 놓은 점프대가 굴림에 덮인다.
        Assert.IsFalse(_preset.TryGetObjectSlot('^', out _), "점프대 심볼을 슬롯으로 쓰면 안 된다");
    }

    [Test]
    public void RewardSymbols_DefaultAndFallback()
    {
        Assert.AreEqual('%', _preset.RewardSlot);
        Assert.AreEqual('$', _preset.Reward);
        Assert.AreEqual(3, _preset.rewardCount);

        _preset.rewardSlotSymbol = "";
        _preset.rewardSymbol = null;
        Assert.AreEqual('%', _preset.RewardSlot);
        Assert.AreEqual('$', _preset.Reward);
    }

    [Test]
    public void MazeDefaults_HaveExtraConnectionChance()
    {
        Assert.AreEqual(0.25f, _preset.extraConnectionChance, 0.0001f);
        Assert.IsNotNull(_preset.shapeFiles);
    }
}
