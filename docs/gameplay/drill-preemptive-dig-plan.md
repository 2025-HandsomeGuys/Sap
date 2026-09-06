# 드릴 선행 Dig (Preemptive Dig) 구현 계획
@tags: drill, preemptive, ImmediateDig, physics, velocity, TerrainChunk, plan, collider

> 작성일: 2026-03-13
> 참고: `drill-and-terrain-dig-analysis.md`

---

## 문제 정의

로그 확인 결과:
```
[DrillStrategy/Fixed] setVel=(-0.08, -6.14)  gravity=0.00
```

velocity는 정상 설정되고 gravity도 0. 그럼에도 플레이어가 안 움직이는 이유:

```
FixedUpdate (0.02초마다):
  DrillStrategy → rb.linearVelocity = dir * speed 설정
  물리 시뮬레이션 → TerrainCollider 충돌 → velocity = 0

HandleUpdate (0.05초마다):
  Digger.RequestDig()
    → DigRoutine 코루틴 시작
      → yield WaitForSeconds(0.05f)  ← 딜레이
        → DigAt() → ModifyTerrain()
          → TerrainChunk.Dig()
            → 콜라이더 갱신 (쓰로틀링)  ← 또 딜레이
```

**이동(FixedUpdate 0.02초)보다 파기+콜라이더갱신 사이클이 훨씬 느리다.**
플레이어가 앞으로 가려 할 때 항상 땅이 먼저 막고 있음.

---

## 선행 Dig 핵심 아이디어

**FixedUpdate에서 velocity 설정 전에 이동 방향 앞쪽을 즉시·동기적으로 파기.**

```
HandleFixedUpdate():
  1. 이동 방향 계산
  2. 앞쪽 영역 즉시 파기  ← NEW (동기, 딜레이 없음)
  3. rb.linearVelocity 설정
```

땅이 뚫린 다음 이동하므로 콜라이더에 막히지 않음.

---

## 우회해야 할 레이어

현재 파기 경로에서 선행 dig가 건너뛰어야 하는 단계:

```
Digger.RequestDig()
  └─ 쿨다운 체크 (0.1초)         ← 건너뜀 (FixedUpdate마다 파야 함)

Digger.DigRoutine() [코루틴]
  └─ WaitForSeconds(0.05f)       ← 건너뜀 (동기 실행 필요)

TerrainCollider 쓰로틀링
  └─ 갱신 지연                   ← 선행 dig 직후 즉시 갱신 필요
```

---

## 구현 계획

### Step 1. `Digger`에 즉시 파기 메서드 추가

기존 `RequestDig()`는 코루틴 기반이라 쓸 수 없음.
`Digger`에 새 동기 메서드 `ImmediateDig()`를 추가한다.

```csharp
// Digger.cs 에 추가
public void ImmediateDig(Vector2 worldPos, float radius, int toolIndex)
{
    // 쿨다운, 코루틴, 딜레이 전부 없음
    // DigAt()의 핵심만 직접 실행: 지형 파기만 (암석/흙 패치 제외)
    if (_mapManager == null) return;
    _mapManager.ModifyTerrain(worldPos, radius, toolIndex);
}
```

**왜 암석/흙 패치는 제외?**
- 선행 dig의 목적은 "이동 경로 확보"
- 암석은 별도 HP 시스템 → 선행 dig로 한 번에 제거하면 밸런스 파괴
- 흙 패치도 별도 상호작용 → 기존 타이머 파기(`RequestDig`)에서 처리

### Step 2. `DrillStrategy.HandleFixedUpdate()` 수정

```
현재:
  dir 계산
  rb.linearVelocity = dir * dashSpeed

변경 후:
  dir 계산
  digPos = playerPos + dir * _preDigOffset   ← 플레이어 앞 파기 위치
  _digger.ImmediateDig(digPos, _preDigRadius, 3)  ← 선행 파기
  rb.linearVelocity = dir * dashSpeed
```

**파기 위치 (_preDigOffset):**
```
플레이어 몸통 반경 + 이동 방향으로 여유분
예: playerBodyRadius(약 0.3) + preDigRadius(약 0.4) = 0.7 정도

너무 멀리 파면: 플레이어가 아직 거기 없는데 파임 (자원 낭비)
너무 가까우면: 콜라이더 갱신 전에 이미 부딪힘

적정값: dashSpeed * FixedDeltaTime * 1.5f 정도
  → 다음 프레임 이동 거리보다 약간 더 앞
```

**파기 반경 (_preDigRadius):**
```
기존 Digger.digRadius 이상이어야 함
플레이어가 통과할 수 있을 만큼 넉넉하게

적정값: 기존 digRadius * 1.2f 정도
```

### Step 3. 콜라이더 즉시 갱신 확인

