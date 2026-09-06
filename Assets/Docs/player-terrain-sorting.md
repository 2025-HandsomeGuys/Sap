# 플레이어 ↔ 지형 정렬 (지하에서 플레이어를 지형 뒤로)

2026-08-08. 지하에서 플레이어가 지형 뒤에 그려지도록(= 파진 구멍으로만 보이도록) 정렬을 바꾼 작업.

---

## 1. 배경 — 왜 order만 바꿔서는 안 됐나

Unity 2D 정렬은 **정렬 레이어가 order보다 항상 우선**한다. 레이어가 다르면 order는 비교조차 안 된다.

프로젝트 정렬 레이어 순서:
`Default(0) < Fog < BackGround < Objects < player < Mineral < Map < LoadingScreen`

작업 전 상태:

| | 지상(DemoUpground) | 지하(DemoUnderground) |
|---|---|---|
| 지형 | **player** / 30 (Tilemap) | **Default** / 0 (청크 스프라이트) |
| 플레이어 | player / 1~20 | player / 1~20 |
| 결과 | 같은 레이어 → order로 갈려 **플레이어가 지형 뒤** ✓ | 레이어가 달라 **플레이어가 항상 지형 앞** ✗ |

지상은 이미 원하던 대로 동작하고 있었다. 지하만 레이어가 어긋나 있었던 것.

## 2. 채택한 방식 — 플레이어를 지형 레이어로 내린다

지하에서만 플레이어 스프라이트를 `Default`로 옮기고 order를 음수 대역으로 내린다.

```
청크 배경 -100 < 특수청크 배경 -99 < 엘리베이터 -60 ~ -50 < [플레이어 -49 ~ -30] < 돌·광물 -1 < 지형 0
```

**지하 엘리베이터는 -60부터**(`ElevatorSpawner.SortingOrder`, 스폰 시 코드로 적용).
프리팹 기본값은 `Default`/order 1이라 지형(0)보다 앞 = 플레이어를 가렸다.
플레이어보다 뒤로 보내려면 -49 미만이어야 하고, 플레이어가 이미 지형 뒤 대역이므로
**엘리베이터도 필연적으로 지형(0) 뒤로 간다** — 승강로 픽셀은 스폰 때 비워지고(`GenerateElevator` 7단계)
착지 방도 PNG로 파여 있어 실제로는 가려지지 않는다. 승강로 구멍보다 큰 스프라이트로 교체하면
가장자리가 지형에 잘리므로 그때는 클리어 반경(`clearRadius`)을 함께 키울 것.

엘리베이터 프리팹은 자식이 여럿이고(케이블 `ElevatorBack` < 하얀 테두리 `ElevatorWhite` < 본체
`Elevator`) 프리팹에서 `Objects` 레이어 order 100/104/105로 앞뒤를 정해 뒀다. 스폰 코드는 이 앞뒤를
**rank로 보존**해 -60부터 1씩 올려 붙인다(`ApplySortingOrder`). 전부 -60으로 눌러버리면 같은 레이어·
같은 order·같은 z가 되어 그리는 순서가 정해지지 않고, 케이블이 본체를 덮는다. 상한은 -50 —
플레이어 대역(-49~-30)을 침범하지 않는다.

플레이어 파츠(FanalPlayer.prefab)는 order **원본 −50**으로 옮긴다. 상대 순서는 그대로 보존된다:

| 파츠 | 지상 | 지하 |
|---|---|---|
| LeftHand | 1 | -49 |
| LeftFeet | 3 | -47 |
| RightFeet / Backpack2_0 | 4 | -46 |
| Body | 5 | -45 |
| Backpack1_0 | 6 | -44 |
| Head | 10 | -40 |
| Hair | 11 | -39 |
| RightHand | 15 | -35 |
| Drill | 20 | -30 |
| Effect | 1000 | **변경 없음** (`keepAboveOrderThreshold=100` 이상은 제외) |

