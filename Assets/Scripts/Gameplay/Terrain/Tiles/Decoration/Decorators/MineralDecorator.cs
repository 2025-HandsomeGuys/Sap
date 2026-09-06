// @tags: decoration, mineral, spawn, chunk, pipeline, generation
using UnityEngine;
using System.Collections.Generic;

public class MineralDecorator : IChunkDecorator
{
    public void Decorate(TerrainChunk chunk, DecorationContext context)
    {
        if (TileDataManager.Instance == null) return;

        TileDataJson tileData = TileDataManager.Instance.GetData(context.TargetTileType);
        if (tileData == null || tileData.minerals == null || tileData.minerals.Count == 0) return;

        // 층 배율 × 전역 배율. 개수 산출은 MineralDensity가, 깊이 곡선은 rule의 rarity가 결정한다.
        // 배경: Assets/Docs/mineral-density-redesign.md §3-2
        var settings = new MineralSpawnSettings
        {
            DensityMultiplier = tileData.mineralDensity * TileDataManager.Instance.GlobalMineralDensity,
            ScatterJitter     = TileDataManager.Instance.MineralScatterJitter,
            LogSummary        = TileDataManager.Instance.LogMineralSpawn,
        };

        // PreOccupiedAreas: ElevatorDecorator가 등록한 엘리베이터 점유 영역 전달 → 해당 영역에 광물 스폰 제외
        MineralGenerator.GenerateMinerals(chunk, tileData.minerals, context.Coord, context.WorldSeed,
                                          context.TargetTileType, context.PreOccupiedAreas, settings);
    }
}
