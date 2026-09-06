# 청크 이음매 통과 감시기 (TerrainSeamWatchdog)

작성: 2026-08-22

## 왜 만들었나

버그 리포트 `2026-08-22_171015`:

> 땅을 뚫고 떨어졌음. 청크의 모서리 경계 부근이였고 거기서 땅을 파니 떨여졌음

리포트 시점 플레이어 좌표 `(-10.02, -9.48)` = 청크 `(-2,-1)` 안, **오른쪽 경계에서 2px /
아래쪽 경계에서 52px**. 즉 `(-2,-1)/(-1,-1)/(-2,-2)/(-1,-2)` 네 청크가 만나는
꼭짓점 `(-10,-10)` 바로 위였다. 로그에는 콜라이더 관련 에러가 한 줄도 없었다.

사용자 확인 결과 **흙은 그대로인데 몸만 통과**했다. 그러면 원인은 지형 삭제가 아니라
**"지형 픽셀은 solid인데 그 자리를 덮는 콜라이더가 없다"** 하나로 좁혀진다.

문제는 이 버그가 **사후 조사가 원천적으로 불가능**하다는 것이다:

- 지형은 세이브에 남지 않는다 (`worldData.dat`는 637바이트짜리 유물이고 청크 픽셀을 담지 않는다)
- 콜라이더는 파기할 때마다 통째로 다시 그려진다 → 사고 당시 모양이 다음 갱신에 사라진다
- 재현이 안 된다. 처음 발견 후 비슷한 상황이 몇 번 더 있었지만 매번 확인에 실패했다

그래서 "터진 뒤에 조사"가 아니라 **상시로 얇게 관찰**하는 쪽으로 갔다.

## 구조

두 층이다.

### 1층 — `TerrainCollider` 자기 진단

[TerrainCollider.cs](../../Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainCollider.cs)

`UpdateCollider()`가 끝날 때마다 자기 결과를 기록한다 (`#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT`).

| 값 | 의미 |
|---|---|
| `LastPathCount` | 이번에 만든 path 수 |
| `LastFailedTraces` | **닫히지 않은 윤곽 수. 0이 정상** |
| `LastFailKind` | 2=OPEN(시작점 복귀 실패), 3=OVERFLOW(maxLoops) |
| `LastFailStart` | 첫 실패 윤곽의 시작 픽셀 |
| `LastUpdateTime` / `LastUpdateFrame` / `UpdateCount` | 갱신 이력 |

`TraceOutlineUnsafe`가 false를 반환하면 그 solid 영역은 **콜라이더 경로를 하나도 못 얻는다**.
= "흙은 있는데 못 밟는다"의 직접적인 기계적 원인이다. 다만 기존 코드는 실패 사유 두 가지를
구분하지 않고 똑같이 false를 뱉었다:

- **TRACE_TINY** — 닫혔지만 점이 3개 미만. 1~2픽셀 부스러기라 무해하고 **매우 흔하다**
- **TRACE_OPEN / TRACE_OVERFLOW** — 진짜 사고

그래서 상태 코드를 나눠 TINY는 세지 않는다. 이걸 안 나누면 파기할 때마다 경보가 울려
아무도 안 본다.

`OnAnomaly` static 이벤트로 두 경우를 신고한다:
- 닫히지 않은 윤곽이 1개 이상
- `pathCount == 0`인데 지형 픽셀이 남아 있음(8픽셀 격자 샘플링) = 청크 전체가 콜라이더 없음

### 2층 — `TerrainSeamWatchdog`

[TerrainSeamWatchdog.cs](../../Scripts/Utils/Diagnostics/TerrainSeamWatchdog.cs)

씬 배치 불필요 — `RuntimeInitializeOnLoadMethod`로 자동 생성, `DontDestroyOnLoad`.

탐지기 4종:

| 이름 | 조건 | 심각 |
|---|---|---|
| **EMBEDDED** | 플레이어 중심 픽셀이 solid가 **2 물리스텝 연속** | O |
| **FALLTHRU** | 한 스텝의 **발 스윕**이 solid 슬랩을 세로로 지나 그 아래 빈 공간으로 나옴 | O |
| **TUNNEL** | 한 `FixedUpdate`에 0.5u 넘게 이동하면서 청크 경계선을 넘음 | O |
| **NOCOVER** | 확실히 지형 안쪽인 점을 그 청크 콜라이더가 안 덮음 | O |
| **TRACE** | 1층 `OnAnomaly` | X |

