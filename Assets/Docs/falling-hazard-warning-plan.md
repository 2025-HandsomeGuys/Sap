# 종유석 낙하 예고(돌가루) 구현 계획

> 설계 문서: `Assets/Docs/falling-hazard-warning-design.md`

**Goal:** 천장 종유석(StalactiteTrap / IcicleHazard)이 떨어지기 전 돌가루 예고 파티클을 재생하고 고정 딜레이 후 낙하시키며, 두 컴포넌트의 중복 낙하 로직을 공통 베이스로 묶는다.

**Architecture:** `FallingHazardBase` 추상 MonoBehaviour를 신설해 낙하/예고/착지/연쇄진동 로직을 통합한다. `StalactiteTrap`/`IcicleHazard`는 이를 상속하고 각자의 트리거(Raycast)·피격·JSON 설정만 오버라이드한다.

**Tech Stack:** Unity 2D, C#, IVibrationReceiver / VibrationManager, SpecialChunkSettingsLoader(JSON), ParticleSystem.

## Global Constraints (프로젝트 규칙 — 모든 태스크에 적용)

- **버전 관리: UVCS(구 Plastic SCM)**. `git` 명령 사용 금지. 커밋은 사람이 직접.
- **테스트 실행: 사람이 직접** Unity Test Runner로 수행. Claude는 테스트/실행 도구(`mcp__mcp-unity__run_tests` 등) 호출 금지. 각 태스크 끝의 검증은 "컴파일 확인 + 수동 체크포인트".
- 클래스명(`StalactiteTrap`, `IcicleHazard`)은 **변경 금지** — prefab 스크립트 참조 유지.
- 파일 상단 `// @tags:` 주석 컨벤션 유지.
- 한국어 주석/로그 유지.

---

## File Structure

| 파일 | 작업 | 책임 |
|------|------|------|
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Core/FallingHazardBase.cs` | Create | 낙하/예고/착지/연쇄진동 공통 추상 베이스 |
| `Assets/Scripts/_Core/Data/SpecialChunkSettingsData.cs` | Modify | `PhysicsSection`에 `fallWarningDelay` 추가 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Entities/IcicleHazard.cs` | Modify | 베이스 상속으로 축소, LoadSettings만 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Traps/StalactiteTrap.cs` | Modify | 베이스 상속, Update(Raycast)/OnLanded(피격)/LoadSettings |
| `Assets/StreamingAssets/specialChunkSettings.json` | Modify(선택) | physics 섹션에 `fallWarningDelay` 명시 |

> `FallingHazardBase`는 `Core/` 디렉토리에 둔다(공통 베이스 성격). `Core/`가 없으면 생성.

---

## Task 1: PhysicsSection에 fallWarningDelay 설정 필드 추가

**Files:**
- Modify: `Assets/Scripts/_Core/Data/SpecialChunkSettingsData.cs` (PhysicsSection, 156-162행 부근)

**Interfaces:**
- Produces: `SpecialChunkSettingsLoader.Instance.Settings.physics.fallWarningDelay` (float, 기본 0.5f)

- [ ] **Step 1: PhysicsSection에 필드 추가**

`PhysicsSection` 클래스(현재 `icicleImpactRadius`, `vibrationDefaultRadius`, `damageStagedThresholds` 보유)에 한 줄 추가:

```csharp
[System.Serializable]
public class PhysicsSection
{
    public float   icicleImpactRadius     = 2.0f;
    public float   vibrationDefaultRadius = 3.0f;
    public float   fallWarningDelay       = 0.5f;   // 종유석 낙하 예고 딜레이(초)
    public float[] damageStagedThresholds = new[] { 0.66f, 0.33f };
}
```

- [ ] **Step 2: 컴파일 확인**

Unity 에디터로 전환 → 콘솔에 컴파일 에러 없음 확인. (기존 JSON에 키가 없어도 기본값 0.5f 사용되므로 역직렬화 안전.)

- [ ] **Step 3: 체크포인트**

사람이 UVCS로 변경 검토/커밋. (자동 커밋 없음)

---

## Task 2: FallingHazardBase 추상 클래스 신설

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Core/FallingHazardBase.cs`

