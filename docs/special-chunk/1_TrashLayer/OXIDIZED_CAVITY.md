# 특수 청크: 산화된 공동 (Oxidized Cavity)
@tags: special-chunk, oxidized-cavity, zone-effect, IZoneEffect, layer1, design

> Layer 1 전용 / 작성일: 2026-03-08

---

## 개요

이미 뚫려 있는 거대 타원형 동굴. 붉게 녹슨 금속 배경 벽을 타고 이동할 수 있지만,
**벽에 붙어 있는 동안 MaxStamina 상한선이 점차 감소**하는 산화 중독 페널티가 있다.
페널티는 동굴을 나가도 **원복되지 않는다** (영구 감소).
천장에는 곡괭이로 파괴 불가능한 종유석이 달려 있어, **플레이어가 아래 접근** 또는 **근처 채굴 진동**에 반응해 낙하한다.

---

## 기존 코드 현황 (재사용 가능)

| 기존 스크립트 | 위치 | 산화된 공동에서의 역할 |
|---|---|---|
| `ZoneEffectTrigger.cs` | `Gameplay/Zones/` | 동굴 트리거 영역 진입/이탈 감지 |
| `IZoneEffect.cs` | `Gameplay/Zones/` | `OxidizedZone`이 구현할 인터페이스 |
| `DamageZone.cs` | `Terrain/Tiles/Damage/` | 코루틴 틱 패턴 참조 |
| `IcicleHazard.cs` | `SpecialChunks/` | 낙하·착지 연쇄 패턴 참조 |
| `IVibrationReceiver.cs` | `SpecialChunks/Interfaces/` | `StalactiteTrap`이 구현할 인터페이스 |
| `VibrationManager.cs` | `SpecialChunks/` | 채굴 진동 → 종유석 낙하 연결 |
| `PlayerStat.cs` | `UI/Player/Stats/` | `SetBaseValue(StatType.MaxStamina)` 영구 감소 |

---

## 필요한 신규 스크립트

### 1. `OxidizedZone.cs`

**역할:** 동굴 내 벽타기 감지 → MaxStamina BaseValue 영구 감소 (원복 없음)

**구현 방식:**
- `IZoneEffect` 구현체 → `ZoneEffectTrigger`와 동일 GameObject에 부착
- `OnEnter`: 플레이어 `PlayerStat` 캐싱, 벽타기 감지 코루틴 시작
- 코루틴: `PlayerController.isWallClimbing == true`인 동안 매 틱마다 `PlayerStat.SetBaseValue(StatType.MaxStamina, 현재기준값 - penaltyPerTick)` 직접 호출
  - `BuffStatProvider` 사용 안 함 — 버프는 제거 가능하므로 부적합
  - 최솟값 가드 (`minMaxStamina` 이하로 내려가지 않음)
- `OnExit`: 코루틴 중단만. **원복 없음.**

> **왜 SetBaseValue인가?**
> `BuffStatProvider.AddBuff()`는 `RemoveBuff()`로 언제든 되돌릴 수 있음.
> 영구 감소는 기준값(BaseValue) 자체를 낮추는 것이 의미상 정확하고 다른 시스템과 충돌 없음.

```
[구조 예시]
OxidizedCavityChunk (GameObject)
 ├─ TerrainChunk
 ├─ BoxCollider2D (isTrigger=true, 동굴 전체 범위)
 ├─ ZoneEffectTrigger          ← OnEnter/OnExit 감지
 ├─ OxidizedZone               ← IZoneEffect 구현, 영구 페널티 로직
 ├─ VibrationManager           ← 채굴 진동 수신 및 전파
 └─ AudioSource                ← 바람 앰비언트 루프
```

---

### 2. `StalactiteTrap.cs` (하이브리드 방식)

**역할:** 플레이어 접근(Raycast) **또는** 채굴 진동(IVibrationReceiver) 둘 중 하나라도 감지되면 낙하

**트리거 이중 구조:**
| 트리거 | 방식 | 발동 조건 |
|---|---|---|
| 1차 | Raycast (하향) | 플레이어가 종유석 바로 아래 접근 |
| 2차 | IVibrationReceiver | VibrationManager가 진동 전파 (채굴/충격) |

**구현 방식:**
- `IVibrationReceiver` 구현 → `VibrationManager`에 자동 감지됨
- `Awake`: `Rigidbody2D.simulated = false`
- `Update`: `Physics2D.Raycast` 하향 감지 → 플레이어 감지 시 `Drop()`
  - `_falling == true`면 즉시 리턴 (중복 방지)