심각(O)이면 세션당 3번까지 F12 리포트를 자동으로 띄운다(`AutoCapture`).

#### EMBEDDED가 2스텝 연속일 때만 걸리는 이유

파기 직후 1스텝은 플레이어 중심이 정상적으로 지형과 겹칠 수 있다(픽셀이 지워지고
콜라이더가 갱신되기까지 최대 `colliderUpdateInterval`=0.2s의 틈이 설계상 존재한다).
1스텝으로 잡으면 삽질할 때마다 울린다.

#### NOCOVER의 오탐을 막는 두 가지 제외

1. **표면에서 3px 이상 안쪽인 점만 본다** (`DeepMarginPx`).
   콜라이더 윤곽은 픽셀 **중심**을 잇고, 그 위에 RDP 단순화가 최대 0.5px 더 안쪽으로 깎는다.
   그래서 표면 1~2px은 원래 안 덮이는 게 정상이다.

2. **청크 가장자리 3px 이내는 뺀다** (`EdgeExemptPx`).
   윤곽이 픽셀 중심을 잇는 결과로, 이웃한 두 청크 콜라이더 사이에는
   **구조적으로 1픽셀(0.01u) 틈**이 항상 있다. 청크 A의 오른쪽 끝 정점은 `x=+9.995`,
   청크 B의 왼쪽 끝 정점은 `x=+0.005`다. 이건 버그가 아니라 현재 설계의 성질이므로
   경보로 만들면 안 되고, 대신 **덤프에서 실측**한다(아래 이음매 스캔).

3. `HasIndestructiblePixels`인 청크는 통째로 제외한다 — 불괴 오버레이 픽셀은
   **설계상** 지형 콜라이더에서 빠지고 오버레이 자체 콜라이더가 담당한다.

## 덤프에 뭐가 찍히나

`Debug.LogError`로 나가므로 `BugReportLogBuffer`가 물고, F12 리포트의 `log.txt`에 그대로 실린다.

1. **플레이어** — 좌표, 청크, 로컬 픽셀, **네 가장자리까지 픽셀 거리**, 중심픽셀 solid 여부,
   grounded, `Rigidbody2D`의 bodyType / `collisionDetectionMode` / interpolation / simulated / sleep,
   자식 콜라이더 전부의 enabled·trigger·bounds
2. **주변 3×3 청크 표** — 로드/active/콜라이더 enabled/`pathCount`/정점 수/`isColliderDirty`/
   마지막 갱신 후 경과 초/실패 윤곽 수/플레이어 좌표를 덮는지
3. **지형 vs 콜라이더 지도** — 21×21 격자(5px 간격, ±0.5u).
   `#`=지형, `.`=공기, `+`=콜라이더 안, `!`=**지형인데 콜라이더 없음**, `P`=플레이어.
   `!`가 찍히는 자리가 곧 통과 지점이다.
4. **이음매 스캔** — 가장 가까운 청크 경계를 가로질러 1픽셀 간격 ±20px로
   지형 줄과 콜라이더 줄을 나란히 찍는다. 실제 틈이 몇 픽셀인지 눈으로 센다.
5. **직전 40 물리스텝 궤적** — 좌표·속도·청크·grounded·중심 solid.
   "떨어지기 직전에 무슨 일이 있었나"가 여기 남는다.

F12를 눌렀을 때는 사고가 없어도 같은 덤프를 `Debug.Log`로 한 번 찍는다.
`BugReportCollector.Collect()`가 `log.txt` 스냅샷보다 먼저 돌기 때문에 리포트에 실린다.
`report.json`의 `extra`에는 한 줄 요약(`seamWatchdog`, `seamWatchdogLastEvent`)만 넣는다 —
여러 줄 문자열을 json에 넣으면 통째로 이스케이프되어 사람이 못 읽는다.

## 콘솔 명령

```
seam            현황 (on/off, 탐침, 자동리포트, 사고 건수, 마지막 사고)
seam on|off     감시기 전체
seam probe      NOCOVER 격자 탐침 토글 (부하가 느껴지면 끈다)
seam auto       사고 시 자동 F12 토글
seam dump       지금 상태를 즉시 덤프
```

## 비용

- `FixedUpdate`마다: 픽셀 조회 1회 + 링버퍼 쓰기. 무시할 수준
- 0.2초마다 NOCOVER 탐침: 21×21=441점. 대부분 공기라 조기 탈락하고,
  `OverlapPoint`는 "확실히 안쪽인 solid" 점에서만 호출된다
