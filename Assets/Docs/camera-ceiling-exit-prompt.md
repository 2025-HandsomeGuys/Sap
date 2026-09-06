# 카메라 Y 상한 + 상단 도달 시 지상 이동 확인창

작성일: 2026-07-25
대상 씬: `DemoUnderground` (동일 구조의 지하 씬 전반)

---

## 1. 목적

지하 탐험 씬에서 플레이어가 맵 최상단으로 올라갔을 때:

1. 카메라가 특정 월드 높이 위로 올라가지 않게 막는다 (천장 너머의 빈 공간·미완성 배경 노출 방지).
2. 그보다 조금 더 위에 있는 트리거에 닿으면 "정말 탐험을 종료하고 지상으로 올라가시겠습니까?" 확인창을 띄운다.

확인창은 **엘리베이터에서 지상으로 나갈 때 쓰는 것과 동일한 `ConfirmationPrompt`** 이며,
`ExploreExitController.RequestExitToSurface()`에 이미 구현돼 있으나 현재 **호출하는 곳이 없어 사용되지 않는 코드**다.
이번 작업은 그 경로를 실제로 연결하는 것이다.

---

## 2. 현황

| 항목 | 상태 |
|------|------|
| `ExploreExitController.RequestExitToSurface()` | 구현돼 있음. 호출처 0개 (dead code) |
| `ExploreExitController` 실제 동작 | `ExitTrigger` 진입 후 **E 키 2초 홀드** → `PrepareSettlement()` |
| `holdHintUI` / `holdProgressImage` | 씬에서 둘 다 미연결 (`fileID: 0`) → 홀드 진행 피드백이 전혀 없는 상태 |
| 씬 `ExitTrigger` 오브젝트 | `BoxCollider2D` + `ExploreExitController`, `confirmationPrompt` 연결됨 |
| 카메라 | `CinemachineCamera` + `CinemachineFollow`. Confiner 없음 |
| `CameraFollow.minY/maxY` | `_hasCinemachineInScene == true`라 `HandleManualCameraMove()`가 실행되지 않음 → 사실상 dead code |
| 엘리베이터 경로 | `ElevatorUI.OnSurfaceButtonClicked()`가 자체 확인창을 띄우고 `ExecuteExit()`(확인창 없이 즉시) 호출 |

---

## 3. 설계 결정

| # | 결정 | 근거 |
|---|------|------|
| D1 | 카메라 상한선과 프롬프트 트리거는 **서로 다른 높이** | 상한선 근처에서 점프만 해도 프롬프트가 뜨는 오작동 방지. 플레이어가 화면 위쪽으로 올라가는 연출도 확보 |
| D2 | 프롬프트 트리거는 **기존 `ExitTrigger` 콜라이더 재사용** | 새 감지 로직 불필요. 씬에서 위치 조정만으로 튜닝 가능 |
| D3 | **E 키 2초 홀드 방식은 제거**하고 확인창으로 대체 | 홀드는 오조작 방지용인데 확인창이 같은 역할을 함. 둘 다 요구하면 이중 확인 |
| D4 | 취소 후에는 **콜라이더를 벗어났다 다시 들어와야** 재표시 | 프롬프트 스팸 원천 차단. 상태 플래그 1개로 해결 |
| D5 | 카메라 클램프는 **커스텀 `CinemachineExtension`** (Confiner2D 아님) | 지형이 가로 무한·세로 깊은 청크 월드라 경계 도형 관리가 부담. Y 한 축만 막으면 충분 |
| D6 | 상한선 = **화면 위쪽 가장자리**가 멈추는 월드 Y (카메라 중심 아님) | "이 높이 위로는 안 보인다"는 의도를 그대로 표현. orthoSize가 바뀌어도 경계선이 고정됨 |

---

## 4. 컴포넌트 설계

### 4.1 `CameraCeilingExtension` (신규)

경로: `Assets/Scripts/Render/Camera/CameraCeilingExtension.cs`
타입: `Unity.Cinemachine.CinemachineExtension` 상속

**인스펙터 필드**

