# 성능 최적화 이력 — 인덱스
@tags: performance, optimization, profiler, gc, job, burst, chunk, terrain, index

이 프로젝트의 **성능 최적화 작업 전체 목록**. 완료 항목과 미처리 항목을 한 곳에서 관리한다.

> ## 규칙
> **성능 최적화 작업을 하면 반드시 이 폴더에 문서를 남기고 아래 표에 한 줄 추가한다.**
> 규모가 작으면 별도 문서 없이 표에 한 줄만 적어도 된다. 자세한 기준은 CLAUDE.md "성능 최적화 작업 규칙" 참고.
>
> 이 인덱스가 생기기 전(2026-07-31 이전) 항목 일부는 세션 메모리에서 복원한 것이라
> **"무엇을 왜 바꿨는가" 수준만 신뢰할 수 있다.** file:line·수치는 현재 코드로 재확인할 것.

---

## 1. 완료 이력

| 날짜 | 작업 | 문제 → 수정 | 실측 | 상세 |
|------|------|------------|------|------|
| — | **콜라이더/비주얼 파이프라인 분리** | 드릴 연속 파기 중 콜라이더가 영구 차단 → `TryUpdateCollider()`를 `ApplyTexture()`에서 분리 | — | [collider-visual-decoupling.md](../collider-visual-decoupling.md) |
| — | **`ProcessDirtyChunksAsync` 6단계 분리** | 6단계 로직이 한 코루틴에 인라인 → 독립 헬퍼 메서드로 추출 | — | [refactoring-plan.md](../refactoring-plan.md) §1 |
| — | **거리장 half-resolution** | 청크 로드 시 chamfer 스파이크 → DF 계산 해상도만 1/2 (`DownsampleMaskJob`/`UpsampleDistanceJob` 추가, 기존 잡 내부 미변경) | 구현 완료, 성능 재측정 미완 | [half-res-distance-field.md](../half-res-distance-field.md) |
| 2026-05-29 | **지형 Tier 1·2 최적화** | 아래 §1.1 참고 | — | 문서 없음 (이 표가 유일 기록) |
| 2026-07-09 | **chamfer strip 스코핑** | BoundarySync가 ext를 **무조건 청크 전체(1000×1000)** 로 확장 → 확장 키를 "이웃 로드 여부"에서 **"dirty rect가 그 경계에 실제 닿았는가"** 로 변경, 닿은 방향만 폭 100px strip | 터레인 경로 **60ms+ → 1.33ms (~45×↓)**, 프로파일러 검증 완료 | 문서 없음 (이 표가 유일 기록) |
| 2026-07-13 | **Job 파이프라인 낭비 제거** | Round1/Round2가 같은 잡을 두 번씩 돌리고, 병렬 잡이 dirty rect가 아니라 청크 전체 격자(1M)로 dispatch → 잡 16→11, 워크아이템 5.5M→50k. `MarkNeighborsDirty` 평행 방향 스코핑 포함 | 경계 드릴 sync point **~19ms → 0.6~0.9ms** | [job-pipeline-waste-removal.md](../job-pipeline-waste-removal.md) (+ `-plan.md`) |
| 2026-07-31 | **청크 로딩 GC 할당 제거** | 신규 청크마다 `new byte[1M]`(+블렌딩 시 6MB) 스테이징 배열을 만들고 버림 → uniform ID + MemSet + 재사용 버퍼 | 청크당 매니지드 할당 **1~6MB → 0** (프레임 시간은 원래 예산 내) | [chunk-load-gc.md](chunk-load-gc.md) |
| 2026-08-07 | **`CheckSupport` 샘플 배열 할당 제거** | `MineralItemController.CheckSupport()`가 호출마다 `new Vector2[9]` 생성. 매설 광물마다 0.5초 주기 코루틴이라 광물 수에 비례해 누적 → `s_supportOffsets` 정적 단위벡터 테이블 + 루프 내 `origin + offset * r` | 코드상 확정 할당 제거(**호출당 ~96B → 0**). 광물 1000개 기준 산술 ~190KB/s, 밀도 3배 시 ~860KB/s. **프로파일러 실측은 미실시** — 체감 랙이 없는 상태의 예방적 수정 | 아래 §1.3 |
| 2026-09-01 | **탐지파동 VFX 버퍼 재사용 + 머티리얼 공유** | `DetectionPulseRelic`이 발동마다 리스트 6개를 새로 만들고, 링·핑 하나하나에 `new Material(Shader.Find(...))`을 돌렸다 → 리스트는 인스턴스 재사용 버퍼로, 머티리얼은 `s_lineMat` static 공유 1개로. 코루틴이 프레임을 넘겨 읽는 버퍼라 **재발동 가드(`_vfxRoutine` stop + `CleanupVFX`)** 를 같이 넣음 | 발동당 리스트 6→0, `Material`+`Shader.Find` (n+1)→0. **저빈도(쿨 60s)라 GC 영향 자체는 미미** — 실질 소득은 머티리얼 생성/파괴 제거와 아래 ⚠의 오브젝트 누수 차단 | 아래 §1.4 |