**돌·광물은 -1 그대로 두어 의도적으로 플레이어보다 앞에 그려진다.** 안 캔 돌·박힌 광물이 플레이어를 가리는 게 "지형 속" 느낌에 맞다.

### 검토했지만 안 한 것 — 지형을 `player`/30으로 올리기 (지상 규칙 그대로 이식)

플레이어 프리팹도, 지상/지하 전환도 필요 없어 처음엔 이쪽이 깔끔해 보였다. 실제 비용을 조사해보니 아니었다.
지형이 `player` 레이어로 올라가면 **지금 `Default`에 있는 게 전부 지형 뒤로 밀린다**:

- 어둠막 Canvas(Default/999), 손전등 FOV 메시(Default/900) → 시야 시스템이 통째로 죽음
- 유물 VFX LineRenderer 15개 파일(Default/50~125)
- 파티클 프리팹(MineralPickupFX, Blizzard, FallingDustWarning, CauldronBubble, MineralSparkle)
- **특수청크 자식 오브젝트 100개+** (사다리·문·레버·상자·종유석·용암·별퍼즐·스위치·케이블…) — 전부 Default/0~2

마지막 항목이 결정타. 이것들은 파진 공간에 있어 대부분 보이긴 하지만 지형 픽셀과 겹치는 부분이 새로 잘려나가고,
특수청크 20종을 전부 눈으로 검증해야 한다. 반면 플레이어를 내리는 쪽은 **다른 어떤 오브젝트 관계도 바뀌지 않는다.**

## 3. 구현

### `PlayerSortingController` (`Assets/Scripts/UI/Player/PlayerSortingController.cs`)

**플레이어 sortingOrder의 단일 소유자.** 외부에서 직접 order를 만지면 모드 전환과 서로 덮어쓴다.

- `SetUnderground(bool)` — 지하/지상 모드 전환
- `SetExtraOrderBoost(int)` — 일시적인 순서 조정(맹인 유물 등). 직접 order를 만지는 대신 이걸 쓴다
- `Refresh()` — 런타임에 자식 스프라이트가 추가된 경우. **이미 등록된 렌더러의 원본값은 다시 캡처하지 않는다** (재캡처하면 적용된 오프셋이 원본으로 굳는다)

캡처 대상은 "`player` 레이어에 있던 몸통 파츠"뿐이다. 이미 `Default`인 것(FlashLight, EncumbranceIcon — 둘 다 order 0)은
지금도 지형과 같은 order라 건드리지 않는다.

**부착은 자동**이다. `RuntimeInitializeOnLoadMethod` + `sceneLoaded`로 씬 로드마다 "Player" 태그 오브젝트에 붙인다
(SoundManager 자동 생성과 같은 패턴). 프리팹을 수정하지 않으므로 **지상 값이 곧 프리팹 원본값**이고, 되돌리기도 쉽다.

### ⚠ 매 프레임 재적용이 필수 — 애니메이션이 sortingOrder를 애니메이트한다

이 작업에서 가장 오래 걸린 함정. 플레이어 애니메이션 클립 5개가 `m_SortingOrder`를 **직접 애니메이트**한다.

| 클립 | 대상 path | 값 |
|---|---|---|
| `Dig/gokdig.anim`, `gokdig1`, `gokdig2` | `BodyBone/ArmPivot/RightArmBone/RightHandBone/RightHand` (=들고 있는 도구) | 11 |
| `Climbidle.anim`, `Climbing.anim` | `BodyBone/Body` | 13 |

Unity Animator는 **어느 클립에서든 애니메이트되는 프로퍼티를 매 프레임 관리**한다. 커브가 없는 상태를 재생 중이어도
Animator 활성 시점에 캐시해둔 기본값(RightHand=15)을 계속 다시 쓴다. 그래서 Start에서 한 번 세팅하는 방식은
다음 프레임에 바로 지워진다.

