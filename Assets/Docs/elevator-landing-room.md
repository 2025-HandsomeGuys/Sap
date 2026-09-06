# 엘리베이터 착지 방 분리 설계

**작성일**: 2026-07-26
**목적**: 구멍 입구로 진입했을 때의 지하 도착 지점과, 엘리베이터로 이동했을 때의 도착 지점을 서로 다른 방으로 만든다. 엘리베이터 방은 지층별로 다른 PNG를 쓴다.

---

## 1. 현재 상태와 문제

씬(`DemoUnderground`)에 `ImageChunkOverrider`가 **1개**만 존재한다. 청크 `(0,0)`에 구멍 입구 PNG를 덮어쓰고 플레이어를 활성화한다.

`ElevatorManager.TeleportPlayer()`는 텔레포트할 때마다 이 **하나뿐인 인스턴스**를 `FindFirstObjectByType`으로 찾아 `SetTargetChunk()`로 재사용한다. 결과적으로 엘리베이터 착지 지점에도 구멍 입구와 똑같은 PNG가 칠해진다.

조사 과정에서 확인된 결함은 3개다.

### 1-A. 착지 후 낙하 (근본 원인)

`TeleportPlayer()`는 `player.position`만 대입하고 **목적지 청크 로드를 기다리지 않는다.** 씬 시작 경로인 `PlayerSpawner`는 Rigidbody를 Kinematic으로 묶고 `HasChunk()` + 콜라이더 갱신까지 기다린 뒤 Dynamic으로 푸는데, 엘리베이터에는 그 보호가 없다.

런타임 계측 결과(얼음층 이동):

```
t=0.0s  playerY=-195.00  목적지청크(0,-20) 로드됨=False   ← 착지, 콜라이더 없음
t=0.5s  playerY=-197.52  로드됨=True                      ← 낙하 중 청크 로드
t=1.0s  playerY=-199.80                                    ← 지형 속에 박혀 정지
```

청크 `(0,-20)`은 월드 Y `-200 ~ -190`. 엘리베이터는 `-195`, 굴착 반경은 1.5유닛(`-196.5 ~ -193.5`)이다. 플레이어는 굴착 구역 아래 단단한 지형 속 `-199.8`에 묻혔고, 엘리베이터는 4.8유닛 위에 있었다. **"엘리베이터가 안 보인다"의 실제 원인이 이것이다.**

0층에서 정상으로 보였던 이유: 목적지 `(0,0)`이 이미 로드돼 있어 콜라이더가 존재했고, 낙하가 없었다.

### 1-B. 페인팅 대상이 한 칸 아래

`ElevatorManager.cs`가 `chunkY - 1`을 넘긴다. 청크 피벗은 좌하단(`ChunkCoords.ToWorld = coord * 10`)이고 엘리베이터는 로컬 `(5,5)`이므로 `FloorToInt((coord.y*10+5)/10) == coord.y`다. 즉 `chunkY - 1`은 **엘리베이터 청크의 바로 아래 청크**를 가리킨다.

0층에서 문제가 드러나지 않았던 이유: 착지 청크 `(0,0)`은 구멍 입구 PNG가 이미 칠해 놓은 청크라, 새로 칠해진 `(0,-1)`은 발밑이라 눈에 띄지 않았다.

### 1-C. 보호 좌표 갱신 누락

`ImageChunkOverrider.Awake()`가 `RegisterProtectedCoord(targetChunkCoord)`를 호출하지만, `SetTargetChunk()`는 등록을 갱신하지 않는다. 엘리베이터 착지 청크는 절차 바위·광물 스폰이 차단되지 않는다.

---

## 2. 설계

### 2-A. 착지 보호 (`ElevatorManager`)

`TeleportPlayer()`를 코루틴화하고 `PlayerSpawner`와 동일한 패턴을 적용한다.