| 2026-09-02 | **광물 풀 경로 일원화 + 풀 키 데이터화** | 주운 광물이 죽은 `ObjectPooler`로 반납 시도 → 항상 `Destroy` 폴백. 60초 만료분만 풀로 돌아가고 획득분은 매번 파괴→재생성. 게다가 반납 키가 `name.Split('_')[1]` 파싱이라 SO 이름에 `_`가 들어가면 넣는 키와 어긋남 → `PickupableItem`을 `MineralGenerator` 풀로 합치고, 풀 신원을 `MineralItemController.poolKey`로 주입 | 획득 광물의 `Destroy`+`Instantiate` 왕복 제거(획득 빈도만큼). **프로파일러 실측 없음** | 아래 §1.5 |
| 2026-09-04 | **파기 물리 쿼리 NonAlloc 전환 + 돌 단일 타격** | 파기 호출부 6곳이 `OverlapCircleAll`/`CircleCastAll`(호출마다 새 배열)을 쓰고 있었다. 드릴 대시는 매 프레임, 전략 둘은 타격마다 탄다 → `MiningTargetPicker`(공용 선정기 + NonAlloc 헬퍼)로 통일. ⚠ 같이 고친 버그: 곡괭이·삽이 원에 걸린 돌을 **전부** 때리고 스태미나도 개수만큼 뺐다 → 보는 방향 최근접 **하나만** | 파기 경로 호출당 배열 할당 **1~2개 → 0**. **프로파일러 실측 없음**(예방적 수정) | 아래 §1.6 |

### 1.1 지형 Tier 1·2 최적화 상세 (2026-05-29)

**Tier 1 — 메모리**

| 대상 | 변경 |
|------|------|
| `ChunkData` | `CurrentPixels` NativeArray 제거 (**청크당 4MB 절감**) |
| `TerrainChunk` | `pixelData` 프로퍼티 제거, `CopyFrom` 6곳 제거 |
| `TerrainCarver` / `RollingRockTrap` / `ElevatorSpawner` / `PixelFloorCollapser` / `IndestructibleOverlayInit` / `SpriteCavityInitializer` | `CurrentPixels` 접근 제거 |
| `DiggableRock` | `_chunk` 필드 캐싱(`Start()`), `GetComponentInParent` 4곳 교체 |

**Tier 2 — CPU**

| 대상 | 변경 |
|------|------|
| `TerrainModifier` | `Array.Clear(1MB)` → `_visitedIndices` 선택적 리셋 |
| `TerrainCollider` | `FindAndTraceAllOutlines` 이중 `Array.Clear` 제거 |
| `TerrainChunk` | `RemoveSpawnedRock` O(n) → swap-and-pop O(1) |
| `DiggableRock` | 파기 핫패스 `Debug.Log` 6개 제거 |

**검토 후 "이미 올바름"으로 판정한 것** (다시 건드리지 말 것)
- `EnsureJobsCompleted()` — 잡 실행 중일 때만 블로킹, 완료 시 no-op
- `_pathBoundsBuffer[16]` — `FixPathWindings`에서 `count > 16` 시 자동 성장 (버그 아님)
- `Array.Clear(_pixelState, 1MB)` 전체 clear — 선택적 clear는 이전 세션의 `state=2`가 새 BFS를 오염시킴. `_visitedIndices` 방식으로 대체한 이유

### 1.2 chamfer strip 스코핑 상세 (2026-07-09)

별도 설계 문서가 없는 작업이라 구현 디테일을 여기 보존한다.

위치: `ChunkJobScheduler.ScheduleBoundarySync()` 끝부분 — Round2 chamfer ext 확장 로직.

