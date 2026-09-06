# 특수 청크 프리팹 에디터 설정 가이드
@tags: special-chunk, editor, setup, guide, prefab, inspector, neighbor-chunk, border

> 마지막 업데이트: 2026-03-20
> 대상: 유니티 에디터에서 특수 청크 프리팹을 처음 만들거나 등록하는 작업자

---

> **관련 문서:**
> - `docs/chunk/hybrid-chunk-design.md` — 하이브리드 청크 상세 설계 (IndestructibleMask 원리, 코드 변경 목록)

---

## 0. 시작 전 체크: 어떤 패턴을 사용할지 결정

| 상황 | 선택 |
|------|------|
| 플레이어가 벽을 **파낼 수** 있어야 한다 | **패턴 A** — `TerrainChunk + SpriteCavityInitializer` |
| 스프라이트를 그대로 표시, 파기 불필요 | **패턴 B** — `LargeStaticTerrainChunk` |
| 청크 크기가 1000px 초과(멀티청크) | **패턴 B** — `LargeStaticTerrainChunk` (chunkGridWidth/Height 설정) |
| **파기 가능 지형 안에 파기 불가 영역이 공존** | **패턴 C** — `TerrainChunk + SpriteCavityInitializer + IndestructibleOverlay 자식` |

---

## 1. 패턴 A — `TerrainChunk + SpriteCavityInitializer` (공동 + 파기 가능)

### 1-1. 공동 스프라이트 준비

1. 포토샵 / Aseprite 등으로 **1000×1000px** 이미지 제작
   - 불투명 픽셀 → 지형으로 채워짐
   - **투명(alpha < 10) 픽셀 → 공동(빈 공간)**
2. `Assets/Sprites/SpecialChunks/` 폴더에 임포트
3. **Import Settings** 변경:
   - `Texture Type` → **Sprite (2D and UI)**
   - `Filter Mode` → **Point (no filter)**
   - `Compression` → **None**
   - `Read/Write Enabled` → **체크 (반드시 켜야 함)**
   - Apply 클릭

### 1-2. 프리팹 루트 GameObject 구성

```
특수청크이름 (루트 GameObject)
├─ [컴포넌트] TerrainChunk
├─ [컴포넌트] SpriteCavityInitializer
│     └─ Source Sprite: 위에서 임포트한 스프라이트 할당
└─ 자식 오브젝트들 (트랩 엔티티, DiggableRock 등)
```

**Inspector 설정 순서:**

1. 빈 GameObject 생성 후 **Prefab으로 저장** (`Assets/Prefabs/SpecialChunks/`)
2. `Add Component` → **TerrainChunk** 추가
3. `Add Component` → **SpriteCavityInitializer** 추가
4. SpriteCavityInitializer 의 **Source Sprite** 필드에 공동 스프라이트 드래그

> `[RequireComponent(typeof(TerrainChunk))]` 때문에 SpriteCavityInitializer를 먼저 추가하면 TerrainChunk가 자동으로 붙는다.

### 1-3. 자식 오브젝트 배치 (선택)

트랩 엔티티, DiggableRock 등을 자식으로 배치한다.

- **DiggableRock** 자식은 인스펙터에서 배치 위치와 스프라이트를 지정
- 자식 이름 앞에 `ROCK_` 또는 `MINERAL_` **접두사 사용 금지** (풀 반납 로직과 충돌)
- 모든 SpriteRenderer: `Sorting Layer = Default`, `Order in Layer = -1` (지형 뒤)

---

## 2. 패턴 C — 하이브리드 (파기 가능 지형 + 파기 불가 영역 공존)

> 예: 공동 안에 금속 기둥처럼 절대 파낼 수 없는 구조물이 있는 방

### 2-1. 개념

