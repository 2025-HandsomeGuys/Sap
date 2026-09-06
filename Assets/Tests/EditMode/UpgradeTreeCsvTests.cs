// @tags: test, editmode, upgrade, tree, csv, reader
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// UpgradeTreeCsvReader 단위 테스트.
/// 파일이 아니라 문자열을 먹여서 검사한다 — 실제 CSV의 내용 변화와 무관하게
/// 파싱 규칙만 고정하기 위해서다.
/// </summary>
public class UpgradeTreeCsvTests
{
    private const string Header =
        "nodeId,tier,effectType,effectValue,isPercentage,parentIds,cost,uiX,uiY,displayNameKey,descriptionKey,locked\n";

    [Test]
    public void Read_ParsesBasicRow()
    {
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "MiningRange_T0_01,0,MiningRangeMultiplier,1.12,true,,60,0,-350,넓은 삽날 I,범위가 넓어집니다.,false\n");

        Assert.AreEqual(1, rows.Count);
        var n = rows[0];
        Assert.AreEqual("MiningRange_T0_01", n.nodeId);
        Assert.AreEqual(0, n.tier);
        Assert.AreEqual(UpgradeEffectType.MiningRangeMultiplier, n.effectType);
        Assert.AreEqual(1.12f, n.effectValue, 0.0001f);
        Assert.IsTrue(n.isPercentage);
        Assert.AreEqual(60, n.cost);
        Assert.AreEqual(new Vector2(0f, -350f), n.uiPosition);
        Assert.AreEqual("넓은 삽날 I", n.displayNameKey);
        Assert.IsFalse(n.locked);
    }

    [Test]
    public void Read_BlankParents_MeansRootNode()
    {
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "A,0,MaxStaminaUp,10,false,,60,0,0,이름,설명,false\n");

        Assert.IsNotNull(rows[0].parentIds, "parentIds는 null이 아니라 빈 배열이어야 한다");
        Assert.AreEqual(0, rows[0].parentIds.Length);
    }

    [Test]
    public void Read_SplitsParentIdsOnSemicolon()
    {
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "C,0,MaxStaminaUp,10,false,A;B,60,0,0,이름,설명,false\n");

        CollectionAssert.AreEqual(new[] { "A", "B" }, rows[0].parentIds);
    }

    [Test]
    public void Read_QuotedDescriptionWithComma_KeepsComma()
    {
        // 실제 데이터에 있는 행이다 — EnvironmentResistance_T2_01의 설명에 쉼표가 들어간다.
        // 인용을 못 다루면 컬럼이 한 칸씩 밀려 조용히 망가진다.
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "E,2,EnvironmentResistance,30,false,,60,0,0,유해 차폐 기어,\"극한 온도, 독소 가스 등 저항 +30%\",false\n");

        Assert.AreEqual("극한 온도, 독소 가스 등 저항 +30%", rows[0].descriptionKey);
        Assert.AreEqual(30f, rows[0].effectValue, 0.0001f);
    }

    [Test]
    public void Read_UnknownEffectType_IsSkippedAndReported()
    {
        var errors = new List<string>();
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "Bad,0,ThisTypeDoesNotExist,1,false,,60,0,0,이름,설명,false\n", errors);

        Assert.AreEqual(0, rows.Count, "모르는 effectType 행은 버린다");
        Assert.AreEqual(1, errors.Count);
        StringAssert.Contains("Bad", errors[0]);
    }

    [Test]
    public void Read_DuplicateNodeId_IsSkippedAndReported()
    {
        var errors = new List<string>();
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "A,0,MaxStaminaUp,10,false,,60,0,0,이름,설명,false\n" +
            "A,0,MaxStaminaUp,20,false,,70,0,0,이름2,설명2,false\n", errors);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(1, errors.Count);
    }

    [Test]
    public void Read_LockedColumn_IsParsed()
    {
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "A,0,MaxStaminaUp,10,false,,60,0,0,이름,설명,TRUE\n");

        Assert.IsTrue(rows[0].locked, "대소문자와 무관하게 읽어야 한다");
    }

    [Test]
    public void Read_MalformedNumber_IsSkippedAndReported()
    {
        var errors = new List<string>();
        var rows = UpgradeTreeCsvReader.ReadText(Header +
            "A,0,MaxStaminaUp,열,false,,60,0,0,이름,설명,false\n", errors);

        Assert.AreEqual(0, rows.Count);
        Assert.AreEqual(1, errors.Count);
    }
}