- `OnVibration()`: `Drop()` 호출 (`IcicleHazard`와 동일 패턴)
- `Drop()`: `_rb.simulated = true`, `_rb.gravityScale = 1f`, `_falling = true`
- `OnCollisionEnter2D`:
  - `Player` 태그: `PlayerStat.UseStamina(staminaDamage)` + `BuffStatProvider.AddBuff(slowId, MoveSpeed, Percent, 0.6f, duration)` 로 이동 속도 저하
  - 착지 연쇄: `VibrationManager.Instance?.TriggerVibration(transform.position, impactRadius)`
  - 파편 파티클 부모 분리 → Play
  - `Destroy(gameObject)`
- 파괴 불가: `IDamageable` 미구현

**IcicleHazard.cs 와 비교:**

| 항목 | IcicleHazard (2층) | StalactiteTrap (1층) |
|---|---|---|
| 낙하 트리거 | VibrationManager만 | Raycast + VibrationManager (하이브리드) |
| 인터페이스 | IVibrationReceiver | IVibrationReceiver |
| 착지 연쇄 진동 | O | O |
| 플레이어 피격 효과 | 없음 | 스태미나 감소 + 이동 속도 저하 |

---

## 프리팹 구성

```
OxidizedCavityChunk (Prefab)
 ├─ TerrainChunk                       ← isStaticSpecialChunk = true
 ├─ BoxCollider2D (isTrigger=true)      ← 동굴 전체 영역, ZoneEffectTrigger용
 ├─ ZoneEffectTrigger
 ├─ OxidizedZone                       ← 벽타기 영구 페널티
 ├─ VibrationManager                   ← 채굴 진동 허브
 ├─ AudioSource                        ← Wind Loop (Loop=true, PlayOnAwake=true)
 └─ Stalactites (부모 오브젝트)
      ├─ StalactiteTrap_1
      │    ├─ SpriteRenderer
      │    ├─ Rigidbody2D (simulated=false)
      │    ├─ Collider2D
      │    ├─ StalactiteTrap.cs        ← IVibrationReceiver 구현 + Raycast
      │    └─ ParticleSystem (파편)
      ├─ StalactiteTrap_2
      └─ ...
```

> **VibrationManager 주의:** VibrationManager는 씬 당 1개 싱글턴.
> 이 청크가 isStaticSpecialChunk 이므로 동굴 청크 내에만 배치 가능.
> 단, 다른 청크의 VibrationManager와 충돌하지 않도록 `Awake`의 중복 방지 로직 확인.

---

## 구현 단계

1. **`OxidizedZone.cs` 작성**
   - `IZoneEffect` 구현
   - 코루틴: 벽타기 감지 + `SetBaseValue` 영구 감소
   - `OnExit`: 코루틴 중단만 (원복 없음)

2. **`StalactiteTrap.cs` 작성**
   - `IVibrationReceiver` 구현
   - `Update` Raycast 하향 감지
   - `Drop()` 공통 낙하 로직
   - 충돌 시 피격 효과 + 착지 연쇄 진동

3. **프리팹 조립**
   - BoxCollider2D + ZoneEffectTrigger + OxidizedZone
   - VibrationManager 배치
   - Stalactite 자식 배치 (Raycast 범위 Inspector에서 조정)
   - Wind AudioSource 설정

4. **SpecialChunkManager 등록** (Task 25.5 이후 통합)

---

## 수치 기획 (조정 가능)

| 항목 | 기본값 |
|---|---|
| 벽타기 페널티 주기 | 1.0s |
| 1회 MaxStamina 영구 감소량 | -5 |
| 최소 MaxStamina 하한 | 20 (이 이하로 내려가지 않음) |
| 이탈 후 원복 | **없음** |
| 종유석 Raycast 감지 범위 | 3 유닛 |
| 착지 연쇄 진동 반경 | 2 유닛 |
| 충돌 스태미나 피격량 | 20 |
| 이동 속도 저하 배율 | 0.6× |
| 이동 속도 저하 지속 시간 | 2.0s |

---

## 주의사항

- `OxidizedZone`은 `BuffStatProvider` 사용 금지 — `PlayerStat.SetBaseValue()` 직접 호출로 영구 감소
- MaxStamina 하한 가드 필수 — 0 이하 또는 `minMaxStamina` 이하로 내려가지 않도록
- `StalactiteTrap`은 `IDamageable` 미구현 → 곡괭이 히트 이벤트 수신 안 함 (레이어/태그로 차단)
- `VibrationManager` 싱글턴 중복 주의 — 이 청크 외 다른 씬 오브젝트에도 있으면 충돌
- 종유석은 청크 자식 → 청크 `Destroy` 시 자동 제거

---

**연관 파일:**
- `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/StalactiteTrap.cs` ← 신규
- `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/OxidizedZone.cs` ← 신규
- `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/VibrationManager.cs` ← 재사용
- `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Interfaces/IVibrationReceiver.cs` ← 재사용
- `Assets/Scripts/Gameplay/Zones/ZoneEffectTrigger.cs` ← 재사용
