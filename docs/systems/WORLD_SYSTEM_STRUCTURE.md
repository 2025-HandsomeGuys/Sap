# World System Structure
@tags: world-system, architecture, chunk-lifecycle, InfinityMapManager, chunk-loading, overview

이 문서는 게임의 월드 생성, 청크 관리, 그리고 데이터 로딩 시스템의 구조를 설명합니다.

## 1. Class Diagram (월드 구조)

```mermaid
classDiagram
    %% Unity Classes
    class MonoBehaviour { <<Unity>> }
    class ScriptableObject { <<Unity>> }

    %% Core Managers
    class WorldManager {
        +static Instance
        +int viewDistanceInChunks
        -Dictionary chunkDataMap
        -Dictionary activeRegionTilemaps
        +DigTiles()
        +GetTileTypeAt()
        -UpdateChunksCoroutine()
        note: 월드 시스템의 총괄 관리자<br/>청크 로드/언로드 및 타일맵 풀링 처리
    }

    class WorldGenerator {
        +TerrainGenerationProfile terrainProfile
        +RuleTile dirtTile
        +RuleTile hardStoneTile
        +InitializeChunkDataCoroutine()
        +CreateTilebaseArray()
        note: 순수 생성 로직 담당<br/>노이즈 알고리즘 및 지형 데이터 채우기
    }

    class ObjectPooler {
        +static Instance
        +SpawnFromPool()
        +ReturnToPool()
        note: 광물 및 아이템 오브젝트의<br/>재사용을 관리하여 성능 최적화
    }

    %% Data Structures
    class ChunkData {
        <<Data Class>>
        +TileType[,] terrainLayer
        +MineralID[,] mineralLayer
        +Vector2Int chunkCoord
        +ChunkStatus status
    }

    class TileDataManager {
        +static Instance
        -Dictionary tileDataDict
        +LoadTileData()
        +GetData(TileType)
        note: JSON 파일에서 타일 속성<br/>(내구도, 스태미나 소모 등) 로드
    }

    %% Configuration (ScriptableObjects)
    class TerrainGenerationProfile {
        <<ScriptableObject>>
        +List~TerrainLayer~ layers
    }
    
    class MineralGenerationProfile {
        <<ScriptableObject>>
        +List~MinableSpawnConfig~ configs
    }

    %% Relationships
    WorldManager --|> MonoBehaviour
    WorldManager --> WorldGenerator : 참조 & 사용
    WorldManager --> ObjectPooler : 광물 스폰 요청
    WorldManager *-- ChunkData : 관리 (Dictionary)
    
    WorldGenerator --|> MonoBehaviour
    WorldGenerator ..> ChunkData : 데이터 채움 (Write)
    WorldGenerator --> TerrainGenerationProfile : 설정 참조

    TerrainGenerationProfile --> MineralGenerationProfile : 포함 (구조상 연결)
    
    TileDataManager --|> MonoBehaviour
```

---

## 2. Sequence Diagram (청크 생성 및 로드 흐름)

플레이어가 이동할 때 새로운 청크가 생성되고 화면에 표시되는 과정을 나타냅니다.

```mermaid
sequenceDiagram
    participant Player
    participant WM as WorldManager
    participant WG as WorldGenerator
    participant OP as ObjectPooler
    participant Region as RegionTilemap (Pool)

    Note over Player, Region: 플레이어 이동 감지
    Player->>WM: Update() (위치 변경 확인)
    WM->>WM: RequestChunkUpdate()

    Note over WM: 청크 업데이트 코루틴 시작
    loop 시야 범위 내 모든 청크 (X, Y)
        alt 청크 데이터가 없는 경우 (New)
            WM->>WM: new ChunkData()
            WM->>WG: InitializeChunkDataCoroutine()
            activate WG
            WG->>WG: 1. 지형(Terrain) 채우기 (Noise)
            WG->>WG: 2. 광맥(Vein) 생성
            WG->>WG: 3. 광물(Mineral) 데이터 배치
            WG-->>WM: 완료 (Generated)
            deactivate WG
            
            WM->>WG: PreSpawnMineralsForChunk()
            WG->>OP: SpawnFromPool(MineralID)
            OP-->>WM: GameObject (광물 인스턴스)
        end
    end

    Note over WM: 타일 배치 및 시각화
    loop 준비된 청크들
        WM->>WG: CreateTilebaseArray()
        WG-->>WM: TileBase[] (타일 에셋 배열)
        WM->>Region: SetTilesBlock(TileBase[])
        note right of Region: 화면에 지형 표시됨
    end

    Note over WM: 언로드 (Unload)
    loop 시야 밖으로 나간 청크
        WM->>Region: SetTilesBlock(Empty)
        WM->>WM: ChunkStatus = Unloaded
        note right of WM: 메모리는 유지하되<br/>렌더링 비용 제거
    end
```

## 3. 주요 데이터 흐름

1.  **설정 (Profile)**: `TerrainGenerationProfile`에서 층(Layer)별 깊이와 등장할 광물(`MineralGenerationProfile`)을 정의합니다.
2.  **생성 (Generator)**: `WorldGenerator`가 이 설정을 읽어 32x32 크기의 `ChunkData` 배열(Terrain, Mineral)을 채웁니다.
3.  **관리 (Manager)**: `WorldManager`는 이 데이터를 받아 실제 `Tilemap`에 타일을 깔고, 광물 위치에는 `ObjectPooler`를 통해 상호작용 가능한 오브젝트(Mineable)를 배치합니다.
4.  **속성 (Data)**: 채굴 시 필요한 타일의 내구도 정보는 `TileDataManager`가 `tileData.json`에서 불러와 제공합니다.

---

## 확인 방법
1. VS Code에서 이 파일을 엽니다.
2. `Ctrl + Shift + V`를 누르거나 우측 상단의 **Open Preview** 버튼을 클릭합니다.
