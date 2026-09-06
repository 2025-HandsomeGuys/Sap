# Job 파이프라인 낭비 제거 설계

작성일: 2026-07-13
대상: `ChunkJobScheduler`, `TerrainJobs`, `TerrainVisualizer`, `TerrainChunk`, `InfinityMapManager.ProcessDirtyChunksAsync`

관련 문서: `collider-visual-decoupling.md`, `half-res-distance-field.md`

---

## 1. 배경

`ProcessDirtyChunksAsync`의 Round1/Round2 2라운드 구조는 청크 간 거리장(distance field) 수렴을 위해 필요하다.
Round1이 "내 청크의 DF를 확정" → 이웃이 Round2의 BoundarySync에서 그 값을 읽음 → 재전파. Jacobi 반복 2회에 해당한다.

문제는 **2라운드가 필요한 잡과 아닌 잡을 구분하지 않고 파이프라인 전체를 두 번 돌린다**는 점이다.
여기에 더해 병렬 잡들이 dirty rect가 아니라 **청크 전체 격자**로 dispatch되고 있다.

이 문서는 동작을 바꾸지 않고 낭비만 제거하는 설계를 정의한다.

---

## 2. 현황 — dirty 청크 1개 / 사이클당 실제 잡 수

`UpdateVisualsArea`가 `onPreChamfer` 콜백(BoundarySync 스케줄)을 Round1·Round2 양쪽에서 받고,
Round2는 `skipInit:false`로 호출되어 Init부터 다시 수행한다.

| 잡 | Step1 | Round1 (`skipInit:true`) | Round2 (`skipInit:false`) | 합계 | 필요량 |
|---|---|---|---|---|---|
| DownsampleMask | 1 | 1 | 1 (Init) + 1 (Chamfer) | **4** | 3 (§4.1) |
| InitDistanceFieldJob | 1 | – | 1 | **2** | 1 |
| BoundarySync | – | 1 | 1 | **2** | 1 |
| Chamfer fwd+bwd | – | 1쌍 | 1쌍 | **2쌍** | 2쌍 (불가피) |
| UpsampleDistance | – | 1 | 1 | **2** | 1 |
| VisualUpdate | – | 1 | 1 | **2** | 1 |

`ScheduleInitLighting`과 `ScheduleChamferPasses`가 각각 내부에서 `DownsampleMaskJob`을 스케줄하기 때문에 DS가 4회가 된다.

### 2.1 dispatch 도메인 문제

`VisualUpdateJob` / `UpsampleDistanceJob`은 rect 스코핑이 `Execute` 내부 early-return으로만 구현돼 있고,
스케줄은 청크 전체 격자로 한다. `DownsampleMaskJob`은 rect 개념이 아예 없다.

```csharp
job.Schedule(data.TotalPixels, JOB_BATCH_SIZE, deps);      // 1,000,000
upsampleJob.Schedule(data.TotalPixels, JOB_BATCH_SIZE, deps); // 1,000,000
downsampleJob.Schedule(data.HalfTotalPixels, 64, deps);     // 250,000 (rect 없음)
```

드릴 30×30 파기 → ext rect 130×130 ≈ 17k 픽셀이 실제 필요량인데 100만 워크아이템을 던진다.

---

## 3. 설계 근거

### 3.1 Round2의 Init은 필요 없다

Chamfer 2패스는 **min-전파**다. 시작값이 참값의 상한(upper bound)이기만 하면 같은 고정점으로 수렴한다.

- Round1이 남긴 DF = "이웃 영향 없는 자기 청크만의 거리장" → 참값(이웃 공기까지 고려한 거리)의 **상한**이다.
- Round2의 BoundarySync는 min-merge라 값을 **낮추기만** 한다.
- 따라서 Round2에서 dirty rect를 `maxDist`로 리셋하고 처음부터 다시 깔 필요가 없다.

**시드(seed) 정확성**: 공기·indestructible 픽셀의 `distance = 0`은 Round1의 Init이 이미 세팅했다.
Round2 Chamfer의 ext rect가 Round1보다 넓어질 수 있으나(strip 확장), 그 확장 영역은 이번 사이클에 dirty가 아니었으므로
base 픽셀이 변하지 않았고 이전 사이클의 값이 유효한 상한이다.

**지형이 추가되는 경우**(블록 설치·특수청크 복원)에는 거리가 *증가*해야 하므로 min-전파만으로는 부족하고 Init 리셋이 필수다.
이는 Step1(또는 Round1)의 Init이 dirty rect를 `maxDist`로 리셋함으로써 보장된다. 아래 3.2 참조.

### 3.2 Init 스킵 구멍 메우기

`ScheduleInitOnlyIfReady` → `TryScheduleInitLighting`은 이전 Init 핸들이 미완료면 **조용히 스킵**한다.

```csharp
public bool TryScheduleInitLighting(...)
{
    if (!_initJobHandle.IsCompleted) return false;   // ← 스킵
    ...
}
```