```
Rigidbody2D → Kinematic 고정
  → 추정 위치(CalculateElevatorPosition)로 이동
  → HasChunk(목적지 청크) 대기 (타임아웃 5초)
  → 실제 엘리베이터가 생성됐으면 그 transform.position으로 위치 재보정
  → 1프레임 대기 (콜라이더 생성)
  → Dynamic 복귀
```

타임아웃 시에는 에러 로그를 남기고 Dynamic으로 복귀시킨다(플레이어가 Kinematic에 갇히는 상태를 만들지 않는다).

위치 재보정 단계가 필요한 이유: 추정 좌표와 실제 엘리베이터 위치는 현재 일치하지만, 향후 `elevatorYOffset` 같은 값이 활성화되면 어긋날 수 있다. 로드된 실제 위치를 신뢰한다.

### 2-B. 페인팅 코어 추출

`ImageChunkOverrider`에 있는 픽셀 덮어쓰기 로직을 static 유틸로 분리한다.

```
ChunkImagePainter.Paint(TerrainChunk chunk, Texture2D terrain, Texture2D border) → bool
```

담당 범위:
- `terrain.isReadable` 검증 (실패 시 에러 로그 + false 반환)
- 청크 크기와 다르면 최근접 리샘플링
- alpha > 10 → `PixelInfo = 1`(solid), 그 외 0(air)
- `border`가 있으면 테두리 텍스처 적용, null이면 기존 BorderData 유지
- `chunk.EnsureJobsCompleted()` → `chunk.LoadChunkData(...)` → `data.MarkDirty()` → `chunk.ForceUpdateCollider()`

**`ForceUpdateCollider()`가 필수인 이유**: 일반 경로인 `TryUpdateCollider()`는 `colliderUpdateInterval`(worldSettings.json, 현재 0.2초) 스로틀에 걸린다. 청크 로드 직후 `Reuse_Step2_Finalize()`가 절차 지형으로 콜라이더를 만들면서 `_lastColliderUpdateTime`을 갱신해 두므로, 페인팅 후 콜라이더 재생성까지 꼬박 0.2초(60fps 기준 ~12프레임)가 걸린다. 그 사이 **픽셀은 방인데 콜라이더는 꽉 찬 지형**이다.

`PlayerSpawner`와 `ElevatorManager.TeleportRoutine`은 모두 1프레임만 기다리고 Rigidbody를 Dynamic으로 푼다. 스로틀 구간에 걸리면 플레이어가 꽉 찬 콜라이더 안에서 물리가 켜져 depenetration으로 지형 아래까지 튕겨나간다. (실측: 스폰 y=8 → y=-9.86, 입구 청크를 통과해 아래 청크까지)

**정리 사항**: 현재 `ImageChunkOverrider`는 `data.HasBeenModified = true`와 `chunk.isDirty = true`를 직접 세팅한다. CLAUDE.md 아키텍처 제약 3에 따라 `data.MarkDirty()` 호출로 교체한다.

호출측(청크 로드 대기, 보호 좌표 등록, 플레이어 활성화)은 유틸에 넣지 않는다 — 두 컴포넌트의 책임이 다르기 때문이다.

### 2-C. 두 컴포넌트

| 컴포넌트 | 역할 | 대상 좌표 | 부가 책임 |
|---|---|---|---|
| `ImageChunkOverrider` (기존) | 구멍 입구 방 | 인스펙터 고정 (`(0,0)`) | `playerObj.SetActive(true)` |
| `ElevatorRoomPainter` (신규) | 엘리베이터 착지 방 | `ElevatorManager`가 지정 | 없음 |

`ImageChunkOverrider`에서 제거할 것:
- `SetTargetChunk()` — 엘리베이터가 더 이상 이 컴포넌트를 재사용하지 않으므로 불필요
- 페인팅 본문 — `ChunkImagePainter.Paint()` 호출로 대체

`ElevatorManager`에서 제거할 것:
- `FindFirstObjectByType<ImageChunkOverrider>()` + `SetTargetChunk()` 블록
- 진단용 `LogLandingDiagnostics()` 코루틴과 그 호출부

