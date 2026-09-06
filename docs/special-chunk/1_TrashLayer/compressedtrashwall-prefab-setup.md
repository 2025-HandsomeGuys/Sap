# CompressedTrashWall 프리팹 구성 가이드
@tags: special-chunk, compressedtrashwall, prefab, setup, guide, IChunkInitializer

> 특수 청크가 미리 파놓은 공동(空洞) 안에 배치되는 쓰레기 봉투.
> 플레이어가 공동 벽을 파고 들어올 때 곡괭이로 채굴할 수 있다.

---

## 1. 개요 및 동작 흐름

### DiggableRock과의 차이

| | DiggableRock | CompressedTrashWall |
|---|---|---|
| 초기 상태 | 지형 픽셀 속에 매립 | **특수 청크가 미리 판 공동 안에 배치** |
| 노출 트리거 | ClearHole()로 봉투 주변 픽셀 직접 제거 | 공동은 이미 비어있음 → **ClearHole 불필요** |
| RevealInTerrain 역할 | 지형 픽셀 제거 + 활성화 | **불필요** (preExposed=true로 처음부터 활성) |

### 동작 흐름

```
[특수 청크 선택] SpecialChunkSelector가 확률적으로 CompressedTrashWall 선택
                  → SpecialChunkManager.SpawnSpecialChunkIfPossible() 호출
  ↓
[청크 스폰] LargeStaticTerrainChunk 프리팹 Instantiate
            → Initialize(coord) 호출 → SetActive(true)
            → 자식으로 배치된 DiggableRock(쓰레기 봉투)이 함께 활성화
  ↓
[봉투 대기] DiggableRock.Start()에서 preExposed=true → 숨김 처리 스킵
            → Renderer·Collider 처음부터 활성화
            → 인접 일반 TerrainChunk 벽이 막혀있으므로 실질적으로 접근 불가
  ↓
[벽 파기] 플레이어가 공동 인접 TerrainChunk를 파서 공동 진입
  ↓
[채굴] 곡괭이(toolIndex=2)로 DiggableRock 타격 → HP 감소 → 크랙 비주얼
  ↓
[파괴] HP 소진 → 광물/스크랩 드롭 → GameObject 비활성화
```

---

## 2. 전제조건 확인

### 2.1 SpecialChunkManager가 씬에 있는지 확인

Hierarchy에서 `SpecialChunkManager` 컴포넌트를 가진 GameObject가 있어야 한다.

**없으면:** 아무 GameObject에 `SpecialChunkManager` 컴포넌트를 추가한다.

**확인 방법 (플레이 중 콘솔):**
```
[SpecialChunkManager] specialChunkSettings.json 적용 완료
```
이 로그가 뜨면 정상. 안 뜨면 컴포넌트가 씬에 없는 것.

---

## 3. 필요 에셋

### 3.1 청크 이미지 (공동 포함)

**파일명 예시:** `TrashWallChunk.png`

- **크기:** 1000×1000 px (청크 1칸 기준)
- **공동(빈 공간):** 투명 픽셀(알파=0)로 표현
- **지형:** 불투명 픽셀로 표현 (DiggableRock이 배치될 공간 주위)

**Import 설정 (Texture Import Inspector):**

| 항목 | 값 |
|---|---|
| Texture Type | `Sprite (2D and UI)` |
| Sprite Mode | `Single` |
| Filter Mode | **`Point (no filter)`** ← 픽셀아트 필수 |
| Compression | **`None`** |
| Read/Write Enabled | **체크 해제 OK** (LargeStaticTerrainChunk는 픽셀 읽기 불필요) |
| Pixels Per Unit | 100 |
| Pivot | **`Bottom Left`** (또는 Sprite Editor에서 Custom → 0, 0) |

> **Pivot = Bottom Left 필수!**
> `ChunkCoords.ToWorld(coord)` 는 청크 **좌하단** 월드 좌표를 반환한다.
> Pivot이 Center면 이미지가 청크 기준보다 반칸 위/오른쪽으로 어긋난다.

### 3.2 봉투 스프라이트 (3장)

| 파일명 (예시) | 용도 | 크기 권장 |
|---|---|---|
| `TrashBag_Normal.png` | HP 67~100% | 32×40 px 이상 |
| `TrashBag_Crack1.png` | HP 34~66% | 동일 크기 |
| `TrashBag_Crack2.png` | HP 0~33% | 동일 크기 |

**Import 설정:**