**기존 버그**
BoundarySync 시 ext를 **무조건 청크 전체(1000×1000)** 로 확장했다.
확장 여부를 이웃 로드 여부(`hasLeft/hasRight/...`)만 보고 판단해서, 정상 플레이(4방향 로드)에선 **항상** full-chunk.
경계뿐 아니라 **청크 중앙 파기도 매 프레임 full-chunk 단일 스레드 chamfer 2패스**를 돌던 게 진짜 낭비였다.

**수정**
확장 키를 "이웃 로드 여부" → **"dirty rect(`_lastExt`)가 그 경계에 실제 닿았는가"** 로 변경.
닿은 경계 방향으로만 폭 `SYNC_DEPTH + PROP_MARGIN = 100px` strip을 확장한다.

- **안전성 논증**: dirty가 경계에서 멀면 BoundarySync는 같은 값만 재기록한다 → 변화 없음 → strip이 불필요.
  이웃이 변경된 케이스도 `MarkNeighborsDirty`가 경계 strip rect로만 이웃을 dirty 처리하므로 커버된다.
- ⚠ **`near*` 4개는 `_lastExt`를 수정하기 _전에_ 먼저 확정해야 한다.** 안 그러면 확장이 연쇄해서 결국 full-chunk가 된다.
- 효과: 중앙 ~100×↓, 경계 1면 ~10×↓. 코너 2면은 full-chunk 폴백(드묾).

### 1.3 `CheckSupport` 샘플 배열 할당 제거 (2026-08-07)

규모가 작아 별도 문서 없이 여기 보존한다.

위치: `MineralItemController.CheckSupport()` (`Assets/Scripts/UI/Items/Minerals/Minerals/`)

**기존**
```csharp
Vector2[] testPoints = new Vector2[] { (Vector2)transform.position, ... 9개 ... };
foreach (var p in testPoints) { ... }
```
`CheckStructuralIntegrityRoutine`이 **매설 광물마다 0.5초 주기**로 `CheckSupport()`를 호출한다.
즉 이 배열은 `광물 수 × 2`회/초로 생성되고 즉시 버려진다.

**수정**
`s_supportOffsets` 정적 단위벡터 테이블(9개)을 두고 루프에서 `origin + s_supportOffsets[i] * r`로 계산.
`Vector2`는 구조체라 곱셈·덧셈에 할당이 없다.

- 샘플 위치·순서·판정 결과는 **완전히 동일하다.** 동작 변경 없음
- `checkCount` 지역변수는 `s_supportOffsets.Length`로 대체 (주석 처리된 디버그 블록의 참조도 같이 갱신)

⚠ **프로파일러 실측을 하지 않았다.** 체감 랙이 보이는 상태가 아니었고,
"호출마다 배열을 새로 만든다"가 코드상 확정적이라 측정 없이 진행한 예방적 수정이다.
수치(~190KB/s @ 광물 1000개)는 `배열 96B × 2회/초 × 광물 수` 산술이지 측정값이 아니다.

**배경**: 광물 밀도 재설계(`Assets/Docs/mineral-density-redesign.md`)로 `globalMineralDensity`를 올릴 수 있게 되면서
광물 수가 몇 배로 늘 수 있게 됐다. 이 할당은 광물 수에 선형 비례한다.

### 1.4 탐지파동 VFX 리스트 재사용 (2026-09-01)

규모가 작아 별도 문서 없이 여기 보존한다.

위치: `Assets/Scripts/Gameplay/Relics/Behaviours/DetectionPulseRelic.cs`

**기존**
`_buffer`(스캔 결과)만 재사용 필드였고, 그 바로 아래에서 `new List<Vector2>(_buffer.Count)` ·
`new List<Vector2Int>(...)`를 만들어 `PulseVFX` 코루틴에 넘겼다. 코루틴은 다시 `pingObjs`/`pingMats`/
`pingDist`/`pingFired` 4개를 더 만들었다. 발동마다 리스트 6개.

**수정**
6개 전부 인스턴스 `readonly List<T>` 필드로 올리고 `Clear()`로 돌려쓴다.
`PulseVFX(center, maxR)`는 더 이상 리스트를 파라미터로 받지 않고 필드를 직접 읽는다.

**⚠ 이 버퍼는 프레임을 넘겨 산다 — 재발동 가드가 세트다**

