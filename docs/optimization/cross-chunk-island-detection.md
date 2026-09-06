# 크로스 청크(Cross-Chunk) Island 감지 최적화

## 1. 개요
*   **작업 목적**: 지형(Terrain) 파괴 시, 청크 경계에 걸쳐 있는 공중 부양 블록(Floating Island)들이 정상적으로 붕괴되도록 최적화.
*   **기존 문제점**: 기존 `TerrainModifier.cs` 내의 BFS 탐색 루프(`CheckNeighborFlat`)는 단일 청크(`ChunkData`) 내에서만 동작. 탐색 좌표가 청크를 벗어나면 무조건 "안전지대(Grounded)"로 판단하여, 청크 경계에 있는 블록들이 부서지지 않고 허공에 남는 현상 발생.

## 2. 해결 방안 (GC-Free Cross-Chunk Reference)
*   유니티의 `NativeArray<Color32>`(C++ 원시 포인터 기반)가 가진 장점을 활용하여, 메모리 할당(GC) 없이 이웃 청크의 픽셀 데이터에 O(1)으로 접근하는 방식 적용.
*   **`NeighborContext` 도입**:
    *   `TerrainModifier`의 파괴/감지 메서드(`Dig`, `Explode`, `CheckFloatingIslandsInArea` 등)에 `NeighborContext` 구조체 추가.
    *   `NeighborContext`는 좌, 우, 상, 하 이웃 청크의 `ChunkData`를 담는 단순 데이터 전달용(Value Type) 래퍼.
*   **좌표 래핑(Coordinate Wrapping) 알고리즘**:
    *   탐색 중 좌표가 `x < 0`이 되면 즉시 `NeighborContext.Left` 배열로 전환.
    *   좌표 `x`를 `(width - 1)`로 변환하여 에러(Index Out of Bounds) 없이 안전하게 이웃 픽셀 조회.
    *   해당 이웃 청크가 아직 로드되지 않은 상태(`null`)인 맵의 끝자락에서는 기존처럼 보수적 판정(Grounded) 유지.

## 3. 기대 효과
*   프레임 드랍(GC Spike) 0% 보장.
*   맵 전체에서 경계를 무시하고 얼음/흙 덩어리들이 부드럽게 무너져 내리는 자연스러운 시각적 완성도 달성.
