# 종유석 낙하 예고(돌가루) 기능 설계

작성일: 2026-06-25

## 배경 / 목적

천장 종유석(`StalactiteTrap`, `IcicleHazard`)이 떨어지기 직전, 낙하 지점에
**돌가루 파티클을 먼저 뿌려 플레이어에게 낙하를 예고**한다.
현재 두 컴포넌트는 트리거 즉시 곧바로 낙하하므로 회피 여지가 없다.

부수 목표: 두 컴포넌트의 거의 동일한 낙하/착지/연쇄진동 로직을 공통 베이스로
묶어 중복을 제거하고, 예고 기능을 단 한 곳에만 구현한다.

## 현재 상태 (As-Is)

| 파일 | 트리거 | 피격 | 착지 효과 |
|------|--------|------|-----------|
| `IcicleHazard` (Entities/) | 진동 수신만 | 없음 | 연쇄진동 + 파편 + Destroy |
| `StalactiteTrap` (Traps/) | 진동 + 하향 Raycast | 스태미나 + 슬로우 | 연쇄진동 + 파편 + Destroy |

- 두 클래스 모두 `IVibrationReceiver` 구현, `Awake`에서 `_rb.simulated=false`,
  `Drop()`에서 물리 활성화, `OnCollisionEnter2D`에서 연쇄진동 후 `Destroy`.
- 낙하 **전** 예고 효과는 어느 쪽에도 없음.
- 참고: 유사한 "예고 → 딜레이 → 실행" 패턴은 `CollapseFloor`(싱크홀)의
  `ICollapseEffect.PlayWarning()` → `WaitForSeconds(collapseDelay)` 구조에 이미 존재.

## 설계 (To-Be)

### 1. 공통화 — `FallingHazardBase` 추상 클래스 신설

```
FallingHazardBase : MonoBehaviour, IVibrationReceiver   (abstract)
 ├─ 공통 필드: _rb, _falling, _warning
 │            warningParticle, warningDelay, shardParticle, impactRadius
 ├─ Awake()              → _rb 셋업(simulated=false), LoadSettings() 호출
 ├─ OnVibration()        → BeginDropSequence()
 ├─ BeginDropSequence()  → 이미 _warning/_falling이면 무시,
 │                         아니면 예고 코루틴 시작
 ├─ [코루틴] WarnThenDrop():
 │       warningParticle?.Play()  →  WaitForSeconds(warningDelay)  →  Drop()
 ├─ Drop()               → _falling=true, simulated=true, gravityScale=1
 ├─ OnCollisionEnter2D() → !_falling이면 무시,
 │                         아니면 연쇄진동 + OnLanded(col)
 │                         + shardParticle 분리재생 + Destroy
 ├─ abstract LoadSettings()         (JSON 값 적용 — 자식별로 다름)
 └─ virtual  OnLanded(Collision2D)  (착지 추가 동작 — 기본 비어있음)

StalactiteTrap : FallingHazardBase
 ├─ Update()              → 하향 Raycast 감지 시 BeginDropSequence()
 ├─ override OnLanded()   → 플레이어 IHazardTarget 피격 + BuffStatProvider 슬로우
 ├─ override LoadSettings()→ traps.stalactite.* + 공통 fallWarningDelay 적용
 └─ OnDrawGizmosSelected()→ Raycast 시각화 (기존 유지)

IcicleHazard : FallingHazardBase
 └─ override LoadSettings()→ physics.icicleImpactRadius + 공통 fallWarningDelay 적용
                            (OnLanded 오버라이드 없음 = 피격 없음)
```

### 2. 동작 흐름

1. 트리거: 진동 수신(`OnVibration`) **또는** StalactiteTrap의 하향 Raycast 감지
2. `BeginDropSequence()` — 중복 진입 차단(`_warning`/`_falling` 플래그)
3. `warningParticle.Play()` — 돌가루 예고 파티클 1회 재생
4. `WaitForSeconds(warningDelay)` — **고정 딜레이** (기본 0.5s, JSON 조절)
5. `Drop()` — 물리 활성화, 중력 낙하
6. 착지(`OnCollisionEnter2D`) → 연쇄진동 + `OnLanded()`(피격 등) + 파편 + Destroy

### 3. 파티클 제공 방식

- `[SerializeField] ParticleSystem warningParticle` — 인스펙터 할당.
  `null`이면 예고 생략(기존 `shardParticle` null 체크 패턴과 동일) → **하위호환 유지**.
- 배치: 종유석 오브젝트의 **자식**으로 두고, 돌가루가 아래로 떨어지도록
  에디터에서 파티클 방향/위치 설정. 코드는 위치 관여 없이 `Play()`만 호출.
- 권장: looping이 아닌 **burst 1회** (딜레이 동안 한 번 흩날림).

### 4. 설정값 (JSON)

`SpecialChunkSettingsData.PhysicsSection`에 공통 필드 추가:

```csharp
public float fallWarningDelay = 0.5f;   // 종유석 낙하 예고 딜레이(초)
```

`specialChunkSettings.json`의 physics 섹션에서 조절. 두 종유석이 공유한다.
베이스의 `LoadSettings()` 자식 구현에서 이 값을 읽어 `warningDelay`에 적용.

## 영향 범위 / 주의

- **하위호환**: 기존 prefab이 `warningParticle` 미할당이어도 정상(예고만 생략).
- **상속 구조 변경**: 클래스명(`StalactiteTrap`/`IcicleHazard`)은 유지되므로
  prefab의 스크립트 참조는 보통 안전. Unity에서 두 prefab의 Missing Script
  여부와 Inspector 필드 유지 확인 필요.
- `_rb.simulated=false` 셋업이 베이스 `Awake`로 이동 → 자식에서 중복 제거.
- 테스트 실행은 사람이 직접 (프로젝트 규칙).

## 비고

- UVCS 프로젝트이므로 git 명령 미사용. 문서는 `Assets/Docs/`에 보관.
- 관련: `Assets/Docs/special-chunk/`, `CollapseFloor`/`ICollapseEffect` 예고 패턴.
