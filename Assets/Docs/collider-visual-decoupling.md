# 콜라이더 갱신과 비주얼 파이프라인 분리
@tags: collider, visual, decoupling, ProcessDirtyChunksAsync, Round2, border-sync, drill, physics, TerrainChunk, ApplyTexture, TryUpdateCollider, IsVisualJobCompleted, neighbor-chunk

**관련 파일**
- `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs`
- `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs`

---

## 배경: 드릴로 땅 팔 때 플레이어가 막히는 버그

드릴 대시 중 플레이어가 지형에 간헐적으로 막히는 버그가 있었다.
삽은 동일한 현상이 없었기 때문에 두 도구의 파기 방식 차이에서 원인을 찾았다.

---

## 원인 분석

### 드릴 파기 경로

드릴 대시 중 두 경로로 파기가 발생한다.

```
FixedUpdate (~0.02s마다):
  DrillStrategy.HandleFixedUpdate()
    → Digger.ImmediateDig()
    → TerrainChunk.Dig(applySmoothing: false)
    → BasePixels 픽셀 수정 + IsColliderDirty = true
    → mgr.MarkChunkDirty() → _dirtyChunksOrdered에 등록
    → rb.linearVelocity = targetVel  ← 플레이어 즉시 이동

Update (0.2s마다):
  DrillStrategy.HandleUpdate()
    → Digger.RequestDig()
    → DigRoutine(delay 0.05s)
    → TerrainChunk.Dig(applySmoothing: true)
    → 동일하게 IsColliderDirty = true + MarkChunkDirty
```

### 콜라이더 갱신 경로 (수정 전)

```
InfinityMapManager.LateUpdate()
  └─ ProcessDirtyChunksAsync 코루틴 시작 (매 LateUpdate)
  └─ GPU 스로틀 루프 (0.05s마다):
       if (IsVisualJobCompleted())  ← 여기서 막힘
         tc.ApplyTexture()
           └─ UpdateTextures()       (GPU 텍스처 업로드)
           └─ UpdateCollider()       ← ApplyTexture 안에 묶여있었음
```

콜라이더 갱신이 `ApplyTexture()` 안에 있었고,
`ApplyTexture()`는 `IsVisualJobCompleted() == true` 일 때만 호출됐다.

### IsVisualJobCompleted가 항상 false가 되는 이유

Unity의 프레임 내 실행 순서는 다음과 같다.

```
FixedUpdate → Update → [yield return null 코루틴 재개] → LateUpdate
```

드릴 연속 파기 중 매 프레임 흐름:

```
프레임 N:
  FixedUpdate: ImmediateDig → dirty 등록
  LateUpdate:
    ProcessDirtyChunksAsync 코루틴A 시작
      → Step1: Init Job 스케줄 → yield return null (정지)
    GPU 스로틀: IsVisualJobCompleted=true → ApplyTexture → UpdateCollider ✓

프레임 N+1:
  FixedUpdate: ImmediateDig → dirty 등록
  [코루틴A 재개 — LateUpdate 직전!]
    → Step2: DoVisualUpdateSkipInit → ScheduleVisualJob
    → _visualsJobHandle = 새 실행 중인 잡
    → IsVisualJobCompleted = false
    → Round1 대기 루프 → yield return null (정지)
  LateUpdate:
    ProcessDirtyChunksAsync 코루틴B 시작
    GPU 스로틀: IsVisualJobCompleted = false → ApplyTexture 미호출 → UpdateCollider 미실행 ✗

프레임 N+2:
  [코루틴A 재개] → Step4: DoVisualUpdate → ScheduleVisualJob (덮어씀)
  [코루틴B 재개] → Step2: ScheduleVisualJob (또 덮어씀)
  LateUpdate: IsVisualJobCompleted = false → UpdateCollider 미실행 ✗
```

드릴로 연속 파기 중에는 매 프레임 이전 코루틴이 LateUpdate 직전에 새 VisualJob을 스케줄한다.
그 결과 `IsVisualJobCompleted`가 항상 `false`로 유지되어,
GPU 스로틀 루프의 `ApplyTexture`가 호출되지 않고,
`ApplyTexture` 안에 묶여 있던 `UpdateCollider`도 함께 차단됐다.

### 왜 삽은 문제가 없었나