증상이 헷갈린다 — 애니메이션은 `m_SortingLayerID`는 건드리지 않으므로 **레이어는 `Default`로 남고 order만 원복**된다.
겉보기엔 "몸통은 지형 뒤로 갔는데 삽만 지형 위"가 되고, 로그를 Start 시점에 찍으면 `-35`로 정상이라 더 헷갈린다.
Play 중 Inspector에서 `RightHand`의 Sorting Layer=`Default` / Order=`15`인 조합을 보면 이 증상이 확정이다.

→ `LateUpdate`에서 매 프레임 재적용한다. 애니메이션이 정한 앞뒤 연출(도구가 몸 앞/뒤로 가는 것)을 죽이지 않으려면
**애니메이션이 쓴 값을 새 원본(baseOrder)으로 채택**하고 그 위에 대역 오프셋만 다시 얹어야 한다.
"우리가 마지막에 쓴 값(`lastWritten`)과 현재값이 다르면 애니메이션이 덮어쓴 것"으로 판별한다.

### 지하 판정

`InfinityMapManager.Instance != null` — 이 매니저는 지하 씬에만 있다(DemoUpground: 0개, DemoUnderground: 1개).

### ⚠ 던전은 씬이 아니라 같은 씬 안의 텔레포트다

`DungeonOverlayController`는 던전 프리팹을 먼 offset에 Instantiate하고 **같은 플레이어 오브젝트를 텔레포트**시킨다.
씬 판정으로는 걸러지지 않는다. 그런데 던전 지오메트리는 이렇게 흩어져 있다:

| | 레이어/order |
|---|---|
| 던전 타일맵 / Level1Chest | Default/0 |
| Door·Lever·Level1Air1 | Objects/30~50 |
| DunDoor·Level1Air2~5 | Map/50 |

지하 모드를 그대로 들고 들어가면 **플레이어가 던전 바닥·벽 타일맵에 통째로 가려진다.**
→ `EnterRoutine`에서 `SetPlayerUndergroundSorting(false)`, `ExitRoutine`에서 `(true)`.
시야 오버레이를 껐다 켜는 자리와 같은 지점이다.

### `PlayerVisionOverlay` 소유권 정리

두 곳을 고쳤다.

1. `BoostPlayerRenderers` — 컨트롤러가 있으면 `SetExtraOrderBoost`에 위임한다.
   기존처럼 자체적으로 order를 저장·복원하면, 부스트가 걸린 상태에서 모드가 바뀔 때 캡처값이 굳어 order가 영구 오염된다.
   (컨트롤러가 없을 때를 위한 기존 폴백 경로는 남겨뒀다.)

2. `ApplyForcedSorting` — 강제 활성 시 어둠막 캔버스를 **플레이어와 같은 레이어**에 둔다.
   지하 모드에서 플레이어는 `Default`인데 어둠막만 `player`로 올리면, order 부스트로도 플레이어를 어둠 위로 꺼낼 수 없다(레이어 우선).

### `RockSpawner`의 존재하지 않던 정렬 레이어

`sr.sortingLayerName = "ground"` — 프로젝트에 `ground`라는 정렬 레이어는 없다. 잘못된 이름은 조용히 무시되어
실제로는 `Default`로 동작하고 있었다. 지형·광물·플레이어와 같은 레이어여야 order 비교가 성립하므로 `Default`를 명시했다.

## 4. 수정한 파일

