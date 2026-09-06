# 버그 리포트 시스템 설계

작성일: 2026-08-10

팀 내부 레벨디자인 QA용. 게임 중 버그를 만나면 단축키 한 번으로 **스크린샷 + 그 시점의 게임 상태 + 최근 로그**를 로컬에 묶어 저장한다.

## 목적과 범위

- **대상**: 팀 내부 레벨디자이너. 외부 유저 배포용이 아니다.
- **저장만 한다.** 서버 업로드·Discord 웹훅·대시보드 연동은 이번 범위 밖.
- 리포트 폴더를 통째로 압축해 사람이 직접 공유하는 것을 전제로 한다.

### Unity 공식 기능을 쓰지 않은 이유

Unity에 `com.unity.cloud.userreporting`(Cloud Diagnostics User Reporting)이 있고 스크린샷·디바이스 정보·로그 첨부를 지원한다. 쓰지 않은 이유:

1. 이 게임에서 재현에 실제로 필요한 값(청크 좌표, 로드된 청크 목록, 층, 던전 내부 여부, 세이브 스냅샷)은 **어차피 커스텀 필드로 직접 채워야 한다.** 공식 SDK를 써도 수집 코드는 그대로 필요하다.
2. 남는 이점은 업로드 파이프라인과 대시보드뿐인데, 로컬 저장만 하기로 한 이상 가치가 없다.
3. SDK 자체가 오래됐고 Unity의 진단 제품군이 Backtrace 쪽으로 이동 중이라 장기 지원이 불확실하다.

## 흐름

```
F12
 └ WaitForEndOfFrame → 스크린샷 캡처      ← UI 뜨기 전
 └ 상태 스냅샷 수집                        ← 값 고정
 └ timeScale = 0 + 메모 오버레이 표시
 └ 저장 → 파일 3개 쓰기 → 토스트(경로 + [폴더 열기])
   취소 → 폐기
```

### ⚠ 순서가 설계의 핵심

**스크린샷과 상태 수집은 반드시 오버레이를 띄우기 전에 끝낸다.**

순서가 뒤집히면 두 가지가 동시에 깨진다:

- 리포트 스크린샷에 버그 리포트 UI 자신이 찍힌다
- 일시정지·UI 전환 이후의 값이 수집되어 "버그가 난 그 시점"이 아니게 된다

수집된 스냅샷은 오버레이가 뜨기 전에 이미 불변 객체로 확정되고, 오버레이는 여기에 메모 문자열만 채워 넣는다.

## 파일 구조

```
Assets/Scripts/Utils/BugReport/
├─ BugReportSystem.cs      진입점. 핫키 감지, 캡처 코루틴 오케스트레이션
├─ BugReportLogBuffer.cs   로그 링버퍼
├─ BugReportCollector.cs   싱글톤들에서 상태 수집 → BugReportData
├─ BugReportData.cs        직렬화 구조체
├─ BugReportWriter.cs      폴더 생성 + 파일 쓰기
├─ BugReportOverlayUI.cs   메모 입력 오버레이 (코드 생성)
└─ BugReportToast.cs       저장 결과 알림 + '폴더 열기' (코드 생성)
```

각 파일이 하나의 책임만 갖는다. 수집기는 UI를 모르고, UI는 파일 쓰기를 모르고, 라이터는 게임 상태를 모른다.

### BugReportSystem

`[RuntimeInitializeOnLoadMethod]`로 자동 생성된다 — **씬 배치 불필요**. `SoundManager`와 같은 패턴.

`DontDestroyOnLoad`로 씬 전환을 넘어 살아남는다. 로그 버퍼가 씬 전환에서 끊기면 안 되기 때문이다.

핫키는 레거시 `Input.GetKeyDown(KeyCode.F12)`. 프로젝트가 전부 레거시 Input을 쓰므로 맞춘다.

### BugReportLogBuffer