`_pings` 이하는 `PulseVFX`가 **연출이 끝날 때까지(~10초) 계속 읽는다.** 재사용 필드로 바꾼 순간
"다음 발동이 진행 중인 연출의 데이터를 덮어쓴다"가 성립한다. 그래서 `OnActivate`에
`_vfxRoutine != null → StopCoroutine + CleanupVFX` 가드를 같이 넣었다.
**가드를 지우면 버퍼 재사용이 곧바로 버그가 된다.**

- 슬롯 간 충돌은 원래 없다 — `RelicManager`가 슬롯마다 `Clone()`으로 별도 인스턴스를 들고 있다
  (`_slotBehaviour[]`). 문제는 **같은 슬롯의 재발동** 하나뿐이다.
- 현재 도달 불가: 쿨타임 60s ≫ 연출 길이(반경 6청크 대각선 ≈ 85유닛 ÷ `pulseSpeed` 6 ≈ 14s).
  쿨타임 감소 유물(`GetCooldownScale`)이 겹치면 좁혀질 수 있어 방어로 넣었다.

**부수 효과 — 오브젝트 누수 차단(사실상 이쪽이 본 소득)**

기존 정리 코드는 코루틴 **끝**에만 있었다. `StopCoroutine`으로 중간에 끊으면 링·핑
`GameObject`와 `Material`이 전부 씬에 남는다. 정리를 `CleanupVFX()`로 빼고 정상 종료·중도 취소
양쪽에서 부르게 해서 이 경로를 막았다. 링 오브젝트(`_ringGo`/`_ringMat`)를 코루틴 지역변수에서
필드로 올린 이유가 이것이다.

**머티리얼 공유 — 리스트보다 이쪽이 실질 비용이었다**

리스트 6개는 60초에 한 번이라 GC 관점에서 사실상 무의미하다. 같은 발동 안의 진짜 비용은
**링 1개 + 핑 n개마다 돌던 `new Material(Shader.Find("Sprites/Default"))`** 였다
(`Shader.Find`는 문자열 전역 검색, `Material`은 네이티브 인스턴스 생성 → 몇 초 뒤 `Destroy`).

핑 색은 전부 `LineRenderer.startColor/endColor`(정점 컬러)로 주고 머티리얼 프로퍼티는 아무도
안 건드린다 → 인스턴스를 나눌 이유가 없다. `s_lineMat` **static 공유 머티리얼 1개**로 통합했다.

- ⚠ **`sharedMaterial`로 대입할 것.** `Renderer.material`은 대입해도 인스턴스 사본이 생겨
  공유가 무효가 되고, 사본이 렌더러와 함께 새어 나간다.
- 씬 언로드로 파괴되면 `LineMaterial()`의 `== null` 체크가 다시 만든다
  (파괴된 `UnityEngine.Object`는 `== null`이 true). 에디터 도메인 리로드도 같은 경로로 복구된다.
- `_pingMats` 리스트와 `_ringMat` 필드, `CleanupVFX`의 머티리얼 `Destroy` 2곳이 통째로 사라졌다.

**핑 오브젝트 풀링은 하지 않았다.** 손익분기가 안 맞는다 — 광물(청크 로드마다 수십~수백 개,
`ObjectPooler`로 풀링 중)과 달리 핑은 60초에 한 번 몇 개다. 풀 생명주기(고갈 정책·상태 오염·
씬 전환 시 정리)와 "풀을 어디 두나"(유물 인스턴스=해제 시 누수 / static=파괴된 참조 잔존 /
씬 MonoBehaviour=유물 하나 때문에 씬 세팅 추가) 비용이 이득보다 크다.

⚠ **프로파일러 실측 없음.** 코드상 확정적인 할당·생성만 제거한 예방적 수정이다.

### 1.5 광물 풀 경로 일원화 + 풀 키 데이터화 (2026-09-02)

규모가 작아 별도 문서 없이 여기 보존한다.

**문제 1 — 퇴장 경로가 둘로 갈려 있었다**

같은 광물 GameObject(프리팹 30개가 `MineralItemController` + `PickupableItem`을 함께 보유)가
어떻게 월드를 떠나느냐에 따라 다른 처분을 받았다.

| 퇴장 방식 | 경로 | 결과 |
|---|---|---|
| 60초 만료 | `MineralLifetime` → `MineralGenerator.ReturnToPool` | 풀 반납 |
| 플레이어 획득 | `PickupableItem.RemoveFromWorld` → `ObjectPooler` | **`Destroy`** |