| 필드 | 타입 | 기본값 | 설명 |
|------|------|--------|------|
| `ceilingY` | `float` | `0` | 화면 위쪽 가장자리가 멈출 월드 Y |
| `ceilingEnabled` | `bool` | `true` | 런타임/에디터에서 클램프 on-off |

**동작**

```
PostPipelineStageCallback(vcam, stage, ref state, deltaTime):
    if stage != CinemachineCore.Stage.Finalize  → return
    if !ceilingEnabled                          → return
    if DungeonOverlayController.IsInDungeon     → return   // §4.1.1
    if !state.Lens.Orthographic                 → return   // 이 프로젝트는 항상 직교

    maxCenterY = ClampMath: ceilingY - state.Lens.OrthographicSize
    currentY   = state.GetFinalPosition().y
    if currentY > maxCenterY:
        state.PositionCorrection += Vector3(0, maxCenterY - currentY, 0)
```

**핵심 근거 3가지**

1. **`Finalize` 단계에서 처리** — Body/Aim 이후 최종 위치가 확정된 시점. `CinemachineFollow`의 damping 결과까지 반영된 값을 클램프한다.
2. **`RawPosition`을 덮지 않고 `PositionCorrection`에 더한다** — Cinemachine의 `Confiner2D`와 동일한 관례. 나중에 임팩트 셰이크 같은 다른 익스텐션이 붙어도 보정이 합성된다.
3. **`OrthographicSize`를 매 프레임 `state`에서 읽는다** — `CameraFollow`가 드릴 대시/UI 오픈에 따라 orthoSize를 `SmoothDamp`로 계속 바꾸므로([CameraFollow.cs](../Scripts/Render/Camera/CameraFollow.cs)), 값을 캐싱하면 줌 전환 중 천장선이 위아래로 흔들린다.

**반중력(Dutch 180° 롤) 처리: 불필요**
Z축 롤은 직교 카메라가 커버하는 월드 사각형의 범위를 바꾸지 않는다. 화면이 뒤집혀도 카메라가 비추는 최상단 월드 Y는 동일하므로 별도 분기가 필요 없다.

### 4.1.1 던전 예외

던전은 지하 지형과 물리 충돌을 피하려고 `DungeonOverlayController.DungeonOffset = (0, 10000, 0)` 위치에 생성된다.
천장선(y≈0)을 그대로 적용하면 카메라가 `ceilingY - halfHeight`로 처박히고 플레이어만 1만 유닛 위에 남아 화면 밖으로 사라진다.
→ `DungeonOverlayController.IsInDungeon`이 true면 클램프를 건너뛴다.

**왜 `IsInDungeon` 조회이고, "플레이어 y > 0" 판정이 아닌가**
`ExitTrigger`는 설계상 `ceilingY`보다 위에 놓인다(§7). 좌표만 보고 끄면 지상 이동 확인창 구간에서 천장이 통째로 풀린다.
던전 진입 여부라는 상태를 직접 묻는 것이 유일하게 정확한 판정이다.

**왜 `DungeonOverlayController`가 끄는 방식이 아닌가**
같은 파일이 `CinemachineConfiner2D`를 던전 동안 `enabled = false`로 꺼두고 이탈 시 되살린다(동일한 y≈0 경계 문제).
이 확장은 우리 코드라 상태를 직접 물을 수 있으므로 suspend/restore 쌍을 늘리지 않는다.
이탈 경로를 하나라도 놓쳤을 때 클램프가 영구히 죽는 사고가 원천적으로 없다.

**타이밍 안전성 (확인 완료)**
- 진입: 카메라 컷(`PreviousStateIsValid = false`) → `TeleportPlayer` → `InDungeon = true`가 `yield` 없이 한 프레임 안에서 연속 실행된다. 첫 Cinemachine 갱신(LateUpdate) 시점엔 이미 `IsInDungeon == true`.
- 이탈: `TeleportPlayer(_returnPosition)` → ... → `InDungeon = false` 역시 한 프레임 안. 클램프가 복귀할 때 플레이어는 이미 지하로 돌아와 있다.
- 양쪽 모두 화면 페이드아웃 중에 일어나므로 시각적 튐이 없다.

**테스트 가능성**
클램프 수식을 순수 static 함수로 분리한다.

