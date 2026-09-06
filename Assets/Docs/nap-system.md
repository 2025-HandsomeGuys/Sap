# 침대 낮잠(Nap) 시스템 설계

## 한 줄 요약
침대에서 **낮잠 = "주식 다음 틱만 지나가되 하루는 끝나지 않는" 짧은 휴식**. 기존 **수면(하루 마무리)** 과 별개다.
낮잠·수면이 **둘 다 가능할 때만** NPC식 선택 팝업이 뜨고, 한쪽만 가능하면 팝업 없이 바로 실행된다.

## 시간/틱 규칙 (설계 결정)
- 이 게임은 시간대가 **오전(Morning)/오후(Afternoon)** 2단계뿐이고, 원래 **하루 넘김 = 주식 1틱**이었다.
- **낮잠**: `StockGameManager.ProcessTick()` **실제 1틱**(뉴스 발생 가능) 호출. `DayCycleManager.AdvanceToNextDay()`를
  **부르지 않는다** → 날짜·시간대 그대로, **정산 연출(DaySummaryUI)·장부 리셋(DayEarningsLedger) 없음**.
  즉 틱과 날짜가 낮잠에서 분리된다. (낮잠 = "다음 시세를 보려고 일부러 한 틱 넘기는" 마켓 타이밍 행동이라 뉴스도 가능하게 둠.
  뉴스 없는 틱을 원하면 `NapSequence`에서 `ProcessNewsFreeTicks(1)`로 바꾸면 된다)
- **수면**: 날짜+1, **`ProcessNewsFreeTicks(StockGameManager.SkipTicks=4)`**(뉴스 없는 4틱), 정산 연출, 장부 리셋, 저장.
  (`BedInteractable.SleepRoutine` — 자는 사이 뉴스가 안 터지게 하는 기존 정책)
- 낮잠도 시세가 바뀌므로 **저장은 한다**(강제매각 정산·낮잠 사용량 반영).

## 가용 조건 / 팝업 (설계 결정)
| 상황 | 낮잠 | 수면 | 결과 |
|------|------|------|------|
| 낮(오전), 낮잠 횟수 남음 | O | X | 팝업 없이 **바로 낮잠** |
| 낮(오전), 낮잠 횟수 0 | X | X | "낮에는 잘 수 없다" 안내 |
| 밤(오후), 낮잠 횟수 남음 | O | O | **선택 팝업**(낮잠/수면) |
| 밤(오후), 낮잠 횟수 0 | X | O | 팝업 없이 **바로 수면** |

- **낮잠은 남은 횟수가 있으면 낮/밤 무관하게 가능**, **수면은 오후(밤)에만**.

## 하루 가능 횟수 = 업그레이드
- `UpgradeEffectType.NapCount`(=750, 합연산) 로 하루 낮잠 횟수를 정한다.
- **업그레이드 노드 2개**(`UpgradeTreeGenerator`): `Nap_T0_01`(짧은 낮잠 I, +1) → `Nap_T0_02`(깊은 낮잠 II, +1).
  둘 다 사면 하루 2회. **선행 = `Facility_Computer_T0`(단말기 개통)** — 주식 시세 타이밍 도구라 거래소가 열린 뒤에 연다.
  - **⚠ 노드 데이터를 코드에 넣은 것만으로는 에셋이 안 생긴다.** Unity에서 **`Tools/Upgrade/Upgrade Tree Generator` →
    "업그레이드 트리 자동 생성 및 링킹 실행"** 을 한 번 돌려야 `Assets/GameData/UpgradeData/Node/`에 SO가 만들어진다.
    (돌리기 전엔 `GetStatValue(NapCount,0)=0` → 낮잠 불가. `UpgradeTreeCostTests`가 노드 존재를 검사한다)
  - 비용(300/800)은 시설 노드처럼 **임시값**이라 T0 예산 테스트에서 제외된다(`IsBudgetExcluded` = 시설 + NapCount).
- **테스트/치트**: 디버그 콘솔 `nap 2`(하루 2회 강제) / `nap reset`(사용량 0) / `nap auto`(업그레이드 값으로 복귀) / `nap`(현황).
  - **낮에는 팝업이 안 뜬다**(수면 불가라 낮잠만 → 바로 낮잠). **선택 팝업을 보려면** `day pm`으로 저녁으로 바꾼 뒤 침대와 상호작용.
- 오늘 쓴 횟수는 `PlayerData.napUsedToday` + `PlayerData.napDay`(어느 날짜 것인지)로 세이브에 남는다.
  `NapManager.NormalizeForToday()`가 `napDay != 현재날짜`이면 새 하루로 보고 0으로 리셋 →
  세이브 로드로 날짜가 점프해도 오탐 리셋이 없다.

## 구성 파일
| 파일 | 역할 |
|------|------|
| `Gameplay/Environment/NapManager.cs` | 하루 낮잠 횟수 관리(자동 생성 싱글톤). Max=업그레이드, Used=세이브. `CanNap`/`ConsumeNap`/`RemainingNaps` |
| `Gameplay/Environment/NapSequence.cs` | 낮잠 코루틴: 페이드아웃→코골이→**틱 1회**→저장→페이드인. 정산 없음 |
| `Gameplay/Environment/BedRestChoiceOverlayUI.cs` | 낮잠/수면 선택 팝업(전부 코드 생성, `UISkin`). W/S·Space·ESC·마우스. `UIState.BedRest`로 입력 차단 |