현재는 Round2의 무조건 Init이 이 경우의 안전망이었다. Round2 Init을 제거하려면 스킵 여부를 추적해야 한다.

→ 스냅샷 튜플에 `initScheduled` 필드 추가. `false`면 Round1이 `skipInit:false`로 Init을 수행한다.
**결과: Init은 항상 정확히 1회 실행된다.**

### 3.3 Round1의 BoundarySync는 제거해도 결과가 같거나 더 정확하다

BoundarySync는 **min-merge**다(덮어쓰기가 아니다). 따라서 "Round2가 다시 쓰니까 괜찮다"는 논증은 성립하지 않는다.
올바른 근거는 이렇다.

Round1 sync가 넣는 값을 V1, Round2 sync가 넣는 값을 V2라 하자.
- V1은 이웃의 **이번 사이클 Chamfer 이전** DF에서, V2는 **이후** DF에서 나온다.
- **파기는 DF를 낮추기만 한다**(공기가 늘어남). 따라서 V2 ≤ V1.
- 결과: `min(old, V1, V2) == min(old, V2)`. Round1 sync를 제거해도 **동일**하다.

지형이 *추가*되는 경우(블록 설치)엔 이웃 DF가 올라가 V2 > V1이 되고, Round1의 V1이 stale-low 값으로 남는다.
즉 이 경우엔 **제거하는 쪽이 더 정확하다.**

### 3.3.1 부수 이득 — Round1 Chamfer 범위 축소

`_lastExt*`의 **strip 확장은 `ScheduleBoundarySync` 내부에서** 일어난다.
Round1의 BoundarySync를 제거하면 Round1에선 확장이 발생하지 않으므로,
Round1의 Chamfer가 경계 strip이 아니라 `ext(dirty)`만 훑는다.
경계 근처 파기 시 Round1 Chamfer 범위가 **청크 전폭 strip → dirty 범위**로 줄어든다.

이것이 안전한 이유: Round1의 유일한 목적은 "이웃이 Round2에서 읽을 내 경계 DF를 갱신"하는 것이다.
- 경계 근처를 팠다면 그 경계 셀은 이미 `ext(dirty)` 안에 있어 Chamfer가 커버한다.
- 청크 중앙을 팠다면 내 경계 DF는 애초에 변하지 않으므로 이전 값이 유효하다.

### 3.4 Round1의 Upsample + Visual

full `DistanceField`를 읽는 곳은 **`VisualUpdateJob` 하나뿐**이다
(`GetNeighborDistance` / `SyncBoundaryDistanceWithNeighbors`는 deprecated 경로, BoundarySync는 half를 읽는다).
따라서 Upsample은 Visual 직전에 1회면 충분하다.

Round1의 Visual 결과는 Round2가 즉시 덮어쓴다. 유일한 부수 효과는 **간헐적 조기 프리뷰**다:
LateUpdate의 GPU 스로틀 루프가 Round1 Visual 완료를 포착하면 텍스처를 1~2프레임 일찍 올릴 수 있다
(단, Round2가 스케줄된 프레임에는 Visual이 in-flight라 `IsVisualJobCompleted()==false` → 업로드되지 않는다. 즉 항상 뜨는 게 아니다).

**판단**: 제거한다. 근거 —
- 파기 체감(콜라이더 개방·파편 파티클)은 비주얼 파이프라인과 무관하게 즉시 발생한다.
- 파기→텍스처 업로드 지연은 이미 3~4프레임(≈60ms @60fps)이다. 여기에 최대 1프레임(16.7ms)이 더해지는 것.
- 드릴 연속 파기 중엔 매 프레임 새 스냅샷이 생기므로 지연이 드러날 수 있는 건 드릴을 떼는 마지막 1회뿐이다.

**안전장치**: `worldSettings.json`의 `rendering.enableRound1Preview` (기본 `false`)로 게이팅.
체감이 이상하면 `true`로 켜서 A/B 비교. 검증이 끝나면 플래그와 관련 코드를 제거한다.

단, 프리뷰를 켜도 **BoundarySync는 부활시키지 않는다**(§3.3에서 무의미함이 증명됨).
따라서 `enableRound1Preview == true`의 프리뷰 프레임은 현행 Round1 프리뷰와 경계 근처에서 미세하게 다를 수 있다.
최종 프레임(Round2 이후)은 양쪽 모두 동일하다. A/B의 판단 대상은 **지연 체감**이지 픽셀 일치가 아니다.

---

## 4. 목표 파이프라인

**DS는 각 라운드 진입 시 정확히 1회씩**만 돈다(§4.1). Init을 수행하는 라운드에서도 DS를 두 번 돌리지 않는다.

