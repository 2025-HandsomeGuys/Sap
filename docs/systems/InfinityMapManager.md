# InfinityMapManager.cs 코드 분석
@tags: InfinityMapManager, chunk-lifecycle, pooling, ModifyTerrain, multi-chunk-dig, neighbor-chunk, ProcessChunkQueue, infinite-map, dig-propagation, SpawnChunk

**InfinityMapManager.cs**는 플레이어의 이동에 따라 게임 월드를 무한하게 확장하고 관리하는 **무한 맵 시스템의 컨트롤 타워**입니다.
`TerrainChunk` 객체들을 생성(Spawn), 배치, 회수(Pool)하며, 플레이어 주변에만 지형이 존재하도록 관리하여 성능을 최적화합니다.

---

## 1. 주요 역할 및 특징

*   **무한 스크롤링**: 플레이어 위치를 실시간으로 추적하여 필요한 청크를 로드하고, 멀어진 청크를 언로드합니다.
*   **오브젝트 풀링 (Object Pooling)**: `chunkPool`을 사용하여 청크를 파괴하지 않고 재사용함으로써 가비지 컬렉션(GC) 및 인스턴스화 부하를 최소화합니다.
*   **깊이별 바이옴 시스템**: `TileDataManager`와 연동하여 Y축 깊이에 따라 다른 지형 텍스처(흙, 돌, 기반암 등)를 적용합니다.
*   **비동기 로딩**: 한 번에 많은 청크를 생성할 때 발생하는 끊김 현상을 방지하기 위해 코루틴(`ProcessChunkQueue`)을 사용하여 로딩을 분산시킵니다.
*   **데이터 지속성**: 플레이어가 땅을 판 흔적(`modifiedPixels`)을 저장했다가, 다시 해당 위치로 돌아왔을 때 복구해줍니다.

---

## 2. 주요 변수

| 변수명 | 설명 |
| :--- | :--- |
| **`activeChunks`** | 현재 화면에 활성화된 청크들을 관리하는 Dictionary입니다. (Key: 좌표, Value: 청크) |
| **`chunkPool`** | 사용이 끝난 청크들을 대기시켜 놓는 큐(Queue)입니다. |
| **`loadQueue`** | 생성해야 할 청크들의 좌표 리스트입니다. 거리순 정렬 후 순차적으로 처리됩니다. |
| **`tileVisualSettings`** | 타일 종류별 메인 텍스처와 테두리 텍스처 정보를 담고 있는 리스트입니다. |
| **`groundPixelCache`** | 텍스처의 `GetPixels32()` 결과(픽셀 배열)를 미리 캐싱하여, 청크 생성 시 텍스처 읽기 비용을 없앱니다. |

---

## 3. 핵심 함수 분석

### 3.1 청크 업데이트 루프 (`UpdateChunks`)
플레이어가 일정 거리 이상 이동했을 때 호출됩니다.
1.  **언로드 (Unload)**: 설정된 `viewDistance`보다 멀어진 청크를 찾아 비활성화하고 풀(`chunkPool`)로 반환합니다. 이때 `SaveChunkData`를 호출하여 변경된 지형 정보를 저장합니다.
2.  **로드 대기열 추가**: 플레이어 주변에 비어있는 위치를 찾아 `loadQueue`에 넣습니다.
3.  **거리순 정렬**: 플레이어와 가까운 청크부터 먼저 로딩되도록 큐를 정렬합니다.
4.  **코루틴 시작**: `ProcessChunkQueue`를 실행하여 실제 생성을 시작합니다.

### 3.2 비동기 생성 프로세스 (`ProcessChunkQueue`)
*   대기열(`loadQueue`)에 있는 좌표들을 하나씩 꺼내 청크를 생성(`SpawnChunk`)합니다.
*   **프레임 분산**: `loadQueue.Count % 3 == 0` 조건을 통해, 한 프레임에 최대 3개까지만 처리하고 `yield return null`로 다음 프레임에 넘깁니다. 이는 게임이 버벅거리는 현상을 막아줍니다.

### 3.3 청크 생성 및 설정 (`SpawnChunk`)
1.  **풀링 (Pooling)**: `chunkPool`에 남는 청크가 있으면 가져오고, 없으면 새로 `Instantiate` 합니다.
2.  **타일 타입 결정**: `TileDataManager`에게 현재 깊이(Y좌표)에 맞는 타일 타입(Dirt, Stone 등)을 물어봅니다.
3.  **픽셀 데이터 준비**:
    *   **저장된 데이터가 있는 경우**: 이전에 땅을 팠던 기록(`worldData`)이 있다면 그 픽셀 데이터를 사용합니다.
    *   **새로운 땅인 경우**: 캐싱된 원본 텍스처 데이터(`groundPixelCache`)를 사용합니다.
4.  **`chunk.Reuse(...)`**: 준비된 픽셀 데이터와 설정을 청크에 주입하여 초기화합니다.

### 3.4 지형 수정 전파 (`ModifyTerrain`)
플레이어가 땅을 팔 때 호출됩니다.
*   단일 청크만 수정하는 것이 아니라, 파괴 범위(`radius`)가 인접한 청크에 걸칠 수 있으므로 **주변 청크들을 모두 검색**하여 `Dig` 명령을 전달합니다.
*   이를 통해 청크 경계선에 걸친 땅을 파도 자연스럽게 양쪽 모두 파이게 됩니다.

---

## 4. 구조적 특징 (Partial Class)
이 파일 역시 `partial class`입니다. 
본 파일에는 주로 **생성 로직(Spawning)**과 **풀링(Pooling)** 기능이 들어있으며, 데이터 저장/관리(`ActiveChunks`, `worldData` 관련 상세) 부분은 `InfinityMapManager.Data.cs`(혹은 유사한 파일)에 분리되어 있을 것으로 추정됩니다.