`ModifyTerrain()` → `TerrainChunk.Dig()` 내부에서:

```
TerrainChunk.Dig()
  → _modifier.Dig()         픽셀 제거
  → MarkChunkDirty()        dirty 마킹
  → (LateUpdate에서 갱신)   ← 여기가 문제
```

TerrainCollider가 LateUpdate 또는 쓰로틀링 주기에 갱신된다면, 같은 FixedUpdate 안에서 파기 직후 velocity를 설정해도 그 프레임 물리에서는 아직 콜라이더가 막혀있을 수 있음.

**확인 필요: TerrainCollider가 즉시 갱신되는지, 아니면 다음 프레임인지.**

- **즉시 갱신이면**: Step 1~2만으로 충분
- **다음 프레임 갱신이면**: velocity는 선행 dig 다음 FixedUpdate에서 효과가 나타남
  → 1 FixedUpdate(0.02초) 지연. 실용적으로는 문제 없을 수 있음

### Step 4. 기존 타이머 파기와 역할 분리

```
HandleFixedUpdate() → ImmediateDig()
  역할: 이동 경로 확보 (지형만, 매 FixedUpdate)
  효과: 파티클/사운드 없음 (조용하게 뚫림)

HandleUpdate() → Digger.RequestDig()
  역할: 파티클, 사운드, 암석 데미지, 흙 패치 상호작용
  간격: _digInterval (0.05초 현재값 유지 또는 조정)
```

이렇게 하면 선행 dig와 기존 파기가 중복으로 같은 픽셀을 제거하지만,
이미 제거된 픽셀은 `alpha == 0` 체크로 즉시 스킵되므로 성능 손실 최소.

---

## 수정 파일 목록

| 파일 | 변경 내용 |
|------|----------|
| `Digger.cs` | `ImmediateDig(Vector2, float, int)` 메서드 추가 |
| `DrillStrategy.cs` | `HandleFixedUpdate()`에 선행 파기 로직 추가, 파라미터 추가 |

**TerrainChunk, TerrainModifier, InfinityMapManager는 건드리지 않음.**

---

## 파라미터 초기값 제안

```
_preDigOffset = 0.5f        // 플레이어 앞 0.5 유닛
_preDigRadius = 0.6f        // 파기 반경 (Digger.digRadius 기준으로 조정)
dashSpeed     = 3.0f ~ 5.0f // 현재 6.14에서 조정 여지
```

dashSpeed를 너무 높이면 선행 파기가 제때 따라가지 못함.
`preDigOffset ≥ dashSpeed * Time.fixedDeltaTime` 조건을 맞춰야 안전.

---

## 예상 동작 흐름 (구현 후)

```
FixedUpdate N:
  dir = (0.00, -1.00)  ← 아래 방향
  ImmediateDig(playerPos + dir * 0.5, radius=0.6)
    → 플레이어 0.5 유닛 아래 픽셀 즉시 제거
    → 콜라이더 갱신 (즉시 or 다음 프레임)
  rb.linearVelocity = (0, -5)

FixedUpdate N+1:
  물리: 이미 뚫린 공간 → 충돌 없음 → velocity 유지
  ImmediateDig(새 playerPos + dir * 0.5) ← 한 단계 더 아래 파기
  rb.linearVelocity = (0, -5)  → 계속 이동

결과: 플레이어가 드릴로 아래를 뚫으며 내려감
```

---

## 주의사항

1. **선행 파기 파티클 없음** — ImmediateDig는 시각 효과 없이 조용히 픽셀만 제거. 파티클은 기존 RequestDig가 담당.

2. **암석 선행 파기 금지** — ImmediateDig는 TerrainChunk(지형)만 대상. 암석(IDiggable)은 기존 타이머 파기로만 처리.

3. **성능** — FixedUpdate마다(초당 50회) 파기 호출. 파기 반경을 작게 유지해야 함. 이미 빈 픽셀은 즉시 스킵되므로 실제 부하는 이동 경계면에만 집중.

4. **콜라이더 갱신 타이밍 확인 필수** — 구현 후 로그로 확인: 선행 파기 직후 다음 FixedUpdate에서 velocity가 유지되는지.

---

## 구현 순서

1. [ ] `Digger.cs` — `ImmediateDig()` 메서드 추가
2. [ ] `DrillStrategy.cs` — `_preDigOffset`, `_preDigRadius` 필드 추가
3. [ ] `DrillStrategy.HandleFixedUpdate()` — 선행 파기 호출 삽입
4. [ ] 테스트: 로그에서 `vel`이 0이 아닌 값으로 유지되는지 확인
5. [ ] 콜라이더 갱신 타이밍에 따라 `preDigOffset` 조정
6. [ ] 로그 제거 (드릴 문제 해결 확인 후)