**Interfaces:**
- Consumes: `IVibrationReceiver`, `VibrationManager.Instance.TriggerVibration(Vector3, float)`
- Produces:
  - `protected void BeginDropSequence()` — 예고→낙하 시퀀스 시작(중복 진입 차단)
  - `protected abstract void LoadSettings()` — 자식이 JSON 값 적용
  - `protected virtual void OnLanded(Collision2D col)` — 착지 시 추가 동작(기본 no-op)
  - `protected` 필드: `_rb`, `_falling`, `_warning`, `warningParticle`, `warningDelay`, `impactRadius`, `shardParticle`

- [ ] **Step 1: 파일 생성**

```csharp
// @tags: special-chunk, hazard, falling, vibration, base
using System.Collections;
using UnityEngine;

/// <summary>
/// 천장 낙하물 공통 베이스 — 진동/감지 트리거 → 돌가루 예고 → 딜레이 → 낙하 → 착지 연쇄진동.
///
/// [SOLID]
///   SRP: 낙하 생명주기(예고·낙하·착지)만 담당.
///   OCP: 트리거/피격/설정은 자식이 오버라이드 (LoadSettings, OnLanded, BeginDropSequence 호출).
///   DIP: VibrationManager.Instance? 로 느슨하게 참조.
///
/// 중복 방지: _warning(예고 중) / _falling(낙하 중) 플래그로 재진입 차단.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public abstract class FallingHazardBase : MonoBehaviour, IVibrationReceiver
{
    [Header("Warning — 낙하 예고  ※ warningDelay는 런타임에 JSON 값으로 덮어씀")]
    [Tooltip("낙하 전 재생할 돌가루 예고 파티클 (없으면 생략)")]
    [SerializeField] protected ParticleSystem warningParticle;

    [Tooltip("예고 후 실제 낙하까지 딜레이 (초) — JSON: physics.fallWarningDelay")]
    [SerializeField] protected float warningDelay = 0.5f;

    [Header("Impact")]
    [Tooltip("착지 후 연쇄 진동 전파 반경")]
    [SerializeField] protected float impactRadius = 2f;

    [Tooltip("착지 시 재생할 파편 파티클 (없으면 생략)")]
    [SerializeField] protected ParticleSystem shardParticle;

    protected Rigidbody2D _rb;
    protected bool        _falling;
    protected bool        _warning;

    protected virtual void Awake()
    {
        _rb           = GetComponent<Rigidbody2D>();
        _rb.simulated = false; // 낙하 전 물리 비활성화
        LoadSettings();
    }

    // ─── IVibrationReceiver ──────────────────────────────────────
    public void OnVibration() => BeginDropSequence();

    /// <summary>예고 시퀀스 시작. 이미 예고/낙하 중이면 무시.</summary>
    protected void BeginDropSequence()
    {
        if (_warning || _falling) return;
        _warning = true;
        StartCoroutine(WarnThenDrop());
    }

    private IEnumerator WarnThenDrop()
    {
        if (warningParticle != null)
            warningParticle.Play();

        yield return new WaitForSeconds(warningDelay);

        Drop();
    }

    private void Drop()
    {
        _falling         = true;
        _rb.simulated    = true;
        _rb.gravityScale = 1f;
        Debug.Log($"[{GetType().Name}] 낙하 시작.");
    }

    private void OnCollisionEnter2D(Collision2D col)
    {
        if (!_falling) return;

        // 연쇄 진동 전파 (자신은 곧 파괴 → 무한 루프 없음)
        VibrationManager.Instance?.TriggerVibration(transform.position, impactRadius);

        // 자식별 착지 추가 동작 (피격 등)
        OnLanded(col);

        // 파편 파티클 — 부모 분리 후 재생 (gameObject 파괴 후에도 유지)
        if (shardParticle != null)
        {
            shardParticle.transform.SetParent(null);
            shardParticle.Play();
        }

        Debug.Log($"[{GetType().Name}] 착지 — 연쇄 진동.");
        Destroy(gameObject);
    }

    // ─── 자식 확장 지점 ──────────────────────────────────────────
    /// <summary>JSON 설정 적용 (자식별 구현).</summary>
    protected abstract void LoadSettings();

    /// <summary>착지 시 추가 동작 (기본 없음). 예: 플레이어 피격.</summary>
    protected virtual void OnLanded(Collision2D col) { }
}
```

