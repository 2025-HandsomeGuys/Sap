# 월드 상호작용 — 키 통일(F)과 근접 선택지 목록

`WorldInteractable` + `PlayerInteractor`가 만드는 "다가가면 할 수 있는 일이 보이고, 골라서 실행한다"의 설계.

---

## 1. 키는 F 하나 — `InteractionKeys`

인게임 월드 상호작용 키는 **F**다(구 E). 단일 원천은 `Assets/Scripts/UI/Interaction/InteractionKeys.cs`.

```csharp
if (InteractionKeys.InteractPressed) { ... }   // KeyCode.F를 직접 쓰지 말 것
```

**UI 안의 조작은 여기와 무관하다** — 이동 WASD · 탭 전환 Q/E · 확인 Space는 그대로다.
UI 확인 키를 상호작용 키와 같게 만들면 창이 열리는 프레임에 그대로 눌려버린다
(그래서 `NpcPopupOverlayUI`는 `confirmKey == E`를 Space로 되돌리는 가드를 갖고 있다).

키를 또 바꿀 일이 생기면 `InteractionKeys.Interact` 한 줄만 고친다. 인스펙터에는 두지 않는다 —
오브젝트마다 다른 키를 두면 화면의 `[F]` 아이콘과 실제 키가 조용히 어긋난다
(그래서 `WorldInteractable.interactKey` 필드는 제거했다).

---

## 2. 근접 선택지 목록

근접하면 오브젝트 위에 **어두운 반투명 패널 + 한 줄씩** 뜬다.

```
┌─────────────────┐
│ [F] 상점         │  ← 지금 고른 줄: 키 아이콘 + 밝은 글자(굵게)
│  ·  강화         │  ← 나머지 줄: 점 + 한 단계 어두운 글자
└─────────────────┘
```

- 고르기 = **마우스 휠**(끝에서 반대쪽으로 순환), 실행 = **F**.
- 조건이 안 되는 항목은 줄 자체가 생기지 않는다. 잠긴 시설은 "아직 사용할 수 없다" 한 줄만 붉게.
- **지금 할 수 없는 일에는 `[F]`를 붙이지 않는다** — 문구만 붉게 띄운다("날이 어두워져 들어갈 수 없다").
  못 하는 일 옆에 키가 있으면 눌러도 되는 것처럼 읽힌다. 아이콘 자리를 통째로 접어 문구가 왼쪽 끝에서 시작한다.
  불가 상태에도 그림을 쓰고 싶으면 `promptSpriteBlocked`에 전용 그림을 지정한다(그 경우만 그려진다).
- 선택 상태는 **오브젝트가 갖는다**(`WorldInteractable.SelectedOption`). 목록을 그리는 쪽(피드백 뷰)과
  실행하는 쪽(`PlayerInteractor`)이 같은 인덱스를 봐야 "보고 있는 것"과 "실행되는 것"이 어긋나지 않는다.

### 선택지를 여러 개 두는 법

`InteractionBehaviour`에서 셋만 override 한다. 기본값은 선택지 1개 = 기존 `GetPrompt`/`Interact` 그대로라
대부분의 동작은 아무것도 안 고쳐도 된다.

```csharp
public override int OptionCount { get; }                        // 매 프레임 불린다 — 무거운 조회는 캐시
public override InteractionPromptInfo GetOption(int index);
public override void InteractOption(int index, GameObject interactor);
```

⚠ **개수와 인덱스 순서는 같은 프레임 안에서 일관돼야 한다.** 목록을 그린 뒤 실행할 때 인덱스가 가리키는
항목이 달라지면 엉뚱한 게 실행된다. `TruckNpcBehaviour`가 프레임당 한 번만 목록을 다시 만드는 이유
(`RebuildActions`), `BedBehaviour`가 "낮잠 → 수면" 순서를 고정하는 이유가 이것이다.

### 지금 여러 선택지를 가진 종류

| 종류 | 선택지 |
|---|---|
| 침대 | 낮잠 자기 / 하루를 마무리 하기 (가능한 것만) |
| 트럭 NPC | 대화 / 퀘스트 / 상점 / 강화 |

**침대**는 더 이상 선택 팝업(`BedRestChoiceOverlayUI`)을 띄우지 않는다 — 목록에서 바로 고른다.
**NPC**도 머리 위 팝업(`NpcPopup` / `NpcPopupOverlayUI`)을 띄우지 않는다. 그래서
`UIStateManager.ReturnToPopupOrClose()`는 대화·상점을 닫을 때 팝업을 되살리지 않고 그냥 월드로 돌아간다 —
되살리면 목록과 팝업이 겹쳐 두 번 고르게 된다.

---

## 3. `[F]` 아이콘

`OptionListView`가 선택된 줄에 그린다. 찾는 순서:

1. 오브젝트 인스펙터 `promptSprite` / `promptSpriteAlt`(둘을 넣으면 번갈아 표시 = 딸깍이는 느낌)
2. `Resources/UI/InteractPrompt/key_up.png` · `key_down.png` — **프로젝트 공용**
3. 둘 다 없으면 글자 `F`를 그린다(안내가 통째로 사라지는 것보다 낫다)