`ObjectPooler`는 **`SpawnFromPool` 호출부가 프로젝트에 0개**라 넣기만 하고 꺼내는 쪽이 없었고,
씬의 `stratumPools`도 비어 있어 `HasPool`이 항상 false → 매번 `Destroy`로 폴백됐다.
플레이 중엔 줍는 쪽이 압도적이라 **풀이 사실상 안 채워지고 청크 로드마다 `Instantiate`가 돌았다.**

→ `RemoveFromWorld`가 `MineralGenerator.ReturnToPool`을 부르도록 통일.

**문제 2 — 넣는 키와 빼는 키가 다른 방식으로 만들어졌다**

```
꺼낼 때: mineralSO.name                       // "Gold"
반납할 때: mineral.name.Split('_')[1]          // "MINERAL_Gold_10_20" → "Gold"
```

MineralSO 이름에 `_`가 하나라도 들어가면(`Gold_Ore`) 두 키가 어긋난다 →
그 큐는 아무도 안 꺼내거나(누수), 같은 앞토막을 가진 다른 광물로 나온다.
**`RockSpawner`가 이미 같은 이유로 이름 기반을 버리고 `DiggableRock.poolKey`로 옮긴 전례가 있다.**

작업 시점 에셋 23개는 전부 언더스코어가 없어 **아직 안 터진 상태**였다. 예방적 수정이다.

→ `MineralItemController.poolKey`(`[NonSerialized]`)를 `SpawnMineralObject`가 주입하고,
   `ReturnToPool`은 그 값으로만 큐를 찾는다.

**부수 효과 — 출처 판별이 공짜로 따라온다**

`IceBreakable.fracturedPrefab` / `SnowmanEntity.lootItemPrefab` /
`DokkaebiCauldron.explosiveHazardPrefab`이 **런타임에 직접 `Instantiate`하는 광물**이 있다.
이것들은 `MineralGenerator`를 안 거쳐 `poolKey`가 비어 있으므로 `ReturnToPool`이 자동으로
파괴한다. 호출측이 출처를 구분할 필요가 없다.

⚠ **주의점**

- `poolKey`는 **상태가 아니라 신원**이다. `MineralItemController.OnEnable`(풀 재사용 리셋)에서
  지우면 안 된다 — 지우는 순간 반납이 전부 `Destroy`로 떨어져 풀링이 조용히 죽는다.
- 이름 규격 `MINERAL_{키}_{x}_{y}`는 **그대로 유지**한다. 반납 키에서는 빠졌지만
  `ChunkSpawner`의 장식 정리 루프와 `InfinityMapManager.DetachMineralsToWorld`가
  청크 자식 중 광물을 이 접두사로 골라내는 데 여전히 쓴다.
- 남아 있는 `Split('_')` 3곳(`InfinityMapManager`·`MineralItemController`·`PickupableItem`)은
  **뒤에서 두 토큰**을 픽셀 좌표로 읽는다. 앞이 아니라 뒤에서 세므로 언더스코어에 안전하다.
- `MINERAL_` 접두사가 있는데 `poolKey`가 비면 주입 실패이므로 `ReturnToPool`이 경고를 찍는다.
  다른 시스템이 만든 광물은 접두사가 없어 조용히 파괴된다(정상).

**남은 작업 (에디터 필요)**

`ObjectPooler`는 이제 코드 참조가 0이지만 컴포넌트가 씬 4곳(`DemoUnderground`,
`CopyDemoUnderground`, `khbScene`, `khbScene 1`)에 배치돼 있어 파일을 지우면 Missing Script가 뜬다.
씬에서 컴포넌트를 뗀 뒤 `ObjectPooler.cs` + `Mineable.spawnedFromLayer`를 함께 제거하면 된다.
파일 상단에 사용 중지 주석을 남겨뒀다.

**미처리** — `_mineralPools`·`_rockPools`에는 `ChunkPool.MAX_POOL_SIZE` 같은 상한도,
씬 전환 `Clear()`도 없다. §2.3 참고.

### 1.6 파기 물리 쿼리 NonAlloc 전환 + 돌 단일 타격 (2026-09-04)

**계기는 성능이 아니라 버그다.** "곡괭이 한 번에 돌이 여러 개 캐진다"를 고치러 들어갔다가
같은 줄에서 프레임마다 배열을 버리고 있는 것을 같이 정리했다.

