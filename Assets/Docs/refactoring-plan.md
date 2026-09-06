# 리팩토링 계획
@tags: refactoring, plan, TerrainChunk, InfinityMapManager, collider, visual

콜라이더·비주얼 파이프라인 버그(collider-visual-decoupling.md 참고)를 분석하면서
드러난 구조적 문제 목록과 우선순위.

---

## 1. `ProcessDirtyChunksAsync` 분리 ✅ 완료 (구현 완료)

**문제**
6단계 로직이 한 코루틴에 인라인되어 있어 Step 2와 LateUpdate 타이밍 사이의 버그를
추적하는 데 상당한 시간이 걸렸다. 기능 추가 시 같은 종류의 회귀 버그가 나기 쉽다.

**변경 내용**
`ProcessDirtyChunksAsync`의 각 단계를 독립 헬퍼 메서드로 추출.
코루틴 본문은 흐름만 표현하도록 단순화.

```
SnapshotScheduleInitJobs()    — Step 1: Init 잡 스케줄
SnapshotScheduleRound1()      — Step 2: Round 1 Visual 잡 스케줄
SnapshotAreAllJobsDone()      — Steps 3, 5: 완료 여부 확인
SnapshotCompleteLighting()    — Step 3 후처리: CompleteLighting
SnapshotScheduleRound2()      — Step 4: Round 2 Visual 잡 스케줄
SnapshotFinalizeAndApply()    — Step 6: 텍스처 업로드
```

**파일**: `InfinityMapManager.cs`

---

## 2. 더티 플래그 소유권 통합 ✅ 완료 (구현 완료)

**문제**
`IsVisualDirty = true`, `IsColliderDirty = true`, `HasBeenModified = true` 세 줄이
TerrainModifier(4곳), ElevatorSpawner(1곳), TerrainChunk.Carve(1곳) 등 여러 파일에
직접 흩어져 있다. 새 플래그가 추가되거나 기존 플래그 의미가 바뀌면
모든 호출 지점을 찾아야 한다.

**변경 내용**
`ChunkData`에 두 가지 의미론적 메서드 추가:
- `MarkDirty()` — 지형 픽셀 수정 후 세 플래그 일괄 세팅 (저장 포함)
- `MarkRenderDirty()` — 렌더링 갱신만 필요할 때 두 플래그 세팅 (저장 제외)

호출 지점을 메서드 호출로 교체.

**파일**: `ChunkData.cs`, `TerrainModifier.cs`, `TerrainChunk.cs`, `ElevatorSpawner.cs`

---

## 3. `worldSettings.json` 청크 설정 미적용 ✅ 완료 (구현 완료)

**문제**
`worldSettings.json`의 `chunk.colliderUpdateInterval` 값이 코드에 로드되지 않았다.
`[SerializeField]` 제거 후 인스펙터 경로도 막혀, 설정 파일과 실제 동작이 불일치.

**변경 내용**
- `InfinityMapManager.ApplyWorldSettings()`에서 `s.chunk.colliderUpdateInterval` 로드
- `TerrainChunk`의 `colliderUpdateInterval`을 `private static s_colliderUpdateInterval`로 변경
- `TerrainChunk.SetDefaultColliderUpdateInterval(float)` 정적 메서드 추가 → 모든 인스턴스에 즉시 적용
- `ShouldUpdateCollider()`가 정적 값을 읽도록 변경
- 비직렬화 필드의 인스턴스 복사 문제를 우회하기 위해 static 패턴 사용

**파일**: `InfinityMapManager.cs`, `TerrainChunk.cs`

---

## 4. 콜라이더 전용 루프 전체 청크 순회 최적화 ✅ 완료 (구현 완료)

**문제**
`InfinityMapManager.LateUpdate`의 콜라이더 갱신 루프가 매 프레임 전체 활성 청크를
순회하며 `tc.isDirty` 불리언 체크를 수행한다.
현재 청크 수에서는 문제없지만, 대규모 맵에서 누적 비용이 생길 수 있다.

**목표**
`MarkChunkDirty()`가 호출될 때 `_dirtyColliderChunks` 별도 집합에 추가하고,
`TryUpdateCollider()` 성공 시 제거하는 방식으로 순회 범위를 축소.

**파일**: `InfinityMapManager.cs`

---

## 5. `ShouldUpdateCollider()` 스로틀 근거 문서화 ✅ 완료 (구현 완료)

**문제**
`colliderUpdateInterval = 0.2s`의 근거가 코드에 명시되지 않았다.
드릴 속도(`dashSpeed`)나 `digRadius`가 바뀌었을 때 이 값도 함께 조정해야 하는지
알기 어렵다.

**목표**
허용 가능한 최대 지연 거리(= dashSpeed × colliderUpdateInterval)가
ImmediateDig 선행 파기 거리(= digRadius)보다 작아야 한다는 조건을 주석으로 명시.
현재 값 기준 계산 결과를 함께 기록.

```
현재 값:
  dashSpeed              = 2.5  unit/s
  colliderUpdateInterval = 0.2  s
  최대 지연 이동 거리      = 0.5  unit

  ImmediateDig 선행 거리 = digRadius = 1.0 unit

  여유 = 1.0 - 0.5 = 0.5 unit  →  현재 값은 안전
  주의: dashSpeed > 5.0 또는 digRadius < 0.5 이면 재검토 필요
```

**파일**: `TerrainChunk.cs` (주석)