```
Step1  : SetDirtyRect(dirty) → DS(ext) → Init(dirty)
           [이전 Init 미완료 시 통째로 스킵 → initScheduled = false]
         ─ yield 1 frame ─
Round1 : SetDirtyRect(dirty) → DS(ext)
         → if (!initScheduled) Init(dirty)          ← Step1이 스킵된 경우의 대체 수행
         → Chamfer(fwd, bwd)
         ─ wait ─
Round2 : SetDirtyRect(dirty)            ← 필수. §5.2 참조
         → DS(ext)
         → BoundarySync              (여기서 _lastExt가 strip으로 확장됨)
         → Chamfer(fwd, bwd)
         → Upsample(ext) → Visual(ext)
         ─ wait ─ ApplyTexture
```

DS 총 3회 = Step1 1 + Round1 1 + Round2 1. (Step1이 스킵되면 2회.)

`enableRound1Preview == true`인 경우에만 Round1 끝에 `Upsample(ext) → Visual(ext)`가 추가된다.

Chamfer 2라운드(총 4 IJob)는 **그대로 유지한다** — 청크 간 수렴에 필수다.
단 Round1엔 BoundarySync가 없어 strip 확장이 일어나지 않으므로,
Round1 Chamfer의 범위는 Round2보다 좁다(§3.3.1).

### 4.1 DownsampleMaskJob이 라운드마다 남는 이유

Round1/Round2는 서로 다른 프레임에서 실행되고, 그 사이에 플레이어가 다시 팔 수 있다.
새 파기는 새 스냅샷 + 새 코루틴을 만들지만, **진행 중이던 이전 코루틴의 Chamfer가 stale한 `BasePixelsHalf`를 읽는 것**을 막아야 한다.
현재 코드의 주석(`ScheduleChamferPasses` 상단)이 지적하는 문제가 이것이다.

따라서 라운드마다 DS는 유지하되, **ext rect로 스코핑**해 비용을 낮춘다(§5.3).

Init(`InitDistanceFieldJob`)과 Chamfer 둘 다 `BasePixelsHalf`를 읽으므로, DS는 **라운드 진입 시 한 번**
(그 라운드의 Init·Chamfer보다 먼저) 돌면 충분하다. 같은 라운드 안에서 `BasePixels`가 바뀔 일은 없다.

결과적으로 DS는 사이클당 3회(Step1, Round1, Round2 각 1회)가 된다. 현재는 4회다.

---

## 5. 변경 사항

### 5.1 `InfinityMapManager`

스냅샷 튜플 타입 변경:

```csharp
// 현재
List<(TerrainChunk chunk, RectInt rect, bool hasRect)>
// 변경
List<(TerrainChunk chunk, RectInt rect, bool hasRect, bool initScheduled)>
```

`_snapshotPool`의 제네릭 타입도 함께 변경.

- `SnapshotScheduleInitJobs`: `chunk.ScheduleInitOnlyIfReady(rect)`의 반환값(bool)을 튜플에 기록.
  `hasRect == false`인 청크는 `initScheduled = false`로 두어 Round1이 full-chunk Init을 수행하게 한다.
- `SnapshotScheduleRound1`: `chunk.ScheduleDistancePass(rect, hasRect, skipInit: initScheduled, withPreview: _enableRound1Preview)` 호출.
- `SnapshotScheduleRound2`: `chunk.ScheduleSyncAndVisualPass(rect, hasRect)` 호출.
- `_enableRound1Preview`: `worldSettings.rendering.enableRound1Preview`에서 읽는 인스턴스 필드(§5.5).

### 5.2 `TerrainChunk` — API 축소

제거: `DoVisualUpdateSkipInit`, `DoFullVisualUpdate`, `DoFullVisualUpdateSkipInit`, `ScheduleInitOnly`

**`DoVisualUpdate`는 남긴다** (as-built 정정 — 설계 시점엔 죽은 코드로 봤으나 아니었다).
`RollingRockTrap`이 바위가 지나간 자리를 즉시 갱신하려고 단발로 호출한다.
`TerrainCarver` / `PixelFloorCollapser`가 `Visualizer.UpdateVisualsArea`를 직접 부르는 것과 같은 부류 —
LateUpdate 파이프라인이 아니라 **단발 전체 갱신** 경로다.

신설:
```csharp
/// Round1: 자기 청크 DF만 확정 — SetDirtyRect → DS → [Init] → Chamfer.
/// BoundarySync 없음. withPreview일 때만 Upsample + Visual 추가(§3.4).
public void ScheduleDistancePass(RectInt rect, bool hasRect, bool skipInit, bool withPreview);

/// Round2: SetDirtyRect → DS → BoundarySync → Chamfer → Upsample → Visual.
public void ScheduleSyncAndVisualPass(RectInt rect, bool hasRect);
```

`ScheduleInitOnlyIfReady`는 `bool`을 반환하도록 변경.

#### ⚠ `_lastExt*` 재확립 — 절대 빠뜨리면 안 되는 것

`_lastExt*`는 `ChunkJobScheduler`의 **필드**이고, 이를 세팅하는 곳은 `ScheduleInitLighting()`과 `SetDirtyRect()` 둘뿐이다.
(구 `RecalcExtRange()`는 `SetDirtyRect()`로 대체됐다 — dirty rect까지 같이 저장하므로 상위 호환이다.)