- 덤프는 사고 시에만. 1.5초 쿨다운으로 도배 방지
- 전부 `#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT` — 릴리스 빌드에서 사라진다

## 조사 중 같이 발견한 것 (아직 안 고침)

### `PreMarkBoundaryVisited`는 되살리면 안 된다

[TerrainCollider.cs](../../Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainCollider.cs)의
`PreMarkBoundaryVisited`는 "이웃 청크가 solid인 경계 픽셀"을 미리 visited로 찍어
윤곽선이 청크 엣지를 따라 꺾이는 아티팩트를 없애려는 코드다.

그런데 윤곽 탐색 시작점 판정이 `isLeftEdge = (x == 0) || 왼쪽이 빈칸`이다.
지하에서는 왼쪽 이웃이 solid인 게 보통이므로, 이게 켜지면 **`x=0` 열이 통째로 visited가 되어
그 청크는 시작점을 하나도 못 찾고 `pathCount = 0`** 이 될 수 있다. 청크 전체가 콜라이더를 잃는다.

현재는 `SetNeighborQuery`를 부르는 곳이 `TerrainChunk.cs.private.0`(컴파일 대상이 아닌 백업본)
뿐이라 `_isNeighborSolid == null` → 함수가 즉시 반환하는 **사문화 상태**다. 그래서 지금 버그의
원인은 아니다. 되살릴 거면 시작점 판정부터 같이 고쳐야 한다.

### 청크 경계의 1픽셀 콜라이더 틈

위에서 설명한 구조적 성질. 4청크 꼭짓점에서는 세로·가로 틈이 십자로 교차한다.
플레이어 캡슐보다 훨씬 작아서 그 자체로 통과 원인이 되긴 어렵지만,
이번 버그가 정확히 그 꼭짓점에서 났으므로 **실측값을 남기려고** 이음매 스캔을 넣었다.
감시기가 실제 틈을 몇 픽셀로 재는지가 첫 번째 확인 대상이다.

## 다음에 사고가 잡히면 볼 순서

1. `log.txt`에서 `[SeamWatchdog] ===== ` 검색
2. **지형 vs 콜라이더 지도**에 `!`가 있나 → 있으면 콜라이더 구멍 확정
3. **3×3 청크 표**에서 그 청크의 `paths`, `실패윤곽`, `갱신후(s)`를 본다
   - `실패윤곽 > 0` → `TraceOutlineUnsafe`가 범인. 시작 픽셀 좌표가 1층 로그에 있다
   - `paths=0` → 청크 전체 소실
   - `갱신후(s)`가 크다 → 갱신이 아예 안 걸린 것. `colDirty`가 true인데 안 갱신됐는지 확인
   - `active=False` / `colEn=False` → 청크가 풀 반납/재사용 중이었다
4. **궤적**에서 떨어지기 직전 속도와 grounded 전이를 본다

---

## 오탐: 텔레포트가 TUNNEL로 잡혀 버그 리포트가 저절로 뜨던 문제 (2026-08-22)

**증상** — F12를 누르지 않았는데 버그 리포트 오버레이가 켜진다(세션당 최대 3회).

**원인** — `BugReportSystem.Capture()`를 F12 외에 부르는 곳은 이 감시기의
`Report(..., severe: true)` → `AutoCapture` 경로뿐이다. 그런데 TUNNEL 판정은
"한 스텝에 0.5u 이상 + 청크 경계 통과"만 봤다. 엘리베이터 층 이동·스폰·던전 출입은
전부 코드가 좌표를 대입하는 이동이라 이 조건을 항상 만족한다. 착지 보호로 Kinematic
고정된 동안에는 아직 로드 안 된 지형 속에 있으므로 EMBEDDED까지 같이 뜬다.

**수정** — 감시기 쪽에서 "물리 이동이 아닌 것"을 걸러낸다.

1. `Rigidbody2D.bodyType != Dynamic`이면 그 스텝은 통째로 건너뛴다(착지 보호 구간).
2. TUNNEL은 `IsScriptedMove(이동량, 속도, fixedDeltaTime)`를 통과해야 보고한다.
   Rigidbody2D는 한 스텝에 최대 `vel*dt`만큼 움직이므로, 그보다 크게 튀었으면
   물리 관통이 아니라 좌표 대입이다(`ScriptedMoveFactor`=2, `ScriptedMoveSlack`=0.25u).
3. Dynamic인 채로 좌표만 대입하는 경로(던전 출입·리셋)는 `SuppressFor()`를 직접 부른다.

