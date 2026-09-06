// @tags: chunk, initialization, data-container, border, pixel-data, rock-save
using UnityEngine;

/// <summary>
/// 청크 초기화에 필요한 모든 데이터를 담는 컨테이너 클래스 (Data Object).
/// TerrainChunk.Reuse_Step1_Prepare 메서드의 인자 복잡도를 줄이는 데 사용됩니다.
/// </summary>
public class ChunkInitializationData
{
    // [Core Data]
    public Transform Player;
    public Color32[] Pixels;    // Ground Pixels
    public byte[] PixelInfo;    // Solid/Air Info
    public bool HasChanges;     // If true, needs visual refresh

    /// <summary>
    /// [GC] PixelInfo 전체가 단일 ID일 때 배열 대신 이 값을 사용 (-1 = 미사용).
    /// PixelInfo 배열보다 우선한다 → ChunkData.FillPixelInfo(MemSet)로 처리.
    /// </summary>
    public int UniformPixelInfoId = -1;

    // [Primary Border]
    public Color32[] BorderPixels;
    public int BorderWidth;
    public int BorderHeight;

    // [Secondary Border]
    public Color32[] SecondaryBorderPixels;
    public int SecondaryBorderWidth;
    public int SecondaryBorderHeight;
    public byte SecondaryTileId;

    // [Rock Save] 저장된 암석 배치. null=신규 청크, 빈 배열=모두 파괴됨
    public RockSaveEntry[] SavedRocks;

    // [Cave] 생성 시점에 뚫을 절차적 굴 설정. default(enabled=false)면 카빙 안 함.
    // 저장 복원 청크는 이미 굴이 픽셀에 들어 있으므로 반드시 비활성으로 넘긴다.
    public CaveCarveSettings Cave;

    // [Cave] 굴 노이즈 도메인 시드. StandardChunkFactory 가 context.WorldSeed 를 넣는다.
    public int WorldSeed;

    // [Constructor for convenience, optional]
    public ChunkInitializationData() { }
}