### 2-D. `ElevatorRoomPainter`

```csharp
[SerializeField] private Texture2D[] layerRoomImages;     // index = layerIndex (정류장 8개)
[SerializeField] private Texture2D[] layerBorderTextures; // 비우거나 null이면 청크 기본 테두리 유지

public void PaintLandingRoom(int xChunk, int layerIndex)
```

정류장은 지층마다 상층·하층 2개씩 총 8개다 (§2-F). 인덱스 순서:

| index | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| 정류장 | 땅 상 | 땅 하 | 얼음 상 | 얼음 하 | 용암 상 | 용암 하 | 우주 상 | 우주 하 |
| 청크 Y | 0 | -10 | -20 | -30 | -40 | -50 | -60 | -70 |

대상 청크: `(xChunk, ElevatorManager.layers[layerIndex].startDepth)` — **엘리베이터 청크 자체**. 1-B의 오프셋 버그가 여기서 해소된다.

**칠하지 않는 조건 (둘 중 하나라도 참이면 스킵):**

1. `SpecialChunkManager.Instance.IsProtectedCoord(coord)`
   → 구멍 입구 `(0,0)`은 `ImageChunkOverrider`가 소유한 좌표다. 엘리베이터 기둥이 `(0,0)`과 겹칠 경우 이 조건이 입구 방을 덮어쓰는 것을 막는다. (겹치지 않는 X의 엘리베이터는 정상적으로 방 PNG가 칠해진다.)

   **기둥 X 오프셋**: `ElevatorManager.elevatorSpawnOffsetX`(기본 0)로 엘리베이터 기둥의 시작 X를 밀 수 있다. 0이면 `x = 0, 5, 10…`이라 구멍 입구 청크에 엘리베이터가 함께 서고, 2로 두면 `x = 2, 7, 12…`가 되어 입구 방과 완전히 분리된다. 어느 쪽이든 위 보호 좌표 검사 덕분에 입구 방은 안전하다.

2. `InfinityMapManager.Instance.IsChunkVisited(coord)`
   → 이미 저장 기록이 있는 청크 = 플레이어가 다녀간 방. 다시 칠하면 판 흔적이 지워지므로 스킵한다. **재방문 시 방은 리셋되지 않는다.**

칠한 뒤에는 `SpecialChunkManager.Instance.RegisterProtectedCoord(coord)`를 호출한다(1-C 수정). 세션 동안 좌표가 누적 등록되며, 이후 재로드 시 절차 바위·광물이 다시 깔리지 않는다.

`layerRoomImages[layerIndex]`가 null이면 아무것도 하지 않는다 — 특정 지층만 기본 지형으로 두는 것을 허용한다.

### 2-F. 지층당 정류장 2개

`ElevatorManager.InitializeLayers()`가 지층마다 **상층**(지층 시작 깊이)과 **하층**(지층 두께의 절반 지점) 두 정류장을 만든다. 4지층 × 2 = **8정류장**.

두께는 다음 지층의 `startDepth`와의 차로 계산하고, 마지막 지층(우주)만 `TileDataManager.terrainDepth`(80) 기준 지형 바닥 `-80`을 끝으로 쓴다. 따라서 `tileData.json`의 `startDepth`나 `terrainDepth`를 바꾸면 하층 위치도 자동으로 따라간다.

지층이 너무 얇아 하층 좌표가 상층과 같아지면 그 하층 정류장은 만들지 않는다 — 좌표가 겹치면 `ShouldSpawnElevator`·`GetLayerIndexByDepth`가 두 정류장을 구분하지 못한다.

`layers` 리스트의 인덱스가 곧 `layerIndex`이며, `ElevatorUI.layerButtons` 순서와 `ElevatorRoomPainter.layerRoomImages` 인덱스가 여기에 1:1 대응한다. **씬의 층 버튼이 8개보다 적으면 남는 층은 선택할 수 없다** — `ElevatorUI.RefreshLayerButtons`가 이 경우 경고 로그를 한 번 남긴다.

