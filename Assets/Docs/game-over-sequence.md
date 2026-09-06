# 게임오버 연출 (GameOverSequenceUI)

지하에서 판이 끝나는 **모든 경로**가 공유하는 단 하나의 시네마틱.
로딩씬으로 넘어가기 직전에 딱 한 번 재생한다.

```
1. HUD 페이드아웃            0.75s   화면의 UI가 사라진다
2. 주변이 서서히 어두워짐     2.40s   끝에는 검은 배경에 플레이어만 남는다 (0.40s 지연 후 시작)
3. 죽는 모션                 2.60s   Animator "Die" 스테이트 재생
4. 완전 암전                 0.95s   → 로딩씬
5. 지상 씬 도착
6. 정산창 (긴급탈출·사망 모두 — 제목/부제만 사유에 따라 갈린다)
                     ────────────
                     전체 ≈ 6.5초 (시작 1.0초 뒤부터 아무 키로 스킵 가능)
```

타이밍은 `GameOverSequenceUI` 상단의 const 한 뭉치(`HudFadeDur` … `SkipGrace`)에 모여 있다.
2단계(`DarkenDur`)가 이 연출의 중심이라 가장 길게 잡았다.
`DieDur`는 **3.0초를 넘기면 안 된다** — `Die.anim`이 3초 루프라 다시 처음으로 돌아간다(실동작은 ≈1.55초에서 끝나고 3초까지 정지 유지).

전부 코드로 생성한다 — **씬/프리팹 세팅 불필요**. 다른 코드 오버레이와 같은 `CodeUI` 키트를 쓴다.

| 파일 | 역할 |
|------|------|
| `Assets/Scripts/UI/GameOver/GameOverSequenceUI.cs` | 연출 본체 (+ `GameOverReason` enum) |
| `Assets/Scripts/Gameplay/GameOverHandler.cs` | 사망 경로 — 스태미나 고갈 감지 → 연출 → 짐 페널티(창고 부분 저장) → 지상 |
| `Assets/Scripts/_Core/Managers/CarryLossPenalty.cs` | 사망·탈출 공용 짐 페널티(가장 비싼 1~2개만 챙김) |
| `Assets/Scripts/UI/Pause/PauseOverlayUI.cs` | 긴급탈출 경로 — `ExecuteEmergencyEscape`에서 같은 연출 호출 |

---

## 진입 경로 두 개

```csharp
// 사망 (GameOverHandler.TriggerGameOver ← PlayerStat.OnStaminaDepleted)
GameOverSequenceUI.Play(GameOverReason.Death, player, () => SceneLoader.LoadScene("DemoUpground"));

// 긴급탈출 (PauseOverlayUI.ExecuteEmergencyEscape)
GameOverSequenceUI.Play(GameOverReason.EmergencyEscape, () => SceneLoader.LoadScene(upground));
```

연출은 **똑같다**. 갈리는 건 앞뒤 처리뿐이다.

| | 사망 | 긴급탈출 |
|---|---|---|
| 연출 전 | — | `SaveManager.MergeInventoriesToWarehouse()` (창고 병합 + `CarryLossPenalty`, `EmergencyEscapeReport` 기록) |
| 연출 중 | `CarryLossPenalty` → 살아남은 광물만 창고로 + `SaveManager.PersistWarehouseOnly()`, 소비아이템 소실 (**전체 저장은 안 함**) | — |
| 지상 도착 후 | `UIStateManager`가 `EmergencyEscapeReport.HasPending` 보고 `EmergencyEscapeOverlayUI` 오픈 (사망 문구) | 같음 (탈출 문구) |

### 짐 페널티 규칙 — 두 경로 공통 (`CarryLossPenalty`)

**가장 비싼 광물 1~2개만 챙기고 나머지는 전부 잃는다.** '비싸다'의 기준은
`MineralPriceDatabase.GetBasePrice`(판매가 업그레이드를 뺀 기저 가격 — 안 그러면 그 노드를 산
광물이 더 자주 살아남는 되먹임이 생긴다). 챙기는 개수는 매판 `[1, 2]`에서 뽑는다.

예전 규칙(무작위 60% 삭제)은 많이 캘수록 많이 남아 실패해도 한 탕이 됐다. 지금은 성과 크기와
무관하게 남는 게 1~2개로 고정되므로 "제때 걸어 올라오는 것"이 항상 이득이다.

사망이 창고를 **부분 저장**하는 이유: 지하 진행을 통째로 저장하면 지하에서의 스탯·위치·시간이
파일에 굳는다(강제 종료와 동일 처리라는 규칙이 깨진다). 그래서 도감(`PersistCodexOnly`)과 같은
방식으로 `warehouseData` 필드만 덧쓴다.

