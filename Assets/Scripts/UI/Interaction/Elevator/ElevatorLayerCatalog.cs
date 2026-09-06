// @tags: elevator, layer, catalog, stops, depth
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 엘리베이터 정류장(LayerInfo) 목록을 만드는 단일 소스.
///
/// 예전엔 <see cref="ElevatorManager.InitializeLayers"/>만 이 계산을 갖고 있었는데,
/// 지상(DemoUpground)에는 ElevatorManager가 없어서 지상 엘리베이터 입구 UI가
/// 정류장 목록을 얻을 방법이 없었다. 그래서 계산을 여기로 뽑아 양쪽이 공유한다.
///
/// TileDataManager가 있으면 지층 startDepth·정류장 오프셋을 읽고,
/// 없으면(지상 씬 등) fallback을 쓴다.
/// </summary>
public static class ElevatorLayerCatalog
{
    /// <summary>
    /// TileDataManager가 없을 때 쓰는 지층 시작 깊이. tileData.json의 startDepth와 같아야 한다.
    /// (지층 두께 27 = 정류장 간격 6 + 9 + 12)
    /// </summary>
    private const int FallbackDirtDepth      =   0;
    private const int FallbackIceDepth       = -27;
    private const int FallbackMagmaDepth     = -54;
    private const int FallbackMeteoriteDepth = -81;
    private const int FallbackTerrainDepth   = 108;

    /// <summary>
    /// 정류장 오프셋 기본값 — tileData.json의 elevatorConfig.stopOffsets와 같아야 한다.
    /// 지층 시작에서 0 / 6 / 15 → 간격 6 → 9 → (다음 지층까지) 12.
    /// </summary>
    private static readonly int[] FallbackStopOffsets = { 0, 6, 15 };

    /// <summary>정류장 표시 이름 접미사. 오프셋이 더 많으면 번호로 대체한다.</summary>
    private static readonly string[] StopSuffixes = { "상층", "중층", "하층" };

    /// <summary>같은 설정 오류를 매 호출마다 찍지 않도록 이미 보고한 메시지를 기억한다.</summary>
    private static readonly HashSet<string> _reported = new HashSet<string>();

    /// <summary>
    /// 지층마다 <b>정류장 오프셋 배열</b>만큼 정류장을 만든다 —
    /// 오프셋은 그 지층 startDepth 기준 아래 방향 거리다.
    /// 반환 리스트의 인덱스가 곧 layerIndex이자 UI 버튼 순서다.
    ///
    /// 예전엔 지층 두께를 균등 3등분(0, 1/3, 2/3)해서 간격이 6/7/7로 사실상 평평했다.
    /// 한 번 장비가 되면 다음 정류장이 공짜로 따라와 진행 리듬이 죽어서, 간격을 데이터로 뺐다.
    /// (ElevatorManager.InitializeLayers와 반드시 같은 결과를 내야 착지 좌표가 어긋나지 않는다.)
    /// </summary>
    public static List<LayerInfo> Build()
    {
        var layers = new List<LayerInfo>();

        var tdm = TileDataManager.Instance;

        int GetDepth(TileType type, int fallback)
        {
            if (tdm != null)
            {
                var data = tdm.GetData(type);
                if (data != null) return data.startDepth;
            }
            return fallback;
        }

        // 지층 원본 정의 (얕은 → 깊은 순).
        var bases = new (TileType type, int depth, string name)[]
        {
            (TileType.Dirt,          GetDepth(TileType.Dirt,          FallbackDirtDepth),      "땅"),
            (TileType.Ice,           GetDepth(TileType.Ice,           FallbackIceDepth),       "얼음땅"),
            (TileType.MagmaRock,     GetDepth(TileType.MagmaRock,     FallbackMagmaDepth),     "용암땅"),
            (TileType.MeteoriteRock, GetDepth(TileType.MeteoriteRock, FallbackMeteoriteDepth), "우주"),
        };

        // 마지막 지층은 다음 지층이 없으므로 지형 바닥을 두께 계산의 끝으로 쓴다.
        int terrainBottom = -(tdm != null ? tdm.terrainDepth : FallbackTerrainDepth);

        for (int i = 0; i < bases.Length; i++)
        {
            int top    = bases[i].depth;
            int bottom = (i + 1 < bases.Length) ? bases[i + 1].depth : terrainBottom;

            // 아래가 음수 방향이므로 두께는 양수로 만든다.
            int thickness = top - bottom;

            int[] offsets  = ResolveOffsets(bases[i].type);
            int   prevDepth = int.MaxValue;

            for (int s = 0; s < offsets.Length; s++)
            {
                int offset = offsets[s];

                if (offset < 0)
                {
                    Report($"[ElevatorLayerCatalog] {bases[i].type} 정류장 오프셋이 음수({offset})다 — 지층 위로 올라간다. 건너뛴다.");
                    continue;
                }

                // 오프셋이 지층 두께 이상이면 그 정류장은 다음 지층에 박힌다.
                // 조용히 밀어 넣으면 '얼음땅 하층'이 용암땅 한복판에 서고, 그 층 지형·광물이 전부 어긋난다.
                if (offset >= thickness)
                {
                    Report($"[ElevatorLayerCatalog] {bases[i].type} 정류장 오프셋 {offset}이 지층 두께 {thickness} 이상이다 — " +
                           $"다음 지층에 박히므로 건너뛴다. tileData.json의 startDepth 또는 elevatorConfig를 맞출 것.");
                    continue;
                }

                int depth = top - offset;

                // 오프셋이 겹치면 같은 깊이에 정류장이 둘 생긴다 —
                // GetLayerIndexByDepth·ShouldSpawnElevator가 둘을 구분하지 못한다.
                if (depth == prevDepth) continue;
                prevDepth = depth;

                layers.Add(new LayerInfo(layers.Count, bases[i].type, depth,
                                         $"{bases[i].name} {SuffixAt(s)}"));
            }
        }

        return layers;
    }

    /// <summary>
    /// 그 지층의 정류장 오프셋. 지층별 덮어쓰기 → 전역 기본값 → 코드 fallback 순.
    /// </summary>
    private static int[] ResolveOffsets(TileType type)
    {
        var cfg = TileDataManager.Instance != null ? TileDataManager.Instance.ElevatorSettings : null;
        if (cfg == null) return FallbackStopOffsets;

        if (cfg.layerStopOffsets != null)
        {
            foreach (var entry in cfg.layerStopOffsets)
            {
                if (entry == null || entry.offsets == null || entry.offsets.Length == 0) continue;
                if (!System.Enum.TryParse(entry.tileType, true, out TileType entryType))
                {
                    Report($"[ElevatorLayerCatalog] layerStopOffsets의 tileType '{entry.tileType}'을 못 읽었다 — 무시한다.");
                    continue;
                }
                if (entryType == type) return entry.offsets;
            }
        }

        return (cfg.stopOffsets != null && cfg.stopOffsets.Length > 0)
            ? cfg.stopOffsets
            : FallbackStopOffsets;
    }

    private static string SuffixAt(int index)
        => (index < StopSuffixes.Length) ? StopSuffixes[index] : $"{index + 1}번째";

    /// <summary>
    /// Build는 정류장 좌표 캐시가 무효화될 때마다 다시 돌 수 있다 —
    /// 설정 오류를 매번 찍으면 콘솔이 범람해서 아무도 안 본다. 같은 메시지는 한 번만.
    /// </summary>
    private static void Report(string message)
    {
        if (!_reported.Add(message)) return;
        Debug.LogError(message);
    }
}