### 2-E. 호출 흐름

```
ElevatorUI.OnLayerButtonClicked(targetLayer)
  └─ ElevatorManager.TeleportPlayer(xChunk, targetLayer)   [코루틴]
       ├─ Rigidbody → Kinematic
       ├─ 추정 위치로 이동
       ├─ HasChunk(목적지) 대기
       ├─ ElevatorRoomPainter.PaintLandingRoom(xChunk, targetLayer)
       ├─ 실제 엘리베이터 위치로 재보정
       ├─ 1프레임 대기
       └─ Rigidbody → Dynamic
```

페인팅을 청크 로드 이후·물리 복귀 이전에 두는 이유: 페인팅은 `LoadChunkData`로 콜라이더를 dirty 상태로 만들므로, 콜라이더가 새 픽셀 기준으로 재생성된 뒤에 플레이어를 Dynamic으로 풀어야 한다.

`ElevatorRoomPainter` 참조는 `ElevatorManager`에 `[SerializeField]`로 연결한다. 비어 있으면 페인팅 단계를 건너뛰고 나머지(착지 보호)는 정상 동작한다.

---

## 3. PNG 제작 제약

- **크기**: 1000×1000 (청크 해상도와 동일). 다르면 최근접 리샘플링되어 화질이 떨어진다.
- **Read/Write Enabled**: Import Settings에서 반드시 켠다. 꺼져 있으면 에러 로그만 남고 칠해지지 않는다.
- **알파**: `alpha > 10` = 땅, 그 이하 = 빈 공간.
- **중앙 비우기 (필수)**: 엘리베이터는 청크 로컬 `(5,5)` = **텍스처 정중앙 (500,500)** 에 선다. 중앙부가 불투명하면 엘리베이터가 벽에 박히고 플레이어가 착지할 공간이 없다. `ElevatorSpawner`의 굴착 반경 150px보다 넉넉하게 비워 둔다.
- **바닥**: 중앙 아래쪽에 발판이 있어야 플레이어가 낙하하지 않는다.

---

## 4. 수정 대상 파일

| 파일 | 변경 |
|---|---|
| `Assets/Scripts/UI/Interaction/Elevator/ElevatorManager.cs` | `TeleportPlayer` 코루틴화 + 착지 보호, overrider 재사용 블록 제거, 진단 코루틴 제거, `ElevatorRoomPainter` 참조 추가 |
| `Assets/Scripts/UI/Interaction/Scene/ChunkImagePainter.cs` | **신규** — 페인팅 코어 static 유틸 |
| `Assets/Scripts/UI/Interaction/Scene/ElevatorRoomPainter.cs` | **신규** — 지층별 방 PNG 보유, 스킵 조건 판정 |
| `Assets/Scripts/UI/Interaction/Scene/ImageChunkOverrider.cs` | 페인팅 본문 → `ChunkImagePainter` 위임, `SetTargetChunk` 제거 |

## 5. 씬 세팅

`DemoUnderground` 씬에 `ElevatorRoomPainter` 오브젝트를 하나 추가하고:
- `layerRoomImages`에 지층별 PNG 4장 할당
- `ElevatorManager`의 `elevatorRoomPainter` 필드에 연결

기존 `Imagechunkoverider` 오브젝트는 그대로 둔다(구멍 입구 전용).

---

## 6. 검증 항목

1. 얼음층으로 이동 → 낙하 없이 엘리베이터 옆에 착지 (`playerY ≈ -195`)
2. 착지 청크에 얼음층 방 PNG가 칠해짐 (한 칸 아래가 아님)
3. 용암층·우주층도 각각 다른 방 PNG
4. 구멍 입구 `(0,0)` 방이 엘리베이터 이동 후에도 원래 PNG 유지
5. 방을 판 뒤 다른 층 갔다가 돌아오면 판 흔적이 남아 있음
6. 방 안에 절차 바위·광물이 스폰되지 않음
   → **2026-08-06 변경**: 두 방 모두 §7에 따라 **광물은 스폰된다**. 바위는 여전히 없다.