```csharp
public static float ClampCenterY(float currentY, float ceilingY, float halfHeight)
    => Mathf.Min(currentY, ceilingY - halfHeight);
```

`PostPipelineStageCallback`은 이 함수를 호출만 한다. EditMode 테스트는 이 함수만 검증한다.

**API 확인 (Cinemachine 3.1.6 기준)**

- `CameraState.PositionCorrection` — `Runtime/Core/CameraState.cs:71`
- `CameraStateExtensions.GetFinalPosition(this CameraState)` — 같은 파일 `:462` (확장 메서드, `Unity.Cinemachine` 네임스페이스)
- `LensSettings.Orthographic` (bool 프로퍼티) — `Runtime/Core/LensSettings.cs:169`

### 4.2 `ExploreExitController` 개편

경로: `Assets/Scripts/UI/Interaction/Scene/ExploreExitController.cs`

**제거 대상**

- `Update()` 전체
- 필드: `holdDuration`, `holdHintUI`, `holdProgressImage`, `_holdTimer`
- 메서드: `ResetHold()`, `SetHintVisible()`
- `[Header("홀드 설정")]`, `[Header("홀드 UI 참조")]`

**변경 후 트리거 처리**

```
OnTriggerStay2D(other):
    if !other.CompareTag("Player")                          → return
    if _isExiting || _promptedInZone                        → return
    if UIStateManager.Instance != null
       && UIStateManager.Instance.CurrentState != UIState.None → return   // 다른 UI 열림 → 대기
    _promptedInZone = true
    RequestExitToSurface()

OnTriggerExit2D(other):
    if !other.CompareTag("Player") → return
    _promptedInZone = false
```

**`OnTriggerEnter2D`가 아니라 `OnTriggerStay2D`인 이유**
인벤토리 등 다른 UI가 열린 채로 트리거에 진입하면 확인창이 겹친다. `Enter`는 1회성이라 그 프레임을 UI 가드로 막으면 프롬프트가 영영 뜨지 않는다. `Stay`를 쓰면 UI를 닫은 다음 프레임에 자연스럽게 뜬다.

**유지 대상 (변경 없음)**

- `RequestExitToSurface()` — 확인 시 `ConfirmExit()`, 취소 시 콜백 없음(`_promptedInZone`은 `true`로 남아 재표시되지 않음)
  - `ConfirmExit()`는 신규 private 메서드로, `_isExiting = true`를 세운 뒤 `PrepareSettlement()`를 호출한다.
    씬 로드가 시작되기 전 `OnTriggerStay2D`가 다시 발동하는 것을 막는다 (기존 코드에는 이 가드가 없었다)
- `ExecuteExit()` — 엘리베이터 경로([ElevatorUI.cs:186](../Scripts/UI/Interaction/Elevator/ElevatorUI.cs)) 전용, 확인창 없이 즉시 이동.
  단 내부의 `SetHintVisible(false)` 호출 한 줄은 함께 제거해야 컴파일된다 (홀드 UI가 사라지므로)
- `PrepareSettlement()`, `ExitToSurface()`

**`_isPlayerInZone` 필드**
홀드 로직 제거 후 사용처가 `_promptedInZone`으로 대체되므로 함께 제거한다.

### 4.3 `CameraFollow` — 변경 없음

`minX/maxX/minY/maxY`와 `HandleManualCameraMove()`는 시네머신이 없는 씬을 위한 폴백이므로 그대로 둔다. 이번 작업 범위 밖.

---

## 5. 데이터 흐름

```
[매 프레임]
CinemachineFollow (Body)
    → ... 파이프라인 ...
    → CameraCeilingExtension (Finalize)
        state.PositionCorrection.y -= 초과분
    → 최종 카메라 위치

[플레이어가 ExitTrigger 진입]
ExploreExitController.OnTriggerStay2D
    → RequestExitToSurface()
        → ConfirmationPrompt.Show("탐험 종료", "정말 ...올라가시겠습니까?", PrepareSettlement, null)
            ├ 확인 → PrepareSettlement()
            │           → SettlementManager.StopTracking()
            │           → DayCycleManager.SetAfternoon()
            │           → StaminaManager 회복 처리
            │           → SaveManager.MergeInventoriesToWarehouse()
            │           → SceneLoader.LoadSettlementScene("DemoUpground")
            └ 취소 → 아무것도 안 함 (_promptedInZone = true 유지)
                      → 트리거를 벗어나야 OnTriggerExit2D에서 false로 리셋
```

