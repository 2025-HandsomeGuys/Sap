# 특수 청크 시스템 — 리팩토링 및 최적화 분석
@tags: special-chunk, refactoring, optimization, plan, SpecialChunkFactory, IChunkInitializer

> 작성일: 2026-03-18
> 대상 디렉토리: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/`
> 분석 범위: SOLID 원칙 위반, 코드 중복(DRY), 런타임 성능 이슈

---

## 요약 (Executive Summary)

| 심각도 | 분류 | 항목 수 |
|--------|------|---------|
| 🔴 High | 코드 중복 / 아키텍처 불일치 | 3 |
| 🟡 Medium | SOLID 위반 | 4 |
| 🟢 Low | 성능 최적화 | 4 |

---

## 🔴 HIGH — 코드 중복 및 아키텍처 불일치

---

### [H-1] Diggable 엔티티 3개가 사실상 동일한 패턴 중복

**영향 파일**
- `Entities/TrashWallEntity.cs`
- `Entities/CompressedTrashWallEntity.cs`
- `Entities/CrystalBlockEntity.cs`

**문제**

세 클래스의 구조가 90% 동일하다.

```
공통 필드:
  float maxHp / _currentHp
  IDamageStageable[] _stageListeners

공통 Awake():
  GetComponents<IDamageStageable>()
  SpecialChunkSettingsLoader 적용

공통 Dig():
  toolIndex != 2 → return
  _currentHp -= damage
  hpRatio 계산
  _stageListeners 브로드캐스트
  VFX.PlayHit()
  _currentHp <= 0 → Die()

공통 Die():
  dropper.Drop(center)
  vfx.PlayDestroy(center)
  Destroy(gameObject)
```

차이는 VFX 타입(`TrashWallVFX`, `CrystalBlockVFX`, `CompressedTrashVFX`)과 Dropper 참조 방식뿐이다.

**추가 불일치**

| 항목 | TrashWallEntity | CrystalBlockEntity | CompressedTrashWallEntity |
|------|:-:|:-:|:-:|
| HitCooldown | ✅ 0.2f | ✅ 0.2f | ❌ 없음 |
| Dropper 타입 | `TrashWallDropper` (구체) | `ILootDropper` (인터페이스) | `ILootDropper` (인터페이스) |
| VFX 캐싱 | ❌ Dig() 마다 GetComponent | ❌ Dig() 마다 GetComponent | ✅ Awake에서 캐시 |
| Settings 적용 | ✅ | ✅ | ❌ 누락 |

**제안 — `DiggableBlockBase` 추상 베이스 클래스 도입**

```csharp
// 신규 파일: Entities/DiggableBlockBase.cs
public abstract class DiggableBlockBase : MonoBehaviour, IDiggable
{
    [Header("HP")]
    public float maxHp = 25f;

    protected float _currentHp;
    protected IDamageStageable[] _stageListeners;
    private float _lastHitTime;
    private const float HitCooldown = 0.2f;

    protected virtual void Awake()
    {
        _stageListeners = GetComponents<IDamageStageable>();
        _currentHp = maxHp;
        ApplySettings();
    }

    protected virtual void ApplySettings() { }  // 각 클래스에서 override

    public void Dig(Vector2 worldPos, float damage, int toolIndex)
    {
        if (!CanDig(toolIndex)) return;
        if (Time.time - _lastHitTime < HitCooldown) return;
        _lastHitTime = Time.time;

        _currentHp -= damage;
        float ratio = Mathf.Max(_currentHp, 0f) / maxHp;

        foreach (var l in _stageListeners) l.OnHpRatioChanged(ratio);
        OnHit();

        if (_currentHp <= 0f) Die();
    }

    protected virtual bool CanDig(int toolIndex) => toolIndex == 2;
    protected abstract void OnHit();   // VFX.PlayHit()
    protected abstract void OnDie();   // dropper + VFX.PlayDestroy()