억제 구간에서는 `_hasPrevPos`를 끊는다 — 안 끊으면 복귀 첫 스텝의 delta가
텔레포트 전 좌표 기준이라 그대로 다시 TUNNEL이 된다.

⚠ 실제 관통은 물리 속도로 일어나므로 `vel*dt` 범위 안이다 — 이 게이트가 진짜 사고를
가리지는 않는다. 회귀 테스트: `Assets/Tests/EditMode/TerrainSeamWatchdogScriptedMoveTests.cs`

---

## 오탐 2: 삽질 중 NOCOVER가 터져 버그 리포트가 저절로 뜨던 문제 (2026-08-22)

**증상** — 지하에서 삽으로 파는 도중(텔레포트 없음, 시간 배속 2.5×) 리포트 창이 켜짐.

**증거** — Editor.log 실측 덤프:

```
[SeamWatchdog] ===== NOCOVER =====
  (5.949,7.882)는 지형 안쪽 3px인데 청크(0,0) 콜라이더가 덮지 않음 (paths=1)
  frame=1530 time=36.60 timeScale=2.50
   0,0 | O True True 1 17 False 0.21 0 False      ← 청크 전체가 정점 17개
```

지도에 `!`(지형인데 콜라이더 없음)는 딱 1점, `+`(공기인데 콜라이더 안)는 여러 점 —
즉 **윤곽이 픽셀 경계 양쪽으로 몇 px씩 어긋나 있는 게 정상 상태**였다.

**원인** — `DeepMarginPx = 3`이 틀린 상수였다. 이 값의 근거는 "RDP가 최대 0.5px 깎는다"였는데,
실제 허용오차는 `worldSettings.json`의 `chunk.colliderSimplifyTolerance = 0.05`u = **5px**다
(코드 기본값 `0.005`u=0.5px의 10배). 윤곽이 5px까지 안쪽으로 깎일 수 있으므로
"안쪽 3px인데 안 덮임"은 **막 판 벽처럼 들쭉날쭉한 곳에서 상시로 참**이 된다.
게다가 4방향만 검사해서 오목한 모서리가 '안쪽'으로 통과했다.
시간 배속은 원인이 아니라 증폭기다 — `Time.time` 기준 탐침이 실시간으론 2.5배 자주 돈다.

**수정**

1. `DeepMarginPx`를 상수에서 **`TerrainCollider.SimplifyTolerance`에서 계산하는 프로퍼티**로 바꿨다
   (`ceil(tol × 100) + 2`px). 설정을 바꾸면 감시기가 따라온다.
2. 안쪽 판정을 4방향 → **8방향**으로. 오목 모서리 통과를 막는다.
3. 청크 가장자리 제외도 `max(EdgeExemptPx, margin)`으로 같이 늘린다.
4. **미덮음 점이 `NoCoverMinPoints`(8)개 이상일 때만** 보고한다. 몸이 빠질 구멍이면
   5px 간격 탐침 격자에 여러 점이 한꺼번에 걸린다. 한두 점은 단순화의 정상 오차다.

⚠ 부산물로 알게 된 것: **터레인 콜라이더는 지형 픽셀에서 최대 5px(0.05u) 안쪽으로 깎여 있다.**
성능(정점·broad-phase 프록시 수)을 위한 설정이지만, 원래 쫓던 "몸이 청크 사이로 빠지는" 버그의
후보이기도 하다. 여기선 값을 건드리지 않았다.

---

## 자동 캡처는 기본 off로 (2026-08-22)

오탐 두 건(텔레포트·삽질)을 고친 뒤에도 엘리베이터 지형에서 리포트 창이 열렸다.
탐지기는 휴리스틱이라 오탐이 완전히 사라지지 않는데, **오탐 한 번의 대가가
"플레이 중 화면을 뺏고 게임을 멈추는 것"이라 너무 비싸다.** 감시기의 값은
사고 순간의 상세 덤프에 있지 리포트 창을 여는 데 있지 않다.

`AutoCapture` 기본값을 `false`로 내렸다. 바뀌는 것은 없다 —
사고는 그대로 `Debug.LogError` 덤프로 남고, F12를 누르면 그 덤프가 리포트에 실린다.
자동으로 띄우고 싶은 세션에서만 콘솔 `seam auto`로 켠다.


---

## FALLTHRU — 착지 관통 탐지기 (2026-08-31)

**증상** — "조금만 위에서 떨어져도 땅 속으로 들어가버린다."

### 기존 탐지기 4종이 이 사고를 못 잡는다