> **시트 스프라이트(버튼+패널 공통)**: `ApplyButtonSprite()`가 `Resources.LoadAll<Sprite>(buttonSpriteResource)`로
> 서브스프라이트(`buttonSpriteName`)를 로드해 `skin.buttonSprite`·`skin.panelSprite` **둘 다**에 물린다
> (인스펙터에서 직접 지정하면 그게 우선).
> 기본값 = `UI/Stock_UISheet`의 `Stock_UISheet_8`, `spritePixelsPerUnit(PPU)=0.4`, `tintButtonSprite=true`.
> 원본 `Assets/Sprites/UI/Stock_UISheet.png`는 Resources 밖이라 런타임 로드가 안 돼서,
> **새 GUID 사본을 `Assets/Resources/UI/Stock_UISheet.png`에 뒀다**(원본은 그대로, GUID가 달라 Market 참조와 무관).
> 다른 서브스프라이트로 바꾸려면 인스펙터 `buttonSpriteName`을 바꾼다.
| `UI/Interaction/Behaviours/SettlementBehaviours.cs` → `BedBehaviour` | **실제 씬의 침대가 쓰는 경로**(WorldInteractable 전략). E 상호작용 분기·프롬프트·둘다면 팝업 |

### 근접 피드백 규칙 (`BedBehaviour`)
- **근접 문구**(`GetPrompt`):
  - 낮 + 낮잠 가능 → `"E - 낮잠 자기"`
  - 낮 + 낮잠 불가 → **`InteractionPromptInfo.None`**(아무것도 안 뜸 — "낮에는 잘 수 없다"도 안 띄운다)
  - 저녁 + 낮잠 가능 → `"E - 수면 / 낮잠"` → 상호작용 시 **선택 팝업**
  - 저녁 + 낮잠 불가 → `"E - 하루를 마무리 하기"` → 바로 수면
- **느낌표**: `HasPendingTask`를 오버라이드하지 않음(= `IsAvailable`) → 잘 수 있을 때만 뜬다.
  `IndicatorAlwaysVisible => true` + `IndicatorHidesWhenNear => true` →
  **멀리선 느낌표로 유도, 가까이 가면 느낌표는 사라지고 문구 라벨이 대신 뜬다**.
- **근접 문구**: `ForcePromptLabelOnApproach => true` — 침대 프리팹의 `showPromptLabel`/
  `promptLabelOnInteractOnly` 인스펙터 설정과 무관하게 라벨이 만들어지고 근접 시 뜬다.
  (이 훅이 없으면 `promptLabelOnInteractOnly`가 켜진 침대는 E를 눌러야 문구가 잠깐 뜬다 — 2026-08 버그)
- 위 두 개는 `InteractionBehaviour`에 추가한 공용 훅(기본 false)이라 다른 종류도 코드로 켤 수 있다.
  `WorldInteractable.Feedback`이 인스펙터 값과 behaviour 훅을 OR로 합친다.

### 페이드 (⚠ ScreenFader 의존 금지)
- 낮잠은 **자체 페이드 오버레이**(`NapSequence.EnsureFadeOverlay`, 코드 생성 검은 CanvasGroup)를 쓴다.
  수면(`SleepSequence`)은 페이드 뒤 `DaySummaryUI`(검은 정산 화면)가 화면을 덮어 `ScreenFader`가 없어도
  티가 안 나지만, **낮잠엔 정산 화면이 없어서** `ScreenFader.Instance`가 씬에 없으면 "화면이 안 어두워지는"
  문제가 생긴다. 그래서 낮잠은 ScreenFader에 의존하지 않고 직접 검은 오버레이를 페이드한다.
- 낮잠은 콘솔에 **로그를 남기지 않는다**(요청). 알림 토스트("잠깐 눈을 붙였다")만 뜬다.
- **자는 동안 플레이어 이동 잠금**: 낮잠(`NapSequence`)·**수면(`SleepSequence`) 둘 다** 진행 중
  `UIState.BedRest`로 세팅한다(끝나면 None 복귀). 플레이어 코드가 이미
  `UIStateManager.IsInputBlocked`(= `CurrentState != None`)를 보므로 **플레이어 파일을 건드리지 않고**
  이동·채굴·상호작용이 막힌다. 지금 None일 때만 잡고, 끝나며 아직 BedRest면 되돌린다.
  (수면 정산 `DaySummaryUI`는 자체 입력을 직접 읽어 이 잠금과 무관 — 클릭 스킵 그대로 동작)
| `Gameplay/Environment/BedInteractable.cs` | 구 MonoBehaviour 침대. 같은 로직을 갖지만 씬 침대는 `BedBehaviour`를 씀(둘 다 유지) |
| `_Core/Data`→`UpgradeEffectSO.cs` | `NapCount=750` 추가 |
| `UI/Player/PlayerData.cs` | `napUsedToday`, `napDay` 추가 |
| `UI/NPC/UIStateManager.cs` | `UIState.BedRest` 추가(빈 case — 입력 차단용) |
| `Utils/DebugConsole/DebugCommands.cs` | `nap` 치트 |

## 완료 / 남은 작업
- ✅ **업그레이드 노드**: `Nap_T0_01`/`Nap_T0_02` 추가(코드). → **Unity에서 트리 생성기 1회 실행 필요**(위 ⚠ 참고).
- ✅ **로컬라이제이션**: `UI_Localization.csv`에 KR/EN/CN 추가 완료
  (`interact_bed_choose`, `interact_bed_nap`, `ui_bed_rest_title`, `ui_bed_nap`, `ui_bed_sleep`,
  `ui_bed_rest_remaining`, `ui_bed_rest_hint`). 기존 파일이라 별도 등록 불필요.
- 후속(선택): 전용 낮잠 효과음이 필요하면 `SfxKeys`에 키 추가(현재는 `SleepSnore` 재사용).
- 후속(선택): 낮잠 중 알림 문구(`NapSequence`/`BedInteractable`의 `ShowNotification`)는 기존 침대 알림처럼
  한글 하드코딩이다 — 알림을 로컬라이즈하는 규약이 생기면 같이 처리.