삽은 클릭 한 번에 파기 후 이동하지 않는다. 플레이어가 지형을 통과하며 이동하는 상황이 없으므로
콜라이더 갱신이 다소 늦어도 막히는 현상이 발생하지 않는다.

---

## 수정 내용

### 핵심 원칙

콜라이더는 `BasePixels`(CPU 메모리)를 직접 읽어 생성된다.
GPU 텍스처 업로드(Visual Job)와 **데이터 의존성이 없다.**
따라서 두 작업을 분리해도 안전하다.

### TerrainChunk.cs

`ApplyTexture()`에서 콜라이더 갱신 로직을 제거하고,
독립 메서드 `TryUpdateCollider()`로 분리했다.

```csharp
// 수정 전: ApplyTexture() 안에서 콜라이더 갱신
public void ApplyTexture()
{
    _visualizer.UpdateTextures();
    _data.IsVisualDirty = false;

    if (_data.IsColliderDirty && ...)   // ← Visual과 커플링
    {
        _colliderManager.UpdateCollider();
        ...
    }
}

// 수정 후: 역할 분리
public void ApplyTexture()
{
    _visualizer.UpdateTextures();        // GPU 텍스처만 담당
    _data.IsVisualDirty = false;
}

public void TryUpdateCollider()          // 콜라이더만 독립 담당
{
    if (!_data.IsColliderDirty) return;
    if (!ShouldUpdateCollider()) return; // colliderUpdateInterval(0.2s) 스로틀

    _colliderManager.UpdateCollider();
    _data.IsColliderDirty = false;
    _lastColliderUpdateTime = Time.time;
}
```

### InfinityMapManager.cs

`LateUpdate()`에 콜라이더 전용 갱신 루프를 추가했다.
`IsVisualJobCompleted()` 조건 없이 `IsColliderDirty`만 보고 동작한다.

```csharp
// 기존 GPU 스로틀 루프 (변경 없음)
if ((Time.time - _lastTextureApplyTime) >= _textureUpdateInterval)
{
    foreach (var chunk in _chunkRegistry.GetAll())
    {
        var tc = chunk as TerrainChunk;
        if (tc != null && tc.isTextureDirty && tc.IsVisualJobCompleted())
            tc.ApplyTexture();  // GPU 텍스처만
    }
}

// 신규: 콜라이더 전용 루프
foreach (var chunk in _chunkRegistry.GetAll())
{
    var tc = chunk as TerrainChunk;
    if (tc != null && tc.isDirty)
        tc.TryUpdateCollider();  // Visual Job 완료 여부와 무관
}
```

### 갱신 구조 변화

```
수정 전:
  ApplyTexture()
    ├─ UpdateTextures()   (GPU)
    └─ UpdateCollider()   ← IsVisualJobCompleted에 종속

수정 후:
  ApplyTexture()          ← IsVisualJobCompleted 필요
    └─ UpdateTextures()   (GPU만)

  TryUpdateCollider()     ← IsColliderDirty만 보면 됨
    └─ UpdateCollider()   (CPU, 독립)
```

---

## 성능 영향

콜라이더 전용 루프는 매 LateUpdate(~60Hz)마다 전체 활성 청크를 순회한다.
각 청크에서 `tc.isDirty` 불리언 체크만 수행하므로 순회 자체의 비용은 무시할 수준이다.

실제 무거운 연산인 `UpdateCollider()`(Moore-Neighbor Tracing)는
`ShouldUpdateCollider()`의 `colliderUpdateInterval = 0.2s` 스로틀에 의해
청크당 최대 초당 5회로 제한된다.

---

## 후속 수정: 비주얼 텍스처 업데이트도 같은 문제

### 증상

콜라이더 디커플링 후 플레이어는 지형을 통과하게 됐지만,
지형의 텍스처(시각적 모습)가 연속 파기 중 업데이트되지 않는 현상이 추가 발생했다.
인접 청크도 동일하게 비주얼이 갱신되지 않았다.

### 원인

비주얼 텍스처 업로드(`ApplyTexture`)도 동일한 `IsVisualJobCompleted` 조건에 막혔다.

기존 `ProcessDirtyChunksAsync` Step 6는 레지스트리 외부 특수 청크에만 `ApplyTexture()`를 호출했다.
일반 청크는 GPU 스로틀 루프에 의존했는데, 그 루프도 `IsVisualJobCompleted = false`에 차단되어 있었다.

