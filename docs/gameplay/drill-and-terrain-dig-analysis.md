# 드릴 & 지형 파기 시스템 심층 분석
@tags: drill, dig, terrain-modifier, neighbor-chunk, MarkDirty, battery, stamina, multi-chunk, TerrainModifier, DrillStrategy, Digger, ModifyTerrain, dig-propagation, ImmediateDig

> 작성일: 2026-03-13
> 대상 시스템: DrillStrategy, Digger, TerrainModifier, TerrainChunk

---

## 목차

1. [시스템 전체 구조](#1-시스템-전체-구조)
2. [DrillStrategy 완전 분석](#2-drillstrategy-완전-분석)
3. [입력 → 픽셀 제거 전체 흐름](#3-입력--픽셀-제거-전체-흐름)
4. [TerrainModifier 알고리즘](#4-terrainmodifier-알고리즘)
5. [배터리 메카닉](#5-배터리-메카닉)
6. [대시 메카닉](#6-대시-메카닉)
7. [비용 시스템 (스태미나 vs 배터리)](#7-비용-시스템-스태미나-vs-배터리)
8. [도구별 비교 분석](#8-도구별-비교-분석)
9. [타이밍 & 수치 레퍼런스](#9-타이밍--수치-레퍼런스)
10. [엣지 케이스 & 주의사항](#10-엣지-케이스--주의사항)
11. [현재 시스템의 문제점 및 개선 제안](#11-현재-시스템의-문제점-및-개선-제안)

---

## 1. 시스템 전체 구조

드릴과 지형 파기는 여러 레이어로 분리되어 있다.

```
[User Input Layer]
  RMB (조준) + LMB (대시/파기)
       │
       ▼
[Strategy Layer]
  DrillStrategy.HandleUpdate()
  DrillStrategy.HandleFixedUpdate()
       │
       ▼
[Coordination Layer]
  Digger.RequestDig()
  Digger.DigRoutine() ← 코루틴 (비동기)
  Digger.DigAt()      ← 우선순위 분기
       │
  ┌────┴──────────┐
  ▼               ▼
[Rock Layer]   [Terrain Layer]
IDiggable      InfinityMapManager.ModifyTerrain()
DiggableRock        │
                    ▼
               TerrainChunk.Dig()
                    │
                    ▼
               TerrainModifier.Dig()  ← 실제 픽셀 제거
```

### 관련 파일

| 역할 | 파일 경로 |
|------|-----------|
| 드릴 핵심 로직 | `Assets/Scripts/UI/Player/Strategies/DrillStrategy.cs` |
| 전략 인터페이스 | `Assets/Scripts/UI/Player/Strategies/IMiningStrategy.cs` |
| 전략 컨텍스트 | `Assets/Scripts/UI/Player/PlayerMining.cs` |
| 파기 조정자 | `Assets/Scripts/Gameplay/Terrain/Tiles/Digger.cs` |
| 픽셀 제거 알고리즘 | `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainModifier.cs` |
| 청크 파기 진입점 | `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` |
| 배터리 UI | `Assets/Scripts/UI/Player/MiningChargeBarUI.cs` |

---

## 2. DrillStrategy 완전 분석

### 2.1 상태 필드

```csharp
// 설정값 (Inspector)
float _maxBattery       // 최대 배터리 (기본 5.0f 초)
float _dashSpeed        // 대시 이동 속도 (기본 15.0f units/sec)
float _digInterval      // 파기 간격 (0.3f 초)

// 런타임 상태
float _currentBattery   // 현재 배터리 (0 ~ _maxBattery)
bool  _isDrillDashing   // 현재 대시 중 여부
float _digTimer         // 마지막 파기 이후 경과 시간

// 컴포넌트 참조
PlayerMining _context   // 전략 컨텍스트
Digger       _digger    // 지형 파기 조정자
Rigidbody2D  _rb        // 대시에 사용하는 물리 컴포넌트
```

### 2.2 HandleUpdate() - 프레임별 로직

```
매 프레임 실행:

1. RMB 감지 (조준)
   ├─ 누르는 중: LookAtMouse() → 드릴 방향 갱신
   └─ 안 누름:   회전 리셋 (대시 중이면 제외)

2. 대시 시작/종료 조건 판단
   ├─ 조준 중 + LMB 누름 + 배터리 > 0  → StartDrillDash()
   └─ 그 외                             → StopDrillDash()

3. 대시 중일 때 (_isDrillDashing == true)
   ├─ 배터리 차감: _currentBattery -= Time.deltaTime
   ├─ 배터리 소진 시: auto-stop
   ├─ 파기 타이머 증가: _digTimer += Time.deltaTime
   └─ 타이머 ≥ 0.3초: Digger.RequestDig(mousePos) + 타이머 리셋
```

### 2.3 HandleFixedUpdate() - 물리 이동

```
대시 중에만 실행:

direction = (mouseWorldPos - playerPos).normalized
rb.linearVelocity = direction * _dashSpeed  // 마우스 방향으로 일정 속도 이동
```

**특징:** 마우스를 움직이면 실시간으로 방향 변경 가능.

### 2.4 StartDrillDash() / StopDrillDash()

```
StartDrillDash():
  _isDrillDashing = true
  _digTimer = _digInterval     ← 시작 즉시 첫 파기 발생
  Animator("DrillDash", true)
  controller.isDashing = true
  controller.isMiningAction = true

StopDrillDash():
  _isDrillDashing = false
  _digTimer = 0f
  Animator("DrillDash", false)
  controller.isDashing = false
  controller.isMiningAction = false
```

### 2.5 GetDigParameters()

```
배터리 ≤ 0이면: CanDig = false (파기 불가)

배터리 > 0이면:
  CanDig:           true
  RadiusMultiplier: 1.0f     ← 반경 보너스 없음
  ToolIndex:        3         ← 드릴
  CanDigTerrain:    true
  CanDigRock:       true
  IgnoreStaminaCost: true    ← 스태미나 소모 없음 (핵심!)
```

---

## 3. 입력 → 픽셀 제거 전체 흐름

### 3.1 입력부터 Digger까지

```
프레임 A: RMB 눌림
  → DrillStrategy.HandleUpdate()
  → LookAtMouse()
  → Animator: DrillAiming = true

프레임 B: LMB 눌림 (RMB 유지 + 배터리 있음)
  → StartDrillDash()
  → _isDrillDashing = true
  → _digTimer = 0.3f   ← 즉시 파기 트리거

프레임 C ~ N (매 프레임):
  → _currentBattery -= deltaTime
  → _digTimer += deltaTime
  → 0.3초마다: Digger.RequestDig(mouseWorldPos)
```

### 3.2 Digger 내부 흐름

```
Digger.RequestDig(targetPos)
  └─ 쿨다운 체크 (0.1초)
     └─ TryDig(targetPos)
        ├─ Animator.SetTrigger("Mine")
        └─ StartCoroutine(DigRoutine(targetPos))

DigRoutine(targetPos)  ←코루틴
  ├─ yield return WaitForSeconds(0.05f)  ← 타격감 딜레이
  ├─ 방향 계산: (targetPos - playerPos).normalized
  ├─ effectiveRadius = digRadius * digParams.RadiusMultiplier
  └─ DigAt(digPosition)
```

### 3.3 DigAt() - 우선순위 분기

```
Digger.DigAt(Vector2 pos)

  1순위: IDirtDiggable (흙 패치) 감지?
    → dirtPatch.DigDirt() → RETURN

  2순위: IDiggable (암석) 감지?
    → digParams.CanDigRock == true?
    → IgnoreStaminaCost == true이면 스태미나 차감 스킵
    → diggable.Dig(pos, radius, toolIndex) → RETURN

  3순위: 일반 지형 (TerrainChunk)
    → mapManager.ModifyTerrain(pos, radius, toolIndex)
```

### 3.4 TerrainChunk.Dig()

```
chunk.Dig(mouseWorldPos, radius, toolIndex)
  ├─ EnsureJobsCompleted()  (진행 중 Jobs 완료 대기)
  ├─ 파티클 콜백 생성 (디버리스 파티클용)
  ├─ _modifier.Dig(data, ...)  ← 실제 픽셀 제거
  └─ 수정 발생 시:
     ├─ BasePixels → CurrentPixels 복사
     ├─ 부유 섬 제거 (Island Removal)
     ├─ 인접 암석 노출 체크
     └─ 청크 Dirty 마킹 (시각적 업데이트 예약)
```

---

## 4. TerrainModifier 알고리즘

픽셀을 실제로 제거하는 핵심 알고리즘.

### 4.1 좌표 변환

```
1. 월드 좌표 → 픽셀 좌표
   mousePixel = worldToPixelConverter(mouseWorldPos)

2. 기하학적 중심 계산 (멀티청크 정밀도 수정)
   direction = (mouseWorldPos - playerWorldPos).normalized
   trueCenterWorld = playerWorldPos + (direction * reachOffset)
   trueCenterPixel = worldToPixelConverter(trueCenterWorld)

   ↑ 단순 픽셀 반올림이 아닌 월드 공간에서 먼저 계산.
     청크 경계에서 양자화 오차 방지.
```

### 4.2 타원형 파기 영역

드릴 파기는 **진행 방향으로 늘어난 타원** 형태:

```
기준: 마우스 방향 벡터를 축으로 하는 타원

각 픽셀 (x, y)에 대해:
  1. 중심으로부터 상대 좌표: (dx, dy)
  2. 방향 벡터 기준으로 회전 (로컬 공간)
  3. 전방(localX ≥ 0)에 VerticalScale 적용
     └─ 스케일 1.5 → 전방으로 넓게 파임
  4. 후방(localX < 0)에는 스케일 1.0 (원형)

최종 거리 = sqrt((localX/scale)² + localY²)
파기 조건: 최종 거리 ≤ radiusPx
```

**결과:** 드릴을 앞으로 밀면서 파는 느낌을 줌. 이동 방향과 파기 모양이 일치.

### 4.3 픽셀 처리 루프

```python
for (y in clampedMinY..clampedMaxY):
  for (x in clampedMinX..clampedMaxX):

    if pixel.alpha == 0: skip  # 이미 빈 공간

    # 타원형 거리 계산 (위 수식)
    if dist > radiusPx: skip

    # 도구 vs 재질 체크
    pixelType = data.PixelInfo[index]  # 1=흙, 2=암석
    if pixelType == 2 and toolIndex == 1:  # 삽으로 암석
      if dist > radiusPx * 0.04: skip      # 중심 4%만 파임

    # 픽셀 제거
    data.BasePixels[index] = Color32(0,0,0,0)  # 투명 처리
    data.PixelInfo[index] = 0
    pixelChanged = true

    # 이벤트 발생
    OnPixelDestroyed.Invoke(worldPos)
    particleCallback(worldPos, color)  # 디버리스 파티클
```

### 4.4 사후 처리

```
TerrainChunk.Dig() 수정 후:

1. BasePixels → CurrentPixels 복사
2. 부유 섬 제거
   - 격리된 픽셀 클러스터 감지 후 제거
   - CheckFloatingIslandsInArea(bounds) 호출
3. 인접 암석 노출
   - 파기 영역 ±1px 확장
   - 겹치는 DiggableRock → RevealInTerrain()
4. 더티 마킹
   - 현재 청크 + 인접 청크 MarkDirty()
   - RectInt 기반 (불필요한 업데이트 최소화)
```

---

## 5. 배터리 메카닉

### 5.1 소모 방식

```
HandleUpdate() 내부 (대시 중에만):
  _currentBattery -= Time.deltaTime  // 초당 1.0 감소 (선형)

조건:
  배터리 ≤ 0 → StopDrillDash() 자동 호출
  배터리 = 0 → CanDig = false → 파기 중단
```

**기본 설정:** maxBattery = 5.0초 → **5초 연속 드릴링** 가능.

### 5.2 배터리 UI (MiningChargeBarUI)

```
드릴 대시 중(_isDrillDashing = true)에만 표시

fillAmount = _currentBattery / _maxBattery  (0.0 ~ 1.0)

색상:
  fillAmount ≥ 0.3: 초록색
  fillAmount < 0.3: 빨간색 (경고)
```

### 5.3 배터리 회복

현재 구현: **배터리 회복 없음** (드릴을 멈춰도 충전되지 않음).
도구 전환 시 `Enter()` 호출 → `_currentBattery = _maxBattery` (최대치로 리셋).

---

## 6. 대시 메카닉

### 6.1 이동 물리

```
HandleFixedUpdate():
  if NOT _isDrillDashing: return

  dir = (mouseWorldPos - playerPos).normalized
  rb.linearVelocity = dir * _dashSpeed  // 일정 속도 직접 지정
```

- **속도:** `_dashSpeed` (기본 15.0 units/sec)
- **방향:** 마우스를 따라 실시간 변경 가능
- **제어:** `rb.linearVelocity` 직접 설정 (관성 없음)
- **플래그:** `controller.isDashing`, `controller.isMiningAction` → 다른 이동 로직 차단

### 6.2 파기 타이머와 대시의 관계

```
StartDrillDash():
  _digTimer = _digInterval  // = 0.3f
  ↑ 대시 시작 즉시 첫 파기 발동

대시 중:
  _digTimer += deltaTime
  if _digTimer >= 0.3f:
    Digger.RequestDig()
    _digTimer = 0f

→ 0.3초마다 파기 (약 3.3회/초)
```

### 6.3 도구 전환 잠금

```
CanSwitchTool(): return !_isDrillDashing
  → 대시 중에는 도구 변경 불가
  → 대시 안 할 때는 자유롭게 전환 가능
```

---

## 7. 비용 시스템 (스태미나 vs 배터리)

### 7.1 각 도구의 비용 방식

| 도구 | IgnoreStaminaCost | 실제 소모 자원 |
|------|:-----------------:|---------------|
| 삽 (Sap) | false | 스태미나 |
| 곡괭이 (Pickaxe) | false | 스태미나 (0.5f/암석 타격) |
| 드릴 (Drill) | **true** | 배터리 (시간 기반) |
| 빈 손 (Empty) | false | 스태미나 |

### 7.2 Digger에서의 처리

```csharp
// Digger.DigAt() 내부
bool ignoreCost = digParams.IgnoreStaminaCost;  // Drill: true

if (!ignoreCost)
    _costCalculator.PayCost(targetTileType, effectiveRadius);
    // ↑ 드릴은 이 분기 스킵 → 스태미나 변화 없음
```

드릴은 배터리를 DrillStrategy 자체에서 관리하며, Digger는 비용 계산에 개입하지 않는다.

---

## 8. 도구별 비교 분석

### 8.1 파기 방식 비교

| 구분 | 삽 | 곡괭이 | 드릴 |
|------|---|------|-----|
| 트리거 | 차지 후 LMB 한 번 | LMB 클릭마다 | 대시 중 0.3초마다 자동 |
| 연속성 | 단발 | 단발 (콤보) | 연속 자동 |
| 이동 | 차지 중 느려짐 | 보통 | 마우스 방향 대시 |
| 자원 | 스태미나 | 스태미나 | 배터리 |
| 암석 파기 | 불가 (중심 4%만) | 가능 | 가능 |
| 반경 보너스 | 차지량 비례 | 콤보 단계 비례 | 없음 (1.0배) |

### 8.2 파기 반경 계산

```
삽:
  radius = baseRadius * chargeRatio      // 0.0 ~ 1.0 배

곡괭이:
  radius = baseRadius * comboMultiplier  // 1.0, 1.2, 1.5 배

드릴:
  radius = baseRadius * 1.0f            // 고정 (보너스 없음)
  단, 타원형 형태로 방향성 있음
```

### 8.3 도구 전환 잠금 조건

| 도구 | 잠금 조건 |
|------|----------|
| 삽 | 차지 중 (차지량 > 0) |
| 곡괭이 | 콤보 윈도우 (1초) |
| 드릴 | 대시 중 (_isDrillDashing) |

---

## 9. 타이밍 & 수치 레퍼런스

| 항목 | 값 | 위치 |
|------|---|------|
| 드릴 파기 간격 | 0.3초 | DrillStrategy._digInterval |
| Digger 쿨다운 | 0.1초 | Digger.digCooldown |
| 파기 시각 딜레이 | 0.05초 | Digger.DigRoutine |
| 배터리 최대 | 5.0초 | DrillStrategy._maxBattery |
| 배터리 소모율 | 1.0/초 | DrillStrategy.HandleUpdate |
| 대시 속도 | 15.0 units/초 | DrillStrategy._dashSpeed |
| 텍스처 업데이트 | 0.05초 쓰로틀 | InfinityMapManager |
| 부유 섬 체크 마진 | 20px | TerrainChunk.Dig |
| 타원 전방 스케일 | 1.5배 | TerrainModifier.VerticalScale |
| 삽의 암석 파기율 | 4% (중심만) | TerrainModifier.ROCK_DIG_THRESHOLD |

---

## 10. 엣지 케이스 & 주의사항

### 10.1 멀티청크 파기

- 드릴은 이동하면서 파기 → 청크 경계에서 파기 요청 가능
- `TerrainModifier.Dig()`는 `trueCenterWorld`를 월드 공간에서 먼저 계산 후 픽셀 변환
- 이로써 청크 A / 청크 B 경계에서 양자화 오차 방지
- 각 청크는 독립적으로 Dig() 호출됨

### 10.2 배터리 소진 타이밍

```
배터리 소진 시 순서:
1. _currentBattery <= 0
2. StopDrillDash() 호출
3. HandleUpdate() return (그 프레임 파기 없음)
4. GetDigParameters()에서 CanDig = false
5. Digger.DigAt()에서 파기 중단
```

주의: 배터리 소진과 파기 타이머가 **같은 프레임**에 동시 조건을 만족할 경우, 배터리 소진이 우선 처리된다.

### 10.3 암석 노출 트리거

```
TerrainChunk.Dig() 후:
  파기 영역을 ±1px 확장한 RectInt 생성
  SpawnedRocks 전체 순회
  → rock.PixelBoundsInChunk와 겹치면 RevealInTerrain()
```

드릴로 빠르게 이동하면서 파기할 경우, 인접 암석이 연쇄적으로 노출될 수 있다.

### 10.4 Island Removal 성능

- 대시 중 0.3초마다 파기 → 각 파기마다 Island Removal 실행
- 큰 지형에서 연속 파기 시 CPU 비용 누적 가능
- `useIslandRemoval` 플래그로 비활성화 가능 (기본 true)

### 10.5 Digger 쿨다운 vs 드릴 간격

```
DrillStrategy 파기 간격: 0.3초
Digger 내부 쿨다운: 0.1초

→ 드릴 간격(0.3s)이 쿨다운(0.1s)보다 크므로 실제로는 쿨다운이 걸리지 않음
→ 만약 드릴 간격을 0.1초 이하로 줄이면 Digger 쿨다운에 막힘
```

---

## 11. 현재 시스템의 문제점 및 개선 제안

### 11.1 배터리 회복 미구현

**현황:** 도구 교체 시에만 배터리 리셋 (`Enter()`).
**문제:** 드릴을 잠깐 쉬어도 회복 안 됨 → 실제 게임에서 배터리 관리 의미가 없음.
**개선 제안:**
- 대시 해제 후 일정 시간이 지나면 자동 회복
- 충전 아이템이나 충전소 추가
- 수동 충전 (특정 키 + 이동 불가) 메카닉

### 11.2 드릴 파기 반경 고정

**현황:** RadiusMultiplier = 1.0f (보너스 없음).
**문제:** 삽(차지), 곡괭이(콤보) 대비 깊이감 부족.
**개선 제안:**
- 배터리 잔량에 따라 반경 증감
- 지속 파기 시간에 따라 반경 증가 (히트 시간 기반)

### 11.3 대시 방향의 즉각 반전

**현황:** `rb.linearVelocity = direction * _dashSpeed` 직접 지정.
**문제:** 관성이 없어 마우스만 돌리면 순간 방향 전환 가능.
**개선 제안:**
- Lerp로 방향 전환 완화
- 최소 대시 지속 시간 추가

### 11.4 드릴 파기 시 파티클 부재 여부 확인

드릴은 0.3초마다 Digger.RequestDig()를 통해 파기하므로, 일반 좌클릭과 동일한 파티클 흐름을 따른다. 충분한 시각적 피드백이 있는지 검토 필요.

### 11.5 CanSwitchTool() 범위

**현황:** 대시 중에는 도구 전환 불가.
**문제:** 배터리 소진으로 자동 stop 후에는 즉시 전환 가능 → 연출상 어색할 수 있음.
**개선 제안:** 대시 종료 후 짧은 쿨다운(0.2초) 동안 전환 잠금 유지.

---

## 빠른 요약

```
드릴 작동 흐름:

1. RMB 누름 → 조준 모드 (LookAtMouse)
2. LMB 누름 → 대시 시작
   - 마우스 방향 15m/s 이동
   - 0.3초마다 파기
   - 배터리 1.0/초 감소
3. 배터리 소진 OR LMB 해제 → 대시 종료

파기 경로:
DrillStrategy → Digger → TerrainChunk → TerrainModifier
  → BasePixels[index] = (0,0,0,0)  ← 픽셀 제거
  → IslandRemoval, RockExpose, DirtyMark

특이사항:
- IgnoreStaminaCost = true (스태미나 소모 없음)
- 타원형 파기 (전방 VerticalScale=1.5배 확장)
- 멀티청크 월드 공간 중심 계산 (양자화 오차 방지)
```

---

*문서 끝*