`Application.logMessageReceived`를 구독하는 링버퍼. 용량 300줄.

- 스택 트레이스는 `Error`/`Exception`/`Assert`만 저장한다. 전부 저장하면 로그 파일이 수 MB로 불어난다.
- 세션 누적 Error/Exception 카운트를 별도로 센다 — "이 리포트 이전에 이미 예외가 나 있었다"가 중요한 단서다.
- 링버퍼는 고정 크기 배열 + 인덱스. 매 로그마다 할당하지 않는다.

### BugReportCollector

싱글톤에서 값을 읽어 `BugReportData`를 만든다. 기존 파일을 수정하지 않는다.

**항목별 개별 try-catch.** 매니저 하나가 null이거나 예외를 던져도 그 필드만 `"<unavailable>"`로 남기고 나머지는 정상 수집된다. 버그 리포트가 버그 때문에 실패하면 안 된다.

### BugReportWriter

`Application.persistentDataPath/BugReports/<타임스탬프>/`에 네 파일을 쓴다. 폴더 생성 실패·디스크 오류는 삼키지 않고 실패 메시지를 남긴다.

같은 초에 F12를 두 번 누르면 폴더명이 충돌한다. 덮어쓰지 않고 `_2` 접미사를 붙인다.

### BugReportOverlayUI

`CodeUI` 키트로 전부 코드 생성 (`CreateImage`/`CreateText`/`CreateTextButton`/`ApplySkin`). 프로젝트의 다른 오버레이와 같은 방식이라 씬 세팅이 필요 없다.

구성: 반투명 암막 → 패널 → 스크린샷 썸네일 미리보기 → `TMP_InputField` 메모 입력 → [저장] [취소].

- `CodeUI`에는 InputField 생성 헬퍼가 없으므로 이 파일에서 직접 만든다.
- 키: **Enter 저장 / ESC 취소.** 프로젝트의 UI 확인 키는 Space로 통일돼 있지만, 텍스트 입력 중에는 Space가 공백 문자다. 여기만 예외.
- IMGUI(`OnGUI`)를 쓰지 않은 이유: 빌드에서 한글 IME 입력이 깨진다.

## 출력

```
%USERPROFILE%/AppData/LocalLow/<회사>/<게임>/BugReports/2026-08-10_142301/
├─ screenshot.png
├─ report.json
├─ log.txt
└─ save.json
```

세이브 스냅샷은 `report.json` 안에 넣지 않고 별도 파일로 뺀다. JSON 문자열을 JSON 안에 넣으면 전부 이스케이프되어 사람이 눈으로 읽을 수 없게 되는데, 읽히는 것이 이 파일들의 유일한 용도다.

### report.json 수집 항목

| 그룹 | 값 |
|---|---|
| **memo** | 사용자가 입력한 한 줄 메모 |
| **meta** | 로컬 시각, 앱 버전, 유니티 버전, 플랫폼, 씬 이름, 해상도, `realtimeSinceStartup`, 메모리 사용량, 세션 누적 Error/Exception 카운트 |
| **player** | 월드 좌표, **청크 좌표**, 속도, 스태미나(`PlayerStat.CurrentStamina`), 골드(`PlayerStat.Gold`), 채광 레벨(`PlayerStat.MiningLevel`), 무게/최대, 장착 도구 |
| **world** | 현재 층, 로드된 청크 수 + 좌표 목록, 던전 내부 여부(`DungeonOverlayController.IsInDungeon`), 일차·시간대 |
| **ui** | `UIStateManager.Instance.CurrentState` |

`SaveManager.Instance.playerData` 전체(`JsonUtility`)는 위 표가 아니라 `save.json`에 따로 쓴다.

청크 좌표와 로드된 청크 목록이 이 게임에서 가장 값진 항목이다. 지형 버그는 대부분 "어느 청크에서, 이웃 청크가 로드된 상태였는가"로 좁혀진다.