- **파기 가능 부분** → `TerrainChunk + SpriteCavityInitializer` (패턴 A와 동일)
- **파기 불가 부분** → 자식 GameObject에 `IndestructibleOverlayInit` 컴포넌트 추가
  - 초기화 시 자신의 스프라이트 픽셀을 `ChunkData.IndestructibleMask`에 마킹
  - `TerrainModifier`가 파기 시 마스크 체크 → 해당 픽셀 제거 스킵
  - `TerrainCollider`가 충돌 생성 시 해당 픽셀 제외 → IndestructibleOverlay 자체 `PolygonCollider2D`가 충돌 담당
  - BFS 라이팅: 픽셀이 **불투명하게 유지**되므로 빛이 뚫리지 않음

### 2-2. 프리팹 구조

```
HybridChunk (루트 GameObject)
├─ [컴포넌트] TerrainChunk
├─ [컴포넌트] SpriteCavityInitializer
│     └─ Source Sprite: 공동 형태 스프라이트 (1000×1000px)
│
├─ IndestructibleOverlay_A (자식 GameObject)  ← 파기 불가 영역 1
│   ├─ [컴포넌트] SpriteRenderer
│   │     └─ Sprite: 오버레이 스프라이트
│   ├─ [컴포넌트] PolygonCollider2D  ← 에디터에서 직접 폴리곤 그리기
│   ├─ [컴포넌트] IndestructibleOverlayInit
│   └─ [컴포넌트] IndestructibleHitFeedback  (선택 — 타격 시 사운드)
│         ├─ Hit Sound: AudioClip 할당
│         └─ Audio Source: AudioSource 컴포넌트 참조
│
└─ IndestructibleOverlay_B (자식, 선택)        ← 파기 불가 영역 2 (필요 시 추가)
    └─ ...
```

### 2-3. IndestructibleOverlay 스프라이트 준비

#### 스프라이트 크기 규칙

- 1000×1000px일 필요는 **없다**. 오버레이가 차지하는 영역만큼만 제작해도 된다.
  - 예: 가로 200px, 세로 400px 금속 기둥 → 200×400px 스프라이트
- 단, **PPU = 100** 이면 1px = 0.01 유닛이므로 1000×1000px 스프라이트 = 10×10 유닛 = 청크 1칸 전체.
- 공동 스프라이트(1000×1000)보다 **크게 만들지 말 것** — 청크 경계 밖은 마스킹되지 않는다.

#### 픽셀 의미

| 픽셀 상태 | 의미 |
|-----------|------|
| **불투명** (alpha ≥ 10) | 파기 불가 영역으로 마킹됨 → BasePixels에 색상 기록, IndestructibleMask = 1 |
| **투명** (alpha < 10) | 파기 가능 지형 그대로 유지 (마킹 안 함) |

> 파기 불가로 만들 영역만 불투명으로, 나머지는 완전 투명(alpha = 0)으로 그린다.

#### Import 설정

| 항목 | 값 |
|------|-----|
| 아트 스타일 | 지형과 **명확히 다른 색/패턴** (금속 질감, 어두운 돌 등) |
| 외곽 처리 | 1~2px 짙은 아웃라인 권장 (공기 영역과 구분) |
| Texture Type | Sprite (2D and UI) |
| Filter Mode | **Point (no filter)** |
| Compression | **None** |
| **Read/Write Enabled** | **반드시 체크** (픽셀 읽기 필요) |
| **PPU (Pixels Per Unit)** | **100** (TerrainChunk PPU와 동일해야 픽셀 좌표가 1:1 매핑됨) |
| Pivot | 원하는 위치 설정 가능 (기본 Center 권장) |

> PPU가 다르면 오버레이 위치와 실제 마스킹된 픽셀 영역이 어긋난다.

### 2-3-1. Transform 위치와 픽셀 좌표 매핑

`IndestructibleOverlayInit`은 오버레이 GameObject의 **로컬 좌표(transform.localPosition)**를 기반으로 청크 픽셀 좌표를 계산한다.

```
청크 픽셀 좌표 = (localPosition × PPU) + 청크 중심 오프셋(500, 500) - 스프라이트 pivot 오프셋
```

실무 규칙:

- **루트(0, 0)에 맞춘 오버레이 배치**: localPosition = (0, 0)으로 두고 스프라이트 내에서 위치를 직접 픽셀로 그리는 방식이 가장 단순하다.
  - 스프라이트를 1000×1000으로 만들고 원하는 영역만 불투명으로 그리면 위치 계산 불필요.
- **오프셋 배치**: 오버레이를 청크 내 특정 위치에만 표시하려면 localPosition을 조정한다.
  - 예: localPosition = (2, 0) → 청크 중앙에서 오른쪽으로 200px 이동
  - 스프라이트 자체는 작게(예: 200×400px) 만들고 localPosition으로 위치를 잡는 방식.
- 배치 후 **Scene 뷰에서 SpriteRenderer가 시각적으로 원하는 위치에 있으면** 픽셀 마스킹도 그 위치에 적용된다.

> Transform을 수정한 뒤 반드시 Play Mode에서 마스크 위치를 확인할 것. 에디터 오버레이와 런타임 픽셀 마스크는 둘 다 같은 Transform을 기준으로 계산된다.

### 2-4. Inspector 설정 순서

**루트 오브젝트:**
1. 패턴 A와 동일하게 `TerrainChunk + SpriteCavityInitializer` 추가, Source Sprite 할당

**자식 IndestructibleOverlay 오브젝트:**
1. 빈 자식 GameObject 생성 (이름 예: `IndestructibleOverlay_Wall`)
2. `Add Component` → **SpriteRenderer** → 오버레이 스프라이트 할당
   - `Sorting Layer = Default`, `Order in Layer = 1` (TerrainChunk 지형 위에 렌더링)
3. `Add Component` → **PolygonCollider2D**
   - Inspector에서 **Edit Collider** 버튼 클릭
   - Scene 뷰에서 오버레이 스프라이트 윤곽선을 따라 점(Vertex)을 찍어 폴리곤을 직접 그린다
   - 복잡한 형태는 **Create New Shape**로 폴리곤을 여러 개 추가 가능
   - 완료 후 **Edit Collider** 버튼 다시 클릭하여 편집 종료
   - > PolygonCollider2D의 폴리곤은 픽셀 마스크와 독립적이다. 스프라이트 외곽과 정확히 일치시킬 필요는 없으며, 게임플레이상 플레이어가 막혀야 하는 영역을 감싸면 된다.
4. `Add Component` → **IndestructibleOverlayInit**
   - 별도 설정 필드 없음. 자동으로 SpriteRenderer 스프라이트를 읽어 마스크 적용
5. (선택) `Add Component` → **AudioSource**
   - 3D 사운드라면 `Spatial Blend = 1`, 아니면 0
6. (선택) `Add Component` → **IndestructibleHitFeedback**
   - `Hit Sound` 필드에 AudioClip 드래그
   - `Audio Source` 필드에 같은 GameObject의 AudioSource 컴포넌트 드래그

### 2-4-1. 여러 IndestructibleOverlay 배치 시 주의사항

같은 청크에 오버레이를 **여러 개** 배치하는 것은 지원된다. 각각 독립적으로 마스킹된다.

- 자식 GameObject를 원하는 수만큼 추가하면 된다 (IndestructibleOverlay_A, _B, _C …)
- 각 오버레이는 **자신의 스프라이트와 Transform**을 기준으로 독립 계산됨
- 오버레이끼리 픽셀 영역이 겹쳐도 괜찮다 (나중에 처리된 오버레이가 덮어쓰지만 결과는 동일)
- 오버레이가 많을수록 초기화 시간이 증가한다 — 불필요하게 쪼개지 말 것

### 2-5. 초기화 실행 순서

`SpecialChunkManager`는 `GetComponentsInChildren<IChunkInitializer>()`로 탐색 후 `InitializationOrder` 오름차순 실행한다.
루트 컴포넌트가 자식보다 항상 먼저 반환되므로 순서가 자동 보장된다.

```
[1] SpriteCavityInitializer.Initialize()  ← 루트 (전체 공동 형태 설정)
      → 투명 픽셀 → BasePixels[idx] = air

[2] IndestructibleOverlayInit.Initialize()  ← 자식 (파기 불가 영역 덮어쓰기)
      → 불투명 픽셀 → BasePixels[idx] = 오버레이 색상 (air 복원)
      → IndestructibleMask[idx] = 1  (파기/충돌 제외 마킹)
```

