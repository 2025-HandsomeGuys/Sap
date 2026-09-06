// @tags: decoration, rock, spawn, chunk, pipeline, generation
using UnityEngine;
using System.Collections.Generic;

public class RockDecorator : IChunkDecorator
{
    private TileVisualSettings _visualSettings;

    public RockDecorator(TileVisualSettings visualSettings)
    {
        _visualSettings = visualSettings;
    }

    public void Decorate(TerrainChunk chunk, DecorationContext context)
    {
        if (_visualSettings == null) return;

        var visualData = _visualSettings.GetDataForType(context.TargetTileType);
        if (visualData.rockPrefabs != null && visualData.rockPrefabs.Count > 0)
        {
            // [Save] 저장된 암석 배치가 있으면 복원, 없으면 신규 생성
            // null = 이 청크는 아직 저장된 적 없음 (첫 방문) → GenerateRocks
            // 배열(빈 배열 포함) = 이전에 저장됨 → RestoreRocks (파괴된 암석은 배열에 없으므로 자동 제외)
            RockSaveEntry[] savedRocks = chunk.GetAndClearSavedRocks();

            if (savedRocks != null)
            {
                TerrainDecorator.RestoreRocks(
                    chunk,
                    savedRocks,
                    visualData.rockPrefabs,
                    context.TargetTileType
                );
            }
            else
            {
                float calculatedPadding = Mathf.Clamp(chunk.textureThickness * chunk.PPU, 5f, 50f) + 5f;

                TerrainDecorator.GenerateRocks(
                    chunk,
                    visualData.rockPrefabs,
                    4, 4,
                    context.Coord,
                    context.WorldSeed,
                    visualData.rockSpacingBuffer,
                    calculatedPadding,
                    checkExistingData: context.IsModified,
                    preOccupiedAreas: context.PreOccupiedAreas,
                    tileType: context.TargetTileType
                );
            }

            // 돌 영역을 PreOccupiedAreas에 등록 → 이후 MineralDecorator가 겹침 방지.
            // 신규 생성·저장 복원 양쪽 모두 등록해야 한다 — 복원 경로를 빼먹으면 재로드 때
            // 광물이 돌 안에 박히고, 그 돌이 부서질 때(ClearHole) 광물만 허공에 남는다.
            foreach (var bound in chunk.GeneratedRockBounds)
                context.PreOccupiedAreas.Add(bound);
        }
    }
}
