using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 정류장 깊이(Y) 배치 검증.
///
/// 배경: 예전엔 지층 두께를 균등 3등분해서 상·중·하 간격이 6/7/7로 사실상 평평했다.
/// 한 번 장비가 되면 다음 정류장이 공짜로 따라와 진행 리듬이 죽는다.
/// 지금은 tileData.json의 elevatorConfig.stopOffsets로 간격을 정한다.
/// </summary>
public class ElevatorLayerCatalogTests
{
    // ─── 어떤 설정에서도 지켜져야 하는 불변식 ────────────────────────────────

    [Test]
    public void Build_LayerIndexMatchesListIndex()
    {
        // ElevatorRoomPainter.layerRoomImages·ElevatorStopLayout.XForLayer가
        // 리스트 인덱스를 그대로 layerIndex로 쓴다.
        var layers = ElevatorLayerCatalog.Build();
        for (int i = 0; i < layers.Count; i++)
            Assert.AreEqual(i, layers[i].layerIndex, $"index {i}");
    }

    [Test]
    public void Build_DepthsStrictlyDecrease()
    {
        // 깊이가 겹치면 GetLayerIndexByDepth·ShouldSpawnElevator가 두 정류장을 구분하지 못한다.
        var layers = ElevatorLayerCatalog.Build();
        Assert.Greater(layers.Count, 0, "정류장이 하나도 없다");

        for (int i = 1; i < layers.Count; i++)
            Assert.Less(layers[i].startDepth, layers[i - 1].startDepth,
                $"{i - 1}({layers[i - 1].startDepth}) → {i}({layers[i].startDepth})");
    }

    [Test]
    public void Build_FirstStopOfEachLayerSitsOnLayerTop()
    {
        // 첫 오프셋이 0이라 각 지층의 첫 정류장은 그 지층 경계에 정확히 선다.
        // 이게 깨지면 '상층'이 지층 시작이라는 랜드마크가 사라진다.
        var layers = ElevatorLayerCatalog.Build();

        var seen = new HashSet<TileType>();
        foreach (var layer in layers)
        {
            if (!seen.Add(layer.tileType)) continue;

            var data = TileDataManager.Instance != null ? TileDataManager.Instance.GetData(layer.tileType) : null;
            if (data == null) continue;   // 지상 씬·EditMode fallback 경로는 대조군이 없다

            Assert.AreEqual(data.startDepth, layer.startDepth, $"{layer.tileType} 상층");
        }
    }

    [Test]
    public void Build_StopsOfSameLayerAreContiguous()
    {
        // 같은 지층 정류장이 흩어지면 '땅 상층 → 얼음땅 상층 → 땅 중층' 같은 UI 순서가 나온다.
        var layers = ElevatorLayerCatalog.Build();

        var finished = new HashSet<TileType>();
        TileType current = default;
        bool started = false;

        foreach (var layer in layers)
        {
            if (!started || !layer.tileType.Equals(current))
            {
                if (started) finished.Add(current);
                Assert.IsFalse(finished.Contains(layer.tileType),
                    $"{layer.tileType} 정류장이 끊겼다가 다시 나온다");
                current = layer.tileType;
                started = true;
            }
        }
    }

    [Test]
    public void Build_StaysWithinTerrainDepth()
    {
        var layers = ElevatorLayerCatalog.Build();
        int bottom = -(TileDataManager.Instance != null ? TileDataManager.Instance.terrainDepth : 108);

        foreach (var layer in layers)
        {
            Assert.LessOrEqual(layer.startDepth, 0, $"{layer.layerName}이 지상에 있다");
            Assert.Greater(layer.startDepth, bottom, $"{layer.layerName}이 지형 바닥({bottom}) 밖이다");
        }
    }

    // ─── 기본값(0/6/15) 구체 검증 ────────────────────────────────────────────

    [Test]
    public void Build_DefaultOffsets_GapsAreSixNineTwelve()
    {
        // TileDataManager가 없으면 카탈로그가 코드 fallback(지층 0/-27/-54/-81, 오프셋 0/6/15)을 쓴다.
        // 실제 매니저가 살아 있는 세션이면 tileData.json이 원본이므로 이 대조는 건너뛴다.
        Assume.That(TileDataManager.Instance, Is.Null, "TileDataManager가 살아 있어 fallback 경로가 아니다");

        var layers = ElevatorLayerCatalog.Build();
        Assert.AreEqual(12, layers.Count, "지층 4개 x 정류장 3개");

        var expected = new[] { 0, -6, -15, -27, -33, -42, -54, -60, -69, -81, -87, -96 };
        for (int i = 0; i < expected.Length; i++)
            Assert.AreEqual(expected[i], layers[i].startDepth, $"{i}번째 정류장 ({layers[i].layerName})");

        // 지층마다 상→중 6, 중→하 9, 하→다음 지층 상 12.
        for (int i = 0; i < layers.Count; i += 3)
        {
            Assert.AreEqual(6, layers[i].startDepth - layers[i + 1].startDepth, $"{layers[i].layerName} 상→중");
            Assert.AreEqual(9, layers[i + 1].startDepth - layers[i + 2].startDepth, $"{layers[i].layerName} 중→하");

            int nextTop = (i + 3 < layers.Count) ? layers[i + 3].startDepth : -108;
            Assert.AreEqual(12, layers[i + 2].startDepth - nextTop, $"{layers[i].layerName} 하→다음 지층");
        }
    }
}