- [ ] **Step 2: 컴파일 확인**

Unity 콘솔에 컴파일 에러 없음 확인. (이 시점엔 아직 자식이 베이스를 상속하지 않으므로 기존 두 클래스는 그대로 빌드됨.)

- [ ] **Step 3: 체크포인트**

사람이 UVCS로 변경 검토/커밋.

---

## Task 3: IcicleHazard를 베이스 상속으로 리팩토링

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Entities/IcicleHazard.cs` (전체 교체)

**Interfaces:**
- Consumes: `FallingHazardBase`(Task 2), `SpecialChunkSettingsLoader.Instance.Settings.physics.{icicleImpactRadius, fallWarningDelay}`(Task 1)

- [ ] **Step 1: 파일 전체 교체**

```csharp
// @tags: special-chunk, hazard, falling, icicle, vibration
using UnityEngine;

/// <summary>
/// 천장 고드름 — 진동 수신 시 돌가루 예고 후 낙하. 착지 시 연쇄 진동 전파.
/// 낙하/예고/착지 로직은 FallingHazardBase가 담당. 본 클래스는 JSON 설정만 적용.
/// 피격 없음(OnLanded 오버라이드 안 함).
/// </summary>
public class IcicleHazard : FallingHazardBase
{
    protected override void LoadSettings()
    {
        if (SpecialChunkSettingsLoader.Instance == null) return;

        var s        = SpecialChunkSettingsLoader.Instance.Settings;
        impactRadius = s.physics.icicleImpactRadius;
        warningDelay = s.physics.fallWarningDelay;
    }
}
```

- [ ] **Step 2: 컴파일 확인**

Unity 콘솔 에러 없음 확인.

- [ ] **Step 3: 수동 검증 — prefab 파티클 재할당**

> ⚠️ 필드명 변경: 기존 `iceShardParticle` → 베이스 `shardParticle`. 직렬화 키가 달라 자동 이전 안 됨.

IcicleHazard prefab을 Prefab Edit 모드로 열어:
- 기존 `iceShardParticle`에 할당돼 있던 얼음 파편 파티클을 베이스의 **`Shard Particle`** 슬롯에 재할당.
- (선택) `Warning Particle` 슬롯에 돌가루 예고 파티클 할당. 미할당이면 예고 생략(정상).

- [ ] **Step 4: 체크포인트**

사람이 UVCS로 변경 검토/커밋.

---

## Task 4: StalactiteTrap을 베이스 상속으로 리팩토링

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Traps/StalactiteTrap.cs` (전체 교체)

**Interfaces:**
- Consumes: `FallingHazardBase`(Task 2), `BeginDropSequence()`, `OnLanded()`, `LoadSettings()`; `SpecialChunkSettingsLoader.Instance.Settings.traps.stalactite.*` + `physics.fallWarningDelay`; `IHazardTarget.ApplyHazardDamage(float)`, `BuffStatProvider.AddBuff(string, StatType, ModifierType, float, float)`

- [ ] **Step 1: 파일 전체 교체**