`Play`는 재진입을 막는다(`IsPlaying`) — 탈출 확정 직후 스태미나가 0이 돼도 연출이 두 번 겹치지 않는다.
`GameOverHandler.TriggerGameOver`도 `GameOverSequenceUI.IsPlaying`을 먼저 확인해 탈출 흐름을 존중한다.

---

## 씬 세팅

**없다.** `GameOverHandler`는 `AutoAttachScenes`(기본 `{ "DemoUnderground" }`)에 등록된 씬이 로드될 때
`PlayerStat`을 가진 오브젝트에 자동으로 붙는다. 직접 붙여 뒀다면 자동 부착은 건너뛴다.

다른 지하 씬을 추가했다면 배열에 이름만 넣으면 된다:

```csharp
GameOverHandler.AutoAttachScenes = new[] { "DemoUnderground", "DemoUnderground2" };
```

인스펙터에서 조정할 수 있는 값(직접 붙였을 때만):

| 필드 | 기본값 | 설명 |
|------|--------|------|
| Player Stat / Mineral Inventory | 비움 | 비워두면 Awake에서 자동 탐색 |
| Mineral Burst VFX | 비움 | 있으면 사망 순간 재생, 없으면 생략 |
| Burst Duration | 1.0 | 가방을 비울 때까지 대기 (HUD가 사라진 뒤 수치가 0이 되도록) |
| Target Scene | `DemoUpground` | 사망 후 돌아갈 지상 씬 |

---

## 설계 메모

### 0. "플레이어만 남기기"는 원형 마스크로 불가능하다 — 그리기 순서로 푼다

이게 이 연출의 핵심이고, 원형 스포트라이트만 계속 좁히다가 오래 헤맨 부분이다.

**구멍을 아무리 좁혀도 구멍 안에는 배경이 보인다.** 플레이어 뒤의 땅·바위가 같이 남는다.
"검은 배경에 플레이어만"을 만들려면 가리는 단위가 **원이 아니라 실루엣**이어야 한다.

그래서 마스크 대신 **그리기 순서**로 푼다:

```
월드(지형·광물·시야 오버레이·손전등)  →  [검은 장막]  →  플레이어 스프라이트
```

- `BuildVeil()` — 검은 `SpriteRenderer`를 최상위 정렬 레이어 `VeilOrder`(20000)에 깐다.
  시야 어둠 캔버스(999)·손전등 메시(900)·광물 스파클(1000+)보다 확실히 위다.
- `RaisePlayerAboveVeil()` — 플레이어 스프라이트를 그 **위**로 올린다.
  서로의 앞뒤(머리/몸/팔)가 뒤섞이면 안 되므로 `VeilOrder + 1 + 원래레이어값*100 + 원래순서`로
  상대 순서를 보존한다. (광물 스파클이 어둠 캔버스 위로 올라가는 것과 같은 방식)
- 장막 알파를 0 → 1로 올리면 **배경만 정확히 지워지고 플레이어는 그대로 남는다.**

부수 효과가 크다 — **시야 오버레이·손전등을 따로 건드릴 필요가 없어진다.** 둘 다 장막 아래라 같이 덮인다.
아래 3번의 조명계 규칙들은 이제 "왜 건드리면 안 되는지"의 기록으로만 남아 있다.

스포트라이트(1번)는 그대로 두되 **전환 중 비네트 역할만** 한다. 배경을 지우는 건 장막이 하므로
구멍은 캐릭터가 절대 안 잘릴 만큼 넉넉히(`SpotEndScale` 1.6) 잡는다.

### 1. 스포트라이트 — RawImage `uvRect` 하나로 끝낸다

"플레이어만 빼고 어두워진다"는 보통 셰이더나 스텐실 마스크로 하는데, 여기선 **화면 전체를 덮은
`RawImage`의 `uvRect`만 매 프레임 갱신**한다.

- 텍스처: 가운데가 뚫린 방사형 알파 마스크 (uv 반경 `HoleUV`=0.13까지 0 → `EdgeUV`=0.20부터 1).
  텍스처 가장자리(반경 0.5~0.707)는 전부 알파 1.
- `wrapMode = Clamp` → uv가 [0,1] 밖으로 나가면 가장자리 텍셀(=불투명)이 그대로 늘어난다.
  **플레이어가 화면 어디에 있든 구멍 바깥은 빈틈없이 덮인다.** 리사이즈·해상도 대응이 따로 필요 없다.