> SpriteCavityInitializer가 air로 비워둔 자리를 IndestructibleOverlayInit이 다시 채우는 구조.
> 순서가 바뀌면 공동이 오버레이 픽셀을 덮어 마스크가 작동 안 한다.

### 2-6. 실전 예시: 공동 안 금속 기둥

아래는 공동(빈 방) 중앙에 파낼 수 없는 금속 기둥 하나를 배치하는 최소 구성이다.

**스프라이트 작업:**
- `CaveRoom_Cavity.png` (1000×1000) — 방 모양 공동. 방 내부(바닥·천장·벽 제외)를 투명으로 그린다.
- `MetalPillar.png` (100×300) — 기둥 형태. 전부 불투명. PPU=100.

**프리팹 구성:**
```
MetalPillarRoom (루트)
├─ TerrainChunk
├─ SpriteCavityInitializer  (Source Sprite = CaveRoom_Cavity)
│
└─ MetalPillar (자식, localPosition = (0, -1))  ← 방 하단 중앙에 배치
    ├─ SpriteRenderer (Sprite = MetalPillar, Order in Layer = 1)
    ├─ PolygonCollider2D  ← 기둥 사각형 외곽으로 폴리곤 그리기
    ├─ IndestructibleOverlayInit
    └─ IndestructibleHitFeedback (선택)
```

**결과:**
- 방 내부는 공동(파기 가능)
- 기둥 픽셀(100×300)은 IndestructibleMask에 마킹 → 곡괭이로 파도 제거 안 됨
- 기둥 PolygonCollider2D가 플레이어 충돌 담당 → 기둥을 통과하지 못함

---

## 4. 패턴 B — `LargeStaticTerrainChunk` (정적 표시, 파기 불필요)

### 2-1. 구조

```
특수청크이름 (루트 GameObject)
├─ [컴포넌트] LargeStaticTerrainChunk
│     ├─ Chunk Grid Width: 가로 차지 청크 수 (예: 4 = 4000px)
│     └─ Chunk Grid Height: 세로 차지 청크 수 (기본 1)
├─ [컴포넌트] SpriteRenderer
│     └─ Sprite: 표시할 스프라이트
└─ [컴포넌트] PolygonCollider2D
```

> `[RequireComponent]`로 **SpriteRenderer** 와 **PolygonCollider2D** 가 자동 추가된다.

### 2-2. 스프라이트 Import 설정

| 항목 | 값 |
|------|-----|
| Texture Type | Sprite (2D and UI) |
| Filter Mode | Point (no filter) |
| Compression | None |
| Read/Write Enabled | **불필요** (픽셀 읽기 없음) |

### 2-3. 멀티청크 크기 설정

예: 가로 4청크(4000px) 짜리 구조물:
- `Chunk Grid Width = 4`
- `Chunk Grid Height = 1`

SpecialChunkDef 의 **chunkSizeX/Y 는 무시**되고 이 값이 우선 사용된다 (`GetSize()` 참고).

---

## 5. SpecialChunkManager 에 프리팹 등록

씬의 **SpecialChunkManager** 게임오브젝트를 선택하고 Inspector를 편집한다.

### 3-1. Pools 리스트 구조

```
Pools
└─ [0] SpecialChunkPool
      ├─ Target Layer: Dirt  ← 이 지층에서 등장
      └─ Chunks
            ├─ [0] SpecialChunkDef
            │      ├─ Prefab: (프리팹 드래그)
            │      ├─ Spawn Chance: 5  (0~100%, 청크당 확률)
            │      ├─ Chunk Type: CollapseFloor  ← SpecialChunkType enum
            │      ├─ Min Depth: 0  (0 = 제한 없음)
            │      ├─ Max Depth: 0  (0 = 제한 없음)
            │      ├─ Chunk Size X: 1
            │      └─ Chunk Size Y: 1
            └─ [1] ...
```