```
// 수정 전 Step 6 — 일반 청크(레지스트리 내부)는 ApplyTexture 미호출
if (!_chunkRegistry.HasChunk(new Vector2Int(chunk.ChunkX, chunk.ChunkY)))
    chunk.ApplyTexture(); // 특수 청크만
```

### 수정 내용

**`InfinityMapManager.ProcessDirtyChunksAsync` Step 6**

Round 2 wait loop(Step 5) 완료 직후는 Visual Job이 확실히 끝난 유일한 시점이다.
이 시점에 레지스트리 내외 모든 청크에 `ApplyTexture()`를 호출하도록 변경했다.

```csharp
// 수정 후 Step 6 — 모든 청크에 즉시 적용
foreach (var (chunk, _, _) in snapshot)
{
    chunk.EnsureJobsCompleted();
    chunk.ApplyTexture(); // 레지스트리 내외 모든 청크
}
```

**`TerrainChunk.ApplyTexture()`**

코루틴 종료 시점과 GPU 스로틀 루프 양쪽에서 호출될 수 있으므로
`IsVisualDirty` 가드를 추가해 중복 GPU 업로드를 방지했다.

```csharp
public void ApplyTexture()
{
    if (_data == null || _visualizer == null) return;
    if (!_data.IsVisualDirty) return; // 이미 업로드됨 — 중복 방지
    _visualizer.UpdateTextures();
    _data.IsVisualDirty = false;
}
```

### 갱신 보장 흐름

```
ProcessDirtyChunksAsync:
  Step 1: Init 스케줄 → yield (1프레임)
  Step 2: Round 1 Visual 스케줄
  Step 3: Round 1 완료 대기 (wait loop)
  Step 4: Round 2 Visual 스케줄 (이웃 경계 동기화 포함)
  Step 5: Round 2 완료 대기 (wait loop)
  Step 6: ApplyTexture() ← Visual Job 완료 보장 시점에 직접 업로드
```

GPU 스로틀 루프는 드릴 비연속 파기나 다른 도구의 fallback으로 유지된다.

## 성능 영향 (추가)

`ApplyTexture()`의 `IsVisualDirty` 가드 덕분에 GPU 스로틀 루프와 코루틴이
동일 청크에 대해 중복 `mainTexture.Apply()` 호출을 하지 않는다.

---

## 후속 수정: 파기 중 플레이어가 땅속으로 꺼지는 버그

**관련 파일 추가**
- `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainCollider.cs`
- `Assets/Scripts/UI/Player/PlayerController.cs`

### 증상

일반 파기(삽·드릴 무관) 도중 플레이어가 갑자기 땅속으로 계속 꺼진다.
파기를 조금이라도 하면 멈춘다.

### 원인 1: `isSlidingDown` 오판정 (PlayerController.cs)

원형 파기 구멍의 가장자리는 거의 수직(~90°)에 가까운 경사면을 생성한다.
`groundCollider`가 이 수직면에 살짝 닿으면, 기존 코드는 **"모든 접촉 중 최대 기울기"** 로
`isSlidingDown`을 결정했기 때문에 다음 상황이 발생했다:

```
접촉 A: 발 아래 평평한 바닥 → angle = 0°
접촉 B: 파기 구멍 측면(벽)   → angle = ~90°
currentMaxSlope = max(0°, 90°) = 90° > maxSlopeAngle(45°)
→ isSlidingDown = true
→ 이동 제어 해제, 중력이 플레이어를 계속 아래로 당김
```

평평한 지지면이 있음에도 수직 벽 접촉 하나 때문에 슬라이딩 상태로 진입하는 것이 문제였다.

#### 수정

최대 기울기 대신 **"유효 지지면(≤ maxSlopeAngle)이 하나라도 존재하는가"** 로 판단한다.