- 구멍 크기 = `uvRect` 확대/축소, 구멍 위치 = `uvRect` 이동. 텍스처는 한 번만 굽고 정적 캐시.

```
uw = HoleUV * Screen.width  / holePx      // x/y의 '픽셀당 uv'가 같아야 타원이 아닌 원
uh = HoleUV * Screen.height / holePx
uvRect = (0.5 - n.x*uw, 0.5 - n.y*uh, uw, uh)   // n = 플레이어의 화면 위치(0~1)
```

#### 구멍 크기 — 화면 비율이 아니라 **플레이어 실제 화면 크기** 기준

"플레이어 빼고 전부 까맣게"를 만들려면 두 값이 같이 맞아야 한다. 둘 다 한 번씩 틀렸다.

- **끝 구멍 크기**: 시작만 화면 비율(`SpotStartFrac` 0.80 — 넓어서 아무것도 안 가림)이고,
  끝은 `MeasurePlayerOnScreen`이 잰 **스프라이트 바운즈의 화면 반경 × `SpotEndScale`(1.6)** 이다.
  화면 높이 비율로 고정하면 카메라 줌·해상도가 달라졌을 때 구멍이 캐릭터보다 커져 지형이 그대로 보인다.
- **falloff 폭**: `EdgeUV / HoleUV` 비가 곧 *완전 검정이 시작되는 거리 ÷ 구멍 반경*이다.
  이게 **3.3배**(0.30/0.09)였을 땐 구멍을 아무리 좁혀도 플레이어 반경 3.3배까지 지형이 비쳐
  주변이 훤히 보였다. 지금은 **1.5배**(0.20/0.13) — 구멍 바로 바깥부터 검정으로 떨어진다.

중심도 `transform.position`이 아니라 **스프라이트 바운즈 중심**을 쓴다. 2D 캐릭터 피벗은 보통 발밑이라
구멍을 좁히는 순간 머리가 잘린다. Die로 눕는 동안 바운즈가 변하므로 매 프레임 다시 잰다.

더 조이고 싶으면 `SpotEndScale`(1.6)을, falloff를 더 급하게 하려면 `EdgeUV`를 `HoleUV` 쪽으로 낮춘다.
다만 배경을 지우는 건 0번의 장막이므로, 여기선 캐릭터가 잘리지 않을 만큼 넉넉한 편이 안전하다.

### 2. HUD 페이드는 인스펙터 연결 없이 캔버스를 긁는다

씬의 **루트 캔버스를 전부 모아** `CanvasGroup`을 붙이고 알파를 내린다.
제외 대상은 두 가지뿐:
- 자기 연출 캔버스
- `PlayerVisionOverlay`의 `VisionOverlayCanvas` — HUD가 아니라 게임 화면의 일부다.
  어차피 0번의 검은 장막 아래라 같이 덮이므로 따로 지울 필요가 없다.

### 3. (기록) 조명계를 직접 건드리려다 밟은 지뢰 세 개

> 0번의 장막 방식으로 바꾸면서 **아래 조작은 전부 제거했다.** 다시 손대고 싶어질 때를 위한 기록이다.


`PlayerVisionOverlay`는 **시야 밖을 어둡게 덮는 막**이다. 밝히는 쪽이 아니라 가리는 쪽이다.
연출 중 밝아지는 순간이 한 번이라도 있으면 "평소 화면 → 그대로 어두워짐"이 깨지고 맵이 번쩍 드러난다.
스포트라이트는 `SpotStartFrac` 0.80으로 넓게 시작하므로 초반의 밝아짐을 전혀 가려 주지 못한다.

당시 `FadePlayerLighting(k)`가 지키려던 규칙 — **셋 다 실제로 한 번씩 틀렸던 것들이다**:

1. **바깥 어둠을 내리지 않는다.** 0으로 '끄면' 덮개가 벗겨져 로드된 갱도 전체가 드러난다.
   올리기만 한다: `SetDarknessOverride(Lerp(darknessAlpha, 1f, k))`.
2. **시야 반경을 건드리지 않는다.** 씬 값이 작다(프리팹 실측 `visionRadiusTiles = 0.45`,
   `darknessAlpha = 0.97`, `falloffTiles = 0.3` — 2026-08-22 초반 시야 축소 이전엔 0.6/0.4).
   절대값 목표를 주면 좁히는 게 아니라 **넓히는** 꼴이 된다
   — 0.6 → 2.5로 잡았다가 4배로 벌어져 맵이 다 드러났다.
   조이는 일은 화면 공간 스포트라이트가 전담한다(어떤 씬 값에도 안전).