---

## 6. 엣지 케이스

| 상황 | 처리 |
|------|------|
| 트리거 안에서 취소 후 계속 서 있음 | `_promptedInZone == true` → 재표시 안 됨 |
| 취소 후 아래로 내려갔다 다시 올라옴 | `OnTriggerExit2D`에서 리셋 → 재표시 |
| 다른 UI(인벤토리 등)가 열린 채 트리거 진입 | 가드로 스킵. UI를 닫으면 다음 `Stay` 프레임에 표시 |
| 확인 후 씬 로드 중 트리거 재발동 | `_isExiting` 가드 (`RequestExitToSurface` 첫 줄) |
| 드릴 대시/UI 줌으로 orthoSize 변동 | 매 프레임 `state.Lens.OrthographicSize`를 읽어 천장선 고정 |
| 반중력 Dutch 180° 롤 | 처리 불필요 (§4.1) |
| 던전 진입 (y=+10000) | `IsInDungeon` 가드로 클램프 스킵 (§4.1.1) |
| 던전 안에서 `ExitTrigger` 발동 | 던전은 별도 프리팹이라 지하 씬의 `ExitTrigger`와 좌표가 1만 유닛 떨어져 있음 — 접촉 불가 |
| 카메라가 원근(perspective) 모드 | 클램프 스킵 — 이 프로젝트는 항상 직교라 실제로는 발생하지 않음 |
| 취소한 플레이어가 화면 밖(상한선 위)에 서 있음 | **씬 배치로 해결** — 트리거를 `[ceilingY - orthoSize, ceilingY]` 띠 안에 둔다 (§7) |
| 트리거가 `ceilingY`보다 위에 있음 | **확인창이 영영 안 뜬다.** 도달 경로 전체가 화면 밖 (§7의 실패 사례) |

---

## 7. 씬 작업 (사람이 직접 수행)

1. `DemoUnderground`의 `CinemachineCamera` 오브젝트에 `CameraCeilingExtension` 컴포넌트 부착
2. `ceilingY`를 지하 맵 천장 높이에 맞춰 설정
3. `ExitTrigger` 오브젝트를 **`[ceilingY - orthoSize, ceilingY]` 구간 안**에 배치

   > **주의 — `ceilingY`보다 위에 두면 안 된다.**
   > 화면 위쪽 가장자리가 곧 `ceilingY`이므로, `ceilingY` 위쪽은 정의상 전부 화면 밖이다.
   > 트리거를 그 위에 두면 플레이어가 화면에서 완전히 사라진 채로 올라가야 닿는다 → 사실상 도달 불가.

   "카메라는 멈췄는데 플레이어만 화면 위로 계속 올라가는" 연출이 성립하는 구간은
   **카메라 중심 상한(`ceilingY - orthoSize`) ~ 화면 상단(`ceilingY`)** 사이의 띠뿐이다.
   트리거는 이 띠 안, 위쪽에 가깝게 두는 것이 좋다.

   `orthoSize`는 `CameraFollow.normalOrthoSize`(현재 `DemoUnderground` 기준 **1.8**)를 쓴다.
   드릴 대시 중에는 더 작아지므로(`drillOrthoSize`) 띠가 좁아지는 쪽이라 안전 방향이다.

   **실측 예시 (2026-07-25 최초 세팅 시 실패 사례)**
   `ceilingY = 10`, `orthoSize = 1.8` → 유효 띠는 **Y 8.2 ~ 10**.
   그런데 `ExitTrigger`가 월드 Y **13~15**(= `World`(y=10.018) + local(y=3.982), 콜라이더 20×2)에 있어
   화면 꼭대기보다 3유닛 위 → 확인창이 한 번도 뜨지 않았다.

   **현재 세팅 (2026-07-28)**
   지표면 기준은 **y = 10**. 이에 맞춰 `DemoUnderground`의 값을 정리했다.

   | 항목 | 이전 | 현재 |
   |------|------|------|
   | `CameraCeilingExtension.ceilingY` | 10 | 10 (변경 없음) |
   | `ExitTrigger` local Y (`World` y=10.018 기준) | 3.982 (월드 13~15 — §7 실패 사례) | **0.482** (월드 **9.5~11.5**) |
   | `UndergroundMinimap.surfaceWorldY` | 0 (씬 값이 미갱신 상태였음) | **10** |
   | `WorldMapOverlay.surfaceWorldY` | 10 (코드 기본값, 런타임 생성) | 10 (변경 없음) |

   `orthoSize = 1.8` 기준 유효 띠는 **Y 8.2 ~ 10**이고, 트리거 콜라이더(20×2)의 **아랫변이 월드 Y 9.5**이므로
   플레이어가 화면 안에 있는 동안(9.5 ≤ 10) 확실히 접촉한다.

   **발동 깊이는 콜라이더 아랫변으로 정해진다.**
   미니맵 깊이 표기가 `surfaceWorldY - playerY`이므로, 아랫변 월드 Y가 `surfaceWorldY - d`일 때 깊이 `d`m에서 발동한다.
   현재 `10 - 9.5 = 0.5` → **지하 0.5m**. 발동 깊이만 바꾸려면 콜라이더 크기는 두고 트리거를 위아래로 옮기면 된다.
   윗변(11.5)이 `ceilingY`를 넘는 것은 무해하다 — 도달성은 아랫변만 결정한다.

   > 지형 높이를 옮기면 위 4개 값을 **같은 양만큼** 함께 옮겨야 한다.
   > 넷 중 하나라도 빠지면 깊이 표시(0m 기준)나 확인창 도달성이 어긋난다.