| 파일 | 변경 |
|---|---|
| `Assets/Scripts/UI/Player/PlayerSortingController.cs` | **신규** |
| `Assets/Scripts/Gameplay/Dungeon/DungeonOverlayController.cs` | 던전 진입/이탈 시 모드 전환 |
| `Assets/Scripts/Render/Lighting/PlayerVisionOverlay.cs` | 부스트 위임 + 어둠막 레이어를 플레이어와 일치 |
| `Assets/Scripts/Render/World/ChunkBackground.cs` | 배경 order -3 → **-100** |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Decoration/RockSpawner.cs` | `"ground"` → `Default` 명시 |
| `Assets/Prefabs/World/BackgroundTile.prefab` | -3 → **-100** |
| 특수청크 배경 12개 프리팹 | -2 → **-99** (RockRolling, Object/background, Crabas, IceWinding, JumpMap_2/3/4, LavaJump, Mine, Mine2, Oxidized1, StarPuzzle) |

플레이어 프리팹과 특수청크 지형·자식 오브젝트는 **건드리지 않았다.**

## 5. ⚠ 동작이 바뀌는 지점 — 인게임 확인 필요

- **어둠막이 이제 플레이어를 덮는다.** 기존엔 플레이어가 `player` 레이어라 어둠막(Default/999) 위에 항상 그려졌다.
  플레이어는 시야 원의 중심이라 비네트가 투명한 영역에 있어 실사용상 차이가 없어야 하지만, 확인할 것.
- **손전등 FOV 마스크(Default/900)가 플레이어 위로 온다.** 빛 마스크면 자연스럽지만 어둡게 깔리는 마스크라면 플레이어가 어두워진다.
- **플레이어가 지형에 파묻히면 완전히 안 보인다.** 텔레포트 착지에서 청크 로드 전엔 보이다가 로드되는 순간 사라지므로 버그처럼 읽힌다.
  (착지 보호 절차는 CLAUDE.md 아키텍처 제약 §13 참고)
- **X-ray 중 돌이 지형 위로 올라간다**(`XRayController` +300) → 그동안 플레이어가 돌에 가린다.
- `GameOverSequenceUI`(게임오버·긴급탈출 연출)는 "검은 배경에 플레이어만" 남기려고 플레이어를 검은 장막 위로 올린다.
  **이때 정렬을 직접 만지면 안 된다** — 지하에서 `PlayerSortingController`가 매 `LateUpdate`마다 플레이어를 Default 레이어로
  되돌리고, 장막을 최상위 레이어에 두면 레이어 우선순위 때문에 플레이어가 장막에 통째로 묻힌다(= "Die 모션이 안 나온다"는 증상).
  그래서 연출은 **장막을 플레이어와 같은 레이어(지하=Default)에 order 20000으로 깔고**, 플레이어는 소유자의
  `SetExtraOrderBoost`로 그 위로 올린다. 소유자가 매 프레임 (애니메이션 order + 지하오프셋 + 부스트)를 재적용하므로
  Die 클립이 Body/RightHand의 order를 기본값으로 되돌려도 다시 끌어올려준다. 소유자가 없는 상황(지상 등)에서만 최상위 레이어로 직접 올린다.
  장막이 가려야 하는 시야막(999)·손전등(900)·광물 스파클(1000+)이 전부 Default라 order 20000이면 전부 덮인다.

## 6. ⚠ 함정 — 청크 배경이 지형과 똑같은 암석 그림이다

`ChunkBackgroundTable`의 배경 스프라이트(`wall_level1_type1.png` 등)는 지형과 구분이 안 되는 암석 덩어리 아트다.
그래서 **파진 굴 안에 선 플레이어가 "지형 위에 떠 있는" 것처럼 보인다** — 실제로는 배경(-100) 위, 지형(0) 아래로 정상 동작 중이다.

정렬이 의심되면 `PlayerSortingController`의 `logSorting`을 켜서 실제 적용값을 먼저 확인할 것.
같은 레이어(`Default`)에서 order -35인 스프라이트가 order 0인 지형 위로 올라오는 일은 없다.
Play 중 Scene 뷰에서 해당 암석을 클릭해 `Background`인지 `GroundChunk`인지 보는 것도 10초짜리 확인법이다.

굴 안에서 플레이어를 배경보다 뒤로 보내는 건 불가능하다 — 배경은 청크 전체를 덮으므로 플레이어가 통째로 사라진다.

## 7. 새 전환 지점을 추가할 때

플레이어가 **지형이 `Default`가 아닌 공간**으로 이동하면 반드시 `SetUnderground(false)`로 되돌려야 한다.
현재 그런 공간은 지상 씬과 던전 둘뿐이다. 새 던전/특수 공간을 만들 때 이 목록을 갱신할 것.
