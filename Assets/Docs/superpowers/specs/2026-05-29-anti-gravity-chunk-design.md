# 반중력 청크 시스템 설계

## 개요

청크 경계에 진입하면 중력이 반전되는 특수 청크 시스템.
플레이어뿐 아니라 청크 내 모든 Rigidbody2D 오브젝트에 반중력이 적용된다.

---

## 동작 명세

| 항목 | 동작 |
|------|------|
| 중력 방향 | 반전 (위쪽으로 당김) |
| 중력 세기 | 기본 gravityScale × -0.7 (약하게) |
| 좌우 이동 | 변경 없음 |
| 점프 | 반전 — 점프 키 → 아래쪽(world 기준)으로 힘 |
| 벽타기 | 변경 없음 |
| 스프라이트 | SpriteRenderer.flipY = true |
| 전환 | Y속도 0으로 감쇄(0.25초) → gravityScale 반전 |
| 이탈 | 역순 전환 후 원상복구 |
| 영향 범위 | 청크 내 진입한 모든 Rigidbody2D |

---

## 컴포넌트 구조

### AntiGravityZone (특수 청크에 부착)

```
[AntiGravityChunk 프리팹]
  └─ AntiGravityZone (MonoBehaviour)
       - BoxCollider2D (isTrigger=true, 청크 전체 커버)
```

책임:
- `OnTriggerEnter2D` / `OnTriggerExit2D` 감지
- 플레이어 감지 시 → `AntiGravityHandler.Activate()` / `Deactivate()` 호출
- 비플레이어 Rigidbody2D 감지 시 → `gravityScale` 직접 조작
- 비플레이어 오브젝트의 원래 gravityScale을 `Dictionary<Rigidbody2D, float>`로 캐싱

### AntiGravityHandler (Player에 미리 부착)

```
[Player]
  ├─ PlayerController
  ├─ AntiGravityHandler  ← 신규
  └─ ...
```

책임:
- `Activate()`: Y속도 감쇄 코루틴 → gravityScale 반전 → SpriteRenderer.flipY = true
- `Deactivate()`: 역순 전환 → 원복
- `public bool IsActive` 프로퍼티 노출

### PlayerController 변경 (최소)

변경 사항 2줄만:

```csharp
// Start() — 캐싱
_antiGravityHandler = GetComponent<AntiGravityHandler>();

// 점프 처리 부분 — 방향 multiplier
float jumpDir = (_antiGravityHandler != null && _antiGravityHandler.IsActive) ? -1f : 1f;
rb.AddForce(Vector2.up * jumpForce * jumpDir, ForceMode2D.Impulse);
```

이동·벽타기·드릴·스태미나 로직은 전혀 건드리지 않는다.

---

## IZoneEffect를 사용하지 않는 이유

기존 `ZoneEffectTrigger`는 `PlayerStat` 컴포넌트가 있는 오브젝트만 감지한다.
반중력은 모든 `Rigidbody2D`에 반응해야 하므로, `AntiGravityZone`이 `OnTriggerEnter2D`를 직접 구현한다.
`IZoneEffect`를 억지로 적용하면 비플레이어 오브젝트 처리를 위한 별도 컴포넌트가 추가되어 오히려 복잡해진다.

---

## 전환 흐름 (AntiGravityHandler.Activate)

```
1. 이미 전환 중이면 기존 코루틴 중단
2. 0.25초 동안 매 프레임 rb.velocity.y를 Mathf.Lerp(currentY, 0, t)로 감쇄
3. 감쇄 완료 후 rb.gravityScale = defaultGravity * -antiGravityMultiplier (기본 0.7)
4. spriteRenderer.flipY = true
```

`Deactivate`는 역순: flipY 복구 → gravityScale 원복 → velocity 보정 없음(자연스럽게 낙하).

---

## 비플레이어 오브젝트 처리

```csharp
// 진입 시
float original = rb.gravityScale;
_originalGravityScales[rb] = original;
rb.gravityScale = original * -antiGravityMultiplier;

// 이탈 시
if (_originalGravityScales.TryGetValue(rb, out float saved))
{
    rb.gravityScale = saved;
    _originalGravityScales.Remove(rb);
}
```

오브젝트가 파괴될 경우 null 체크로 Dictionary 정리.

---

## 청크 설정

- 프리팹: `AntiGravityChunk` (특수 청크 시스템에 등록)
- `AntiGravityZone`의 `antiGravityMultiplier`: Inspector에서 조정 (기본 0.7)
- `transitionDuration`: Inspector에서 조정 (기본 0.25초)
- `IChunkInitializer` 구현 불필요 — 지형 픽셀 수정 없음, 트리거 영역만 필요

---

## 범위 외 (1차 구현 미포함)

- 카메라 동작 — 기존 그대로 유지
- 구역 시각적 표현 (배경, 파티클) — 기존 그대로 유지
- 반중력 상태 UI 표시 방향 — 기존 그대로 유지