4. 인스펙터의 `holdDuration` / `holdHintUI` / `holdProgressImage` 필드는 코드에서 사라진다 (이미 미연결이라 손실 없음)

---

## 8. 테스트

`Assets/Scripts/Utils/Tests/` 하위에 EditMode 테스트를 작성한다.
**Unity Test Runner 실행은 사람이 직접 수행한다** (프로젝트 규칙).

`CameraCeilingExtension.ClampCenterY` 검증 케이스:

| 케이스 | 입력 (currentY, ceilingY, halfHeight) | 기대값 |
|--------|----------------------------------------|--------|
| 상한 아래 — 통과 | `(-10, 0, 2.5)` | `-10` |
| 상한 초과 — 클램프 | `(5, 0, 2.5)` | `-2.5` |
| 경계값 — 그대로 | `(-2.5, 0, 2.5)` | `-2.5` |
| 줌 인(halfHeight 축소) | `(5, 0, 1.8)` | `-1.8` |
| 음수 ceilingY | `(0, -20, 2.5)` | `-22.5` |

`ExploreExitController`의 트리거 상태 전이는 `MonoBehaviour` + 물리 의존이라 EditMode 테스트 대상에서 제외하고, 씬에서 수동 확인한다.

**수동 확인 항목**

1. 상한선까지 올라가면 카메라가 멈추고 플레이어만 화면 위로 올라간다
2. 드릴 대시 중(줌 인) 천장선이 흔들리지 않는다
3. 트리거 진입 → 확인창 표시 → 확인 → 지상 씬 이동
4. 취소 → 재표시 안 됨 → 내려갔다 올라오면 재표시
5. 엘리베이터 exit 경로는 기존과 동일하게 동작
6. **던전 진입 → 카메라가 던전을 정상적으로 비춘다** (천장 클램프에 걸려 화면이 처박히지 않음)
7. 던전 이탈 → 지하로 복귀하면 천장 클램프가 다시 동작

---

## 9. 범위 밖

- `CameraFollow`의 dead code 정리
- 다른 지하 씬(`CopyDemoUnderground` 등) 적용 — 필요 시 동일 절차 반복
- 카메라 X축 제한
- 확인창 문구 로컬라이제이션 (현재 하드코딩 유지, 엘리베이터 경로와 동일 수준)