### 3-2. 각 필드 설명

| 필드 | 설명 |
|------|------|
| `Target Layer` | 이 풀이 적용되는 지층 (`Dirt` / `Ice` / `MagmaRock` / `MeteoriteRock`) |
| `Prefab` | 루트 컴포넌트가 `TerrainChunk` 또는 `LargeStaticTerrainChunk` 인 프리팹 |
| `Spawn Chance` | 0~100 사이 float. 해당 좌표에서 이 청크가 선택될 확률 |
| `Chunk Type` | `SpecialChunkType` enum 값. 기믹 스크립트 연동용 |
| `Min Depth` / `Max Depth` | Y 좌표 절댓값 기준 깊이 제한 (0 = 제한 없음) |
| `Chunk Size X/Y` | 차지하는 청크 수. **LargeStaticTerrainChunk 이면 무시** |

### 3-3. 지층별 Y 범위 참고

| 지층 | Y 청크 좌표 | Depth 범위 |
|------|------------|-----------|
| Dirt | 0 ~ -19 | 0 ~ 19 |
| Ice | -20 ~ -39 | 20 ~ 39 |
| MagmaRock | -40 ~ -59 | 40 ~ 59 |
| MeteoriteRock | -60 ~ -79 | 60 ~ 79 |

---

## 6. 스프라이트 레이어 설정 (Sorting Layer)

모든 시각 요소에 Sorting Layer를 반드시 지정한다.

| 오브젝트 | Sorting Layer | Order in Layer |
|---------|--------------|---------------|
| 지형 텍스처 (TerrainChunk) | Default | 0 |
| 암석 (DiggableRock) | Default | **-1** |
| 자식 장식물 | Default | **2** |
| **IndestructibleOverlay (패턴 C)** | Default | **1** (지형 위에 보여야 함) |
| LargeStaticTerrainChunk | Default | 원하는 값 |

---

## 7. 최종 체크리스트

프리팹 생성 후 아래 항목을 모두 확인한다.

### 프리팹 구조
- [ ] 루트에 `TerrainChunk` 또는 `LargeStaticTerrainChunk` 중 하나만 있다
- [ ] `IChunk` 구현체가 루트에 존재한다 (`GetComponent<IChunk>()` 성공 조건)
- [ ] 자식 이름에 `ROCK_` / `MINERAL_` 접두사가 없다

### SpriteCavityInitializer 사용 시
- [ ] `Source Sprite` 가 할당되어 있다
- [ ] 해당 스프라이트의 `Read/Write Enabled` 가 켜져 있다
- [ ] 스프라이트 크기가 1000×1000px 이다 (다르면 경고 + 일부만 복사)

### IndestructibleOverlay 사용 시 (패턴 C)
- [ ] 루트에 `SpriteCavityInitializer` 가 있다 (공동 형태 먼저 설정)
- [ ] 자식 오버레이 오브젝트에 `IndestructibleOverlayInit` 이 있다
- [ ] 오버레이 스프라이트의 `Read/Write Enabled` 가 켜져 있다
- [ ] 오버레이 스프라이트의 **PPU = 100** (TerrainChunk PPU와 동일)
- [ ] `PolygonCollider2D` 폴리곤을 에디터에서 직접 그렸다
- [ ] `IndestructibleOverlay` SpriteRenderer `Order in Layer = 1`

### SpecialChunkManager 등록
- [ ] 알맞은 `Target Layer` 풀에 등록했다
- [ ] `Prefab` 필드가 비어있지 않다
- [ ] `Chunk Type` 이 `None` 이 아니다
- [ ] 멀티청크라면 `Chunk Size X/Y` 또는 `chunkGridWidth/Height` 가 올바르게 설정되어 있다

### 비주얼 스타일
- [ ] 3D 메시, 3D 파티클 사용 안 함 (픽셀아트 2D 스프라이트만 사용)
- [ ] `Filter Mode = Point (no filter)` 설정 확인
- [ ] `Compression = None` 설정 확인

---