    private void Die()
    {
        Vector3 center = GetComponent<SpriteRenderer>()?.bounds.center ?? transform.position;
        OnDie();
        Destroy(gameObject);
    }
}
```

`TrashWallEntity`는 `DiggableBlockBase`를 상속하고 `OnHit()`/`OnDie()` 만 구현하면 된다.
→ 세 클래스 합산 약 130줄 → 50줄 수준으로 축소 가능.

---

### [H-2] ScrapExplosion — 같은 패턴인데 아키텍처 불일치

**영향 파일**
- `Traps/ScrapExplosion.cs`

**문제**

`ScrapExplosion`도 `IDiggable`을 구현하지만, 나머지 Diggable 엔티티와 완전히 다른 구조를 갖는다.

| 비교 항목 | TrashWallEntity 등 | ScrapExplosion |
|----------|:---:|:---:|
| IDamageStageable (HP 비주얼 단계) | ✅ | ❌ |
| ILootDropper 분리 | ✅ | ❌ 직접 내장 |
| VFX 컴포넌트 분리 | ✅ | ❌ Instantiate 직접 |
| HitCooldown | ✅ | ❌ |
| `damage` 파라미터 이름 | damage | radius ← **잘못된 이름** |

`Dig(Vector2 worldPos, float radius, int toolIndex)` — 두 번째 파라미터가 `radius`로 선언되어 있어
`IDiggable` 계약상 의미(`damage`)와 불일치한다. `_currentHp -= radius` 로 사용하고 있어
실제로는 damage 값이 radius 변수명에 담겨 온다. 오해를 유발하는 명명이다.

또한 `DamagePlayer()`와 `DestroyNearbyItems()`가 **같은 center, 같은 radius**로 두 번
`Physics2D.OverlapCircleAll`을 호출한다 — 불필요한 중복 물리 쿼리.

**제안**

1. `Dig` 파라미터명 `radius` → `damage` 수정 (IDiggable 계약과 일치)
2. `_dropper` 캐시 + `ILootDropper` 인터페이스 사용으로 변경
3. `DamagePlayer` + `DestroyNearbyItems` 합산 → 단일 `OverlapCircleAll` 후 태그/컴포넌트로 분기

---

### [H-3] Mineral Drop 로직 중복

**영향 파일**
- `Traps/ScrapExplosion.cs` — `TryDropMineral()`
- `Behaviours/TrashWallDropper.cs` — `TryDrop()`

**문제**

두 메서드의 내부가 거의 동일하다.

```csharp
// ScrapExplosion.TryDropMineral
MineralSO so = MineralDatabase.Instance.GetMineralByID(id);
if (so == null || so.mineralPrefab == null) { ... return; }
Vector3 dropPos = new Vector3(center.x + Random.Range(...), ...);
GameObject spawned = Instantiate(so.mineralPrefab, dropPos, Quaternion.identity);
var ctrl = spawned.GetComponent<MineralItemController>();
if (ctrl != null) ctrl.mineralData = so;

// TrashWallDropper.TryDrop — 위와 동일
```

`LootTable.cs`가 이미 이 패턴을 추상화하고 있는데도 `ScrapExplosion`은 직접 구현한다.
`CrystalBlockDropper`는 `LootTable.ExecuteDrop()`을 사용하는 올바른 패턴을 쓴다.

**제안**

`ScrapExplosion`의 `DropMinerals()`를 `ILootDropper` 컴포넌트 (`CrystalBlockDropper` 패턴) 또는
`LootTable` ScriptableObject 기반으로 교체해 drop 로직을 완전히 분리한다.
`TrashWallDropper`도 장기적으로 `LootTable` 기반으로 통일하면 drop 설정을 코드 수정 없이
JSON/에셋으로만 관리할 수 있다.

---

## 🟡 MEDIUM — SOLID 위반

---

### [M-1] OCP 위반 — SpecialChunkManager의 타입 검사

**영향 파일**
- `SpecialChunkManager.cs` (line 240~254)

**문제**

`SpawnSpecialChunkIfPossible()`이 `IChunk` 구현 타입에 따라 분기한다.

```csharp
// 현재 코드
if (anchorChunk is LargeStaticTerrainChunk staticChunk)
    staticChunk.Initialize(coord);                    // 타입별 특수 처리

if (anchorChunk is not LargeStaticTerrainChunk)
    anchorObj.SetActive(false);                       // 타입별 활성화 지연
```

새 IChunk 구현을 추가할 때마다 `SpecialChunkManager`를 수정해야 한다.

**제안**

`IChunk` 인터페이스에 두 책임을 추가한다.

```csharp
public interface IChunk
{
    // 기존 멤버 ...
    void OnSpawned(Vector2Int coord);    // Initialize(coord) 통합
    bool NeedsDelayedActivation { get; } // SetActive(false) 조건
}
```

`SpawnSpecialChunkIfPossible`은 타입 검사 없이:

```csharp
anchorChunk.OnSpawned(coord);
if (anchorChunk.NeedsDelayedActivation)
    anchorObj.SetActive(false);
```

---

### [M-2] DIP 위반 — 트랩의 PlayerStat 직접 참조

**영향 파일**
- `Traps/ScrapExplosion.cs` — `DamagePlayer()`
- `Traps/DelayedBlast.cs` — `ApplyExplosionDamage()`
- `Traps/StalactiteTrap.cs` — `OnCollisionEnter2D()`
- `Entities/RollingRockEntity.cs` — `ApplyPlayerImpact()`

**문제**

모두 `PlayerStat` 구체 클래스를 직접 `GetComponent`한다.

```csharp
PlayerStat stat = col.gameObject.GetComponent<PlayerStat>();
stat?.UseStamina(staminaDamage);
```

DelayedBlast.cs 132번 줄 주석에도 이미 인지된 문제로 기록돼 있다:
> `// 차후 Task #31에 맞춰 IHazard 연동 또는 직접 처리`

