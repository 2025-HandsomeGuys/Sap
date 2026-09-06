using System.IO;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// tileData.json의 광물 규칙 스키마 무결성. 29개 rule을 손으로 마이그레이션했기 때문에
/// 오타·누락을 컴파일이 아니라 테스트로 잡아야 한다.
/// 배경: Assets/Docs/mineral-density-redesign.md §4
/// </summary>
public class TileDataMineralRuleTests
{
    private static TileDatabaseJson LoadDatabase()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "tileData.json");
        Assert.IsTrue(File.Exists(path), $"tileData.json 없음: {path}");
        var db = JsonUtility.FromJson<TileDatabaseJson>(File.ReadAllText(path));
        Assert.IsNotNull(db, "tileData.json 파싱 실패");
        Assert.IsNotNull(db.tiles, "tiles 배열 없음");
        return db;
    }

    [Test]
    public void EveryRule_HasSpawnAndDropFields()
    {
        var db = LoadDatabase();
        int ruleCount = 0;

        foreach (var tile in db.tiles)
        {
            if (tile?.minerals == null) continue;
            foreach (var rule in tile.minerals)
            {
                ruleCount++;
                string where = $"{tile.tileType}/{rule.mineralType}";

                Assert.IsTrue(MineralDensity.HasValidRange(rule.perChunk), $"{where}: perChunk 누락/길이부족");
                Assert.GreaterOrEqual(rule.perChunk[1], rule.perChunk[0], $"{where}: perChunk max < min");
                Assert.GreaterOrEqual(rule.perChunk[0], 0f, $"{where}: perChunk 음수");

                Assert.IsNotNull(rule.rockDropCount, $"{where}: rockDropCount 누락");
                Assert.AreEqual(2, rule.rockDropCount.Length, $"{where}: rockDropCount 길이는 2여야 함");
                Assert.GreaterOrEqual(rule.rockDropCount[1], rule.rockDropCount[0], $"{where}: rockDropCount max < min");
                Assert.GreaterOrEqual(rule.rockDropCount[0], 1, $"{where}: rockDropCount min은 1 이상");

                Assert.GreaterOrEqual(rule.rockDropWeight, 0f, $"{where}: rockDropWeight 음수");
                Assert.LessOrEqual(rule.rockDropWeight, 1f, $"{where}: rockDropWeight는 0~1");
            }
        }

        Assert.AreEqual(29, ruleCount, "광물 규칙 개수가 예상과 다르다 — 규칙을 추가/삭제했다면 이 숫자도 갱신할 것");
    }

    [Test]
    public void EveryRule_HasParsableRarityAndSaneDepthRange()
    {
        var db = LoadDatabase();

        foreach (var tile in db.tiles)
        {
            if (tile?.minerals == null) continue;
            foreach (var rule in tile.minerals)
            {
                string where = $"{tile.tileType}/{rule.mineralType}";
                // rarity 오타는 조용히 Common으로 처리돼 버린다 — 명시적으로 검사한다
                bool known = rule.rarity == "Common" || rule.rarity == "Rare";
                Assert.IsTrue(known, $"{where}: rarity가 'Common'/'Rare'가 아님 → '{rule.rarity}'");
                Assert.GreaterOrEqual(rule.maxDepth, rule.minDepth, $"{where}: maxDepth < minDepth");
            }
        }
    }

    [Test]
    public void DensityMultipliers_AreNonNegative()
    {
        var db = LoadDatabase();
        Assert.GreaterOrEqual(db.globalMineralDensity, 0f, "globalMineralDensity 음수");
        foreach (var tile in db.tiles)
            Assert.GreaterOrEqual(tile.mineralDensity, 0f, $"{tile.tileType}: mineralDensity 음수");
    }
}
