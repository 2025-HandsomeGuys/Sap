// @tags: pipeline, chunk, generation, data-container, dto
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Holds all data required to generate/spawn a single chunk.
/// Acts as a DTO between ChunkDataProvider and ChunkSpawner.
/// </summary>
public class ChunkGenerationContext
{
    // Basic Info
    public Vector2Int Coord;
    public int WorldSeed;
    
    // Dimensions
    public int SourceWidth;
    public int SourceHeight;

    // Special Chunk (if applicable)
    public IChunk SpecialChunkInstance;

    // 멀티 청크 스페셜: 앵커와 함께 스폰된 서브 청크 목록 (null = 단일 청크)
    public List<(Vector2Int coord, IChunk chunk)> SubChunkInstances;

    // Data Sources
    public ChunkSaveData SavedData;

    // Tile Data (for procedural generation)
    public TileType TileType;
    public Color32[] GroundPixels;
    public byte[] PixelInfo; // ID map

    /// <summary>
    /// [GC] PixelInfo 전체가 단일 ID일 때 배열 대신 이 값만 전달한다 (-1 = 미사용, PixelInfo 배열 사용).
    /// 블렌딩이 일어나지 않는 대다수 청크가 여기 해당 → 청크당 1MB 매니지드 할당 제거.
    /// </summary>
    public int UniformPixelInfoId = -1;

    // Border Data
    public Color32[] BorderPixels;
    public int BorderWidth;
    public int BorderHeight;

    // Secondary Border (Blending)
    public Color32[] SecondaryBorderPixels;
    public int SecondaryBorderWidth;
    public int SecondaryBorderHeight;
    public byte SecondaryTileId;

    // 링크 피스 전용 프리팹 (null이면 기본 chunk prefab 또는 풀 재사용)
    public GameObject LinkedPiecePrefab;

    // Flags
    public bool IsRestoredFromSave => SavedData != null;
    public bool IsSpecialChunk => SpecialChunkInstance != null;
    /// <summary>서브청크 자리로 예약된 좌표 — 일반 청크 생성 차단, 앵커 스폰 시 자동 등록됨</summary>
    public bool IsBlocked;
}
