# 디버그 콘솔 · QA 세이브 픽스처

작성 2026-08-10. 밸런싱/QA 단계 진입을 위한 개발 도구 2종.

관련 코드: `Assets/Scripts/Utils/DebugConsole/`, `Assets/Scripts/Editor/QAFixtureEditorMenu.cs`

---

## 0. 왜 만들었나

밸런싱은 "조건 하나만 바꿔서 다시"를 수백 번 반복하는 작업이다.
그 반복 비용이 높으면 밸런싱을 안 하게 되고, 결국 감으로 숫자를 정하게 된다.

- **디버그 콘솔** — 조건을 바꾸는 비용을 없앤다 (골드·날짜·스태미나·시간 배속)
- **세이브 픽스처** — "그 상황까지 도달하는" 비용을 없앤다 (20분 플레이 → 1초 로드)

둘 다 `BugReportSystem`(F12 자동 스크린샷)과 같은 구조다:
`RuntimeInitializeOnLoadMethod` 자동 생성 · 씬 배치 불필요 ·
`#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE`로 릴리즈에서 통째로 컷.

---

## 1. 디버그 콘솔

### 조작

| 키 | 동작 |
|---|---|
| `` ` `` / `F9` | 콘솔 열기·닫기 (열려 있는 동안 `timeScale = 0`) |
| `]` / `[` | 배속 한 칸 위/아래 — 0.1 → 0.25 → 0.5 → 1 → 2 → 4 → 8 → 16 |
| `\` | 배속 1배 복귀 |

콘솔 안에서 Enter 실행 · ↑↓ 히스토리 · Tab 자동완성 · ESC 닫기.

명령: `help` `time` `gold` `day` `stamina` `nap` `tool` `node` `relic` `escape` `pos` `clear` `fx`

### `escape` — 던전 중도 탈출

던전에는 끝 방의 출구 문 말고 나가는 경로가 없다. 중간 지점 테스트를 마쳤을 때 죽거나
끝까지 가는 대신 이 명령으로 나온다. 별칭 `dexit` · `탈출`.

출구 문과 **완전히 같은 경로**(`DungeonEscape.TryLeave`)를 탄다 — 그 던전은 탐험 완료로
표시되어 **재입장 불가**가 되고, 부순 돌·먹은 보상과 함께 세이브에 확정된다.
콘솔로 나간 회차는 `MarkRunAsFixture`로 밸런스 집계에서 빠진다.

### `node` — 업그레이드 노드 해금·잠금

| 명령 | 동작 |
|---|---|
| `node` | 단축 이름 목록 + 각 노드의 해금 여부 |
| `node coin` | 그 노드 강제 해금 (`coin` = `Facility_Coin_T1`, 코인 거래 개통) |
| `node coin off` | 다시 잠금 |
| `node MiningRange_T0_02` | 단축 이름이 없는 노드는 id를 그대로 |

`tool`과 같은 원리다 — 별도 플래그 없이 `UpgradeManager.DebugForceUnlock`/`DebugForceLock`으로
트리 상태만 건드린다. 없는 id는 해금하지 않고 목록을 돌려준다("켰는데 아무 일도 안 일어난다"를 막는다).

코인 탭은 `MarketSceneController`가 `OnUpgradeStateChanged`를 듣고 있어 **마켓 씬 안에서 켜도 바로** 나타난다.

### `relic` — 유물 지급·회수 + 드롭 표 확인

유물이 상점에서 빠지고 탐험 드롭(돌 완파 + 던전 상자)으로 옮겨가면서, 특정 유물을 손에
넣는 방법이 "될 때까지 돌 캐기"밖에 없어졌다. 씬에 붙는 `RelicDebugGranter`(F1~F12)는
인스펙터에 미리 등록한 유물만, 그 컴포넌트가 있는 씬에서만 된다.

| 명령 | 동작 |
|---|---|
| `relic` | 티어별 목록 + 보유 여부(■/□). 드롭 표에 빠진 유물도 알려준다 |
| `relic magnet` / `relic gambler` | 지급 + 빈 슬롯 자동 장착 (앞부분만 쳐도 인식) |
| `relic magnet 3` | 지급 후 Lv3까지 |
| `relic all` | 전부 지급 |
| `relic off xray` / `relic clear` | 회수 (장착 해제 → 스폰물 정리 → 보유 해제 순) |
| `relic where` | **지금 지층**과 티어별 가중치·미보유 수·실효 확률 |
| `relic drop` | 지금 지층 드롭 표로 추첨해 발밑에 떨어뜨림 |
| `relic drop xray` | 그 유물을 발밑에 떨어뜨림 (픽업 연출·줍기 확인용) |
| `relic pity` / `relic pity reset` | 피티 카운터 보기·초기화 |

`where`·`drop`은 지급이 아니라 **드롭 시스템 자체**를 보는 명령이다. 실제 드롭과 같은
경로(지층 해석 → 추첨 → 픽업 생성)를 타므로 "이 층에서 뭐가 나오나"를 그대로 확인할 수 있다.
확률·피티는 건너뛰지만 **피티 카운터는 건드리지 않는다** — 치트가 밸런스 상태를 오염시키면
그 세이브로 잰 드롭률을 못 믿는다.

`relic off`는 도감 발견 기록을 지우지 않는다. "본 적 있다"는 되돌릴 성질이 아니다.

설계: `Assets/Docs/relic-exploration-drop.md`

### `tool` — 도구 해금·잠금

| 명령 | 동작 |
|---|---|
| `tool` | 도구별 해금 현황 + 지금 든 도구 |
| `tool drill` / `tool 3` | 그 도구 해금 (영문 별칭·번호·한글 이름 접두 모두 인식) |
| `tool all` | 조건이 걸린 도구 전부 해금 |
| `tool lock pickaxe` / `tool lock all` | 다시 잠금 (해금 전 상태로 되돌려 보기) |

도구 상태를 따로 들고 있지 않다 — `toolConfig.json`의 `unlockNodeIds`가 가리키는
**업그레이드 노드를 트리에 넣고 빼는 것뿐**이다(`UpgradeManager.DebugForceUnlock` /
`DebugForceLock`). 별도 플래그를 만들면 세이브·업그레이드 UI와 두 갈래로 갈라진다.

도구 이름(`names`)과 콘솔 입력용 영문 별칭(`debugAliases`)도 `toolConfig.json`에 있으므로
도구가 늘어도 명령 코드는 안 고친다.

잠근 도구를 손에 든 채로 남으면 그걸로 계속 팔 수 있으므로,
상태 변경 뒤 `ToolController.RefreshSelection()`으로 다음 가용 도구로 넘긴다.

> 예전 `ToolController`의 **F8 치트는 제거했다.** `debug_pickaxe` / `debug_drill`이라는
> 존재하지 않는 노드 id를 해금하고 있어서 실제로는 아무 효과가 없었다
> (실제 id는 `PickaxeUnlock_T0_01` / `DrillCapacity_T1_01`).

### 명령 추가 방법

어느 파일에서든 한 줄이면 된다. 시스템별 치트는 그 시스템 옆에 두는 편이 낫다.

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
private static void Register()
{
    DebugCommandRegistry.Register("mineral", "mineral <종류> <개수>",
        "광물 인벤토리에 추가", args => { /* ... */ return "완료"; });
}
```

### ⚠ 시간 배속 — 매 프레임 재적용해야 하는 이유

이 프로젝트는 **20여 곳에서 `Time.timeScale = 1f`를 하드코딩한다.**
(`PauseOverlayUI`, `ShopOverlayUI`, `WarehouseOverlayUI`, `GameManager.OpenMainMenu`,
`EmergencyEscapeSequenceUI`, 각 오버레이의 씬 전환 안전망 …)

한 번만 세팅하는 방식이면 **상점을 한 번 열었다 닫는 순간 배속이 조용히 풀린다.**
그걸 모르고 측정한 밸런스 수치는 전부 오염된다.

그래서 `DebugTimeScaleDriver`가 **LateUpdate에서 매 프레임 다시 쓴다.**
LateUpdate인 이유는 오버레이들이 Update/열기 시점에 건드리므로 마지막 발언권이 필요해서다.

단, **`timeScale == 0`(일시정지)일 때는 손대지 않는다.**
각 오버레이의 `_prevTimeScale` 복원 흐름을 그대로 두고, 복원된 다음 프레임에 덮어쓴다.
여기서 0을 덮어쓰면 일시정지 화면 뒤에서 게임이 계속 돈다.

### ⚠ fixedDeltaTime을 배속만큼 안 키우는 이유

물리 스텝 수/실시간 = `timeScale / fixedDeltaTime` 이다. 양쪽 다 문제가 있다.

- **배속만큼 그대로 키우면** — 물리 부하는 그대로지만 한 스텝에 플레이어가 훨씬 멀리 이동해
  **지형을 뚫는다.** 이 게임은 픽셀 지형 콜라이더라 특히 취약하다.
- **안 키우면** — 8배속에서 물리 스텝이 8배가 되어 프레임이 무너지고,
  그 프레임 저하 자체가 측정값을 다시 왜곡한다.

절충으로 **2배까지만 늘린다**(`DebugTimeScale.MaxFixedStepFactor`).
8배속 기준 물리 부하 4배, 스텝 간 이동거리 2배.

**이동·충돌·낙하가 걸린 밸런스는 4배 이하에서 볼 것.** 콘솔도 4배 초과 입력 시 경고를 띄운다.

### 배속 표시기는 장식이 아니다

배속이 1이 아니면 화면 우상단에 주황색 `■ TIME x4`가 항상 뜬다(`DebugTimeScaleDriver`).
배속을 켜둔 걸 잊고 측정한 수치는 전부 쓰레기가 되므로, 켜져 있다는 사실이 항상 보여야 한다.
배속은 씬을 넘어가도 유지되며, 명시적으로 끄기 전까지 안 풀린다.

### `gold` 명령이 `DayEarningsLedger.Report()`를 안 부르는 이유

CLAUDE.md 아키텍처 제약 §12는 골드 증감 지점에 `Report()` 병행 호출을 요구하지만,
**디버그 골드는 의도적으로 예외다.**

디버그로 넣은 돈이 '광물 판매' 같은 실제 카테고리에 섞이면
하루 정산 화면(`DaySummaryUI`)으로 밸런스를 읽을 때 그 수치가 거짓말이 된다.
장부는 추적 안 된 변동을 '기타(other)'로 흡수하도록 설계돼 있어 합계는 안 어긋난다.

---

## 2. QA 세이브 픽스처

"이 상황부터 시작" 스냅샷. `Assets/QA/Fixtures/<이름>/` 에 폴더 하나씩.

```
Assets/QA/Fixtures/
  d01_start/          playerData.json  meta.txt
  d07_layer2_drill/   playerData.json  worldData.bin  meta.txt
  _backup/            ← fx load 직전 상태 (자동)
```

### 명령

| 명령 | 동작 |
|---|---|
| `fx` / `fx list` | 목록 (메모·저장시각·지형 포함 여부) |
| `fx save <이름> [메모…]` | 현재 상태를 픽스처로 굽기 |
| `fx load <이름> [슬롯]` | 픽스처를 슬롯에 붓고 `DemoUpground` 재시작 |
| `fx del <이름>` | 삭제 |
| `fx dir` | 픽스처 경로 출력 |

에디터: `Tools/QA/픽스처 폴더 열기` · `세이브 폴더 열기` · `픽스처 목록 출력`

### ⚠ 왜 세이브 슬롯으로는 안 되는가 — worldData.bin은 슬롯을 모른다

이 게임의 저장 상태는 **파일 두 개**로 나뉘어 있다.

| 파일 | 내용 | 담당 |
|---|---|---|
| `playerData_{0~4}.json` | 골드·스탯·퀘스트·코인·업그레이드 | `SaveManager` |
| `worldData.bin` | **파진 지형(청크)** | `WorldPersistenceSystem` |

그런데 `WorldPersistenceSystem.cs:10`의 `_saveFileName = "worldData.bin"`에는
**슬롯 인덱스가 없다. 5개 슬롯이 지형 파일 하나를 공유한다.**

결과:
- 슬롯 3에 "7일차 2층"을 만들어두고 슬롯 0으로 새 게임을 해도 **파진 굴이 그대로 이어진다**
- `SaveManager.DeleteSave()`는 `playerData_{n}.json`만 지운다 → **슬롯을 지워도 굴은 남는다**

그래서 픽스처는 두 파일을 **한 폴더에 함께 굽고 함께 되돌린다.**

> 📌 **미해결**: `worldData.bin`을 슬롯별로 나눌지는 별건이다.
> 세이브 슬롯 5개를 실제 기획으로 쓸 거라면 이건 픽스처와 무관하게 고쳐야 한다.

### ⚠ 픽스처에 worldData.bin이 없으면 살아있는 파일을 지운다

"아직 아무것도 안 판 1일차" 픽스처에는 지형 파일이 없는 게 정상이다.
이때 복원하면서 살아 있는 `worldData.bin`을 **지운다**(`QAFixtureStore.Restore`).

안 지우면 직전에 플레이하던 굴이 그대로 남아, 그 픽스처가 거짓말이 된다.

### ⚠ 파일만 갈아끼우면 안 먹는다 — 씬 재시작 필수

이미 로드된 청크와 매니저 상태가 메모리에 살아 있다.
`fx load`는 파일 교체 후 반드시 `SceneLoader.LoadScene("DemoUpground")`를 태운다.

그래야 `GameManager.OnGeneralSceneLoaded` → `SaveManager.Load()`가 돌고,
새로 생성된 `InfinityMapManager`가 새 `worldData.bin`을 읽는다.

지상 허브로 되돌리는 이유는 로드 경로가 가장 단순해서다
(`DemoUnderground`는 `LoadingSceneController`가 Load를 따로 처리한다).

### `fx save`는 먼저 디스크로 내린다

픽스처는 '디스크에 있는 파일'을 복사한다. 그래서 굽기 전에 순서대로 호출한다:

```
SaveManager.RefreshReferences()   // 씬 전환으로 끊긴 인벤토리 참조 복구
SaveManager.Save()                // playerData_{슬롯}.json
InfinityMapManager.SaveAllData()  // worldData.bin
```

`RefreshReferences()`를 빼먹으면 `equipmentInventory`가 null인 채 저장돼
**장비가 통째로 빠진 픽스처**가 나온다(SaveManager 주석의 기존 버그와 같은 경로).

`worldData.bin`은 평소 `InfinityMapManager.OnApplicationQuit`에서만 자동 저장된다 —
씬 전환으로는 안 써지므로 명시 호출이 필요하다.

### 에디터 vs 빌드 경로

- 에디터: `Assets/QA/Fixtures/` — **UVCS가 관리**. 재설치·다른 머신에서도 남는다
- 빌드: `<persistentDataPath>/QA/Fixtures/` — 그 빌드 안에서만 유효

버전관리에 남길 픽스처는 에디터에서 구울 것.

---

## 3. 아직 안 한 것

- **세션 메트릭 로거** — `Telemetry`(`_Core/Telemetry/`)가 이미 JSONL로 깔려 있다.
  밸런스 KPI(층별 체류시간·시간당 채굴량·귀환 사유)를 여기에 얹으면 된다. 새로 만들 필요 없음
- **목표 곡선 문서** — "N일차에 골드 X~Y, 층 Z" 의도를 숫자로 박아둔 표.
  이게 없으면 로그를 모아도 좋은 수치인지 판단할 근거가 없다
- **경제 시뮬레이터** — `CoinPriceEngine`이 순수 C#이라 EditMode에서 몬테카를로가 가능하다
- **자동 플레이 봇** — 사람 편차를 제거한 시간당 채굴량 baseline
- `tp <x> <y>` 명령 — 착지 보호(CLAUDE.md §13)를 지켜야 해서 보류
