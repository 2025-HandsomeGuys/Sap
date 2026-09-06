# 특별 프리펩 청크 시스템 (Special Prefab Chunk System)
@tags: special-chunk, implementation, plan, prefab, IChunkInitializer, SpecialChunkFactory, architecture

## 목표 설명 (Goal Description)
무한 맵 생성 시, 사용자가 제작한 프리펩(지형 및 구조물)이 특정 확률로 등장하는 시스템입니다.
맵의 **결정론적 생성(Deterministic Generation)**을 보장하며, 지층 경계면을 피해 자연스럽게 배치됩니다.

## 주요 규칙 (Core Rules)
1.  **결정론적 생성 (Deterministic)**: 플레이어가 언제 방문하든, `(좌표 + 월드 시드)`가 같다면 항상 동일한 특별 청크가 등장해야 합니다.
2.  **지층별 관리 (Layer Specific)**: 각 지층(`TileType`)마다 등장 가능한 특별 청크 풀(Pool)을 따로 관리합니다.
3.  **경계면 생성 금지 (No Boundary Spawn)**: 서로 다른 지층이 섞이는(Blending) 경계 구간에서는 특별 청크 생성을 **스킵**하여 어색한 연결을 방지합니다.
4.  **오브젝트 풀링 제외**: 특별 청크는 구조가 다양하므로 풀링하지 않고 `Instantiate`/`Destroy` 방식으로 관리합니다.

## 워크플로우 (Workflow)
1.  **제작**: 씬에서 지형을 깎고 `[Bake Terrain]` 기능으로 프리펩에 저장.
2.  **등록**: `SpecialChunkManager`에 **[지층 타입별]**로 `(Prefab, SpawnChance)` 등록.
3.  **실행**:
    - `InfinityMapManager`가 청크 생성 시도.
    - **경계 체크**: `ShouldBlendLayer`가 `true`면 일반 청크 생성.
    - **확률 체크**: `Hash(x, y, worldSeed)`로 결정론적 난수 생성 후 당첨 여부 확인.
    - **생성**: 당첨 시 `SpecialChunkManager`에서 프리펩 인스턴스화.

## 변경 예정 사항 (Proposed Changes)

### Tiles
#### [NEW] `SpecialChunkManager.cs`
- **데이터 구조**:
    ```csharp
    [System.Serializable]
    public struct SpecialChunkPool {
        public TileType targetLayer; // 해당 지층
        public List<SpecialChunkDef> chunks; // 후보군
    }
    
    [System.Serializable]
    public struct SpecialChunkDef {
        public TerrainChunk prefab;
        public float spawnChance; // 0.0 ~ 1.0 (0~100%)
        // public int minDepth, maxDepth; // (Optional: 깊이 제한이 필요하다면 추가)
    }
    ```
- **함수**: `public TerrainChunk GetSpecialChunk(Vector2Int coord, TileType layerType, int seed)`
    - 시드와 좌표를 섞어 난수 생성 (`XXHash` 또는 간단한 비트 연산 활용).
    - 해당 `layerType` 풀에서 확률 체크 후 프리펩 반환.

#### [MODIFY] [TerrainChunk.cs](file:///c:/Users/onebe/GameProject/Sap/Sap-UVCS/Assets/Scripts/Tiles/Refactored/TerrainChunk.cs)
- `public bool IsStaticMap;` (특별 청크 식별용)
- `[ContextMenu("Bake Current Shape")]`: 현재 모양을 텍스처로 저장하는 에디터 툴 (Editor Only).
- **메모리 관리**: `IsStaticMap`인 경우 `PredefinedShape` 텍스처 메모리 해제 주의.

#### [MODIFY] [InfinityMapManager.cs](file:///c:/Users/onebe/GameProject/Sap/Sap-UVCS/Assets/Scripts/Tiles/InfinityMapManager.cs)
- **생성 로직 ([SpawnChunk_Phase1](file:///c:/Users/onebe/GameProject/Sap/Sap-UVCS/Assets/Scripts/Tiles/InfinityMapManager.cs#409-558)) 수정**:
    1.  `ShouldBlendLayer(...)` 체크 -> `true`면 특별 청크 **SKIP** (일반 청크 생성).
    2.  `SpecialChunkManager.Instance.GetSpecialChunk(coord, targetType, worldSeed)` 호출.
    3.  결과가 있으면 `Instantiate` (풀링 X, `IsStaticMap = true`).
    4.  결과가 없으면 기존 `chunkPool` 사용.
- **반환 로직 ([ReturnChunkToPool](file:///c:/Users/onebe/GameProject/Sap/Sap-UVCS/Assets/Scripts/Tiles/InfinityMapManager.cs#695-700)) 수정**:
    - `chunk.IsStaticMap`이면 -> `Destroy(chunk.gameObject)`.
    - 아니면 -> `chunkPool.Enqueue()`.

## 검증 계획 (Verification Plan)
1.  **결정론적 테스트**: 같은 좌표(예: `100, 100`)에 갔다 왔을 때 항상 같은 특별 청크가 뜨는지 확인.
2.  **경계면 테스트**: 지층이 바뀌는 구간(Blend되는 곳)에는 절대 특별 청크가 뜨지 않는지 확인.
3.  **지층별 테스트**: `Dirt` 층에는 `Dirt`용 특별 청크만, `Rock` 층에는 `Rock`용만 뜨는지 확인.
4.  **메모리**: 생성 후 파괴 시 텍스처 메모리/게임 오브젝트가 깔끔하게 해제되는지 프로파일러 확인.
