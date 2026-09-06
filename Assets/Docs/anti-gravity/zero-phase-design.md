# 반중력 존 위상 재설계 — 저중력(달) 순환 + 부드러운 뒤집힘

작성일: 2026-07-06 (개정: 2026-07-07 무중력 → 저중력 모델 전환)

## 배경 / 문제

`AntiGravityZone`은 위상을 순환하며 플레이어·오브젝트의 중력을 바꾼다.

**초기 버그:** 무중력(gravityScale=0) 위상에서 점프하면 감속하는 힘이 없어 무한 상승("하늘로 날아감").
**추가 피드백:** 무중력에서 화면 중앙으로 떠오르는 연출이 어색함 → **달처럼 저중력** 상태로 변경.
각 중력 방향에서 중력이 약해졌다가 방향이 뒤집히는 흐름을 원함.

## 목표 경험

"달에 착지한 것처럼" 중력이 약해져 낙하·점프가 느리고 붕 뜨는 느낌.
- 완전 무중력(0)이 아니라 **약한 중력**이라 플레이어는 여전히 바닥/천장에 붙어 정상 조작 가능.
- 각 방향(아래/위)에서 강→약으로 약해진 뒤 반대 방향으로 뒤집힘.

---

## 설계

### 1. 4위상 순환

`Normal → LowNormal → Inverted → LowInverted → (반복)`

| 위상 | 방향 | gravityScale |
|------|------|--------------|
| `Normal` | 강한 아래 | `defaultScale` |
| `LowNormal` | 약한 아래(달) | `defaultScale × lowGravityFactor` |
| `Inverted` | 강한 위 | `-|defaultScale| × antiGravityMultiplier` |
| `LowInverted` | 약한 위(달) | 위 값 `× lowGravityFactor` |

- `GravityPhases.Cycle[]` 배열 + `NextIndex()`로 인덱스 순환 (Low가 두 번 등장하므로 위상이 아닌 인덱스로 진행).
- `lowGravityFactor` 기본 0.3 (0=무중력, 1=원래 중력). 핸들러(플레이어)·존(비플레이어) 각각 SerializeField.
- 지속 시간: `normalDuration` / `lowDuration`(두 Low 공통) / `invertedDuration`.

### 2. 중력 적용

- `AntiGravityHandler`가 Normal 외 위상에서 `_overrideGravity=true`로 목표 gravityScale을 매 `FixedUpdate` 재적용
  (PlayerController가 order 0에서 gravityScale을 원복하므로 order 100에서 덮어씀).
- 무중력 시절의 부력/drag/속도클램프/미세보정 로직은 **전부 제거** — 저중력은 실제 중력이라 별도 부유 처리 불필요.

### 3. 부드러운 뒤집힘 (localScale.y)

**중요:** 플레이어 Rigidbody2D는 **Freeze Rotation Z**라 `transform.rotation`으로는 뒤집을 수 없다
(물리 스텝마다 0으로 원복됨). 따라서 **`localScale.y` 부호 반전**(1 ↔ -1)으로 위아래를 반사한다.

- 방향이 바뀌는 전환(`LowNormal → Inverted`, `LowInverted → Normal`)에서만 `FlipTransition` 코루틴 실행:
  `transitionDuration` 동안 `localScale.y`를 `1 → 0 → -1`(또는 역)로 lerp → "옆으로 돌아 뒤집히는" 연출.
  동시에 세로 속도를 0으로 완화.
- 같은 방향 내 전환(강↔약)에서는 뒤집힘 없이 즉시 현재 방향 유지.
- 좌우 flip은 PlayerController가 `localScale.x`로 처리 → 충돌 없음.
- 점프 방향: `IsGravityInverted`(= 중력이 위쪽인 위상)일 때 아래로 점프. Inverted·LowInverted 모두 해당.

---

## 영향 파일

- `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Zones/GravityPhase.cs` — enum 4위상, Cycle/NextIndex/IsInvertedDirection/ScaleFor(+lowFactor)
- `Assets/Scripts/UI/Player/AntiGravityHandler.cs` — 저중력 목표 적용 + localScale.y 부드러운 flip
- `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Zones/AntiGravityZone.cs` — lowDuration/lowGravityFactor, ScaleFor 시그니처
- `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/Zones/AntiGravityBackground.cs` — phaseSprites 4칸(index=위상)
- `Assets/Tests/EditMode/GravityPhaseTests.cs` — 순환·ScaleFor·IsInvertedDirection 검증

## 비목표 (YAGNI)

- 무중력(0) 부유·attractor·부력·세로 속도 클램프 (저중력으로 대체)
- 방향 전환 시 회전(transform.rotation) 기반 flip (Freeze Rotation Z로 불가)
