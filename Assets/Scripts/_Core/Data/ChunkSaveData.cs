// @tags: chunk, save, data-container, dto, terrain, rock, pixel
using UnityEngine;
using System.Collections.Generic;

// [Storage Data Model]
[System.Serializable]
public class ChunkSaveData
{
    public Color32[] modifiedPixels; // Data representing modified terrain pixels
    public byte[] pixelInfo;         // Pixel metadata (0:Air, 1:Dirt, 2:Rock)
    public bool hasChanges;          // Optimization flag

    // [Rock Layout Save] 청크 언로드 시 암석 배치 스냅샷 저장
    // null = 아직 저장된 적 없는 신규 청크 (GenerateRocks 실행)
    // 빈 배열 = 저장됐지만 암석이 없음 (파괴 등) → RestoreRocks 실행 (0개 복원)
    public RockSaveEntry[] savedRocks;

    // 저장 당시 일반 청크였으면 true, 특수 청크(IChunkInitializer 보유)였으면 false.
    // 재로드 시 일반→특수 변환 버그 방지에 사용.
    public bool wasNormalChunk;
}

/// <summary>
/// 청크 언로드 시 암석 1개의 위치·종류를 저장하는 데이터 구조.
/// spriteSetIndex는 TileVisualSettings.rockPrefabs[]의 인덱스 (세션 내에서만 유효).
/// </summary>
[System.Serializable]
public class RockSaveEntry
{
    public int boundsX;         // PixelBoundsInChunk.x
    public int boundsY;         // PixelBoundsInChunk.y
    public int boundsW;         // PixelBoundsInChunk.width
    public int boundsH;         // PixelBoundsInChunk.height
    public int spriteSetIndex;  // rockPrefabs[] 인덱스

    public float savedHp;         // 언로드 시점의 현재 HP
    public float lastDamageTime;  // 언로드 시점의 Time.time (마지막 데미지 기준)
    public bool  isRevealed;      // 노출된 상태였는지
    public float angle;           // Z 회전 (도, CCW)
    public float scale;           // 균등 스케일 (0이면 구버전 데이터 → 1로 처리)
}