현재 Round2는 `skipInit:false`로 호출되어 `ScheduleInitLighting()`이 `_lastExt`를 **자기 rect로 재확립**하는 부수 효과가 있다.
§3.1에 따라 Round2의 Init을 제거하면 **이 재확립 주체도 함께 사라진다.**

그대로 두면 Round2의 DS·Chamfer·Upsample·Visual이
Round1이 남긴 `_lastExt`, 혹은 **동시 실행 중인 다른 `ProcessDirtyChunksAsync` 코루틴이 덮어쓴 `_lastExt`**를 읽는다
(LateUpdate는 dirty가 있으면 매 프레임 새 코루틴을 띄우므로 같은 청크에 대해 코루틴이 겹칠 수 있다).

→ **`ScheduleSyncAndVisualPass`는 맨 먼저 `_jobScheduler.SetDirtyRect(data, ...)`를 호출해야 한다.**
`ScheduleDistancePass`도 마찬가지다 (as-built: 두 Pass 모두 첫 줄이 `SetDirtyRect`다).

이 항목은 §8의 "`_lastExt` 숨은 가변 상태" 과제가 왜 필요한지 보여주는 사례다.

**주의**: `RefreshVisuals()`, `FinishVisualsAfterInit()`, `LoadChunkData()`, `InitializeAfterGeneration()`은
LateUpdate 파이프라인 밖의 **단발 전체 갱신** 경로다. 이들은 `UpdateVisualsFull` 계열을 계속 사용하며,
반드시 Init + BoundarySync + Chamfer + Upsample + Visual 전체를 수행해야 한다. 이번 변경의 영향을 받지 않는다.

### 5.3 `ChunkJobScheduler` — 잡 스케줄 분리

현재 두 메서드가 여러 잡을 묶어 수행한다:
- `ScheduleInitLighting` = `_lastExt` 세팅 + DS + Init
- `ScheduleChamferPasses` = DS + Chamfer(fwd/bwd) + Upsample

각 잡을 1:1로 스케줄하는 메서드로 분리하고, **조합은 호출자(§5.2의 두 Pass 메서드)가 정한다.**

```csharp
public void SetDirtyRect(ChunkData data, int minX, int minY, int maxX, int maxY); // dirty + ext rect 저장
public void ScheduleDownsample(ChunkData data);      // half ext 스코프
public void ScheduleInit(ChunkData data);            // half ext 스코프 (DS 안 함)
public void ScheduleChamferPasses(ChunkData data);   // fwd + bwd 만
public void ScheduleUpsample(ChunkData data);        // full ext 스코프
public void ScheduleVisualJob(...);                  // full ext 스코프 (기존)
```

모두 `_lastExt*`를 읽기만 한다 — 단, `ScheduleBoundarySync`는 예외적으로 `_lastExt`를 **확장한다**(기존 동작 유지).

**의존성 규칙 (as-built)**: 위 스케줄 메서드는 **전부 `AllPrevHandles()`로 over-depend** 한다.
"조합은 호출자가 정한다"는 계약이므로 호출 순서에 의존하는 좁은 의존성을 쓰면 안 된다.
(청크 내 파이프라인은 어차피 직렬이라 병렬성 손실이 0이다.)

`ScheduleInitLighting`은 `SetDirtyRect + ScheduleDownsample + ScheduleInit`을 묶는 얇은 헬퍼로 남긴다 —
`ScheduleInitArea` / `TryScheduleInitArea` / `UpdateVisualsArea`가 사용한다.
(`ScheduleFullVisualUpdate`는 호출자가 없어 **제거했다.**)

#### DS 스코프와 strip 확장의 순서 (중요)

`ScheduleBoundarySync`는 실행 도중 `_lastExt*`를 **경계 strip 방향으로 확장**한다.
Round2의 호출 순서는 `DS → BoundarySync(ext 확장) → Chamfer → Upsample → Visual`이므로,
**DS는 확장 전 ext를, Chamfer는 확장 후 ext를 쓴다.** 즉 strip 확장 영역은 이번 사이클에 DS되지 않는다.

이는 안전하다: 확장 영역은 이번 사이클의 dirty rect가 아니므로 `BasePixels`가 변하지 않았고,
그 셀의 `BasePixelsHalf`는 마지막으로 파였던 사이클에 갱신된 값이 그대로 유효하다.
파인 픽셀은 항상 dirty rect ⊂ ext rect 안에 있으므로 DS 대상에서 누락되지 않는다.

이 불변식이 깨지는 유일한 경우는 **dirty rect 밖의 `BasePixels`를 수정하는 코드**가 생기는 것이다.
그런 코드를 추가할 일이 있으면 반드시 해당 영역도 dirty로 마킹해야 한다.

### 5.4 `TerrainJobs` — rect 스코프 dispatch

`IJobParallelFor` 4개 — `InitDistanceFieldJob`, `DownsampleMaskJob`, `UpsampleDistanceJob`, `VisualUpdateJob` — 를
rect 넓이만큼만 dispatch한다. 넷 다 기존엔 전체 격자로 스케줄하고 `Execute` 안에서 early-return했다.