| 항목 | 값 |
|---|---|
| Filter Mode | **`Point (no filter)`** |
| Compression | **`None`** |
| Pivot | `Bottom Center` 권장 |

---

## 4. 봉투(DiggableRock) 프리팹 구성

### 4.1 컴포넌트 목록

```
CompressedTrashBag (GameObject — 봉투 프리팹)
├─ SpriteRenderer
├─ PolygonCollider2D
├─ DiggableRock
└─ DamageStagedVisuals
```

> 이 프리팹은 나중에 청크 프리팹의 **자식**으로 에디터에서 배치된다.

### 4.2 SpriteRenderer 설정

| 필드 | 값 |
|---|---|
| Sprite | `TrashBag_Normal` |
| Color | (1, 1, 1, 1) |
| Sorting Layer | `Default` |
| Order in Layer | **-1** ← 지형(0) 뒤에 렌더링 |

> Order in Layer = -1 이면 지형보다 뒤에 렌더링된다.
> 공동이 열린 후 플레이어가 진입하면 보인다.

### 4.3 PolygonCollider2D 설정

| 필드 | 값 |
|---|---|
| Enabled | **true** ← preExposed=true이므로 처음부터 활성 |
| Is Trigger | false |

### 4.4 DiggableRock 설정

| 필드 | 값 | 이유 |
|---|---|---|
| `selfRegister` | **false** | 부모가 LargeStaticTerrainChunk (TerrainChunk 아님) → 자동 등록 불가 |
| `preExposed` | **true** | 공동이 이미 비어있으므로 처음부터 Renderer·Collider 활성 유지 |
| `minExposedPixels` | 0 | preExposed이므로 즉시 활성화 (기본값 변경 불필요) |
| `MaxHp` | 10 | 코드에서만 적용됨 (Inspector는 참고용) |
| `tileType` | 해당 지층 TileType | 광물 드롭 규칙 참조 (`Dirt`, `HardStone` 등) |

### 4.5 DamageStagedVisuals 설정

| 필드 | 값 |
|---|---|
| `stages` 배열 크기 | 3 |
| `stages[0]` | `TrashBag_Normal` |
| `stages[1]` | `TrashBag_Crack1` |
| `stages[2]` | `TrashBag_Crack2` |

---

## 5. 청크 프리팹 구성 (TerrainChunk + SpriteCavityInitializer)

> ⚠️ **LargeStaticTerrainChunk는 여기서 사용하지 않는다.**
> CompressedTrashWall은 공동 주변 벽이 파기 가능해야 하므로 `TerrainChunk`를 써야 한다.
> `LargeStaticTerrainChunk`는 완전히 정적(파기 불가) 구조물에만 사용한다.
> (CLAUDE.md 섹션 5-A 참조)

### 5.1 계층 구조

```
CompressedTrashWallChunk (GameObject — 특수 청크 루트)
├─ TerrainChunk              ← IChunk 구현체. 픽셀 파기 가능
├─ SpriteCavityInitializer   ← IChunkInitializer: 스프라이트 투명 픽셀 → 공동
│    sourceSprite: 공동 모양 이미지
└─ TrashBag_1 (자식)         ← 에디터에서 미리 배치한 DiggableRock 프리팹 인스턴스
   ├─ (봉투 2개 이상 배치 시 TrashBag_2, TrashBag_3 추가)
```

> **설계 원칙:** 봉투 오브젝트는 에디터에서 자식으로 직접 배치.
> 런타임 `Instantiate()`로 동적 생성하지 않는다.

### 5.2 Transform (루트)

| 필드 | 값 |
|---|---|
| Position | (0, 0, 0) — 런타임에 주입됨 |
| Rotation | (0, 0, 0) |
| Scale | (1, 1, 1) |

### 5.3 TerrainChunk 설정

`TerrainChunk`는 Inspector에서 건드릴 필드가 거의 없다. 컴포넌트를 추가하는 것만으로 충분.

| 필드 | 값 |
|---|---|
| `pixelsPerUnit` | 100 (프로젝트 기본값) |

### 5.4 SpriteCavityInitializer 설정

| 필드 | 값 |
|---|---|
| `sourceSprite` | 공동 모양 스프라이트 (투명=공동, 불투명=지형) |

**이 스프라이트의 Import 설정 추가 요건:**

| 항목 | 값 |
|---|---|
| **Read/Write Enabled** | **✅ 체크 필수** ← 없으면 런타임 에러 |
| Filter Mode | `Point (no filter)` |
| Compression | `None` |
| Pivot | **`Bottom Left`** |

