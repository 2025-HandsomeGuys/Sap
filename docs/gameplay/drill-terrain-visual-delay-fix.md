# 드릴 파기 중 지형 시각 업데이트 지연 버그 수정
@tags: drill, visual, delay, IsVisualJobCompleted, ApplyTexture, VisualJob, terrain-chunk, bug, ProcessDirtyChunksAsync

> 작성일: 2026-03-26
> 관련 파일: `InfinityMapManager.cs`, `TerrainChunk.cs`, `TerrainVisualizer.cs`, `ChunkJobScheduler.cs`

---

## 증상

드릴로 지형을 파는 동안 화면의 지형 모습이 실시간으로 갱신되지 않고,
드릴 조작을 완료(LMB 해제)한 순간 밀려 있던 변경사항이 한꺼번에 반영된다.

---

## 근본 원인

### 텍스처 업로드 가드 조건

`InfinityMapManager.LateUpdate()` 텍스처 적용 구간:

```csharp
if (tc != null && tc.isTextureDirty && !tc.IsJobRunning())
    tc.ApplyTexture();
```

`IsJobRunning()`은 **Init / ForwardChamfer / BackwardChamfer / Visual** Job 중 하나라도 미완이면 `true`를 반환한다.

```csharp
// ChunkJobScheduler.IsJobRunning()
return !_initJobHandle.IsCompleted || !_forwardJobHandle.IsCompleted
    || !_lightingJobHandle.IsCompleted || !_visualsJobHandle.IsCompleted;
```

### 연쇄 차단 흐름

드릴의 `ImmediateDig()`는 `FixedUpdate`(0.02s)마다 호출되어 `MarkChunkDirty`를 등록한다.
`LateUpdate`는 매 프레임 이 dirty 목록을 보고 새 `ProcessDirtyChunksAsync` 코루틴을 시작한다.

```
LateUpdate 프레임 N:
  1. 더티 청크 → 코루틴 시작
     └─ 즉시 실행: ScheduleInitOnlyIfReady()
          → 새 Init Job 예약 (_visualsJobHandle 완료 후 체인)
          → _initJobHandle.IsCompleted = false
          → yield return null
  2. 텍스처 적용 체크:
     └─ isTextureDirty=true, IsJobRunning()=true → ⛔ SKIP

→ 다음 LateUpdate에서도 ImmediateDig로 새 dirty → 새 코루틴 → 새 Init Job
→ IsJobRunning() 항상 true → 텍스처 업로드 영구 차단
```

드릴 조작 종료 시 `ImmediateDig` 중단 → 새 dirty 없음 → 마지막 Job 완료 → `IsJobRunning()=false`
→ 그제야 `ApplyTexture()` 호출 → 모든 변경사항이 한 번에 표시됨.

---

## 수정 방향

`_visualsJobHandle`이 완료된 시점이면 `outputTexture`에 대한 쓰기가 끝난 것이다.
Init / Chamfer Job은 `distanceField`만 수정하고 `outputTexture`는 건드리지 않는다.
따라서 Visual Job만 완료됐으면 텍스처 업로드를 즉시 진행해도 안전하다.

```
Visual Job 완료 여부만 확인:
  _visualsJobHandle.IsCompleted = true → 텍스처 업로드 허용
  Init / Chamfer Job 실행 중 → 무관 (outputTexture 비접근)
```

---

## 변경 내용

### 1. `ChunkJobScheduler.cs` — 메서드 추가

```csharp
/// <summary>Visual Job만 완료 여부 확인 (Init/Chamfer는 무시).</summary>
public bool IsVisualJobCompleted() => _visualsJobHandle.IsCompleted;
```

### 2. `TerrainVisualizer.cs` — 메서드 추가

```csharp
public bool IsVisualJobCompleted()
    => _jobScheduler == null || _jobScheduler.IsVisualJobCompleted();
```

### 3. `TerrainChunk.cs` — 메서드 추가

```csharp
public bool IsVisualJobCompleted()
    => _visualizer == null || _visualizer.IsVisualJobCompleted();
```

### 4. `InfinityMapManager.cs` — 텍스처 적용 가드 조건 변경

```csharp
// 변경 전
if (tc != null && tc.isTextureDirty && !tc.IsJobRunning())

// 변경 후
if (tc != null && tc.isTextureDirty && tc.IsVisualJobCompleted())
```

---

## 안전성 검토

| Job 종류 | outputTexture 접근 | 텍스처 업로드 차단 필요? |
|---|:---:|:---:|
| Init (`InitBFSJob`) | ❌ `distanceField` 초기화만 | ❌ 불필요 |
| ChamferForward | ❌ `distanceField` 읽기/쓰기 | ❌ 불필요 |
| ChamferBackward | ❌ `distanceField` 읽기/쓰기 | ❌ 불필요 |
| Visual (`VisualUpdateJob`) | ✅ `outputTexture` 쓰기 | ✅ 필수 |

→ `_visualsJobHandle.IsCompleted` 하나로 충분히 안전한 조건이다.

---

## 영향 범위

- 드릴 파기 중 지형이 `_textureUpdateInterval`(기본 0.05s) 주기로 실시간 갱신됨
- 다른 도구(삽, 곡괭이)의 단발 파기는 이미 드릴처럼 연속 dirty를 등록하지 않으므로 동작 변화 없음
- 청크 로딩 중 Init/Chamfer 실행 중인 상황에서도 이전 Visual 결과는 즉시 표시 가능