```csharp
// @tags: trap, special-chunk, hazard, falling, vibration, damage, player
using UnityEngine;

/// <summary>
/// 산화된 공동 종유석 — 플레이어 접근(하향 Raycast) 또는 채굴 진동 감지 시
/// 돌가루 예고 후 낙하. 착지 시 연쇄 진동 + 플레이어 스태미나 피격 + 이동속도 저하.
/// 낙하/예고/착지 공통 로직은 FallingHazardBase가 담당.
/// </summary>
public class StalactiteTrap : FallingHazardBase
{
    [Header("Detection  ※ 런타임에 specialChunkSettings.json 값으로 덮어씀")]
    [Tooltip("플레이어 감지 하향 Raycast 거리 (유닛) — JSON: traps.stalactite.detectionRange")]
    [SerializeField] private float detectionRange = 3f;

    [Tooltip("플레이어 레이어 마스크")]
    [SerializeField] private LayerMask playerLayer;

    [Header("Impact — Player  ※ 런타임에 specialChunkSettings.json 값으로 덮어씀")]
    [Tooltip("충돌 시 플레이어 스태미나 피격량 — JSON: traps.stalactite.staminaDamage")]
    [SerializeField] private float staminaDamage = 20f;

    [Tooltip("이동 속도 저하 배율 (0.6 = 40% 감소) — JSON: traps.stalactite.slowMultiplier")]
    [SerializeField] private float slowMultiplier = 0.6f;

    [Tooltip("이동 속도 저하 지속 시간 (초) — JSON: traps.stalactite.slowDuration")]
    [SerializeField] private float slowDuration = 2.0f;

    private void Update()
    {
        if (_warning || _falling) return;

        // Raycast 하향 감지 — 플레이어가 아래 진입 시 예고 후 낙하
        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, detectionRange, playerLayer);
        if (hit.collider != null)
            BeginDropSequence();
    }

    protected override void LoadSettings()
    {
        if (SpecialChunkSettingsLoader.Instance == null) return;

        var s          = SpecialChunkSettingsLoader.Instance.Settings;
        detectionRange = s.traps.stalactite.detectionRange;
        staminaDamage  = s.traps.stalactite.staminaDamage;
        slowMultiplier = s.traps.stalactite.slowMultiplier;
        slowDuration   = s.traps.stalactite.slowDuration;
        impactRadius   = s.traps.stalactite.impactVibrationRadius;
        warningDelay   = s.physics.fallWarningDelay;
    }

    protected override void OnLanded(Collision2D col)
    {
        if (!col.gameObject.CompareTag("Player")) return;

        var target = col.gameObject.GetComponent<IHazardTarget>();
        var buff   = col.gameObject.GetComponent<BuffStatProvider>();

        target?.ApplyHazardDamage(staminaDamage);
        buff?.AddBuff(
            $"StalactiteSlow_{GetInstanceID()}",
            StatType.MoveSpeed,
            ModifierType.Percent,
            slowMultiplier,
            slowDuration
        );

        Debug.Log($"[StalactiteTrap] 플레이어 피격 — 피해 -{staminaDamage}, 이동속도 {slowMultiplier}x ({slowDuration}s)");
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * detectionRange);
    }
#endif
}
```

- [ ] **Step 2: 컴파일 확인**

Unity 콘솔 에러 없음 확인.

- [ ] **Step 3: 수동 검증 — prefab 파티클 재할당**

> ⚠️ 필드 변경: 기존 `shardParticle`은 베이스로 이동(필드명 동일 → 보통 직렬화 유지). 기존 `impactVibrationRadius` 필드는 제거되고 베이스 `impactRadius`로 통합 — prefab의 해당 인스펙터 값은 초기화될 수 있으나, 런타임 `LoadSettings()`가 JSON으로 덮어쓰므로 동작에는 무관.

StalactiteTrap prefab을 Prefab Edit 모드로 열어:
- `Shard Particle` 슬롯이 유지됐는지 확인(끊겼으면 착지 파편 파티클 재할당).
- (선택) `Warning Particle` 슬롯에 돌가루 예고 파티클 할당.
- `Detection`/`Impact — Player` 필드 값 유지 확인.