## 입력 차단

메모를 입력하는 동안 `Input.GetKeyDown` 기반 전역 단축키(M=지도, I=인벤토리 등)가 그대로 발동한다. 한글 입력 중이라도 키 핸들러는 포커스와 무관하게 돈다.

### ⚠ `UIState`를 바꾸는 방식은 쓰지 않는다

처음에는 `SetState(UIState.Popup)`으로 기존 차단 경로를 재사용하려 했으나, `UIStateManager`를 확인해보니 세 가지가 깨진다:

1. `popupRoot`(NPC 팝업 패널)를 **활성화**한다 — 엉뚱한 UI가 화면에 뜬다
2. 상태 전환 시 열려 있던 오버레이들을 `CloseStatic()`으로 **닫아버린다** — 리포트 대상인 상태 자체가 바뀐다. 버그 리포트 도구로서 치명적이다
3. 대화 중(`UIState.Dialogue`)이면 `SetState`가 **차단**되어(`UIStateManager.cs:120`) 복원도 실패한다

### 실제 방식 — static 플래그

`BugReportOverlayUI.IsOpen` / `.ClosedThisFrame` static 프로퍼티를 두고, `UIStateManager`가 이를 읽어 차단한다. `SettingsOverlayUI`·`WarehouseOverlayUI` 등이 쓰는 것과 **똑같은 관례**이며, `UIStateManager.cs:108`에 "새 전면 오버레이를 추가하면 여기에 한 줄만 더하면 된다"고 명시된 확장 지점이다.

`UIStateManager.cs` 두 곳에 추가한다 (버그 리포트가 조건부 컴파일이므로 `#if`로 감싼다):

- `IsInputBlocked` 체인 — 플레이어 이동·채굴 차단
- 전역 단축키 핸들러의 조기 반환 체인 — Tab/M/J/ESC 차단

`ClosedThisFrame`이 없으면 오버레이를 ESC로 닫은 그 프레임의 ESC가 일시정지 메뉴를 연다.

## 일시정지

`PauseOverlayUI`와 같은 패턴 — `_prevTimeScale`에 현재 값을 저장하고 `0`으로 내린 뒤, 닫을 때 저장해둔 값을 복원한다. `1f`로 하드코딩하지 않는다. 일시정지 메뉴 위에서 F12를 눌렀을 때 게임이 멋대로 재개되면 안 된다.

오버레이 애니메이션·키 반복은 `unscaledDeltaTime`을 쓴다.

## 확장 지점

```csharp
public static event Action<Dictionary<string, string>> ExtraContext;
```

`BugReportCollector`에 이벤트 하나만 둔다. 새 시스템이 자기 값을 리포트에 넣고 싶으면 한 줄 구독하면 끝이다. 인터페이스 구현도, 수집기 수정도 필요 없다.

구독자 콜백도 try-catch로 감싼다 — 확장이 리포트를 깨뜨리지 못하게.

## 활성 범위

에디터와 개발 빌드에서는 항상 활성. 정식 빌드에서는 `ENABLE_BUG_REPORT` 스크립팅 심볼이 정의된 경우에만 컴파일된다.

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
```

## 하지 않는 것

- **업로드 / 웹훅 / 대시보드** — 로컬 저장으로 충분하다고 판단. 필요해지면 `BugReportWriter` 뒤에 전송 단계를 덧붙이면 된다.
- **입력 리플레이 기록** — 재현에는 최고지만 상시 기록 비용이 크고, 지형이 절차적으로 변하는 이 게임에서는 입력만으로 재현이 보장되지 않는다.
- **리포트 목록 뷰어 UI** — 폴더를 직접 여는 것으로 충분하다.
- **청크 픽셀 덤프** — 청크당 수 MB. 지형 자체가 깨진 버그에는 유용하겠지만 대부분의 경우 낭비다. 필요해지면 별도 토글로 추가.