| 잡 | 기존 dispatch | 스코프 rect |
|---|---|---|
| `InitDistanceFieldJob` | `HalfTotalPixels` (250k) | half ext (`_lastExt / 2`) |
| `DownsampleMaskJob` | `HalfTotalPixels` (250k) | half ext (`_lastExt / 2`) |
| `UpsampleDistanceJob` | `TotalPixels` (1M) | full `_lastExt` |
| `VisualUpdateJob` | `TotalPixels` (1M) | full `_lastExt` |

`InitDistanceFieldJob`은 rect가 둘(`dirty`, `ext`)이다. **dispatch는 ext 기준**으로 하고,
`inDirty` 분기(리셋 vs 시드만 보정)는 `Execute` 안에 그대로 남긴다.

> **개명 주의**: 이 잡의 원래 이름은 `InitBFSJob`이었다. BFS가 아니라 seed 리셋이므로 이름을 바로잡았고,
> 동시에 Burst 커널 캐시를 강제로 무효화하는 효과도 노렸다 (§7의 Burst 경고 참조).

`Execute(int index)`의 index를 전역 픽셀 인덱스가 아니라 **rect 로컬 인덱스**로 해석:

```csharp
public int rectMinX, rectMinY, rectWidth;  // rectWidth = rectMaxX - rectMinX

public void Execute(int i)
{
    int x = rectMinX + i % rectWidth;
    int y = rectMinY + i / rectWidth;
    int index = y * width + x;
    ...
}
```

스케줄: `job.Schedule(rectWidth * rectHeight, JOB_BATCH_SIZE, deps)`

기존의 `visMinX/visMaxX/...` (및 `InitBFSJob`의 `inExt`) early-return 가드는 제거한다
— dispatch 도메인이 곧 rect이므로 불필요하다.

**주의 1 — 이웃 픽셀 읽기.** `VisualUpdateJob`은 기울기 추정과 오목 모서리 보정을 위해 `distanceField`의
상하좌우 이웃을 읽고, `ChamferForward/BackwardPassJob`도 마찬가지다. 이는 rect 밖 인덱스를 읽을 수 있으나
**읽기 전용**이고 `[NativeDisableParallelForRestriction]`이 이미 붙어 있으므로 문제없다.
`outputTexture` 쓰기는 rect 안으로 국한된다.

**주의 2 — `index == 0` 가드.** `VisualUpdateJob`에 `if (index == 0) LogBorderInvalid(...)`가 있다.
rect 로컬 인덱스로 바뀌면 이 조건이 가리키는 픽셀이 달라지므로 **로컬 인덱스 `i == 0` 기준으로 고친다**
(경고를 청크당 1회만 찍는 것이 목적이므로 어느 픽셀이든 무방하다).

**주의 3 — 빈 rect.** rect 넓이가 0이면 `Schedule(0, ...)`이 된다.
스케줄을 건너뛰되, **핸들 필드는 이전 값을 그대로 두지 말고 `default`로 두지도 말 것** —
이전 핸들을 유지해야 미완료 잡에 대한 의존성이 유실되지 않는다. 가장 안전한 처리는
넓이 0이면 그 잡 스케줄만 건너뛰고 **해당 핸들 필드를 건드리지 않는 것**이다.

### 5.5 `WorldSettingsData` / `worldSettings.json`

```json
"rendering": {
  "enableLayerBlending": true,
  "globalLightFalloff": 30.0,
  "textureUpdateInterval": 0.05,
  "enableRound1Preview": false
}
```

`RenderingSection`에 `public bool enableRound1Preview = false;` 추가.

**static으로 만들지 말 것.** 프리뷰 여부를 판단하는 주체는 파이프라인의 주인인 `InfinityMapManager`(싱글톤)이며,
`SnapshotScheduleRound1`이 `chunk.ScheduleDistancePass(rect, hasRect, skipInit, withPreview: _enableRound1Preview)`
처럼 **인자로 내려보내면 된다.**

(`TerrainChunk.s_colliderUpdateInterval`이 static인 이유는 `TerrainChunk`가 프리팹에서 `Instantiate`되어
non-serialized 필드가 복사되지 않기 때문이다 — CLAUDE.md 아키텍처 제약 #4.
이 플래그는 매니저만 알면 되므로 그 사유가 적용되지 않는다. 불필요한 전역 상태를 만들지 않는다.)

---

## 6. 예상 효과

드릴 30×30 파기(ext = 130×130 full = 65×65 half), 청크 중앙, dirty 청크 1개 기준:

| 잡 | 현재 (횟수 × dispatch) | 개선 후 |
|---|---|---|
| DownsampleMask | 4 × 250k = 1.0M | 3 × 4.2k = 12.6k |
| InitBFS | 2 × 250k = 500k | 1 × 4.2k = 4.2k |
| UpsampleDistance | 2 × 1M = 2.0M | 1 × 16.9k = 16.9k |
| VisualUpdate | 2 × 1M = 2.0M | 1 × 16.9k = 16.9k |
| BoundarySync (IJob) | 2 | 1 |
| Chamfer fwd+bwd (IJob) | 4 | 4 (횟수 동일, **Round1 범위 축소** — §3.3.1) |
| **잡 스케줄 수** | **16** | **11** |
| **총 워크아이템** | **~5.5M** | **~50k** |

Visual·Upsample(각 1M 워크아이템)이 사이클당 2회 → 1회로 줄고, 그마저도 rect 넓이로 dispatch된다.

경계 근처 파기에서는 §3.3.1의 효과가 추가된다 — Round1 Chamfer가 청크 전폭 strip(최대 1000px 스캔라인)
대신 dirty 범위만 훑는다. `[LAGDIAG]`가 보고하던 `fwd`/`bwd` 스파이크가 여기서 나온다.

---

## 6.1 실측 (2026-07-13, 구현 완료 후)

`LagDiag = true`, 임계값 0.5ms. 드릴로 청크 경계를 가로질러 파면서 수집.

| 상황 | chamferArea | CompleteAll | 분해 |
|---|---|---|---|
| 청크 신규 로드 | 1000×1000 | 0.9ms | `init=0.9` |
| 청크 신규 로드 | 1000×1000 | **2.3ms** (최대) | `fwd=0.7 bwd=1.6` |
| 경계 드릴 (세로 strip) | 127×1000 | 0.6ms | `fwd=0.4 bwd=0.2` |
| 경계 드릴 (가로 strip) | 1000×190 | 0.6ms | `fwd=0.1 bwd=0.5` |
| 경계 드릴 | 187×1000 | 0.9ms | `fwd=0.6 bwd=0.3` |

**결론:**
- 모든 sync point가 **2.3ms 이하**. 1ms를 넘는 건 청크 **신규 로드**(전체 거리장 계산 — 불가피)뿐이다.
- **경계 드릴이 0.6~0.9ms.** 종전 `fwd+bwd ~19ms` 대비 대폭 개선.
- **`vis=0.0` 전부** — Visual 잡이 계측에 안 잡힌다. rect 스코핑 + 사이클당 1회의 효과.
- **`dirtyChunks=0` 전부** — dirty 큐 backlog 없음. 워커 포화 해소.

### 6.2 `MarkNeighborsDirty` 평행 방향 스코핑 (2026-07-13 완료)

§6.1 실측에서 경계 드릴의 `chamferArea` 세로가 **1000(청크 전체 높이)** 으로 나온 원인이다.

**문제.** `TerrainChunk.MarkNeighborsDirty`가 이웃에게 넘기는 rect의 **평행 방향이 변 전체**였다:

```csharp
// 종전
if (crossedLeft) mgr.MarkNeighborDirty(ChunkX - 1, ChunkY, new RectInt(width - STRIP, 0, STRIP, height));
//                                                                                    ↑ y: 0 ~ 1000 전체
```

수직 방향(경계 법선)은 이미 `STRIP=50`으로 잘 잘려 있었으나, 평행 방향은 통째였다.
y=200 근처를 팠어도 이웃은 `50×1000` 을 dirty로 받아 Init·Chamfer·Upsample·Visual이 전부 세로 1000을 훑었다.

**해결.** 평행 방향도 `판 구간 ± BORDER_MARGIN(50)` 으로 국한한다.
`DigResult.RawMinY/RawMaxY`(및 X)가 이미 있었는데 `crossed*` 판정에만 쓰고 rect에는 안 쓰고 있었다.

```csharp
int yLo = Mathf.Clamp(result.RawMinY - BORDER_MARGIN, 0, height);
int yHi = Mathf.Clamp(result.RawMaxY + BORDER_MARGIN, 0, height);
int ySpan = yHi - yLo;
if (crossedLeft && ySpan > 0)
    mgr.MarkNeighborDirty(ChunkX - 1, ChunkY, new RectInt(width - STRIP, yLo, STRIP, ySpan));
```

**안전성 근거.**
1. **영향 반경이 유한하다.** 우리 공기가 이웃 거리장을 낮출 수 있는 범위는 `maxDist(255) ÷ 직교비용(5) = 51px`.
   테두리 렌더에 실제로 쓰이는 건 `dist ≤ textureThicknessPx(≤50) × 5 = 250`, 즉 **50px 이내**.
   판 구간에서 평행 방향으로 50px 넘게 떨어진 이웃 픽셀은 애초에 영향을 받지 않는다.
2. **이웃 측에서 ext가 한 번 더 붙는다.** `SetDirtyRect`가 `dirty ± EXT_MARGIN(50)` → 최종 커버리지 **±100**.
3. **`BoundarySyncJob`은 이미 dirty span에 국한돼 있다** (`syncMinY/syncMaxY = _lastExt/2`).
   별도 정합성 처리가 필요 없다. sync 범위 밖 경계 셀은 그쪽 우리 DF가 안 변했으므로 이전 값이 유효하다.
