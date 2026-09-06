// @tags: decoration, elevator, spawn, chunk, pipeline
using UnityEngine;

public class ElevatorDecorator : IChunkDecorator
{
    public void Decorate(TerrainChunk chunk, DecorationContext context)
    {
        if (ElevatorManager.Instance == null) return;
        
        // 엘리베이터 생성 조건 확인 (X 좌표)
        if (!ElevatorManager.Instance.ShouldSpawnElevator(context.Coord.x, context.Coord.y)) return;

        int elevatorLayerIndex = ElevatorManager.Instance.GetLayerIndexByDepth(context.Coord.y);
        if (elevatorLayerIndex >= 0)
        {
            // 실제 생성 위임 (기존 로직 활용)
            Rect? occupiedQuadrant = TerrainDecorator.GenerateElevator(
                chunk, 
                context.Coord.x, 
                context.Coord.y, 
                elevatorLayerIndex, 
                context.WorldSeed, 
                context.IsModified
            );

            // 점유 영역 등록
            if (occupiedQuadrant.HasValue)
            {
                context.PreOccupiedAreas.Add(occupiedQuadrant.Value);

                // 컨텍스트를 공유하지 않는 경로(DecorateMineralsOnly — 특수청크·보호 좌표)도
                // 엘리베이터 영역을 알아야 광물이 승강로 안에 박히지 않는다.
                chunk.SetElevatorArea(occupiedQuadrant.Value);
            }

            // 이 정류장을 방 페인터에 등록한다(칠하기는 안 함).
            // 데코레이터 중 엘리베이터가 첫 번째이므로 여기서 선 보호 좌표가 뒤따르는
            // Rock·Mineral을 같은 로드에서 즉시 스킵시키고, 실제 페인팅은 데코 루프가 끝난 뒤
            // 파이프라인이 부르는 IChunkPostLoadPainter 훅에서 일어난다.
            // → 텔레포트로 착지하지 않고 걸어서 도달한 정류장도 방이 생긴다.
            if (ElevatorManager.Instance.roomPainter != null)
                ElevatorManager.Instance.roomPainter.RegisterStop(context.Coord, elevatorLayerIndex);
        }
    }
}
