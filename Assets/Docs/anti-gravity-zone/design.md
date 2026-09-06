# 반중력 구간 특수청크 — 3단계 위상 + 배경 예고 설계

작성일: 2026-06-30

## 목표

반중력 점프맵 특수청크를 **3단계 중력 위상**으로 확장하고, 배경 오브젝트를 통해
중력이 언제 바뀔지 플레이어에게 예고한다.

- 위상: **정상(Normal) → 무중력(Zero) → 역중력(Inverted)** 순환
- 배경 오브젝트가 현재 위상 이미지를 표시하고, 전환 직전 **다음 위상 색을 가속 점멸**하여 예고
- 수동 배치 데미지 함정(`MagmaFloorDamage` 등 기존 컴포넌트)은 그대로 재사용 (이 설계 범위 외)

## 현재 코드 기준

| 파일 | 현재 상태 | 변경 |
|------|-----------|------|
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Zones/AntiGravityZone.cs` | 2단계 토글(정상↔역중력), `OnPhaseChanged(bool)` 이벤트(구독자 없음) | 3단계로 확장, 예고 이벤트 추가 |
| `Assets/Scripts/UI/Player/AntiGravityHandler.cs` | `Activate/Deactivate` 2-state | `SetPhase(GravityPhase)` 일반화 |
| `Assets/Scripts/UI/Player/PlayerController.cs` | `IsGravityInverted`로 점프 방향 반전 | 의미 유지(역중력 단계에서만 true) |
| (신규) `AntiGravityBackground.cs` | 없음 | 배경 오브젝트용 위상 표시 + 점멸 예고 |

## 1. 위상 모델

```csharp
public enum GravityPhase { Normal, Zero, Inverted }
```

순환 순서: `Normal → Zero → Inverted → Normal → …`

`AntiGravityZone` SerializeField:
- `normalDuration`, `zeroDuration`, `invertedDuration` — 각 단계 지속시간(초)
- `warningLeadTime` — 전환 몇 초 전부터 예고할지
- `antiGravityMultiplier` — 역중력 강도(기존 0.7 유지)

### CycleCo 흐름 (단계마다 반복)

1. 현재 위상 설정 → 존 내부 전체 적용 → `OnPhaseChanged(현재)` 발화
2. `(지속시간 - warningLeadTime)` 대기
3. `OnPhaseWarning(다음위상, warningLeadTime)` 발화
4. `warningLeadTime` 대기 → 다음 위상으로 진행

`지속시간 < warningLeadTime`인 경우 예고는 단계 시작과 동시에 발화(음수 대기 방지).

## 2. 이벤트 API

```csharp
// 전환 순간. 새 위상 전달.
public event Action<GravityPhase> OnPhaseChanged;
// 전환 leadTime초 전. 곧 올 위상과 남은 시간 전달.
public event Action<GravityPhase, float> OnPhaseWarning;
```

`OnPhaseWarning`이 leadTime을 함께 넘겨 구독자가 가속 점멸을 자급식으로 계산하게 한다.

## 3. 플레이어 / Rigidbody 위상 적용

### AntiGravityHandler (플레이어)
`Activate/Deactivate` → `SetPhase(GravityPhase phase)` + `ExitZone()`로 일반화.

- **Normal**: `gravityScale = _defaultGravity`, 스프라이트 Y 정상
- **Zero**: `gravityScale = 0` (둥둥 뜸). 점프 입력 시 위로 임펄스만, 감속 없음 → 천천히 상승하다 천장/바닥 충돌로 정지. (세부 부양감은 구현 단계에서 튜닝)
- **Inverted**: `gravityScale = _defaultGravity * -antiGravityMultiplier`, 스프라이트 Y flip (기존 로직 재사용)

`IsGravityInverted`는 **Inverted 단계(및 전환 코루틴 진행 중)에만** true → `PlayerController`의 점프 방향 로직 변경 없음.
천장 충돌 데미지(`ApplyCeilingImpact`)는 Inverted 단계에서만 유효하도록 유지.

### 비플레이어 Rigidbody2D
기존 `_savedGravityScales` 딕셔너리로 원본 보관. 위상별:
- Normal → 저장값 복원, Zero → `0`, Inverted → `-abs(base) * multiplier`

## 4. 배경 오브젝트 — AntiGravityBackground (신규)

특수청크 프리팹의 **전용 배경 오브젝트(SpriteRenderer)** 에 부착. (전역 `BackgroundManager`와 무관)

SerializeField:
- `AntiGravityZone zone` — 미지정 시 `GetComponentInParent`
- `Sprite[] phaseSprites` — index = `(int)GravityPhase` (정상/무중력/역중력)
- `float blinkStartInterval = 0.4f`, `float blinkEndInterval = 0.08f` — 점멸 가속 구간

동작:
- `OnEnable` 구독 / `OnDisable` 해제
- `OnPhaseChanged(phase)` → 점멸 코루틴 중지, `sprite = phaseSprites[phase]`로 확정
- `OnPhaseWarning(next, leadTime)` → 점멸 코루틴 시작
  - 현재 표시 중인 sprite(= 현재 위상) ↔ `phaseSprites[next]` 토글
  - 경과 비율에 따라 토글 간격을 `blinkStartInterval → blinkEndInterval`로 선형 단축(가속)
  - 베이스는 항상 현재 위상 → "지금"이 흔들리지 않고 다음 색이 끼어드는 점멸

계산량: 코루틴 1개 + sprite 토글. `Update` 미사용.

## 5. 엣지 케이스

- 플레이어 존 이탈: `AntiGravityHandler.ExitZone()` → 즉시 Normal 복원. 배경은 청크 소속이라 위상 순환 계속.
- 존 비활성/언로드: `OnDisable`에서 전체 Rigidbody 복원 + 플레이어 핸들러 Normal 복원.
- 점멸 중 전환: `OnPhaseChanged`가 점멸 코루틴을 항상 먼저 중지하므로 sprite 잔상 없음.

## 6. 테스트

- EditMode: 위상 순환 순서(`NextPhase`) 계산, `AntiGravityHandler.SetPhase`의 `gravityScale` 결과값 검증.
- PlayMode(사람 수행): 3단계 전환 체감, 점멸 가독성, 점프맵 통과 난이도.

## 범위 외 (YAGNI)

- 데미지 함정 신규 구현 (기존 `MagmaFloorDamage`/`FallingHazardBase` 재사용)
- 사운드 큐 (필요 시 `OnPhaseWarning` 구독으로 추후 추가 가능)
- 카메라 오버레이 틴트 (지형까지 물들어 부적합 — 명시적으로 채택 안 함)