> **Pivot = Bottom Left 필수!**
> `ChunkCoords.ToWorld(coord)` 는 청크 **좌하단** 월드 좌표를 반환한다.
> Pivot이 Center면 이미지가 청크 그리드와 어긋난다.

### 5.5 자식 봉투 Transform 설정

봉투가 공동 안에 위치하도록 에디터에서 직접 이동.

> 청크 루트 기준 로컬 좌표 예시 (1000px 청크, PPU=100 기준):
> - 청크 크기 = 10 유닛 (1000px ÷ 100 PPU)
> - 공동 중앙이 청크 좌하단에서 (5유닛, 5유닛) 이면 → 자식 LocalPosition = (5, 5, 0)

---

## 6. SpecialChunkManager 등록

### 6.1 Inspector에서 Pools 설정

1. Hierarchy에서 `SpecialChunkManager` 컴포넌트를 가진 GameObject 선택
2. Inspector → `Pools` 리스트에서 해당 지층의 `SpecialChunkPool` 찾기 (없으면 `+` 버튼으로 추가)
3. `SpecialChunkPool.targetLayer` = 이 청크가 생성될 지층 (예: `Dirt`, `HardStone`)
4. `Chunks` 리스트에 항목 추가

### 6.2 SpecialChunkDef 필드 설정

| 필드 | 값 | 주의사항 |
|---|---|---|
| `prefab` | `TerrainChunk` 컴포넌트 | ⚠️ **아래 주의 참조** |
| `spawnChance` | 원하는 확률 (예: `15`) | 0~100 범위 |
| `chunkType` | `CompressedTrashWall` | |
| `minDepth` | `0` (제한 없음) | |
| `maxDepth` | `0` (제한 없음) | |
| `chunkSizeX` | `1` | |
| `chunkSizeY` | `1` | |

> ### ⚠️ prefab 필드 할당 방법 (가장 자주 막히는 부분)
>
> `SpecialChunkDef.prefab`의 타입은 `MonoBehaviour`이다.
> Unity Inspector에서 GameObject를 드래그하면 연결이 **안 된다**.
>
> **올바른 방법:**
> 1. Project 창에서 `CompressedTrashWallChunk` 프리팹을 씬에 임시로 드래그해서 배치
> 2. Hierarchy에서 해당 오브젝트 클릭 → Inspector에서 `TerrainChunk` 컴포넌트 헤더 확인
> 3. `TerrainChunk` 컴포넌트 헤더 오른쪽의 **⋮ 메뉴 → Copy Component**
> 4. SpecialChunkManager Inspector로 돌아가 `prefab` 필드 옆의 작은 원(◎)을 클릭
> 5. 검색창에 `CompressedTrashWallChunk` 입력 → 프리팹 선택
>
> **또는 더 간단한 방법:**
> - `prefab` 필드에 Project 창에서 프리팹을 직접 드래그하되,
>   드래그 후 Inspector의 `prefab` 필드에 **컴포넌트 이름(`TerrainChunk`)이 표시**되면 성공.
>   **`GameObject`라고 표시되면 실패** — 다시 시도해야 함.

---

## 7. 동작 확인 (로그 흐름)

플레이 모드에서 아래 순서로 콘솔 로그가 나와야 정상이다.

### 정상 흐름

```
[TRACE][ChunkDataProvider] GetContext 진입: (x, y)
[SpecialChunkManager] SpawnSpecialChunkIfPossible 호출: coord=(x, y), layer=Dirt
[SpecialChunkSelector] (x, y) prefab=CompressedTrashWallChunk roll=12.3 chance=15 → 당첨
[SpecialChunkManager] (x, y) → 선택됨: CompressedTrashWallChunk
[TRACE][ChunkDataProvider] (x, y) → 1-B 특수청크 스폰 성공: Special_(x)_(y)
```

### 문제별 로그와 원인

