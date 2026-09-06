using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonRoomFillerTests
{
    private DungeonGenPresetSO _preset;

    [SetUp]
    public void SetUp()
    {
        _preset = ScriptableObject.CreateInstance<DungeonGenPresetSO>();
        _preset.roomWidth = 4;
        _preset.roomHeight = 4;
        _preset.objectSlots = new List<DungeonGenPresetSO.ObjectSlot>();
        _preset.rewardCount = 0;
        _preset.BuildLookup();
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(_preset);

    // 슬롯 1종 등록. candidates는 "심볼:가중치" 문자열로 받는다.
    private void AddSlot(string symbol, float fillChance, params string[] candidates)
    {
        var slot = new DungeonGenPresetSO.ObjectSlot
        {
            symbol = symbol,
            fillChance = fillChance,
            candidates = new List<DungeonGenPresetSO.WeightedSymbol>(),
        };
        foreach (string c in candidates)
        {
            string[] parts = c.Split(':');
            slot.candidates.Add(new DungeonGenPresetSO.WeightedSymbol
            {
                symbol = parts[0],
                weight = parts.Length > 1 ? int.Parse(parts[1]) : 1,
            });
        }
        _preset.objectSlots.Add(slot);
        _preset.BuildLookup();
    }

    // 타일은 전부 빈칸, 오브젝트만 지정한 격자를 만든다.
    private static ComposeResult Make(params string[] objectRows)
    {
        int h = objectRows.Length;
        int w = objectRows[0].Length;
        var res = new ComposeResult
        {
            Width = w,
            Height = h,
            Tiles = new char[h, w],
            Objects = new char[h, w],
            Links = new char[h, w],
        };
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                res.Tiles[r, c] = '.';
                res.Objects[r, c] = objectRows[r][c];
                res.Links[r, c] = '.';
            }
        return res;
    }

    private static int Count(ComposeResult res, char symbol)
    {
        int n = 0;
        for (int r = 0; r < res.Height; r++)
            for (int c = 0; c < res.Width; c++)
                if (res.Objects[r, c] == symbol) n++;
        return n;
    }

    [Test]
    public void FillChanceOne_SingleCandidate_AlwaysPlaced()
    {
        AddSlot("?", 1f, "!");
        var res = Make("....", ".??.", "....", "....");

        DungeonRoomFiller.Fill(res, _preset, new System.Random(1));

        Assert.AreEqual(2, Count(res, '!'));
        Assert.AreEqual(0, Count(res, '?'));
    }

    [Test]
    public void FillChanceZero_SlotErased()
    {
        AddSlot("?", 0f, "!");
        var res = Make("....", ".??.", "....", "....");

        DungeonRoomFiller.Fill(res, _preset, new System.Random(1));

        Assert.AreEqual(0, Count(res, '!'));
        Assert.AreEqual(0, Count(res, '?'), "채우지 못한 슬롯은 '.'로 지운다");
    }

    // 템플릿이 직접 놓은 오브젝트(E/X, 압력판·문 같은 짝 기믹)는 슬롯이 아니므로 그대로 남아야 한다.
    [Test]
    public void NonSlotSymbols_LeftUntouched()
    {
        AddSlot("?", 1f, "!");
        var res = Make("....", ".EX.", "..?.", "....");

        DungeonRoomFiller.Fill(res, _preset, new System.Random(1));

        Assert.AreEqual('E', res.Objects[1, 1]);
        Assert.AreEqual('X', res.Objects[1, 2]);
        Assert.AreEqual('!', res.Objects[2, 2]);
    }

    // 확률 타일이 벽으로 굴러 슬롯이 벽 속에 묻히는 경우. 프리팹만 박히고 보이지 않으므로 지운다.
    [Test]
    public void SlotBuriedInWall_ErasedWithWarning()
    {
        AddSlot("?", 1f, "!");
        var res = Make("....", ".??.", "....", "....");
        res.Tiles[1, 1] = 'W';

        DungeonRoomFiller.Fill(res, _preset, new System.Random(1));

        Assert.AreEqual('.', res.Objects[1, 1]);
        Assert.AreEqual('!', res.Objects[1, 2]);
        Assert.AreEqual(1, res.Warnings.Count, string.Join("; ", res.Warnings));
    }

    [Test]
    public void Weights_ZeroWeightCandidateStillReachable()
    {
        // weight <= 0은 조립기(WeightedPick)와 같게 1로 취급한다.
        AddSlot("?", 1f, "!:0");
        var res = Make("....", ".?..", "....", "....");

        DungeonRoomFiller.Fill(res, _preset, new System.Random(3));

        Assert.AreEqual('!', res.Objects[1, 1]);
    }

    [Test]
    public void Reward_PicksExactlyRewardCount()
    {
        _preset.rewardCount = 2;
        var res = Make("%%%%", "....", "%%%%", "....");

        DungeonRoomFiller.Fill(res, _preset, new System.Random(5));

        Assert.AreEqual(2, Count(res, '$'));
        Assert.AreEqual(0, Count(res, '%'), "뽑히지 않은 보상 후보는 지운다");
        Assert.AreEqual(0, res.Warnings.Count, string.Join("; ", res.Warnings));
    }

    [Test]
    public void Reward_FewerCandidatesThanRequested_WarnsAndPlacesAll()
    {
        _preset.rewardCount = 5;
        var res = Make("....", ".%%.", "....", "....");

        DungeonRoomFiller.Fill(res, _preset, new System.Random(5));

        Assert.AreEqual(2, Count(res, '$'));
        Assert.AreEqual(1, res.Warnings.Count, string.Join("; ", res.Warnings));
    }

    [Test]
    public void SameSeed_SameResult()
    {
        AddSlot("?", 0.5f, "!:3", "_:2", "C:1");
        _preset.rewardCount = 2;

        string Run(int seed)
        {
            var res = Make("?%?%", "?.?.", "%??%", "?.?.");
            DungeonRoomFiller.Fill(res, _preset, new System.Random(seed));

            var sb = new System.Text.StringBuilder();
            for (int r = 0; r < res.Height; r++)
                for (int c = 0; c < res.Width; c++)
                    sb.Append(res.Objects[r, c]);
            return sb.ToString();
        }

        Assert.AreEqual(Run(77), Run(77));
        Assert.AreNotEqual(Run(77), Run(78), "시드가 다르면 배치도 달라야 한다");
    }
}
