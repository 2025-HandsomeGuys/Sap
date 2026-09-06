# 버그 조사: 암석 중복 생성 (2026-03-18)
@tags: bug, rock, DiggableRock, duplicate, chunk-reload, SpawnedRocks

## 증상

- 지형을 파면 **같은 자리에 같은 모양의 암석이 2개씩 겹쳐서** 나타남
- 게임 시작 직후(첫 방문 청크)부터 발생 — 청크 재로드와 무관
- `enableDiskSave` 설정과 무관
- 갔다가 돌아온다고 더 늘어나지는 않음
- 희귀 광물이 돌에 무조건 생기도록 코드를 변경한 이후 발생 추정

---

## 원인 분석

### 핵심 메커니즘

`GenerateRocks()`는 `RockLayoutCalculator.GetRockLayout()`을 호출하며,
이 함수는 **청크 좌표 + 월드 시드** 기반의 결정론적 PRNG로 암석 위치를 계산한다.

```
baseSeed = (coord.x * HASH_X) ^ (coord.y * HASH_Y) ^ worldSeed
```

`CollisionChecker.IsCollidingRadial()`은 **같은 호출 내** `results` 리스트만 참조하므로,
`GenerateRocks()`를 **두 번 호출**하면 두 번째 호출의 `results`가 비어있어
첫 번째와 완전히 동일한 위치에 암석이 생성된다.

→ **결론: `GenerateRocks()`가 같은 청크에 대해 2번 호출되고 있음**

### 유력한 원인

"희귀 광물이 돌 안에 생기도록" 구현하기 위해 **암석을 먼저 생성하고 광물을 배치**하는 순서로 바꾸면서, `ChunkSpawner.InitializeDecorators()`에서 `RockDecorator`를 앞으로 이동 추가했지만 **기존 항목을 제거하지 않아 두 개가 된 것**으로 추정.

현재 코드가 이런 상태일 가능성:

```csharp
// ChunkSpawner.InitializeDecorators() — 버그 상태 (추정)
_decorators.Add(new ElevatorDecorator());
_decorators.Add(new RockDecorator(visualSettings)); // ← 새로 추가
_decorators.Add(new MineralDecorator());
_decorators.Add(new RockDecorator(visualSettings)); // ← 기존 것 (미삭제)
```

정상 상태:

```csharp
_decorators.Add(new ElevatorDecorator());
_decorators.Add(new MineralDecorator());
_decorators.Add(new RockDecorator(visualSettings)); // 하나만 존재
```

---

## 관련 코드 흐름

```
ChunkLoadingRunner.ProcessChunkQueue()
  └─ pipeline.ExecutePhase2_FinalizeAndDecorate()
       └─ tChunk.Reuse_Step2_Finalize()          // SetActive(true)
       └─ _spawner.DecorateChunk_Phase2()
            ├─ 기존 자식 정리 (ROCK_* → ReturnToPool)
            └─ foreach decorator in _decorators
                 ├─ ElevatorDecorator.Decorate()
                 ├─ MineralDecorator.Decorate()
                 └─ RockDecorator.Decorate()
                      └─ TerrainDecorator.GenerateRocks()
                           └─ RockLayoutCalculator.GetRockLayout()  ← 결정론적 PRNG
```

### 핵심 파일 목록

| 파일 | 역할 |
|------|------|
| `ChunkSpawner.cs` | `InitializeDecorators()` — 데코레이터 목록 관리 |
| `TerrainDecorator.cs` | `GenerateRocks()` — 암석 배치 파사드 |
| `RockDecorator.cs` | `IChunkDecorator` 구현, `GenerateRocks` 호출 |
| `RockLayoutCalculator.cs` | 시드 기반 결정론적 배치 계산 |
| `CollisionChecker.cs` | `IsCollidingRadial()` — **같은 호출 내** 충돌 검사만 수행 |

---

## 진단 방법

`TerrainDecorator.cs` 25번째 줄 주석 해제:

```csharp
Debug.Log($"[GenerateRocks] Called for chunk {coord}, checkExistingData={checkExistingData}");
```

게임 실행 후 Console에서 **같은 청크 좌표로 두 번 출력**되면 원인 확정.

---

## 수정 방향

1. `ChunkSpawner.InitializeDecorators()`에서 `RockDecorator`가 **하나만** 등록되어 있는지 확인
2. 만약 두 개라면 하나 제거
3. "희귀 광물 → 돌 안에 생성" 기능은 아래 두 가지 올바른 구현 방향 중 선택:

### 방향 A: 데코레이터 순서 변경 (권장)

```
ElevatorDecorator → RockDecorator → MineralDecorator(rocks 참조하여 희귀광물 배치)
```

- `DecorationContext`에 `SpawnedRocks` 참조 추가
- `MineralDecorator`에서 희귀 광물만 암석 위치에 배치

### 방향 B: 암석 파괴 시 드롭 (현재 구현 확인)

현재 `DiggableRock.DropRareMinerals()`가 이미 구현되어 있음.
암석을 파괴하면 희귀 광물이 드롭됨 — 별도 변경 없이 이 메커니즘 활용 가능.

---

## 코드 구조 메모 (조사 중 파악)

- `_decorators` 리스트는 `ChunkSpawner` 인스턴스당 1회 lazy init (`InitializeDecorators`)
- `ChunkSpawner` 인스턴스는 `InfinityMapManager`에 1개만 존재
- `CollisionChecker.EXTRA_SAFETY_MARGIN = 40f` — 같은 호출 내에서는 절대 겹치지 않음
- 청크 반환(`ChunkPool.Return`) 시 암석 자식은 별도 정리 없이 청크와 함께 비활성화됨
- 청크 재사용 시 `DecorateChunk_Phase2` 앞단의 정리 루프가 `ROCK_*` 이름의 자식을 모두 반납