---

## 7. 방 안 광물 스폰 (2026-08-06 추가)

### 문제

보호 좌표는 `ChunkSpawner.DecorateChunk_Phase2_Steps`에서 바위·광물 데코를 통째로 스킵하므로,
구멍 입구 방·엘리베이터 착지 방에는 광물이 하나도 나오지 않았다.

단순히 `MineralDecorator`를 허용하면 2026-07-11에 고친 버그가 재발한다:

| 문제 | 원인 |
|------|------|
| 광물이 방 공동에 떠서 튀어나옴 | 데코(Phase 2)가 **페인팅보다 먼저** 돈다. `MineralGenerator.IsWellSupported`가 절차 지형 기준으로 자리를 고른 뒤 PNG가 그 자리를 공동으로 만든다 → `CheckSupport` 탈락 → `WakeUp()` → 루트로 분리 |
| 캔 광물이 계속 되살아남 | `ChunkImagePainter.Paint`의 `LoadChunkData`가 PixelInfo를 0/1로 통째로 덮어 광물 마커(3)·수집 마커(4)를 지운다 |

### 설계 — 로드 후 처리 훅

**순서를 "페인팅 → 광물"로 뒤집는 것**이 핵심이다. 폴링으로 `HasChunk`를 감시하면
Phase 1(레지스트리 등록)과 Phase 2 사이에 끼어들어, Phase 2 시작의 자식 정리 루프
(`ROCK_`/`MINERAL_` 풀 반납)가 방금 만든 광물을 쓸어간다. 그래서 파이프라인이 직접 부른다.

```
IChunkPostLoadPainter                      (신규 인터페이스)
  └ SpecialChunkManager.RegisterPostLoadPainter(coord, painter)
       └ ChunkGenerationPipeline Phase 2, 데코레이터 루프 '직후'에 호출
            └ ImageChunkOverrider / ElevatorRoomPainter .OnChunkLoaded()
                 1. ChunkImagePainter.Paint()          ← 픽셀 확정
                 2. InfinityMapManager.SpawnMineralsInChunk()
                      └ ChunkSpawner.DecorateMineralsOnly(ignoreProtection: true)
```

- 훅은 **매 로드마다** 불린다 → 언로드·재로드 후에도 광물이 유지된다.
- 재로드 시 페인팅은 `IsChunkVisited(coord)`가 false일 때만 — 세이브 기록이 있으면
  판 흔적이 원래 PNG로 되돌아가므로 건너뛴다.
- 재스폰 위치는 결정론적 시드라 동일하고, 이미 캔 자리는 수집 마커(4)가 걸러낸다.

### 두 컴포넌트의 진입 시점이 다르다

| | `ImageChunkOverrider` (구멍 입구) | `ElevatorRoomPainter` (엘리베이터 착지) |
|---|---|---|
| 좌표 | 인스펙터 고정 1개 | 텔레포트할 때마다 결정 (x × layerIndex) |
| 등록 시점 | `Awake` — 청크가 로드되기 전 | `PaintLandingRoom` — 청크가 **이미 로드·데코된 뒤** |
| 첫 페인팅 | 훅이 처리 (첫 로드는 무조건 칠함) | `PaintLandingRoom`이 직접 (훅 미등록 상태라 안 불림) |
| 첫 광물 | 훅이 처리 | `PaintLandingRoom`이 직접 호출 |
| 이후 재로드 | 훅 | 훅 (`_paintedRooms`에 좌표→layerIndex 보관) |

`ElevatorRoomPainter`의 첫 페인팅은 Phase 2 자식 정리 루프가 이미 지난 시점이라
그 자리에서 바로 광물을 깔아도 쓸려가지 않는다. 이 호출은 **방을 칠하기 전에 절차 데코가
깔아둔 광물(이제 방 공동에 떠 있다)을 풀 반납하고 새 픽셀 기준으로 다시 까는 역할도 겸한다** —
`DecorateMineralsOnly`가 `MINERAL_` 자식을 먼저 정리하기 때문. 기존에 있던 "착지 방 공중에
광물이 떠 있는" 상태가 함께 해소된다.

