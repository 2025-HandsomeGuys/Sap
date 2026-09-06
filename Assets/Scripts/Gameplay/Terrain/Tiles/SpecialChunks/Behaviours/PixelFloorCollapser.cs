// @tags: trap, collapse, digging, pixel, terrain, chunk, interface
using UnityEngine;

/// <summary>
/// IFloorCollapser 구현 — TerrainChunk의 픽셀 배열을 직접 조작하여 바닥을 소멸시킨다.
/// SRP: 픽셀 수정 + Visualizer 갱신 책임만 담당.
/// DIP: CollapseFloor는 이 구체 클래스가 아닌 IFloorCollapser 인터페이스에 의존한다.
/// </summary>
[RequireComponent(typeof(TerrainChunk))]
public class PixelFloorCollapser : MonoBehaviour, IFloorCollapser
{
    private TerrainChunk _chunk;

    void Awake()
    {
        _chunk = GetComponent<TerrainChunk>();
    }

    /// <summary>
    /// 청크 상단 <paramref name="floorThicknessPx"/> 행의 픽셀을 air(투명)로 클리어한다.
    /// TerrainChunk의 NativeArray에 직접 접근하므로 EnsureJobsCompleted() 후 처리.
    /// </summary>
    public void CollapseFloor(int floorThicknessPx)
    {
        if (_chunk == null)
        {
            Debug.LogError("[PixelFloorCollapser] TerrainChunk 컴포넌트가 없습니다!");
            return;
        }

        var data = _chunk.GetData();
        if (data == null)
        {
            Debug.LogError("[PixelFloorCollapser] ChunkData가 null입니다. 청크가 초기화되었는지 확인하세요.");
            return;
        }

        // NativeArray 쓰기 전 진행 중인 Job 완료 대기 (racing condition 방지)
        _chunk.EnsureJobsCompleted();

        int chunkW      = _chunk.width;
        int chunkH      = _chunk.height;
        int clearStartY = Mathf.Max(0, chunkH - floorThicknessPx);
        Color32 air     = new Color32(0, 0, 0, 0);

        // 상단 floorThicknessPx 행을 air로 설정
        for (int y = clearStartY; y < chunkH; y++)
        {
            int rowOffset = y * chunkW;
            for (int x = 0; x < chunkW; x++)
            {
                data.BasePixels[rowOffset + x] = air;
                data.PixelInfo [rowOffset + x] = 0; // PIXEL_ID_AIR
            }
        }

        _chunk.isTextureDirty = true;
        _chunk.isDirty        = true;

        // Visualizer 즉시 갱신 (변경된 영역만)
        if (_chunk.Visualizer != null)
        {
            _chunk.Visualizer.UpdateVisualsArea(
                0, clearStartY, chunkW, chunkH,
                _chunk.ChunkX, _chunk.ChunkY,
                _chunk.SyncBoundaryDistanceWithNeighbors
            );
        }
    }
}
