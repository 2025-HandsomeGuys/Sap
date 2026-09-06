using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Relic.Data;
using Relic.Drop;
using UnityEngine;

/// <summary>
/// 유물 탐험 드롭의 추첨 규칙 검증. Unity 오브젝트 없이 도는 순수 로직만 다룬다.
/// 실제 JSON(relicDropSettings.json)도 같이 읽어 오타·티어 누락을 잡는다.
/// </summary>
public class RelicDropTableTests
{
    private static RelicDropSettingsData MakeSettings()
    {
        return new RelicDropSettingsData
        {
            enabled = true,
            layers = new[]
            {
                new RelicDropSettingsData.LayerEntry { tileType = "Dirt",      tierWeights = new[] { 100, 0, 0 } },
                new RelicDropSettingsData.LayerEntry { tileType = "MagmaRock", tierWeights = new[] { 25, 100, 8 } },
            },
            tiers = new[]
            {
                new RelicDropSettingsData.TierEntry { tier = 1, relics = new[] { "Magnet", "Mp3" } },
                new RelicDropSettingsData.TierEntry { tier = 2, relics = new[] { "Anvil" } },
                new RelicDropSettingsData.TierEntry { tier = 3, relics = new[] { "XRay" } },
            }
        };
    }

    [Test]
    public void TryPick_UnknownLayer_Fails()
    {
        var picked = RelicDropTable.TryPick(MakeSettings(), "NoSuchLayer", null,
                                            new System.Random(1), out var id);
        Assert.IsFalse(picked);
        Assert.AreEqual(RelicID.None, id);
    }

    [Test]
    public void TryPick_Dirt_OnlyYieldsTier1()
    {
        var s = MakeSettings();
        var rng = new System.Random(12345);
        for (int i = 0; i < 200; i++)
        {
            Assert.IsTrue(RelicDropTable.TryPick(s, "Dirt", null, rng, out var id));
            Assert.That(id, Is.EqualTo(RelicID.Magnet).Or.EqualTo(RelicID.Mp3),
                        $"흙층 가중치는 [100,0,0]인데 {id}가 나왔다");
        }
    }

    [Test]
    public void TryPick_NeverReturnsOwnedRelic()
    {
        var s = MakeSettings();
        var owned = new HashSet<RelicID> { RelicID.Magnet, RelicID.Anvil };
        var rng = new System.Random(7);

        for (int i = 0; i < 500; i++)
        {
            Assert.IsTrue(RelicDropTable.TryPick(s, "MagmaRock", owned.Contains, rng, out var id));
            Assert.IsFalse(owned.Contains(id), $"이미 가진 {id}가 다시 뽑혔다");
        }
    }

    [Test]
    public void TryPick_AllOwned_Fails()
    {
        var s = MakeSettings();
        var owned = new HashSet<RelicID> { RelicID.Magnet, RelicID.Mp3, RelicID.Anvil, RelicID.XRay };

        Assert.IsFalse(RelicDropTable.TryPick(s, "MagmaRock", owned.Contains, new System.Random(3), out var id));
        Assert.AreEqual(RelicID.None, id);
    }

    /// <summary>
    /// 티어가 비면 그 가중치는 추첨에서 빠져야 한다. 1티어를 다 모은 뒤 마그마층에서 뽑으면
    /// 25/133 확률로 "꽝"이 되는 게 아니라 2·3티어 안에서만 나와야 한다.
    /// </summary>
    [Test]
    public void TryPick_EmptyTierIsExcludedFromWeights()
    {
        var s = MakeSettings();
        var owned = new HashSet<RelicID> { RelicID.Magnet, RelicID.Mp3 };   // 1티어 전멸
        var rng = new System.Random(99);

        for (int i = 0; i < 300; i++)
        {
            Assert.IsTrue(RelicDropTable.TryPick(s, "MagmaRock", owned.Contains, rng, out var id));
            Assert.That(id, Is.EqualTo(RelicID.Anvil).Or.EqualTo(RelicID.XRay));
        }
    }

    /// <summary>해당 지층의 티어가 가장 자주 나와야 한다(겹치되 자기 층이 주력).</summary>
    [Test]
    public void TryPick_OwnTierDominates()
    {
        var s = MakeSettings();
        var rng = new System.Random(2024);
        int tier2 = 0, other = 0;

        for (int i = 0; i < 4000; i++)
        {
            RelicDropTable.TryPick(s, "MagmaRock", null, rng, out var id);
            if (id == RelicID.Anvil) tier2++; else other++;
        }

        // 가중치 25 : 100 : 8 → 2티어가 약 75%
        Assert.Greater(tier2, other, $"마그마층 주력이 2티어가 아니다 (2티어 {tier2} vs 그 외 {other})");
    }