4. `ScheduleBoundarySync`의 strip 확장 키(`nearLeft` 등)는 여전히 성립한다 —
   이웃의 dirty rect는 법선 방향으로 여전히 경계에 닿아 있다.

**효과.** 이웃 chamferArea `100×1000` → `100×~230`. 면적 **~4배 감소**.
이웃의 Upsample·Visual dispatch도 ~100k → ~13k.

**실측 검증 (2026-07-13).** 4개 청크가 맞닿는 코너를 드릴로 관통하며 `MarkNeighborsDirty` 진입 시점을 계측:

| 청크 | dig raw bounds | 넘긴 span |
|---|---|---|
| (1,0) | (-90,-14)~(70,146) | ySpan=196, xSpan=120 |
| (0,-1) | (935,968)~(1095,1128) | ySpan=**82**, xSpan=115 |
| (1,-1) | (-14,937)~(146,1097) | ySpan=113, xSpan=196 |

- **1000이 하나도 없다.** 전부 82~196.
- 클램프도 정확하다: `(0,-1)`은 dig y가 1128까지 나가지만 청크 높이 1000에서 잘려 `[918,1000]` = 82.
  `(1,0)`은 dig x/y가 음수(`DigResult.Raw*`는 "may exceed chunk")인데 0으로 클램프됐다.
- 렌더 결과에 차이 없음 — 경계를 가로질러·따라서 파도 테두리가 그대로 이어진다.

**주의.** `MarkChunkDirty`는 같은 청크에 여러 번 찍히면 **바운딩 박스로 union**한다.
한 프레임에 같은 경계의 y=100과 y=900을 동시에 파면 union이 거의 전체가 된다.
최악의 경우가 "종전과 같아지는" 것뿐이라 회귀는 아니다.

---

## 7. 검증

`InfinityMapManager.LagDiag = true`로 켜면 나오는 `[LAGDIAG] CompleteAll` / `[LAGDIAG] pipeline` 로그로 전후 비교.

### 회귀 확인 포인트

1. **청크 경계를 가로질러 파기** — 경계 꺾임(kink), 직선 스트라이프 border 아티팩트가 없는지.
   `ScheduleBoundarySync`의 strip 확장 로직이 `_lastExt`에 의존하므로 가장 위험한 지점이다.
2. **코너(대각) 방향 동시 파기** — 두 경계에 동시에 닿는 경우.
3. **특수청크 재로드 후 테두리** — `RestoreSavedPixels` → `FinishVisualsAfterInit(skipInit:true)` 경로가
   §5.2의 단발 전체 갱신 경로로 그대로 유지되는지.
4. **블록 설치(지형 추가) 후 거리장** — Init 리셋이 정확히 1회 수행되어 거리가 올바르게 *증가*하는지.
5. **드릴 연속 파기** — 여러 `ProcessDirtyChunksAsync` 코루틴이 같은 청크에 대해 동시 실행되는 상황.
   §5.2의 `RecalcExtRange` 누락이 드러나는 지점이다. 텍스처가 뒤처지거나, 파낸 구멍의 테두리가
   엉뚱한 위치에 그려지거나(= 잘못된 `_lastExt`로 Visual dispatch), 갱신이 통째로 누락되지 않는지 본다.
6. `enableRound1Preview` `true`/`false` 양쪽에서 최종 결과가 동일한지 (프리뷰는 지연에만 영향).

### ⚠ EditMode 테스트로 검증되지 **않는** 것 — Job Safety 속성

`TerrainJobsRectScopeTests`는 `IJobParallelForExtensions.Run(n)` 으로 잡을 실행한다.
`Run`은 **메인 스레드에서 전체 인덱스 범위를 순차 실행**하므로 parallel-for의 index 제약을 적용하지 않는다.

따라서 rect-local dispatch의 **유일한 함정** — 쓰기 대상 NativeArray에
`[NativeDisableParallelForRestriction]` 을 빠뜨리는 것 — 을 이 테스트는 **잡아내지 못한다.**
속성이 빠져도 EditMode는 초록이고, **Play 모드에서만** 터진다:
`IndexOutOfRangeException` 또는 "job is not allowed to write to NativeArray at index N".

**EditMode 그린을 job safety의 증거로 쓰지 말 것.** 잡 struct의 필드나 dispatch 도메인을 바꿀 땐
반드시 Play 모드에서 확인한다.

### ⚠ Burst 커널 캐시 — struct 필드를 바꿀 때

Task 2에서 `InitBFSJob`의 필드를 바꿨을 때(`extMaxX/extMaxY` 제거, `extWidth` 추가)
Burst가 **이전 커널을 계속 사용**해 소스상 불가능한 `DivideByZero`가 발생했다.
옛 커널은 `index / width` 로 나눴는데, 구조체가 int 하나 짧아지면서 그 오프셋이
필드 끝을 넘어 0을 읽은 것이다. Unity 재시작으로도 풀리지 않았다.