| 탐지기 | 왜 안 걸리나 |
|---|---|
| EMBEDDED | 슬랩을 **뚫고 아래 빈 공간으로 나오면** 중심 픽셀이 solid가 아니다. 파묻힌 게 아니라 통과한 것 |
| TUNNEL | 청크 경계를 넘어야 걸리는데, 자기가 판 굴 바닥은 **청크 한복판**이다. 0.5u 기준도 못 넘는다 |
| NOCOVER | 0.2초 주기 격자 탐침이라 **사고 순간을 지나칠 확률이 크고**, 미덮음 점 8개 이상을 요구한다. 얇은 슬랩은 그만큼 안 나온다 |
| TRACE | 윤곽이 정상적으로 닫혔으면(=슬랩이 통째로 단순화로 사라진 경우) 실패가 아니다 |

그래서 **한 물리 스텝의 이동선(스윕)을 직접 훑는** 탐지기를 새로 넣었다.

### 판정

발 밑(`PlayerController.groundCollider.bounds.min.y`) 기준으로 `prevPos → curPos`를
**1픽셀(0.01u) 간격**으로 샘플링한다. 다음을 모두 만족하면 사고다.

1. 내려가는 스텝이고, `IsScriptedMove`가 아니다 (텔레포트 제외)
2. 지나친 solid 픽셀 ≥ `FallThruMinSolidPx`(2)
3. 마지막 solid 이후 빈 공간이 `FallThruMinClearPx`(3)픽셀 이상 이어진다
4. 그 solid 구간을 **세로로** 지났다 (`firstSolid.y - lastSolid.y ≥ 2px`)

**3번이 정상 착지와의 구분선이다.** 정상 착지는 스윕이 solid **안에서 끝난다** —
콜라이더 윤곽이 지형 픽셀보다 최대 `DeepMarginPx`만큼 안쪽이라 발이 원래 조금 잠긴다.
**4번은 난간 모서리를 스치며 걸어 내려가는 스텝**(가로 이동 지배)을 걸러낸다.

### 덤프의 핵심은 마지막 줄

같은 구간에 `Physics2D.Linecast(발스윕, groundLayer)`를 쏴서 **범인을 가른다.**
이 둘은 고치는 곳이 완전히 다르다.

| Linecast | OverlapPoint | 결론 | 고칠 곳 |
|---|---|---|---|
| 맞음 | — | 콜라이더는 있었는데 통과 | **물리** — 플레이어 CCD / 낙하속도 |
| 안 맞음 | false | 그 자리에 콜라이더가 없음 | **콜라이더 생성** — 얇은 슬랩이 단순화로 소실 |
| 안 맞음 | true | 스윕 판정만 실패 | 레이어 마스크 / broad-phase |

같이 찍는 값: `vy`·스텝이동량·**낙하높이**·체공시간·`fixedDeltaTime`,
슬랩 두께(px), 그 청크의 `paths`/`colDirty`/`갱신후(s)`/`실패윤곽`,
현재 `colliderSimplifyTolerance`(px 환산).
낙하높이는 "조금만 위에서"가 실제로 몇 u인지를 수치로 확정하려고 넣었다.

### 사고 전부터 의심되는 값 3개 (아직 안 건드림)

덤프가 어느 쪽인지 갈라주기 전까지는 손대지 않는다. 셋 다 서로를 증폭한다.

| 값 | 현재 | 비고 |
|---|---|---|
| 플레이어 `m_CollisionDetection` | **0 = Discrete** | `Player.prefab` / `FanalPlayer.prefab` 양쪽. 캡슐이 0.2×0.5u인데 `maxFallSpeed`=30u/s면 한 스텝(0.02s) 이동이 **0.6u > 몸 높이** |
| `Physics2DSettings.m_DefaultContactOffset` | **0.0001** | Unity 기본값 0.01의 1/100. 접촉이 사실상 겹친 뒤에야 생성된다 |
| `chunk.colliderSimplifyTolerance` | **0.05u = 5px** | 윤곽이 지형에서 최대 5px 안쪽으로 깎인다 → **두께 10px 이하 슬랩은 콜라이더가 통째로 사라질 수 있다.** 자기가 판 굴 사이 바닥이 정확히 이 두께다 |

`m_Interpolate`도 프리팹마다 다르다(`Player.prefab`=0, `FanalPlayer.prefab`=1) — 보간은
관통의 원인은 아니지만 눈에 보이는 위치와 물리 위치를 어긋나게 해 재현 판단을 흐린다.