**할당 제거** — `Physics2D.OverlapCircleAll`·`CircleCastAll`은 호출마다 새 배열을 반환한다.
파기 호출부 6곳이 전부 그 계열이었다:

| 위치 | 전 | 후 |
|------|-----|-----|
| `Digger.ImmediateDig` | `CircleCastAll` | `CircleCast` + `_castHits` 재사용 |
| `Digger.DigAt`(불괴 스윕) | `CircleCastAll` | 〃 |
| `Digger.ImmediateDigRock` / `TryDigRockOnly` / `DigAt`(돌) | `OverlapCircleAll` | `MiningTargetPicker.PickNearest` (내부 static 버퍼) |
| `PickaxeStrategy` / `SapStrategy`(지형 청크) | `OverlapCircleAll` | `MiningTargetPicker.Overlap` + 인스턴스 `_terrainHits` |

빈도가 핵심이다. `ImmediateDig`는 **드릴 대시가 매 프레임** 타고, 전략 두 곳은 타격마다 탄다
(삽은 큰 반경 쿼리를 하나 더 돌려 스윙당 2회였다). 반환 배열 크기는 겹친 콜라이더 수에
비례하므로 돌이 많은 지층일수록 커진다. **프로파일러 실측은 안 했다** — 예방적 수정이다.

⚠ **동작 변경 — 돌은 이제 한 번에 하나만 맞는다.**
`PickaxeStrategy`·`SapStrategy`는 파기 원에 걸린 `IDiggable`을 **전부** 때리고 있었다.
그 부작용으로 **스태미나도 겹친 돌 개수만큼** 빠졌다(`UseStamina`가 루프 안에 있었다).
`Digger` 쪽 3곳은 첫 히트에서 `return`이라 하나만 때리긴 했지만, 그 '첫'은 Physics2D가
돌려준 순서 — 보장이 없어서 **가까운 돌이 아니었다.**

이제 셋 다 `MiningTargetPicker.PickNearest(digCenter, radius)` 하나를 거친다.
`digCenter`가 이미 `플레이어 위치 + 바라보는 방향 × 반경`이라 **방향 판정을 따로 넣지 않았다** —
그 점에 제일 가까운 하나가 곧 "보는 방향에서 제일 가까운 돌"이다. 거리는 `transform`이 아니라
`Collider2D.ClosestPoint`로 잰다(돌 크기가 제각각이고, `RockHitAnimator`가 transform을
흔드는 동안 값이 어긋난다 — CLAUDE.md §17).

지형 청크 루프는 **일부러 그대로 다 훑는다.** 파기 원이 청크 경계에 걸치면 양쪽이 다 깎여야
한다 — 여기까지 하나로 줄이면 이음매에 안 파인 띠가 남는다.

**버퍼 불변식** — `PickNearest`의 static 버퍼는 밖으로 나가지 않는다. 선정이 끝난 뒤에야
`Dig()`가 불리므로 그 안에서 폭발 등이 다시 오버랩을 돌려도 읽는 중인 배열을 덮어쓸 수 없다.
호출부가 버퍼를 들고 루프를 도는 구조로 되돌리면 이 보장이 깨진다. 그래서 지형 루프용
버퍼는 전략 **인스턴스 필드**로 따로 뒀다.

`ContactFilter2D`는 `MiningTargetPicker.DefaultFilter` 하나로 통일했다. 기본값은
`useTriggers = false`라 그냥 두면 트리거 콜라이더가 통째로 안 잡혀 구 API와 결과가 달라진다
(`LightningRelic`이 이미 밟은 함정).

---

## 2. 미처리 항목

2026-07-31 기준 코드에 전부 남아 있음을 확인함.

### 2.1 다음 병목 후보

| 항목 | 내용 | 근거 |
|------|------|------|
| **`Physics2D.Simulate` (~5ms)** | Idle 스파이크 제거 후 **게임 스레드 최상위 비용**으로 드러남. 가설: 콜라이더 재생성 또는 debris 파티클 콜라이더 | 2026-07-09 프로파일링 |
| **`GameObject.Activate` (청크 로드 프레임 85회, 1.90ms)** | 데코레이터가 풀 오브젝트를 활성화하는 비용. 분산 여지 있음 | 2026-07-31 프로파일러 |
| **매설 광물의 물리·폴링 상시 비용** | 매설 광물 하나가 `Rigidbody2D`+`PolygonCollider2D`를 들고 있고 `CheckStructuralIntegrityRoutine`이 0.5초마다 돈다. 위 두 병목(`Physics2D.Simulate`·`Activate`)이 **광물 수에 선형 비례**하는데, 2026-08-07 밀도 재설계로 `globalMineralDensity` 한 값으로 광물 수를 몇 배씩 올릴 수 있게 됐다. 후보: 매설 상태에선 물리 컴포넌트를 끄고 캐낼 때 켜기 — `OnNearbyTerrainDug` 이벤트 경로가 이미 있어 0.5초 폴링 자체가 불필요할 수 있다 | 미측정. ⚠ 낙하 버그 이력이 얽힌 영역(`project_mineral_bugfixes` 참고)이라 **프로파일러 근거 없이 손대지 말 것** |