```csharp
// 수정 전: 모든 접촉 중 최대 기울기로 primaryNormal·isSlidingDown 결정
for (int i = 0; i < contactCount; i++)
{
    float angle = Vector2.Angle(Vector2.up, contactBuffer[i].normal);
    if (angle > currentMaxSlope)
    {
        currentMaxSlope = angle;
        primaryNormal = contactBuffer[i].normal;  // 가장 가파른 노멀 사용
    }
}
if (currentMaxSlope > maxSlopeAngle) isSlidingDown = true;
else                                  isSlidingDown = false;  // 접촉 0개면 0°≤45° → false (안전)

// 수정 후: 유효 지지면(≤ maxSlopeAngle)만 primaryNormal·currentMaxSlope에 반영
// hasGroundContact로 "접촉 자체가 없는 일시적 상태"와 "벽만 닿는 상태"를 구분한다.
bool hasValidSupport = false;
bool hasGroundContact = false;
for (int i = 0; i < contactCount; i++)
{
    if (groundLayer포함 레이어)
    {
        hasGroundContact = true;
        float angle = Vector2.Angle(Vector2.up, contactBuffer[i].normal);
        if (angle <= maxSlopeAngle)   // 수직 벽은 지지면으로 간주하지 않음
        {
            hasValidSupport = true;
            if (angle > currentMaxSlope) { currentMaxSlope = angle; primaryNormal = ...; }
        }
    }
}
// 접촉 없음(콜라이더 갱신 직후 일시적 상태) → false로 안전 처리
if (hasGroundContact)
    isSlidingDown = !hasValidSupport;
else
    isSlidingDown = false;
```

| 상황 | 수정 전 | 수정 후 |
|------|---------|---------|
| 평평한 바닥 + 파기 구멍 벽 동시 접촉 | `isSlidingDown = true` (버그) | `isSlidingDown = false` (정상) |
| 가파른 경사면만 접촉 | `isSlidingDown = true` (정상) | `isSlidingDown = true` (정상) |
| 평평한 바닥만 접촉 | `isSlidingDown = false` (정상) | `isSlidingDown = false` (정상) |

---

### 원인 2: Box2D 접촉 캐시 오염 (TerrainCollider.cs)

`UpdateCollider()` 호출 시 기존 경로를 그대로 두고 새 경로로 교체하면, Box2D가 이전 접촉 상태를 유지하면서 플레이어에게 **잘못된 방향(아래쪽)의 impulse**를 적용할 수 있다.

Box2D는 접촉 쌍을 프레임 간 warm-starting으로 유지한다. 콜라이더 경로가 바뀌어도 이전 접촉 캐시가 그대로 남아, 플레이어가 가까이 있는 폴리곤 경로의 아래쪽 법선을 잘못 받아 `rb.linearVelocity.y`가 음수가 된다. 이것이 FixedUpdate에서 보존되어 지속적으로 아래로 이동하는 피드백 루프를 만든다.

```
FixedUpdate N:
  rb.linearVelocity.y = (Box2D 잘못된 impulse로) -v

FixedUpdate N+1:
  targetVelocity.y = rb.linearVelocity.y  // -v 보존
  rb.linearVelocity = targetVelocity       // 세팅
  Box2D: 또 잘못된 impulse 적용            // -v 추가
→ 루프 반복, 플레이어 계속 꺼짐
```

파기를 하면 콜라이더가 다시 업데이트되어 접촉 상태가 리셋되므로 꺼짐이 멈췄다.

#### 수정

새 경로 계산 전에 `pathCount = 0`으로 기존 경로를 지워 Box2D가 접촉을 처음부터 재계산하게 한다.

```csharp
// 수정 전
public void UpdateCollider()
{
    _polyCollider.offset = Vector2.zero;
    InitializeTracingState();
    FindAndTraceAllOutlines();
    ApplyPathsToCollider();  // 기존 경로 위에 덮어씀 → 접촉 캐시 오염 가능
}

// 수정 후
public void UpdateCollider()
{
    _polyCollider.offset = Vector2.zero;
    _polyCollider.pathCount = 0;          // ← 기존 경로 먼저 삭제, 접촉 캐시 초기화
    InitializeTracingState();
    FindAndTraceAllOutlines();
    ApplyPathsToCollider();
}
```

**안전성**: `pathCount = 0`과 새 경로 설정이 모두 LateUpdate 안에서 연속 실행된다. Unity는 물리를 FixedUpdate에서만 실행하므로, FixedUpdate는 `pathCount = 0`인 중간 상태를 절대 보지 않는다. 최종 상태(새 경로)만 다음 물리 스텝에 반영된다.