## 8. 자주 하는 실수

| 실수 | 증상 | 해결 |
|------|------|------|
| `Read/Write Enabled` 미설정 | `Texture is not readable` 에러, 공동 생성 실패 | Import Settings에서 체크 |
| `Source Sprite` 미할당 | 에러 로그 출력, 공동 없이 일반 지형으로 채워짐 | 스프라이트 드래그 할당 |
| 공동 청크에 `LargeStaticTerrainChunk` 사용 | 벽 파기 불가, 공동 진입 불가 | `TerrainChunk + SpriteCavityInitializer` 로 교체 |
| `TerrainChunk` + 공동 스프라이트 없음 | Phase 1에서 일반 지형으로 채워져 공동 사라짐 | `SpriteCavityInitializer` 추가 및 스프라이트 할당 |
| `Chunk Type = None` | 기믹 스크립트 연동 안 됨 | 올바른 enum 값 선택 |
| 잘못된 `Target Layer` | 해당 지층에서 절대 등장 안 함 | 지층 확인 후 올바른 풀에 등록 |
| 자식 이름에 `ROCK_` 접두사 | 청크 언로드 시 자식이 풀로 반납 시도 → NullRef | 접두사 제거 |
| **오버레이 PPU가 100이 아님** | 오버레이 스프라이트와 마스크 픽셀 위치 어긋남 | Import Settings에서 PPU = 100 설정 |
| **`IndestructibleOverlayInit` 없이 오버레이 배치** | 시각적으로는 보이지만 파기 가능 → 픽셀 파임 | `IndestructibleOverlayInit` 컴포넌트 추가 |
| **초기화 순서 역전** (오버레이 루트에 배치) | 공동이 오버레이 픽셀을 air로 덮어 마스크 무효 | `SpriteCavityInitializer`는 루트, `IndestructibleOverlayInit`은 자식에 배치 |
| **`PolygonCollider2D` 폴리곤 미설정** | 오버레이 물리 충돌 없음 → 플레이어 통과 | Edit Collider로 폴리곤 직접 그리기 |
| **오버레이 `Order in Layer = 0`** | 지형 텍스처에 가려져 오버레이 안 보임 | `Order in Layer = 1` 설정 |

---

## 9. 내부 동작 요약 (참고)

```
[Phase 1] ChunkDataProvider가 해당 좌표 선택
          → SpecialChunkManager.SpawnSpecialChunkIfPossible()
          → Instantiate(prefab)
          → IChunkInitializer.Initialize() 호출 (SpriteCavityInitializer 등)
          → NeedsDelayedActivation 이면 SetActive(false)

[Phase 2] ChunkGenerationPipeline
          → 루트에 IChunkInitializer 있으면 → 데코레이션 스킵 (프리팹 보존)
          → 루트에 IChunkInitializer 없으면 → 일반 데코레이션 실행

[Phase 3] UpdateBoundaryLighting 실행
```

> `IChunkInitializer` 가 루트에 있으면 랜덤 암석/광물/엘리베이터가 **추가되지 않는다.**
> 특수 청크 내부에 암석이 필요하다면 프리팹 자식으로 직접 배치할 것.

**패턴 C 추가 흐름:**

```
[Phase 1 - SpriteCavityInitializer]
  → 투명 픽셀 → BasePixels[idx] = air(0,0,0,0)
  → CurrentPixels 동기화

[Phase 1 - IndestructibleOverlayInit (자식)]
  → 스프라이트 불투명 픽셀 읽기
  → 로컬 좌표 × PPU(100) - pivot = 청크 픽셀 좌표
  → BasePixels[idx] = 오버레이 색상  (air 복원)
  → IndestructibleMask[idx] = 1
  → CurrentPixels 동기화

[런타임 - 파기 시도]
  → TerrainModifier: IndestructibleMask[idx] == 1 → 파기 스킵
  → TerrainCollider: IndestructibleMask[idx] == 1 → 폴리곤에서 제외
  → IndestructibleHitFeedback: IIndestructibleHit.OnHitAttempt() → 사운드 재생
```