| 보이는 로그 | 원인 | 해결 |
|---|---|---|
| `[SpecialChunkManager] SpawnSpecialChunkIfPossible 호출` 자체가 없음 | SpecialChunkManager가 씬에 없거나 Pools가 비어있음 | Hierarchy에서 SpecialChunkManager 컴포넌트 확인 |
| `풀 없음 (poolIndex=-1)` | Pool의 `targetLayer`와 청크 생성 레이어가 불일치 | Pool의 targetLayer 값 확인 |
| `prefab null, 스킵` | SpecialChunkDef.prefab 필드가 비어있음 | prefab 필드에 LargeStaticTerrainChunk 컴포넌트 할당 |
| `roll=XX.X chance=15.0 → 꽝` | 이 좌표에서 확률 탈락 (정상) | 다른 좌표에서 재시도하거나 spawnChance 올리기 |
| `레이어 경계 근처, 스킵` | 지층 경계 Y 좌표 근처 | 더 깊은 위치에서 테스트 |
| `[SpecialChunkManager] Prefab '...' missing IChunk` | 프리팹에 LargeStaticTerrainChunk(또는 TerrainChunk)가 없음 | 루트 오브젝트에 LargeStaticTerrainChunk 컴포넌트 추가 |
| 청크는 나오는데 봉투가 안 보임 | DiggableRock 자식이 프리팹 안에 없음 / preExposed=false | 자식 배치 확인, preExposed=true 확인 |

---

## 8. 체크리스트

### 씬 확인

- [ ] `SpecialChunkManager` 컴포넌트를 가진 GameObject가 Hierarchy에 있음
- [ ] `SpecialChunkManager.Pools`에 적절한 `targetLayer`의 Pool이 있음

### CompressedTrashWallChunk 프리팹 루트

- [ ] `TerrainChunk` 컴포넌트 부착 (`LargeStaticTerrainChunk` **사용 금지** — 벽이 파기 가능해야 함)
- [ ] `SpriteCavityInitializer` 컴포넌트 부착
- [ ] `SpriteCavityInitializer.sourceSprite` 할당
- [ ] `sourceSprite` Import: `Read/Write Enabled = ✅`, `Filter Mode = Point`, `Compression = None`, `Pivot = Bottom Left`

### SpecialChunkDef 등록

- [ ] `prefab` 필드에 `TerrainChunk` 컴포넌트가 표시됨 (`GameObject`가 아님)
- [ ] `spawnChance` > 0
- [ ] `chunkType` = `CompressedTrashWall`
- [ ] `chunkSizeX` = 1, `chunkSizeY` = 1

### 봉투 자식(DiggableRock)

- [ ] 자식 GameObject가 프리팹 내에 배치되어 있음 (에디터에서 직접)
- [ ] `selfRegister` = **false**
- [ ] `preExposed` = **true**
- [ ] SpriteRenderer `Order in Layer` = **-1**
- [ ] `PolygonCollider2D` Enabled = **true** (preExposed이므로 활성 상태로 시작)
- [ ] `DamageStagedVisuals.stages` 배열 크기 = 3, 스프라이트 3장 할당
- [ ] `DiggableRock.tileType` = 해당 지층 TileType

---

## 9. SortingOrder 레이어 구조

```
Order  오브젝트
  0    TerrainChunk (청크 지형 텍스처, SpriteCavityInitializer가 공동 생성)
 -1    DiggableRock 자식 (봉투) ← 지형 뒤에 위치하지만 공동에서는 보임
```

---

## 10. 주의사항

- **TerrainChunk 사용 필수:** 공동 주변 벽을 플레이어가 파낼 수 있어야 한다. `LargeStaticTerrainChunk`는 파기 불가 구조물에만 사용.
- **Read/Write Enabled 필수:** `SpriteCavityInitializer`가 스프라이트 픽셀을 읽으려면 텍스처에 Read/Write가 활성화되어야 한다.
- **Pivot 필수:** 스프라이트 Pivot이 Bottom Left가 아니면 청크 이미지가 청크 그리드와 어긋남.
- **selfRegister=false 필수:** 부모가 TerrainChunk지만 자동 등록 대신 IChunkInitializer로 처리. (현재 구현에서는 preExposed=true이면 직접 상호작용 가능하므로 등록 없이도 동작)
- **preExposed=true 필수:** false이면 Start()에서 Renderer·Collider가 꺼져 봉투가 보이지 않고 상호작용 불가.
- **DirtPatch 불필요:** 공동이 미리 비어있으므로 봉투 위에 DirtPatch를 배치하지 않는다.
- **HP 값:** `DiggableRock.MaxHp`는 `[System.NonSerialized]`로 코드에서만 설정 가능. Inspector에서 보이는 값은 참고용 기본값.
- **PolygonCollider2D 경로 보존:** 스프라이트 교체 시 Unity가 콜라이더를 자동 재생성하는 버그가 있음. `DamageStagedVisuals.ApplySprite()`가 자동 처리하므로 별도 코드 불필요.