- [ ] **Step 4: 체크포인트**

사람이 UVCS로 변경 검토/커밋.

---

## Task 5: 통합 수동 플레이테스트 + JSON 설정

**Files:**
- Modify(선택): `Assets/StreamingAssets/specialChunkSettings.json` (physics 섹션)

**Interfaces:** 없음 (검증 태스크)

- [ ] **Step 1: (선택) JSON에 fallWarningDelay 명시**

`specialChunkSettings.json`의 `physics` 객체에 키 추가(원하는 딜레이로). 생략 시 코드 기본값 0.5f 사용.

```json
"physics": {
  "icicleImpactRadius": 2.0,
  "vibrationDefaultRadius": 3.0,
  "fallWarningDelay": 0.5
}
```

- [ ] **Step 2: 예고 파티클 생성·배치**

돌가루 ParticleSystem을 만들어 각 종유석 prefab의 자식으로 배치하고 `Warning Particle` 슬롯에 할당.
- 종유석 끝/아래에서 아래로 흩날리도록 방향·위치 설정.
- `Looping = false`, 짧은 burst 권장(딜레이 동안 1회).

- [ ] **Step 3: 수동 플레이테스트 — 핵심 회귀 확인**

PlayMode에서 다음을 직접 확인:

1. **예고 후 낙하**: 종유석 아래로 플레이어 접근(StalactiteTrap) 또는 채굴 진동 발생 시 → 돌가루 파티클 재생 → `fallWarningDelay`초 뒤 낙하 시작. 콘솔에 `낙하 시작` 로그가 파티클보다 **딜레이만큼 늦게** 찍히는지 확인.
2. **즉시 사라짐 버그 미재발**: 천장에 붙은 종유석이 트리거 직후 사라지지 않고, 예고→낙하 순서로 진행되는지 확인. (낙하 자체는 기존처럼 착지 시 Destroy — 이는 정상)
3. **피격(StalactiteTrap)**: 낙하한 종유석이 플레이어에 착지 → 스태미나 감소 + 이동속도 슬로우 로그 확인.
4. **피격 없음(IcicleHazard)**: 고드름은 착지해도 피격 로그 없이 연쇄진동 + Destroy만.
5. **연쇄진동**: 착지 시 주변 다른 낙하물이 연쇄 반응하는지 확인.
6. **하위호환**: `Warning Particle` 미할당 종유석이 에러 없이 예고만 생략하고 낙하하는지 확인.

> ⚠️ 별개 이슈: "천장에 붙어 있어 낙하 즉시 충돌→사라짐"은 종유석을 천장에서 살짝 띄워 배치하거나 낙하 직후 짧은 시간 천장 레이어 충돌을 무시해야 근본 해결됨. 본 계획 범위(예고 기능)와 분리된 배치/물리 문제이므로, 3-2 확인 중 재발하면 별도 태스크로 다룬다.

- [ ] **Step 4: 체크포인트**

사람이 UVCS로 최종 검토/커밋.

---

## Self-Review

- **Spec 커버리지**: 설계 1(공통화)→Task 2~4, 2(동작 흐름)→Task 2, 3(파티클 제공)→Task 2 필드 + Task 5 배치, 4(설정값)→Task 1 + Task 5, 영향범위/주의(파티클 재할당, 직렬화)→Task 3-3, 4-3. 누락 없음.
- **Placeholder**: 모든 코드 스텝에 실제 전체 코드 포함. TBD/TODO 없음.
- **타입 일관성**: `BeginDropSequence()`, `LoadSettings()`, `OnLanded(Collision2D)`, `impactRadius`, `warningDelay`, `shardParticle`, `warningParticle` 명칭이 Task 2 정의와 Task 3·4 사용처에서 일치.
- **프로젝트 규칙**: git 미사용·테스트 수동·클래스명 유지 반영.
