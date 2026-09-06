# TerrainChunk.cs 코드 분석
@tags: terrain-chunk, neighbor-chunk, border, UpdateBordersInArea, CacheNeighbors, IsTransparent, dig, rendering, pooling, ApplyTexture, collider

**TerrainChunk.cs**는 게임 내 무한 맵 시스템에서 **개별 지형 덩어리(청크)**를 관리하는 핵심 스크립트입니다. 
SpriteRenderer와 Texture2D를 사용하여 픽셀 단위의 파괴 가능한 지형을 구현하고 있습니다.

---

## 1. 주요 역할 및 특징

*   **픽셀 단위 지형 편집**: 텍스처의 픽셀 알파값을 조절하여 땅을 파거나 채우는 기능을 수행합니다.
*   **무한 맵 지원**: `InfinityMapManager`와 연동되어 플레이어 이동에 따라 재사용(Pooling)됩니다.
*   **시각적 디테일**: 땅의 테두리(Border)를 텍스처와 단색으로 구분하여 자연스러운 단면을 표현합니다.
*   **최적화**: 변경된 부분만 텍스처를 갱신하고, 물리 연산(Collider)을 지연 업데이트합니다.

---

## 2. 주요 변수

| 변수명 | 설명 |
| :--- | :--- |
| **`pixelData`** | 현재 청크의 모든 픽셀 색상 정보를 담고 있는 배열입니다. (Color32 Array) |
| **`texture`** | 실제 게임 화면에 그려지는 텍스처입니다. `pixelData`를 기반으로 업데이트됩니다. |
| **`isDirty` / `isTextureDirty`** | 지형 변경이 발생했는지 체크하여, 불필요한 연산을 방지하는 플래그입니다. |
| **`solidBorderColor` / `borderTexture`** | 땅을 팠을 때 드러나는 단면의 색상과 텍스처입니다. |
| **`leftChunk`, `rightChunk`...** | 인접한 청크들의 참조를 저장하여 청크 경계면에서의 자연스러운 연결을 돕습니다. |

---

## 3. 핵심 함수 분석

### 3.1 초기화 및 재사용 (`FirstTimeInit`, `Reuse`)
*   **`FirstTimeInit(w, h)`**: 게임 시작 시(또는 청크 생성 시) 텍스처와 배열을 메모리에 할당합니다.
*   **`Reuse(...)`**: 플레이어가 멀리 이동하여 화면 밖으로 나간 청크를 재활용할 때 호출됩니다.
    *   기존 위치에서 새로운 위치로 이동하며, 새로운 지형 데이터(`sourcePixels`)를 덮어씁니다.
    *   `UpdateCollider` 대신 `SetFullSquareCollider`(빠른 처리)를 사용할지, 정밀한 콜라이더를 생성할지 결정합니다.

### 3.2 땅 파기 (`Dig`)
플레이어가 땅을 클릭하거나 상호작용할 때 호출되는 가장 중요한 함수입니다.
1.  **좌표 변환**: 마우스 월드 좌표를 텍스처 내부의 픽셀 좌표(x, y)로 변환합니다.
2.  **원형 범위 체크**: 플레이어와 마우스 사이의 각도, 거리(`reachOffset`), 반지름을 계산하여 파낼 범위를 정합니다.
3.  **픽셀 제거**: 해당 범위 내의 픽셀 투명도(Alpha)를 0으로 만듭니다.
4.  **최적화 처리**: 
    *   `isDirty = true`로 설정하여 나중에 콜라이더와 텍스처를 갱신하게 합니다.
    *   `CheckFloatingIslandsInArea`를 호출하여 공중에 뜬 부유섬을 제거합니다(옵션).

### 3.3 테두리 처리 (`UpdateBordersInArea`)
땅을 파낸 후, 흙과 공기의 경계면을 예쁘게 다듬는 로직입니다.
*   공기(투명한 픽셀)와의 거리를 계산하여, 깊이에 따라 **단색 테두리(`solidColor`)** 혹은 **텍스처 테두리(`borderTexture`)**를 칠해줍니다.
*   이로 인해 땅을 팠을 때 단순히 구멍만 뚫리는 것이 아니라, 입체감 있는 단면이 생성됩니다.

### 3.4 이웃 청크 연결 (`CacheNeighbors`, `IsTransparent`)
*   청크 경계선(예: x=0 또는 x=width-1) 근처를 계산할 때, 인접한 `leftChunk`, `rightChunk` 등의 픽셀 정보를 참조합니다.
*   이를 통해 청크와 청크 사이가 끊겨 보이지 않고 하나의 거대한 맵처럼 동작하게 합니다.

### 3.5 렌더링 최적화 (`LateUpdate`, `ApplyTexture`)
*   `Dig` 함수에서 매 픽셀마다 `texture.Apply()`를 호출하면 성능 저하가 심각합니다.
*   따라서 `isTextureDirty` 플래그만 켜두고, 프레임의 마지막인 `LateUpdate`에서 한 번만 `ApplyTexture`를 수행하여 GPU 부하를 줄입니다.

---

## 4. 참고 사항 (Partial Class)
이 파일은 `partial class`로 정의되어 있습니다. 즉, 클래스의 기능이 여러 파일로 나뉘어 있습니다.
*   **TerrainChunk.cs**: 렌더링, 데이터 관리, 땅 파기 로직 (본 파일)
*   **TerrainChunk.Physics.cs (추정)**: `UpdateCollider`, `SetFullSquareCollider` 등 물리 충돌체 관련 로직
*   **TerrainChunk.Island.cs (추정)**: `CheckFloatingIslandsInArea` 등 부유섬 탐지 및 제거 로직

> 이 구조는 거대한 하나의 클래스를 기능별로 파일로 분리하여 유지보수를 용이하게 하기 위함입니다.