**제안**

```csharp
public interface IHazardTarget
{
    void ApplyHazardDamage(float amount);
}
```

`PlayerStat`이 `IHazardTarget`을 구현하면 트랩은 구체 클래스를 몰라도 된다.
추후 몬스터나 NPC에도 동일 패턴 적용 가능.

---

### [M-3] SRP 위반 — RollingRockEntity가 너무 많은 책임

**영향 파일**
- `Entities/RollingRockEntity.cs`

**문제**

`RollingRockEntity`는 현재 3가지 책임을 직접 수행한다.

1. **물리 이동** — `Activate()`, `FixedUpdate()` impulse/gravity
2. **지형 파괴** — `FixedUpdate()`의 `ExplodeTerrain()`
3. **플레이어 피격 + 넉백** — `OnCollisionEnter2D()`, `ApplyPlayerImpact()`

추가로 넉백 억제를 위해 `PlayerController.isDashing = true`를 직접 설정한다.
이는 PlayerController의 내부 상태를 외부에서 조작하는 강한 결합이다.

```csharp
// RollingRockEntity.cs:141
playerController.isDashing = true;   // PlayerController 내부 상태 외부 조작
```

**제안**

- 넉백은 `PlayerController`에 `ApplyExternalKnockback(Vector2 force, float duration)` 메서드를 추가하고 내부에서 isDashing 관리하도록 캡슐화
- 또는 `IKnockbackReceiver` 인터페이스 도입

---

### [M-4] ISP 위반 — IndestructibleOverlayInit의 이중 계약

**영향 파일**
- `Behaviours/IndestructibleOverlayInit.cs`

**문제**

`IndestructibleOverlayInit`이 `IChunkInitializer`와 `IIndestructibleHit` 두 인터페이스를 동시에 구현한다.

- `IChunkInitializer.Initialize()` — 일회성 초기화 (청크 생성 시)
- `IIndestructibleHit.OnIndestructibleHit()` — 반복 런타임 이벤트 (파기 시도마다)

두 책임의 생명주기와 호출 맥락이 완전히 다르다.
한 컴포넌트에 묶여 있어 각각 테스트하거나 교체하기 어렵다.

**제안**

`IndestructibleHitFeedback`을 별도 컴포넌트로 분리한다.

```
IndestructibleOverlayInit  (IChunkInitializer만)
IndestructibleHitFeedback  (IIndestructibleHit만 — 오디오/VFX 피드백)
```

---

## 🟢 LOW — 성능 최적화

---

### [P-1] Dig() 핫패스에서 GetComponent 반복 호출

**영향 파일**
- `Entities/TrashWallEntity.cs` (line 60, 72~73)
- `Entities/CrystalBlockEntity.cs` (line 50, 62~63)

**문제**

```csharp
// TrashWallEntity.Dig() — 매 타격마다 호출
GetComponent<TrashWallVFX>()?.PlayHit();

// TrashWallEntity.Die()
GetComponent<TrashWallDropper>()?.Drop(center);
GetComponent<TrashWallVFX>()?.PlayDestroy(center);
```

`CompressedTrashWallEntity`는 이미 Awake에서 캐싱(`_vfx`, `_dropper`)하고 있다.
나머지 두 엔티티만 누락됐다. Die()는 한 번만 호출되므로 영향이 작지만,
`PlayHit()`은 타격마다 호출되므로 더 중요하다.

**제안**

```csharp
// Awake에서 캐싱
private TrashWallVFX _vfx;
private TrashWallDropper _dropper;

void Awake() {
    _vfx     = GetComponent<TrashWallVFX>();
    _dropper = GetComponent<TrashWallDropper>();
    ...
}
```

---

### [P-2] OverlapCircleAll 이중 호출 (ScrapExplosion)

**영향 파일**
- `Traps/ScrapExplosion.cs` (line 94, 113)

**문제**

`Explode()`에서 동일한 center, 동일한 radius로 `OverlapCircleAll`을 **두 번** 호출한다.

```csharp
private void DamagePlayer(Vector3 blastCenter) {
    Collider2D[] hits = Physics2D.OverlapCircleAll(blastCenter, _explosionRadius); // 1번째
    ...
}

private void DestroyNearbyItems(Vector3 blastCenter) {
    Collider2D[] hits = Physics2D.OverlapCircleAll(blastCenter, _explosionRadius); // 2번째
    ...
}
```

물리 쿼리 비용이 2배로 든다.

**제안**

단일 쿼리 후 루프 안에서 Player / Item 분기 처리:

```csharp
private void ProcessExplosion(Vector3 blastCenter)
{
    Collider2D[] hits = Physics2D.OverlapCircleAll(blastCenter, _explosionRadius);
    foreach (var hit in hits)
    {
        if (hit.CompareTag("Player"))       { /* 스태미나 감소 */ break; }
        else if (hit.GetComponent<MineralItemController>() != null ||
                 hit.GetComponent<PickupableItem>()        != null)
            Destroy(hit.gameObject);
    }
}
```

---

### [P-3] SubChunkRegistry.UnregisterByAnchor — O(n) 선형 탐색

**영향 파일**
- `Core/SubChunkRegistry.cs` (line 62~70)

**문제**

앵커 언로드 시 `_anchorMap` 전체를 순회한다.

```csharp
foreach (var kv in _anchorMap)
    if (kv.Value == anchorCoord) toRemove.Add(kv.Key);
```

특수 청크 등록이 많아질수록 O(n)으로 느려진다.

**제안**

역방향 인덱스를 추가한다.

```csharp
// 역인덱스 추가
private readonly Dictionary<Vector2Int, List<Vector2Int>> _reverseMap
    = new Dictionary<Vector2Int, List<Vector2Int>>();

// Register/RegisterReserved 시 _reverseMap 갱신
// UnregisterByAnchor는 _reverseMap[anchorCoord] 직접 조회 → O(k)
```

이 최적화는 게임 초반 청크가 적을 때는 효과 없고, 장시간 플레이로 누적될 때 의미 있다.

---

### [P-4] RollingRockEntity — FixedUpdate 내 FindFirstObjectByType 지연 캐싱

**영향 파일**
- `Entities/RollingRockEntity.cs` (line 79~85)

**문제**

```csharp
// FixedUpdate — _isActive인 동안 매 프레임 체크
if (_terrain == null)
{
    _terrain = Object.FindFirstObjectByType<StaticChunkTerrainManager>() as ITerrainManager
            ?? Object.FindFirstObjectByType<InfinityMapManager>() as ITerrainManager;
    if (_terrain == null) return;
}
```

`FindFirstObjectByType`은 비싼 씬 탐색이다. `_terrain == null`인 상태에서 매 FixedUpdate마다
호출될 수 있다. `DelayedBlast`도 동일 패턴을 사용한다.

**제안**

`Activate()` 시점에 한 번 찾고, 없으면 경고 후 비활성화한다.
또는 `InfinityMapManager`를 singleton으로 주입받을 수 있도록
`ITerrainManager`를 static 레지스트리에 등록한다.

---

## 우선순위 작업 목록

| 순서 | 항목 | 예상 난이도 | 비고 |
|------|------|:-----------:|------|
| 1 | [H-1] `DiggableBlockBase` 도입 | 중 | 3개 클래스 통합, 버그 위험 낮음 |
| 2 | [H-2] `ScrapExplosion` 파라미터명 수정 + 구조 정비 | 하 | 오해 유발 버그 잠재 위험 제거 |
| 3 | [P-1] `TrashWallEntity`, `CrystalBlockEntity` VFX/Dropper 캐싱 | 하 | 5분 작업, 즉시 효과 |
| 4 | [P-2] `ScrapExplosion` OverlapCircleAll 단일화 | 하 | 물리 쿼리 50% 감소 |
| 5 | [M-2] `IHazardTarget` 인터페이스 도입 | 중 | Task #31 선행 작업 |
| 6 | [M-1] `IChunk.OnSpawned` / `NeedsDelayedActivation` | 중 | 새 청크 타입 추가 시 필요 |
| 7 | [M-3] `PlayerController` 넉백 캡슐화 | 중 | PlayerController 수정 포함 |
| 8 | [H-3] Drop 로직 `LootTable` 통합 | 중 | `ScrapExplosion` 드롭 설정 JSON화 |
| 9 | [P-3] `SubChunkRegistry` 역인덱스 | 하 | 장기 플레이 최적화 |
| 10 | [M-4] `IndestructibleOverlayInit` 분리 | 하 | 구조 정리 |

---

## 현재 잘 된 부분 (유지)

- **`DamageStagedVisuals`** — `IDamageStageable` 인터페이스 + OCP 준수. `PolygonCollider2D` 경로 보존 패턴도 올바름.
- **`CollapseFloor` + `ICollapseEffect`** — Observer 패턴으로 VFX/오디오 완전 분리.
- **`VibrationManager` + `IVibrationReceiver`** — 체인 리액션을 느슨하게 연결.
- **`LootTable` ScriptableObject** — `CrystalBlockDropper`가 올바르게 활용.
- **`SpecialChunkSelector`** — 순수 C# 클래스로 선택 로직 분리. 테스트 가능.
- **`SpecialChunkFootprint`** — MonoBehaviour 비의존 순수 수학 클래스.
- **`TileEventDispatcher`** — SOLID Observer 패턴 모범 사례.