지상 씬의 오브젝트들은 **인스펙터 지정을 비워 두고 공용 아이콘을 쓰게 통일**했다 —
그림을 바꿀 때 파일 두 개만 갈아 끼우면 전부 따라온다.

아이콘이 글자와 크기·높이가 안 맞으면 `promptSpriteScale`(글자 크기 기준 배율) ·
`promptSpriteOffset`(px)으로 조정한다. 아이콘과 글자 사이 간격은 코드 상수
`OptionListView.IconTextGap`(글자 크기 배수)다.

---

## 3-1. 배경 판

| 인스펙터 | 뜻 |
|---|---|
| `labelBackdrop` | 배경 판을 쓸지. 끄면 판만 투명해지고 줄 배치는 그대로 |
| `labelBackdropSprite` | 배경 스프라이트(**9-슬라이스**로 늘어난다). 비우면 코드가 그리는 단색 반투명 판 |
| `labelBackdropSpriteColor` | 스프라이트 틴트(흰색 = 원본 그대로) |
| `labelBackdropPadding` | 안쪽 여백 px — `x`=왼 `y`=위 `z`=오른 `w`=아래 |

스프라이트를 넣을 땐 임포트 설정에서 **Border**를 잡아야 늘어날 때 모서리가 뭉개지지 않는다.

⚠ 여백을 `RectOffset`으로 직렬화하지 않고 `Vector4`로 받는 이유: 이 프로젝트에서
`[SerializeField]`를 `= new RectOffset(...)`으로 초기화하면 **그 아래 필드들의 초기화가 통째로 끊긴다**
(Reset해도 0/검정으로 들어온다). 새 여백 항목을 추가할 때도 같은 규칙을 따를 것.

---

## 4. 문구 규칙

`interact_*` 로컬라이제이션 키의 값에서 **`E - ` 접두사를 전부 걷어냈다**. 키 표시는 아이콘이 맡으므로
문구는 행동 이름만 남긴다("상점", "지하로 들어가기"). 새 문구를 추가할 때도 키 이름을 넣지 말 것 —
넣으면 `[F] E - 상점`이 된다.

---

## 4-A. 지상 출구 (지하 → 지상)

지하의 빛기둥 아래 같은 "지상 복귀 지점"도 Kind `SurfaceExit`(지상 출구)로 통일했다.
근접하면 다른 상호작용 오브젝트와 똑같이 `지상으로 나가기` 목록이 뜨고 F로 실행된다
(문구 키 `interact_surface_enter`, 우선순위 기본 10 — 엘리베이터와 동급).

동작은 `SurfaceExitBehaviour` → `ExploreExitController.RequestExitToSurface()` 한 줄이다.
확인창·정산·씬 전환은 전부 컨트롤러가 들고 있으므로 여기서 만들지 말 것.
씬에 `ExploreExitController`가 하나 있어야 한다.

기존 `SurfaceExitBeacon`은 같은 일을 하는 단독 컴포넌트로 남아 있다(빛기둥 `SurfaceLightShaft`와는
여전히 서로 무관). **같은 오브젝트에 둘 다 붙이면 상호작용이 두 번 실행된다** — 인스펙터가 경고를 띄운다.

---

## 5. 씬 세팅 (지상)

`DemoUpground`의 상호작용 오브젝트는 전부 `WorldInteractable` 하나로 통일돼 있다.

| 오브젝트 | Kind |
|---|---|
| Bed | 침대 |
| PC | PC(마켓 씬) |
| Wardrobe | 작업대 |
| Elevator | 엘리베이터 입구 |
| Portal_GotoGround | 땅굴 입구 |
| NoticeBoard | 서브퀘스트 게시판 |
| Truck | 트럭 NPC (대화·퀘스트·상점·강화) |

공통으로 맞춘 값:

- `showPromptLabel = 1` — 근접 목록을 띄운다(구 '문구 라벨' 체크가 이 뜻으로 바뀌었다)
- `promptLabelOnInteractOnly = 0` — 다가가면 바로 뜬다(상호작용한 순간에만 반짝이지 않는다)
- `promptSprite` / `promptSpriteAlt` = 비움 — 공용 `[F]` 아이콘 사용

---

## 6. 코드 지도

| 파일 | 역할 |
|---|---|
| `UI/Interaction/InteractionKeys.cs` | 상호작용 키 단일 원천(F) |
| `UI/Interaction/PlayerInteractor.cs` | 대상 탐지 · 휠로 선택지 이동 · F로 실행 |
| `UI/Interaction/WorldInteractable.cs` | 선택 인덱스 보유(`SelectedOption`/`CycleOption`/`GetOptionPrompt`) |
| `UI/Interaction/WorldInteractable.Feedback.cs` | `OptionListView` — 목록 UI(월드 스페이스 캔버스) |
| `UI/Interaction/Behaviours/InteractionBehaviour.cs` | `OptionCount`/`GetOption`/`InteractOption` 기본 구현 |
| `UI/Interaction/Behaviours/SettlementBehaviours.cs` | 침대·NPC의 선택지 정의 |