세션이 바뀌면 그 방은 세이브 기록이 있어 `PaintLandingRoom`의 '스킵 2'에 걸리고 보호 좌표에도
등록되지 않는다 → 일반 청크로서 절차 데코가 정상 동작한다(광물 나옴). 즉 이번 변경은
**세션 내 첫 방문** 케이스의 구멍을 메우는 것이다.

### 엘리베이터 겹침

`DecorateMineralsOnly`는 자체 `DecorationContext`를 만들므로 `ElevatorDecorator`가
등록한 점유 영역을 못 본다. 시작 방 `(0,0)`에는 엘리베이터가 있으므로
`TerrainChunk.ElevatorArea`(`ElevatorDecorator`가 세팅, `Reuse_Step1_Prepare`에서 리셋)를
경유해 제외 영역으로 넘긴다.

### ⚠ 동작 변경

- 플레이어 활성화 시점이 "청크 등록 직후"에서 "Phase 2 데코 완료 후"로 밀렸다.
  타임아웃(10초)과 실패 시 강제 활성화 폴백은 그대로다.
- 페인팅 시점도 같이 밀렸다. 이전에는 `ImageChunkOverrider`의 코루틴 타이밍에 따라
  Phase 2와의 순서가 사실상 미정의였는데, 이제 확정된다.

### 수정 대상 파일

| 파일 | 변경 |
|------|------|
| `UI/Interaction/Scene/IChunkPostLoadPainter.cs` | 신규 — 훅 인터페이스 |
| `UI/Interaction/Scene/ImageChunkOverrider.cs` | 훅 구현. 폴링 코루틴 → 플레이어 활성화 대기 전용. `spawnMinerals` 토글 |
| `UI/Interaction/Scene/ElevatorRoomPainter.cs` | 훅 구현 + `_paintedRooms` 좌표 기록. 첫 페인팅 직후 광물 직접 호출. `spawnMinerals` 토글 |
| `Gameplay/Terrain/Tiles/SpecialChunkManager.cs` | 좌표→훅 레지스트리 (`RegisterPostLoadPainter` 등 3개) |
| `Gameplay/Terrain/Tiles/Generation/Pipeline/ChunkGenerationPipeline.cs` | Phase 2 양쪽 변형에 `InvokePostLoadPainter` 호출 |
| `Gameplay/Terrain/Tiles/Generation/Pipeline/ChunkSpawner.cs` | `DecorateMineralsOnly(..., ignoreProtection)` + 엘리베이터 영역 제외 |
| `Gameplay/Terrain/Tiles/InfinityMapManager.cs` | `SpawnMineralsInChunk(coord)` 공개 진입점 |
| `Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` | `ElevatorArea` 프로퍼티 + 재사용 시 리셋 |
| `Gameplay/Terrain/Tiles/Decoration/Decorators/ElevatorDecorator.cs` | 점유 영역을 청크에도 기록 |

### 검증 항목

1. 게임 시작 → 입구 방 벽 안에 광물이 박혀 있음 (허공에 뜬 것 없음)
2. 방 안 광물을 캔 뒤 멀리 갔다 돌아오면 **캔 것만** 안 돌아옴
3. 시작 방 엘리베이터 승강로 안에 광물이 없음
4. `spawnMinerals` 끄면 기존처럼 광물 0개
5. 엘리베이터로 얼음층 이동 → 착지 방 벽 안에 광물, 승강로 안엔 없음
6. 착지 직후 방 공중에 떠 있는 광물이 없음 (칠하기 전 절차 광물이 정리됨)
7. 다른 층 갔다가 같은 정류장으로 돌아오면 광물이 그대로 (캔 것 제외), 판 흔적도 유지