    [Test]
    public void TryPick_SameSeed_IsDeterministic()
    {
        var s = MakeSettings();
        RelicDropTable.TryPick(s, "MagmaRock", null, new System.Random(555), out var a);
        RelicDropTable.TryPick(s, "MagmaRock", null, new System.Random(555), out var b);
        Assert.AreEqual(a, b);
    }

    // ================================================================
    //  실제 JSON 검증
    // ================================================================

    private static RelicDropSettingsData LoadShipped()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "relicDropSettings.json");
        Assert.IsTrue(File.Exists(path), $"relicDropSettings.json 없음: {path}");
        var data = JsonUtility.FromJson<RelicDropSettingsData>(File.ReadAllText(path));
        Assert.IsNotNull(data, "relicDropSettings.json 파싱 실패");
        return data;
    }

    [Test]
    public void ShippedJson_HasNoUnknownRelicNames()
    {
        var bad = RelicDropTable.FindUnknownRelicNames(LoadShipped());
        Assert.IsEmpty(bad, "RelicID에 없는 이름: " + string.Join(", ", bad));
    }

    /// <summary>
    /// 한 유물이 두 티어에 걸치면 드롭률이 조용히 두 배가 된다.
    /// </summary>
    [Test]
    public void ShippedJson_HasNoDuplicateRelicAcrossTiers()
    {
        var seen = new HashSet<string>();
        var dup = new List<string>();
        foreach (string n in LoadShipped().AllRelicNames())
            if (!seen.Add(n)) dup.Add(n);

        Assert.IsEmpty(dup, "여러 티어에 중복 기재된 유물: " + string.Join(", ", dup));
    }

    /// <summary>
    /// 상점에서 유물을 뺀 이상 드롭 표가 유일한 획득 경로다. 표에 없는 유물은 영영 못 얻는다.
    /// 예외는 둘 — TestStatRelic(테스트용)과 ElevatorTracker(업그레이드 노드 지급).
    /// </summary>
    [Test]
    public void ShippedJson_CoversEveryObtainableRelic()
    {
        var covered = new HashSet<RelicID>();
        foreach (string n in LoadShipped().AllRelicNames())
            if (RelicDropTable.TryParseRelic(n, out var id)) covered.Add(id);

        var missing = new List<RelicID>();
        foreach (RelicID id in System.Enum.GetValues(typeof(RelicID)))
        {
            if (id == RelicID.None || id == RelicID.TestStatRelic || id == RelicID.ElevatorTracker) continue;
            if (!covered.Contains(id)) missing.Add(id);
        }

        Assert.IsEmpty(missing, "드롭 표에 없어 획득 불가능한 유물: " + string.Join(", ", missing));
    }

    /// <summary>지층 이름이 tileData.json과 어긋나면 그 층에서 유물이 통째로 안 나온다.</summary>
    [Test]
    public void ShippedJson_LayerNamesParseAsTileType()
    {
        var s = LoadShipped();
        Assert.IsNotNull(s.layers, "layers 누락");
        foreach (var l in s.layers)
            Assert.IsTrue(System.Enum.TryParse(l.tileType, out TileType _),
                          $"TileType에 없는 지층 이름: {l.tileType}");
    }

    /// <summary>지층별로 '자기 티어'의 가중치가 최대여야 한다(사용자 합의 규칙).</summary>
    [Test]
    public void ShippedJson_EachLayerPeaksAtItsOwnTier()
    {
        var s = LoadShipped();
        // 지층 tier(tileData.json) → 주력 유물 티어. 흙(0)·얼음(1)은 둘 다 1티어가 주력이다.
        var expected = new Dictionary<string, int>
        {
            { "Dirt", 1 }, { "Ice", 1 }, { "MagmaRock", 2 }, { "MeteoriteRock", 3 },
        };

        foreach (var l in s.layers)
        {
            if (!expected.TryGetValue(l.tileType, out int peak)) continue;
            int[] w = l.tierWeights;
            Assert.IsNotNull(w, $"{l.tileType} tierWeights 누락");

            for (int i = 0; i < w.Length; i++)
            {
                if (i + 1 == peak) continue;
                Assert.Less(w[i], w[peak - 1],
                    $"{l.tileType}: {peak}티어({w[peak - 1]})가 최대여야 하는데 {i + 1}티어가 {w[i]}");
            }
        }
    }
}