증상이 **"소스상 불가능한 예외"** 면 스테일 커널을 의심하고, **잡 struct의 타입명을 바꿔**
캐시 엔트리를 새로 만들어라. (이때 `InitBFSJob` → `InitDistanceFieldJob` 으로 개명했다.)

---

## 8. 이번 범위에서 제외하는 것 (후속 과제)

- **JobHandle 8개 수동 의존성 관리.** `_initJobHandle`, `_forwardJobHandle`, `_lightingJobHandle`,
  `_boundarySyncHandle`, `_downsampleHandle`, `_upsampleHandle`, `_visualsJobHandle`,
  `_externalDistanceFieldReaders` — 하나 누락하면 `InvalidOperationException`.
  이번에 `AllPrevHandles()`로 8개 합성을 **한 곳에 모았고** 모든 스케줄 메서드가 이를 쓰지만,
  여전히 "어떤 잡이 어떤 버퍼를 읽고 쓰는가"를 사람이 추적해야 한다.
  `CompleteAllJobs` / `CompleteLighting` / `IsJobRunning` 3곳에 핸들 목록이 중복돼 있다.
  → **리소스 단위 의존성 추적기**로 자동화하는 별도 과제. `AllPrevHandles()`가 그 진입점이다.
- **`_lastExt*` 숨은 가변 상태.** 스케줄 메서드들이 필드로 rect를 주고받아 호출 순서에 의존한다.
  이번에 `SetDirtyRect()`로 진입점을 하나로 좁혔지만, `ScheduleBoundarySync`가 여전히 이 필드를 **변형**한다.
  → rect를 명시적 값 객체로 전달하는 별도 과제.
- **`BasePixelsHalf` / `PixelInfoHalf` 버퍼 제거.** Init/Chamfer가 full `BasePixels`를 inline 2×2 샘플하게 하면
  `DownsampleMaskJob`과 두 NativeArray(청크당 ~1.25MB)를 통째로 없앨 수 있다.
  §5.3~5.4의 스코핑으로 DS 비용이 충분히 내려가므로 후속 과제로 미룬다.
- **이름과 실체 불일치.** `_lightingJobHandle`은 ChamferBackwardPass 핸들,
  `TerrainLightingCalculator`는 lighting이 아니라 BoundarySync 스케줄러. Lighting BFS 제거 후 남은 유산.
  (`InitBFSJob` → `InitDistanceFieldJob` 개명은 이번에 완료했다 — §5.4 참조.)
- **`CompleteAllJobs` 내 `LagDiag` 임시 계측 코드.** 전후 비교에 쓰이므로 유지하고, 검증 완료 후 제거한다.
- **`enableRound1Preview` 플래그.** A/B 검증이 끝나면 플래그와 `withPreview` 인자를 제거한다.
- **`hasRect == false` 폴백 경로.** `MarkChunkDirty`가 항상 rect를 함께 등록하므로 현재는 도달 불가다.
  방어적으로 남겨뒀다.

---

## 9. 건드리면 안 되는 기존 동작

구현 중 "낭비처럼 보여서" 잘라내기 쉬운 것들이다. **의도된 동작이다.**

### 9.1 `BoundarySyncJob`은 `near*` 여부와 무관하게 엣지에 쓴다

`BoundarySyncJob`의 `if (hasLeft) { ... }` 블록은 이웃이 로드돼 있기만 하면 실행된다.
`ScheduleBoundarySync`의 `nearLeft` 판정은 **`_lastExt` strip 확장 여부**만 결정할 뿐,
Job 내부의 엣지 쓰기를 막지 않는다.

그 결과 **청크 중앙을 팠을 때** BoundarySync가 쓴 엣지 halfDF 값은 ext rect 밖이라
Chamfer가 안쪽으로 전파하지도, Upsample이 full DF로 옮기지도 않는다. 화면에는 영향이 없다.

**하지만 이웃 청크의 BoundarySync가 우리 halfDF 엣지 열을 읽는다.**
즉 이 "전파되지 않는 엣지 값"은 청크 간 거리 전파(Dijkstra)의 중간 상태로서 의미가 있다.
`near*`가 false일 때 BoundarySync를 통째로 건너뛰면 이웃의 경계 거리가 틀어진다.

→ **`ScheduleBoundarySync` 호출 조건을 바꾸지 말 것.** rect 스코핑(§5.4)의 대상도 아니다
(이미 `syncMinX/syncMaxX`로 dirty span에 국한돼 있다).

### 9.2 Chamfer 2라운드

청크 간 수렴(Jacobi 2회 반복)에 필수다. 한 라운드로 줄이면 경계 거리장이 한 사이클 뒤처진다.

### 9.3 콜라이더 / 비주얼 분리

`TryUpdateCollider()`를 `ApplyTexture()` 안으로 되돌리면 드릴 연속 파기 중 콜라이더가 영구 차단된다.
(`collider-visual-decoupling.md`, CLAUDE.md 아키텍처 제약 #2)