3. **끝에서 컴포넌트를 끄지 않는다.** 끄는 순간 어둠이 사라져 맵이 다시 드러난다. 어둠 1인 채로 계속 돌린다.

손전등만 예외로 정리한다. 원뿔은 어둠을 **뚫어** 앞을 밝히므로 그대로 두면 밝은 부채꼴이 끝까지 남는다.
`maxLightDistance`를 0으로 줄여 서서히 꺼뜨리고 `k == 1`에서 컴포넌트를 끈다
(`IsActive => enabled && ...` 라 컨트롤러 하나로 `FlashlightFOVGraphic`·`FOVBuilder`가 같이 멈춘다).
**시작 시점에 끄면 안 된다** — 빔이 툭 사라져 "평소 화면 그대로"가 깨진다.

결론: 이 방향은 **원 안에 배경이 남는 한계** 때문에 애초에 목표에 도달할 수 없었다. 0번이 정답이다.

### 4. Die 모션이 즉시 끊기는 문제

`Player.controller` Base Layer에는 `isGrounded == false`인 **AnyState 전이**가 있다.
공중에서 죽으면 `Play("Die")` 직후 낙하 모션에 잡아먹힌다. 그래서 재생 전에:

- 모든 Animator 파라미터를 중립값으로 리셋 (트리거는 `ResetTrigger`)
- `isGrounded`만 **true로 고정** → 공중 판정 AnyState 전이가 성립하지 않는다
- Leg Layer(마스크 레이어) 가중치를 0으로 → 다리가 Die 포즈를 덮어쓰지 않는다
- 2D IK(`IKManager2D`)가 있으면 끈다 → LateUpdate에서 다리 본을 타깃으로 되돌리는 걸 막는다
  (패키지 어셈블리 참조 없이 **타입 이름으로** 찾는다. 없으면 아무 일도 안 한다)

`Die` 스테이트는 전이가 없는 고립 상태라 한 번 들어가면 그대로 머문다.

**루프 주의** — `Die.anim`은 `LoopTime`이 켜져 있어서 3.0초에 처음(선 자세)으로 되돌아간다.
`DieDur`만 3초 아래로 잡는 걸로는 부족하다. 그 뒤에 암전(`BlackoutDur` 0.95초)이 더 붙어
**암전이 끝나기 전에 시체가 벌떡 일어난다.** 그래서 `FreezeDieMotionIfDone`이
`DieFreezeAt`(2.0초, 클립이 더 짧으면 `길이 - 0.05`)에서 `Animator.speed = 0`으로 세운다.

실동작이 ≈1.53초에 끝나고 3.0초까지 같은 포즈를 유지하는 구간이라 2.0초에 얼려도 티가 안 난다.
`enabled = false`가 아니라 `speed = 0`인 이유는, 마지막 포즈가 매 프레임 계속 적용되게 두기 위해서다.

### 5. timeScale은 1로 되돌린다

긴급탈출은 일시정지 메뉴(`timeScale = 0`)에서 들어온다. 그 상태로는 **Animator가 안 돌아 Die가 재생되지 않는다.**
연출 시작 시 `Time.timeScale = 1f`로 강제하고, 연출 자체는 `unscaledDeltaTime`으로 진행해
어디서 불려도 흐름이 같게 만든다. (구 `EmergencyEscapeSequenceUI`는 반대로 `timeScale = 0`으로 얼렸다)

조작이 꺼진 뒤 시체가 미끄러지지 않도록 `Rigidbody2D`의 **가로 속도만** 매 프레임 죽인다
(중력은 그대로 둬서 공중에서 죽으면 바닥에 눕는다).

### 6. 정렬 순서

`32050` — 설정(31000)·정산(30820) 위, 로딩씬 캔버스(32767)보단 아래.
연출 끝의 완전 암전을 로딩씬이 자연스럽게 이어받는다.

---

## 참고

- **SFX**: `SoundDataSO`에 `game_over`(사망) / `emergency_escape`(탈출)를 등록하면 시작 순간 재생된다. 없으면 무음.
- **스킵**: 시작 1.0초(`SkipGrace`) 뒤부터 아무 키/클릭으로 건너뛴다(짧은 암전으로 바로 넘어감).
- **단축키 차단**: 재생 중 `UIStateManager`가 `GameOverSequenceUI.IsPlaying`으로 ESC/M/J/Tab을 전부 막는다.
- 구 `EmergencyEscapeSequenceUI`(화면 흔들림 + 붉은 경보 + "긴급 탈출" 타이틀 슬램)는 **더 이상 호출되지 않는다.**
  파일과 `UIStateManager`의 `IsPlaying` 체크만 남아 있다.