### 2.2 이득 대비 비용이 나쁜 것 (하지 말라는 뜻은 아니지만 우선순위 낮음)

| 항목 | 내용 | 왜 미뤘나 |
|------|------|----------|
| JobHandle 수동 의존성 → 리소스 단위 추적기 | 진입점 `AllPrevHandles()` | 이미 모든 스케줄 메서드가 over-depend해서 위험은 대부분 해소됨. **오버엔지니어링 주의** |
| `BasePixelsHalf`/`PixelInfoHalf` 버퍼 제거 | 청크당 ~1.25MB, DS 잡 3회 → 0회 | DS가 이미 rect 스코핑돼 계측에 안 잡힘 → 얻는 건 메모리뿐. Burst 잡 내부를 건드려야 함 |
| `IslandRemoval` 조건부 스킵 | 파기 영역이 경계와 완전히 분리되면 섬 발생 불가 | 조건 판정 비용 vs 이득 미검증 |
| `PolygonCollider2D` 부분 갱신 | — | 이미 0.2s 스로틀 적용 중, 여지 소폭 |
| `_pathBoundsBuffer` 초기 크기 16 → 32 | 성장 빈도 감소 | 우선순위 낮음 |

### 2.3 정리(cleanup) 대기

| 항목 | 내용 |
|------|------|
| `LagDiag` 계측 코드 | 현재 off. `ChunkJobScheduler` / `TerrainChunk` / `InfinityMapManager`에 잔존 |
| `enableRound1Preview` 플래그 | 현재 false. `InfinityMapManager` / `WorldSettingsData` |
| `_lastExt*` 숨은 가변 상태 | rect를 값 객체로. 진입점 `SetDirtyRect()` |
| 이름 불일치 | `_lightingJobHandle`(실제 ChamferBackward), `TerrainLightingCalculator`(실제 BoundarySync 스케줄러) |
| `*.cs.private.0` 잔여 파일 | `TerrainChunk.cs.private.0`, `TerrainCollider.cs.private.0` — 컴파일은 안 되지만 Grep 결과를 오염시킴 |
| `_CodeBackup/` | 프로젝트 루트. 2026-07-09 백업 등 |
| `ObjectPooler` 제거 | 코드 참조 0(§1.5). 씬 4곳에서 컴포넌트를 뗀 뒤 `ObjectPooler.cs` + `Mineable.spawnedFromLayer` 삭제. **에디터 작업 필요** |
| 광물·돌 풀에 상한/`Clear` 없음 | `MineralGenerator._mineralPools`·`RockSpawner._rockPools` 둘 다 `ChunkPool.MAX_POOL_SIZE`(32) 같은 상한도, 씬 전환 시 `Clear()`도 없다. static이라 씬을 바꿔도 딕셔너리는 살아남고 안의 GameObject만 파괴돼 **죽은 참조가 큐에 남는다**(꺼낼 때 null 스킵 루프가 있어 크래시는 없지만, 지상↔지하를 오갈 때마다 훑고 버리는 비용). 성장 상한은 "동시 존재 최대 광물 수"라 무한은 아니지만 `globalMineralDensity`에 비례 |

---

## 3. 반복해서 걸린 함정

최적화 작업 전에 반드시 읽을 것.

### 3.1 `Idle`은 원인이 아니라 증상이다
프로파일러의 `Idle` 스파이크는 **sync point**(메인 스레드가 async 잡 완료를 동기 대기)의 증상이다.
`Idle`을 줄이려 하지 말고 **누가 무엇을 기다리는지**를 찾아라.
터레인에서는 `dig`이 `BasePixels`를 쓰기 전 `EnsureJobsCompleted()`로 대기하는 지점이었고,
진짜 비싼 건 **단일 스레드 Chamfer 2패스**였다.

### 3.2 Burst 스테일 커널 (2시간 날린 것)
Job struct의 **필드를 바꾸면** Burst가 옛 커널을 계속 쓸 수 있다. **Unity 재시작으로도 안 풀린다.**

> 증상이 **"소스상 불가능한 예외"**(가드가 있는데 DivideByZero 등)면 이걸 의심하라.
> → **잡 struct의 타입명을 바꿔서** 캐시 엔트리를 새로 만든다.
> (`InitBFSJob` → `InitDistanceFieldJob` 개명이 이 이유)

### 3.3 EditMode 테스트는 job safety를 못 잡는다
`IJobParallelForExtensions.Run(n)`은 메인 스레드 순차 실행이라 **parallel-for index 제약을 적용하지 않는다.**
`[NativeDisableParallelForRestriction]`을 빠뜨려도 EditMode는 초록이고 **Play에서만** 터진다.
**EditMode 그린을 job safety의 증거로 쓰지 말 것.**

### 3.4 Chamfer 2패스는 min-전파다
시작값이 참값의 **상한**이면 리셋 없이도 같은 고정점에 수렴한다.
→ Round2의 Init(리셋)이 필요 없다. `ChamferConvergenceTests`가 이걸 증명한다.
Round 구조를 다시 건드릴 때 반드시 이해하고 시작할 것.

### 3.5 프레임 시간이 예산 안이어도 GC 할당은 따로 본다
청크 로딩 프레임이 15ms(60fps 예산 내)라 **눈에 안 보여도** 그 프레임이 1.3MB를 할당하고 있었다.
매니지드 1MB/4MB 블록은 Boehm 힙을 영구히 키우고, 몇 초 뒤 **보이는** GC 랙으로 돌아온다.
프로파일러에서 Time ms만 보지 말고 **GC Alloc 열을 같이 볼 것.**

### 3.6 상수 짝 맞추기
`BoundarySyncJob.SYNC_DEPTH`(=50)와 스케줄러 쪽 `SYNC_DEPTH`는 **반드시 일치**해야 한다.
strip이 얕으면 경계 "꺾임(kink)"이 재발한다.

### 3.7 `IsCompleted` 는 `Complete()` 의 대용이 아니다

`JobHandle.IsCompleted == true` 는 **잡 실행이 끝났다**는 뜻일 뿐, NativeArray 의
`AtomicSafetyHandle` 은 **`Complete()` 를 불러야** 풀린다.
폴링만 하고 메인 스레드에서 그 배열을 읽으면 정확히 이 예외가 난다:

> The previously scheduled job X writes to the NativeArray ... You must call JobHandle.Complete() ...

`ChunkLoadingRunner` 의 Phase 1 대기 루프(`IsJobRunning()`)가 이 대용으로 쓰이고 있었고,
`Reuse_Step2_Finalize()` 는 "BasePixels 에 쓰는 잡이 없다"는 (당시엔 맞았던) 전제로
`EnsureJobsCompleted()` 를 뺀 상태였다. 나중에 **BasePixels writer 인 `CarveCaveBoundedJob`** 이
추가되면서 그 전제가 깨졌다 → `UpdateCollider()` 의 BasePixels 읽기에서 터짐.

- 재현 조건: **신규 생성 청크**만 굴을 뚫는다(`IsRestoredFromSave` 면 `CaveCarveSettings.None`).
  → 미탐사 지역으로 텔레포트(엘리베이터)하면 배치 전체가 신규라 한꺼번에 터진다.
- 수정: `ChunkJobScheduler.CompleteCarve()`(= `_caveHandle.Complete()`)를
  `Reuse_Step2_Finalize()` 의 콜라이더 갱신 직전에 호출. 이미 끝난 잡이라 블로킹은 없다.
- 함께 고친 것: Phase 2 에서 실패한 청크가 `results` 에 남아 **풀에 반납된 청크에
  Phase 2.5 가 Visual 잡을 다시 예약**하고 있었다. 그 청크가 다음 배치에서 재사용되면
  `Reuse_Step1_Prepare` 의 BasePixels 쓰기가 터지고 **로딩이 통째로 멈춘다.**

> 새 잡이 `BasePixels` 에 쓰기 시작하면 `Reuse_Step2_Finalize` 의 전제를 다시 확인할 것.
