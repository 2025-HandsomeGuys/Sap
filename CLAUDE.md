# Sap-UVCS — Claude Code 컨텍스트

Unity 2D 지형 파기 게임. 지형은 픽셀 단위로 파괴되며, 청크 단위로 관리된다.

## 버전 관리

이 프로젝트는 **UVCS (Unity Version Control, 구 Plastic SCM)** 를 사용한다.
`git status`, `git diff`, `git log`, `git commit` 등 git 명령어를 사용하지 않는다.

---

## 테스트

Unity Test Runner (EditMode / PlayMode) 실행은 **사람이 직접** 수행한다.
Claude는 `mcp__mcp-unity__run_tests` 등 Unity 테스트 실행 도구를 호출하지 않는다.

### `Assets/Tests/EditMode/UpgradeTreeCostTests.cs`는 건드리지 않는다

업그레이드 트리의 가격·좌표·선행을 바꾸면 이 파일의 기대값(총액·최소경로·면허가·구매리듬)이
어긋난다. 그때 **기대값을 같이 고치지 않는다.** 고치면 "테스트가 통과하도록 테스트를 고친" 꼴이라
트리를 지키는 장치가 사라진다.

데이터만 바꾸고, **어떤 테스트가 어떤 값에서 왜 실패하는지 표로 보고**한다.
기대값을 갱신할지는 사람이 정한다.

---

## 파일 검색

코드 파일 위치를 찾을 때는 Grep으로 `@tags:.*{키워드}` 패턴을 먼저 검색한다.
전체 파일 내용을 읽기 전에 이 방식으로 후보를 좁힌다.

예시: "콜라이더 관련 파일" → `Grep(@tags:.*collider, Assets/Scripts)`

---

## 디렉토리 구조

```
Assets/Scripts/
├── _Core/
│   ├── Data/          — ChunkData, WorldSettingsData, ItemSO, Excel 데이터 구조체
│   ├── Interfaces/    — 공통 인터페이스
│   ├── Items/         — 아이템 기본 타입
│   └── Managers/      — GameManager, SaveManager, TileDataManager, DayCycleManager, SoundManager
├── Gameplay/
│   ├── Environment/   — BedInteractable, BuffZone
│   ├── Terrain/Tiles/ — 지형 청크 시스템 (핵심 파일 섹션 참고)
│   │   ├── Chunk/     — TerrainChunk, TerrainCollider, TerrainVisualizer, ChunkJobScheduler, TerrainModifier
│   │   ├── Generation/— TerrainGenerator, TerrainBlender, TerrainCarver, Pipeline/
│   │   └── SpecialChunks/ — RollingRockEntity, IcicleHazard, CrystalBlockEntity 등
│   └── Zones/         — ZoneEffectTrigger, IZoneEffect
├── Render/
│   ├── Lighting/      — GlobalLightingManager, FlashlightController, FlashlightFOVBuilder
│   ├── FogOfWar/      — FieldOfView, PlayerVisionOverlay
│   └── World/         — 배경·월드 렌더링
├── Systems/
│   ├── Quest/         — 퀘스트 시스템
│   └── Tutorial/      — 튜토리얼
├── UI/
│   ├── Player/        — PlayerController, StaminaManager, PlayerMining, EncumbranceController, PlayerInputHandler
│   │   ├── Stats/     — 스탯 UI 및 프로바이더
│   │   ├── Strategies/— 플레이어 전략 패턴
│   │   └── Tools/     — 드릴·도구 UI
│   ├── Items/
│   │   ├── Equipments/— 장비 잠금/해제 UI
│   │   ├── Minerals/  — 광물 UI (낙하·픽업)
│   │   └── Warehouse/ — 창고 UI
│   ├── Inventory/     — 인벤토리 UI
│   ├── Shop/          — 상점
│   ├── Settlement/    — 정착지
│   ├── NPC/           — NPC 상호작용
│   └── Upgrade/       — 업그레이드 UI
├── Camera/            — 카메라 제어
└── Utils/             — 디버그, 에디터 확장, 테스트 헬퍼
```

---

## 핵심 파일

| 파일 | 역할 |
|------|------|
| `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs` | 청크 생명주기·LateUpdate 파이프라인 총괄 (partial class) |
| `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.Data.cs` | InfinityMapManager partial — 데이터/필드 정의 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainChunk.cs` | 청크 렌더링·물리 담당 MonoBehaviour |
| `Assets/Scripts/_Core/Data/ChunkData.cs` | 청크 순수 데이터 컨테이너 (NativeArray 관리) |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainCollider.cs` | 콜라이더 메시 생성 담당 (TerrainChunk에서 분리) |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainVisualizer.cs` | 텍스처 비주얼 담당 (TerrainChunk에서 분리) |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/ChunkJobScheduler.cs` | Burst 잡 스케줄·완료 폴링 |
| `Assets/Scripts/Gameplay/Terrain/Tiles/Chunk/TerrainModifier.cs` | 파기 로직 |
| `Assets/Scripts/_Core/Data/WorldSettingsData.cs` | worldSettings.json 매핑 구조체 |
| `Assets/StreamingAssets/worldSettings.json` | 런타임 설정값 (colliderUpdateInterval 등) |
| `Assets/Scripts/UI/Items/Minerals/MineralLifetime.cs` | 월드 광물 60초 자동소멸 타이머. `DespawnCoroutine` → `MineralGenerator.ReturnToPool()` |
| `Assets/Scripts/UI/Player/CollectionCodex.cs` | 도감 '발견' 전역 static 저장소(순수 C#). `CodexCategory`(광물/장비/아이템/유물) 4종을 한 벌로. 획득 훅: 광물=`MineralInventory.AddItem`·아이템=`ItemInventory.AddItem`·장비=`EquipmentInventory.AddItem`·유물=`RelicInventory.Grant`이 `Discover(cat, id)` 호출(첫 발견이면 true·`OnDiscovered` 이벤트). id는 각 enum의 `ToString()`. 세이브는 `EquipmentUpgradeStore`와 같은 패턴(`CaptureSaveData`/`ApplySaveData`, `PlayerData.collectionCodex`), 뉴게임은 `Clear()` |
| `Assets/Scripts/UI/Player/CodexOverlayUI.cs` | 도감 오버레이(전부 코드 생성, `UISkin`). **4탭**(광물/장비/아이템/유물), Q/E 또는 클릭으로 전환. 좌측 카테고리별 그룹 격자(광물=단계·장비=부위·아이템=효과유형·유물=패시브/액티브, 미발견=실루엣+???)+우측 상세. WASD/마우스 공용 포커스로 상세 갱신. 열 때 창고·가방·보유유물을 발견 백필(구버전 세이브 보정). **일시정지 메뉴(`PauseOverlayUI`) '도감' 버튼 → `UIState.Codex`**로 열림. 인스펙터 `backgroundSprite`로 장식 배경 지정 가능. loc 키는 `ui_codex_*`. 광물 첫 발견 시 획득 토스트(`AcquisitionNotifier.NotifyMineral(..., isNew)`)에 '새 광물!' 뱃지(`ui_codex_new_badge`) — 호출부가 `AddItem` 전에 `IsDiscovered`로 판정 |
| `Assets/Scripts/_Core/Managers/DayEarningsLedger.cs` | 하루 골드 증감 정적 장부. 상점·주식·코인·업그레이드가 `Report()`로 기록, 침대 정산에서 소비. `PlayerData.dayEarnings`로 세이브 연동 |
| `Assets/Scripts/UI/DaySummary/DaySummaryUI.cs` | 수면 시 하루 정산 연출 오버레이(전부 코드 생성, 씬 세팅 불필요). 클릭=한 블럭 스킵, 꾹 누르면 빨리감기. `BedInteractable.SleepRoutine`이 재생 |
| `Assets/Scripts/UI/Settings/SettingsOverlayUI.cs` | 설정 오버레이(전부 코드 생성). 호출 화면 캡처→다운샘플 블러+어둡게 깔고 위에 패널 표시. `GameManager.OpenSettings()`→`SettingsOverlayUI.Open()`. 열려 있는 동안 `UIStateManager`가 전역 단축키 차단(`IsOpen`/`ClosedThisFrame`). 구 `SettingsScene` 전환 방식은 미사용. 인스펙터 도트 스프라이트(테두리/탭/버튼/값상자/**선택강조**) 지정 시 코드 생성 라운드 대신 9-슬라이스로 대체. **키보드 조작**: W/S 줄 이동(꾹 누르면 반복, 순환 없음)·A/D 값 조절·**Space 선택**·ESC 취소. (UI의 확인·선택 키는 전부 스페이스바로 통일 — E는 월드 상호작용 전용) 포커스 줄에는 강조 이미지(`focusSprite`/`focusPadding`/`focusSpriteAlpha` — 흰색 꽉 찬 스프라이트를 넣으면 자동으로 반투명 + 줄 내용 뒤에 깔림, 비우면 코드 생성 테두리)가 붙고 **마우스 호버도 같은 포커스를 공유**한다. 항목은 `RegisterNav` 호출 순서 = 화면 순서이므로 새 줄을 추가하면 그 자리에 자동 편입. **변경은 대기(pending)만 하고 '적용' 버튼에서만 매니저에 커밋(`LoadPending`/`ApplyPending`), '취소'·ESC는 미적용 닫기** — 위젯 get/set은 `_p*` 대기값만 읽고 씀 |
| `Assets/Scripts/UI/Interaction/Elevator/ElevatorStopLayout.cs` | 엘리베이터 정류장 **청크 X의 단일 원천**(순수 static). **층(Y)마다 엘리베이터는 정확히 하나**이고, 그 X를 `XForLayer(층)`이 `MinX`~`MaxX`(−5~+5) 안에서 `(층, Seed)` 해시로 뽑는다 → 아래로 갈수록 좌우 지그재그. 이웃 층끼리는 `MinSeparation`(2)청크 이상 벌리고, 첫 정류장(Y=0)은 구멍 입구 청크(0,0)와 겹치지 않게 x=0을 후보에서 뺀다. **`Seed`는 잠수마다 바뀐다** — `SaveManager.PrepareUndergroundEntry`가 지하 진입 때 한 번 뽑아 `PlayerData.diveElevatorSeed`에 저장하고 `SaveManager.Load`가 되살린다. **여기서 난수를 돌리지 말 것**(청크는 언로드/재로드가 잦아 잠수 내내 같은 값이어야 한다). worldSeed를 안 쓰는 이유: 0이면 세션마다 새로 뽑히고 세이브에 안 남아 잠수 도중 게임을 다시 열면 배치가 바뀐다. 지상 씬엔 ElevatorManager도 InfinityMapManager도 없어서 매니저 의존도 못 한다. 소비처: `ShouldSpawnElevator`(스폰 판정)·`GetStopXForLayer`→`TeleportRoutine`(층 이동)·`ElevatorEntryUI.SelectStop`(지상 진입, **`PrepareUndergroundEntry` 뒤에 계산해야 새 시드가 반영됨**)·`ElevatorTrackerRelic`(예측 탐지) — X를 직접 계산하지 말 것. 범위를 넓히면 그 층에서 엘리베이터를 찾으러 걷는 거리가 그대로 늘어난다 |
| `Assets/Scripts/UI/Interaction/Elevator/ElevatorStopUnlockStore.cs` | 정류장 **해금 기록** 전역 static 저장소(순수 C#). **그 층 엘리베이터에 직접 걸어가 본 적이 있어야** 그 정류장으로 이동할 수 있다. 해금 훅은 `ElevatorController.Update`의 근접 감지(지도 마커 `IMapElevator`와 같은 시점) → `Unlock(yChunkPosition)`. **키는 layerIndex가 아니라 청크 깊이(Y)** — 카탈로그에 정류장을 끼워 넣으면 인덱스가 밀려 구세이브가 엉뚱한 층을 연다. `AlwaysUnlockedLayerIndex`(0, 가장 얕은 층)는 항상 열림 — 아니면 지상에서 아무 데도 못 내려간다. 세이브는 `CollectionCodex`와 같은 패턴(`PlayerData.elevatorStops`, `SaveManager.PersistCodexOnly`가 해금 즉시 덧씀 → 지하 사망해도 유지). 게이트 지점: `ElevatorEntryUI`·`ElevatorOverlayUI`·구 `ElevatorUI`(줄 비활성+`???`)와 `ElevatorManager.TeleportPlayer`(안전망) |
| `Assets/Scripts/UI/Interaction/Elevator/ElevatorOverlayUI.cs` | 지하 엘리베이터 층 선택 오버레이(전부 코드 생성, `UISkin`). `ElevatorManager.OpenElevatorUI`가 **씬에 이 컴포넌트가 있으면 우선 사용**하고, 없을 때만 구 프리팹 `ElevatorUI`로 폴백. '지상으로 나가기'는 `CodeConfirmPopup` 확인 후 `ExploreExitController.ExecuteExit()` |
| `Assets/Scripts/UI/Interaction/Elevator/ElevatorEntryUI.cs` | 지상 엘리베이터 입구의 정류장 선택 오버레이(전부 코드 생성, `UISkin`). 씬에 배치돼 있으면 그 인스펙터 설정을 쓰고, 없으면 `Open()`이 임시 오브젝트를 만들어 기본 스타일로 띄운다 |
| `Assets/Scripts/UI/Interaction/Scene/ExploreExitOverlayUI.cs` | '탐험 종료' 확인 오버레이(전부 코드 생성, `UISkin`). `ExploreExitController`가 **씬에 있으면 우선 사용**하고, 없을 때만 구 `ConfirmationPrompt` 프리팹으로 폴백. UIStateManager 상태는 건드리지 않는다(트리거 재진입 판정과 물리므로) |
| `Assets/Scripts/Render/World/SurfaceLightShaft.cs` + `UI/Interaction/Scene/SurfaceExitBeacon.cs` | 천장 구멍에서 새어드는 **빛기둥 연출** + 그 아래 **E키 지상 복귀**. 두 컴포넌트는 서로를 모른다(따로 붙이고 뗄 수 있음). 빛기둥은 스프라이트를 코드로 굽고(사다리꼴을 알파에 인코딩, 바닥 웅덩이·먼지 포함) **어둠막(`PlayerVisionOverlay`, order 999)보다 높은 `sortingOrder` 1000**으로 그린다 — Light2D는 어둠막에 묻혀서 못 쓴다. 셰이더는 `Custom/SpriteAdditive` → 없으면 `Sprites/Default`로 degrade. 기준점은 **빛이 닿는 바닥**이고 기둥은 위로 뻗는다. E키는 확인창을 직접 만들지 않고 `ExploreExitController.RequestExitToSurface()`에 위임(오버레이 탐색·폴백·정산이 전부 따라옴). **청크 자식으로 붙이지 말 것** — 씬에 고정 배치. 설계: `Assets/Docs/surface-light-shaft.md` |
| `Assets/Scripts/UI/Player/UndergroundMinimap.cs` | 원형 언더그라운드 미니맵(전부 코드 생성). 청크 픽셀 직접 샘플링. `viewRadius`↓=확대. **각진 경계 제거 핵심**: 시야 원 안은 지형을 빠짐없이 채우고(미탐사 공동=암석 톤 은닉, 탐사한 판 굴만 앰버 `tun=empty?e:0`) **어둠은 오직 `_vision` LUT(플레이어 중심 원형 falloff, `visionFullRadius`/`visionEdgeRadius`/`visionFloor`=0)에서만** 생김 → 탐사-셀 경계가 아니라 깔끔한 원. `MapMarkerRegistry`의 돌 **실루엣 모양** 채움(`PaintRockShape`, 마스크 없으면 `PaintRockDisc`)+엘베·입구 마커(`UpdateMarkers`). 플레이어/마커/프레임 스프라이트 인스펙터 지정 가능(`playerMarkerSprite`/`elevatorMarkerSprite`/`entranceMarkerSprite`/`frameSprite`, 비우면 코드 아이콘). **던전 안(`DungeonOverlayController.IsInDungeon`)에선 어두운 빈 화면+`NO SIGNAL` 문구만 표시**(정적 노이즈 없음, 정상 렌더 중단) |
| `Assets/Scripts/UI/Player/WorldMapOverlay.cs` | M 키 전체 지도 오버레이(전부 코드 생성). 휠=확대/드래그=이동. `UIStateManager`의 `UIState.WorldMap` 연동(`SetState(WorldMap)`→`Open()`, `SetState(None)`→`CloseStatic()`). **던전 안에선 M을 눌러도 안 열림**(UIStateManager가 `DungeonOverlayController.IsInDungeon` 체크). 지형은 `MapTerrainCache`에서 읽음. 미니맵과 동일하게 **지형 빠짐없이 채움+어둠은 `VisionAtTexel`(보이는 영역/텍스처 중심 원형 비네트, 팬·줌 무관, `visionFullFrac`/`visionFadeFrac`/`visionFloor`)에서만** → 각진 경계 없음. 돌 실루엣 채움+마커+인스펙터 스프라이트(마커+`panelFrameSprite`/`buttonSprite`). **마커/플레이어 스프라이트는 자체 지정이 비어있으면 미니맵(`UndergroundMinimap`)의 것을 폴백으로 공유** — 미니맵 한 곳만 지정하면 됨 |
| `Assets/Scripts/UI/Player/MapTerrainCache.cs` | 지도용 지형 스냅샷 캐시(static). 로드된 청크를 청크당 RES×RES(96)로 다운샘플 저장 → 멀어져 언로드돼도 미니맵·전체지도가 실제 터널 모양을 또렷하게 유지. 미니맵이 매 프레임 근처 청크를 채워 넣음(`Capture`), 지도는 `TrySampleEmpty`로 조회 |
| `Assets/Scripts/UI/Player/MapRockCache.cs` | 지도용 '안 캔 돌' 실루엣 스냅샷 캐시(static). `DiggableRock`이 노출 시 `Capture`(마스크→GRID×GRID(32) 불리언+월드 AABB, 없으면 원반), 캐질 때(`DestroyRock`) `Remove`. **언로드(풀 반납)만으로는 안 지움** → 파온 경로처럼 멀어져도 지도에 유지. 재로드 시 같은 좌표키로 갱신(중복 없음). 미니맵·전체지도가 `Entries`를 `Sample`로 조회해 채움(광물돌은 별도 색), 변경은 `IsDirty` |
| `Assets/Scripts/UI/Player/MapMarkerRegistry.cs` + `MapMarkerVisuals.cs` | 지도 마커 세션 레지스트리(static)+아이콘 팩토리(돌 채움은 `MapRockCache`로 분리). **엘베**: `ElevatorController.Start`가 로드 시 `Discover(Elevator)`(영속). **청크입구**(`IMapEntrance` — `DungeonEntranceInteractable`/`DungeonDoorChunk`): `PlayerInteractor`가 **상호작용 범위 도달 시** `Discover(ChunkEntrance)`(영속). **이미 탐험해 재입장 불가한 입구**는 그리기 시점에 `IsEntranceUsed`(월드→청크 좌표 변환 후 `DungeonStateStore.IsUsed`)로 판정해 별도 회색 스프라이트(`entranceUsedMarkerSprite`/`MapMarkerVisuals.EntranceUsed`) 사용. `IMapRock`(돌 실루엣 인터페이스) 정의도 이 파일. 미니맵·전체지도가 `Markers` 폴링, 변경은 `IsDirty` |
| `Assets/Scripts/UI/Interaction/Scene/ChunkImagePainter.cs` | PNG → 청크 픽셀 교체 코어(static). 리샘플링·PixelInfo 생성·`LoadChunkData`·`MarkDirty`까지. 청크 로드 대기와 보호좌표 등록은 호출측 책임 |
| `Assets/Scripts/UI/Interaction/Scene/ImageChunkOverrider.cs` | **구멍 입구** 착지 청크 전용 PNG 교체 + 플레이어 활성화. 좌표는 인스펙터 고정. `IChunkPostLoadPainter`로 등록되어 **매 로드마다** 파이프라인 Phase 2 끝에서 호출됨 → 페인팅 후 광물 스폰(`spawnMinerals`). 재로드 시 페인팅은 `IsChunkVisited`가 false일 때만(판 흔적 보존). 배경: `Assets/Docs/elevator-landing-room.md` §7 |
| `Assets/Scripts/UI/Interaction/Scene/ElevatorRoomPainter.cs` | **엘리베이터** 착지 방 PNG 교체. 지층별 `layerRoomImages[]`. 보호좌표·방문청크는 스킵(판 흔적 보존). `ElevatorManager.TeleportRoutine`이 호출. 칠한 좌표를 `IChunkPostLoadPainter`로 등록해 재로드 때마다 광물 재스폰(`spawnMinerals`). 첫 페인팅은 Phase 2가 끝난 뒤라 훅이 안 불려서 그 자리에서 직접 호출 |
| `Assets/Scripts/_Core/Managers/SoundManager.cs` | 사운드 총괄 싱글톤. SFX 원샷 풀(10) + 3D `PlaySFXAt` + 앰비언스 2레이어 크로스페이드(`SetAmbience`) + 상태 루프(`Loop`/`StopLoop`/`SetLoopPitch`). 모든 재생이 `HasSFX` 가드를 타므로 **클립 미등록 시 조용히 무음**(경고 없음). **씬 배치 불필요** — `RuntimeInitializeOnLoadMethod`로 자동 생성되고 `soundData`/`Master` 믹서를 `Resources`에서 자동 해석한다(둘 다 `Assets/Resources/`에 있어야 함) |
| `Assets/Scripts/_Core/Managers/SfxKeys.cs` | 효과음 키 상수 전체(39개). 재생 호출은 **반드시 이 상수를 통해서만** 한다(리터럴 오타는 무음으로 조용히 넘어감). `SfxKeys.All`은 에디터 툴의 미등록 키 리포트용 |
| `Assets/Scripts/Audio/AmbienceDirector.cs` | 시간대·층에 따라 앰비언스 자동 전환. 판정은 순수 로직 `AmbienceSelector.Select(isSurface, time, layer)`로 분리(EditMode 테스트 있음). 던전 안에선 정지 |
| `Assets/Scripts/Audio/FootstepPlayer.cs` + `HeartbeatSfx.cs` | 플레이어 프리팹 부착. 발소리(속도 연동 간격, 지상=풀밭/지하=흙), 스태미나 저하 시 심장 루프(낮을수록 pitch↑) |
| `Assets/Scripts/Editor/SfxFolderImporter.cs` | `Tools/Sound/Rescan SFX Folder` — `Assets/Audio/SFX/`의 **파일명을 그대로 키로** `SoundData.asset`에 일괄 등록. 아직 음원이 없는 키를 콘솔에 나열해준다 |
| `Assets/Scripts/Utils/BugReport/BugReportSystem.cs` | **F12 = 버그 리포트**(팀 QA용). 스크린샷+상태+로그+세이브를 `persistentDataPath/BugReports/<타임스탬프>/`에 저장. `RuntimeInitializeOnLoadMethod`로 자동 생성(씬 세팅 불필요), `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD \|\| ENABLE_BUG_REPORT`로만 컴파일. **캡처는 오버레이보다 먼저** — 순서를 바꾸면 리포트에 자기 UI가 찍히고 값이 "그 시점"이 아니게 된다. 새 상태값을 넣고 싶으면 `BugReportCollector.ExtraContext` 이벤트를 한 줄 구독(수집기 수정 불필요). 설계: `Assets/Docs/bug-report-system.md` |
| `Assets/Scripts/Utils/DebugConsole/` | **디버그 콘솔 + QA 세이브 픽스처**(개발 전용, 씬 배치 불필요). `` ` ``/F9 콘솔, `[`/`]` 시간 배속, `fx` 픽스처. 배속은 `DebugTimeScaleDriver`가 **LateUpdate에서 매 프레임 재적용**한다 — 프로젝트 20여 곳이 `Time.timeScale = 1f`를 하드코딩해서 한 번 세팅으로는 상점 한 번 여닫으면 풀린다. 설계: `Assets/Docs/qa/debug-console-and-fixtures.md` |
| `Assets/Scripts/Utils/Diagnostics/TerrainSeamWatchdog.cs` | **청크 이음매 통과 감시기**(개발 전용, 씬 배치 불필요). "흙은 그대로인데 청크 사이로 몸이 빠지는" 버그 전용. 재현이 안 되고 지형·콜라이더 둘 다 사후 증거가 안 남아서 상시 관찰로 간다. 탐지 4종 — `EMBEDDED`(중심 픽셀 solid 2스텝 연속)·`TUNNEL`(한 스텝에 경계 넘어 순간이동)·`NOCOVER`(지형 안쪽인데 콜라이더가 안 덮음)·`TRACE`(`TerrainCollider.OnAnomaly` — 닫히지 않은 윤곽). 걸리면 **지형 vs 콜라이더 21×21 지도 + 3×3 청크 콜라이더 표 + 이음매 1px 스캔 + 직전 40스텝 궤적**을 로그로 찍고, 심각 사고는 F12 리포트를 자동 캡처(세션당 3회). 콘솔 `seam`으로 토글. **NOCOVER 오탐 방지 3제외**(표면 3px·청크 가장자리 3px·불괴 오버레이 청크)를 줄이면 삽질할 때마다 울려서 아무도 안 본다. 설계: `Assets/Docs/qa/terrain-seam-watchdog.md` |
| `Assets/Scripts/_Core/Telemetry/` | 플레이 텔레메트리(로컬 JSONL). `Telemetry.Log(name, payload)` 한 줄로 기록하고 버퍼·파일은 뒤에서 처리. 공통 필드에 `run_id`(회차)·`is_fixture`(디버그 조작 여부)가 실린다 — 밸런스 CSV가 이 둘로 회차를 가르고 조작된 판을 걸러낸다. 밸런스판은 `Tools/telemetry/export_csv.py`로 `days.csv`/`dives.csv`를 굽는다. 설계: `Assets/Docs/telemetry/design.md` + `balance-csv-design.md` |
| `Assets/Scripts/UI/Core/CodeHeldItem.cs` | 코드 오버레이(창고·지하 인벤토리) 공용 **"손에 집기"** 컨트롤러. 좌클릭=스택 집기, 우클릭=1개 내려놓기, 휠=개수 조절. **집을 때 데이터를 안 뺀다**(손 개수만 추적) — 실제 이동/버리기만 호스트(`CodeHeldItem.IHost`)의 기존 로직(`MoveBetween`/`Withdraw`/`Deposit`/`AskDiscard`)으로 처리해 무게·용량·세이브 정합성 유지. 드래그는 그대로(손에 든 동안만 시작 차단). 키보드 스페이스는 옛 즉시 이동 유지. 옛 `HeldItemManager`(프리팹 UI 전용)와 별개. 설계: `Assets/Docs/ui-held-item-system.md` |
| `Assets/Scripts/Gameplay/Dungeon/Authoring/Generation/` | 던전 절차 생성. 형태 마스크(`Shapes/*.txt`) → 미로 배선 → 방 조립 → E/X 배치 → 공동 메우기 → 슬롯 채우기. 에디터 창은 `Tools/Dungeon/Map Importer`. 설계: `Assets/Docs/dungeon-generation/maze-and-shape-design.md` |
| `Assets/Docs/` | 설계 결정 문서 디렉토리 |

---

## 아키텍처 제약 — 반드시 지켜야 할 것

### 1. `InfinityMapManager`는 partial class
수정 전에 `InfinityMapManager.cs`와 `InfinityMapManager.Data.cs` 양쪽을 확인할 것.

### 2. 콜라이더와 비주얼 파이프라인은 의도적으로 분리되어 있다
- `TryUpdateCollider()`는 `ApplyTexture()`와 독립적으로 호출된다.
- `UpdateCollider()`를 `ApplyTexture()` 안으로 다시 넣으면 드릴 연속 파기 중 콜라이더가 영구 차단되는 버그가 재발한다.
- 배경: `Assets/Docs/collider-visual-decoupling.md`

### 3. 더티 플래그는 반드시 ChunkData 메서드로 세팅
직접 플래그 3개를 나열하지 말고 메서드를 사용한다.

```csharp
// 금지
data.IsVisualDirty = true;
data.IsColliderDirty = true;
data.HasBeenModified = true;

// 올바른 방법
data.MarkDirty();        // 지형 픽셀 수정 시 (저장 포함)
data.MarkRenderDirty();  // 비주얼·콜라이더 dirty (저장 제외) — HasBeenModified는 false 유지
```

### 4. `colliderUpdateInterval`은 static
`TerrainChunk`의 `s_colliderUpdateInterval`은 모든 인스턴스가 공유한다.
변경 시 `TerrainChunk.SetDefaultColliderUpdateInterval()`을 사용한다.
`worldSettings.json`의 `chunk.colliderUpdateInterval` 값이 Awake에서 자동 적용된다.

### 5. LateUpdate 패턴 — 비주얼 업데이트는 매니저가 일괄 처리
`TerrainChunk` 내부에서 직접 `ApplyTexture()` / `RefreshVisuals()`를 호출하지 않는다.
파기 후 `InfinityMapManager.MarkChunkDirty()`를 호출하면 매니저가 `ProcessDirtyChunksAsync`로 처리한다.

### 6. 콜라이더 dirty 추적은 `_dirtyColliderChunks` 집합 사용
`MarkChunkDirty()` 호출 시 자동으로 집합에 추가된다.
LateUpdate 콜라이더 루프는 전체 청크 순회 없이 이 집합만 순회한다.

### 7. `IndestructibleMask` — 파기 불가 픽셀 처리
`ChunkData.IndestructibleMask[idx] != 0`인 픽셀은 파기·콜라이더 생성에서 제외된다.
`data.HasIndestructiblePixels`가 true인 청크에서만 인접 8방향 보호 체크(`IsAdjacentToIndestructible`)가 활성화된다.
직접 `IndestructibleMask`를 세팅하지 말고 오버레이 시스템을 통해 초기화할 것.

### 8. 특수 청크 자식 오브젝트 배치 — 반드시 프리팹 직접 편집
씬 인스턴스에 자식을 드래그 후 Apply to Prefab하면 local position이 틀어진다.
`local position = child world - chunk root world` → 청크가 y=-41에 있을 때 world y=-2 오브젝트의 local Y = 39.22 (유효 범위 초과).
반드시 Project 창에서 프리팹을 더블클릭(Prefab Edit 모드)한 뒤 자식을 배치한다.
청크 1칸 = 10유닛 (1000px ÷ 100PPU). 자식 local position 유효 범위: X(0~10), Y(0~10).

### 12. 골드 증감 지점 추가 시 `DayEarningsLedger.Report()` 병행 호출
플레이어 골드가 움직이는 새 코드(`AddGold`/`SpendGold`)를 추가하면
`DayEarningsLedger.Report(카테고리, ±금액)`도 함께 호출해 하루 정산에 반영한다.
누락돼도 최종 손익은 어긋나지 않고(하루 시작 골드 대비 실제 차이로 계산) '기타' 항목으로 흡수된다.

### 13. 플레이어를 먼 좌표로 텔레포트할 때는 착지 보호 필수
청크가 로드되지 않은 좌표로 순간이동시키면 콜라이더가 없어 플레이어가 낙하하고,
몇 백 ms 뒤 청크가 로드되면서 지형 속에 파묻힌다.

`Rigidbody2D`를 Kinematic으로 고정 → `InfinityMapManager.HasChunk(목적지)` 대기 →
1프레임 대기(콜라이더 생성) → Dynamic 복귀 순서를 지킨다.

구현 참고: `ElevatorManager.TeleportRoutine()`, `PlayerSpawner.SpawnRoutine()`
배경: `Assets/Docs/elevator-landing-room.md` §1-A

### 10. 광물 수명 타이머 — "MineralDug" 태그로 상태 구분
`MineralItemController.WakeUp()` 호출 시 `gameObject.tag = "MineralDug"` 설정.
`OnEnable()`(풀 재사용 시)에서 `gameObject.tag = "Untagged"`로 초기화.

`InfinityMapManager.DetachMineralsToWorld()`는 이 태그로 처리를 분기한다:
- `"MineralDug"` 태그 있음 → 월드 분리 + `MarkMineralCollected()` + `MineralLifetime` 타이머 부착
- 태그 없음 (땅 속 광물) → `MineralGenerator.ReturnToPool()`만 호출, collected 마킹 없음 → 청크 재로드 시 재생성

**주의:** Unity Project Settings > Tags & Layers에 `MineralDug` 태그가 등록되어 있어야 한다.
없으면 `WakeUp()`에서 UnityException 발생.

### 11. 특수 청크 파진 픽셀 복원 — `RestoreSavedPixels` 경로
특수청크(IChunkInitializer 보유 TC)는 언로드 시 `wasNormalChunk=false`로 저장된다.
재로드 시 `ChunkDataProvider` 1-B 분기에서 `context.SavedData`에 데이터 전달 → `SpecialChunkFactory`가 `tChunk.RestoreSavedPixels()` 호출.

**호출 순서 필수**: `IChunkInitializer.Initialize()` → `ApplyBorderDataOnly()` → `RestoreSavedPixels()`
`Initialize()`에서 세팅된 `IndestructibleMask`가 유지되어야 하므로 순서를 바꾸면 안 된다.
`RestoreSavedPixels()`는 `ScheduleInitJobOnly()` 미호출 — 특수청크는 `FinishVisualsAfterInit(skipInit:true)`로 처리됨.

### 14. 새 효과음은 `SfxKeys` 상수 → `SoundManager` 경로로만 추가
인스펙터에 `AudioClip`을 직접 물리지 않는다. `SfxKeys`에 키를 정의하고
`SoundManager.PlaySFX(SfxKeys.X)` / `PlaySFXAt(...)` / `Loop(...)`을 호출한다.
음원은 `Assets/Audio/SFX/`에 **키와 같은 파일명**으로 넣고
`Tools/Sound/Rescan SFX Folder`를 실행하면 등록된다.

기존에 인스펙터 `AudioClip`을 쓰던 컴포넌트(`IceBreakable`, `FragileIceBlock`,
`IndestructibleHitFeedback`)는 **인스펙터 클립 우선, 없으면 키 폴백** 구조다.
프리팹에 이미 연결된 참조를 깨지 않기 위한 것이니 순서를 뒤집지 말 것.

앰비언스는 `SoundManager.SetAmbience(layer, key)`를 쓴다. `BGM` 그룹의 자식인
`Ambience` 믹서 그룹을 타므로 BGM 볼륨 슬라이더에 함께 묶인다.

폭발음은 `InfinityMapManager.ExplodeTerrain()` 한 곳에 있다 — 개별 폭발 호출부
(`ScrapExplosion`/`DelayedBlast`/`ExplosiveMineralReactor`/`DashBombRelic`)에
따로 넣지 말 것. 중복으로 울린다.

유물 공통 발동음은 `RelicManager.ActivateSlot()`에 있고, 전용음이 있는 유물은
`RelicBehaviour.HasOwnActivationSfx`를 override해 공통음을 끈다.

배경: `Assets/Docs/audio/sound-system-design.md`

### 16. 플레이어 sortingOrder는 `PlayerSortingController`가 단독 소유
지하에서 플레이어는 지형과 같은 `Default` 레이어의 음수 order(-49~-30)로 내려가 지형 뒤에 그려진다.
지상·던전에서는 원래 `player` 레이어로 돌아온다.

플레이어 스프라이트의 `sortingOrder`/`sortingLayerID`를 직접 대입하지 말 것 — 모드 전환과 서로 덮어쓴다.
일시적인 순서 조정은 `PlayerSortingController.SetExtraOrderBoost()`를 쓴다.

**플레이어가 지형이 `Default`가 아닌 공간으로 이동하면 반드시 `SetUnderground(false)`로 되돌린다.**
현재 해당 공간은 지상 씬과 던전(`DungeonOverlayController`) 둘뿐이다.

지하 정렬 대역: 청크 배경 -100 < 특수청크 배경 -99 < 엘리베이터 -60(`ElevatorSpawner.SortingOrder`) < **플레이어 -49~-30** < 돌·광물 -1 < 지형 0
배경 order를 올리거나 돌·광물을 내리면 플레이어 대역을 침범한다.

**애니메이션 클립 5개가 `m_SortingOrder`를 애니메이트한다**(`RightHand`=들고 있는 도구, `Body`).
Animator가 매 프레임 그 값을 다시 쓰므로 컨트롤러는 `LateUpdate`에서 재적용한다 — 한 번 세팅으로는 안 된다.
플레이어 스프라이트에 새 정렬 애니메이션을 넣으면 이 규칙이 흡수하니 그대로 두면 된다.

배경: `Assets/Docs/player-terrain-sorting.md`

### 17. 돌은 프리팹으로 정의된다 — SR·콜라이더·DiggableRock은 반드시 같은 GameObject

`TileVisualSettings.rockPrefabs[]`가 돌 종류 목록이다. 스프라이트·균열·티어·조각·HP는
전부 프리팹 인스펙터에 있고, `RockSpawner`는 `Instantiate`만 한다.

**`SpriteRenderer` + `PolygonCollider2D` + `DiggableRock`을 자식으로 쪼개지 말 것.**
파기 호출부 5곳(`Digger`×3, `SapStrategy`, `PickaxeStrategy`, `PlasmaCutterRelic`)이
전부 히트한 콜라이더에서 `GetComponent<IDiggable>`을 부른다 — `GetComponentInParent`가 아니다.
`PolygonCollider2D`가 같은 GO의 SR에서 모양을 굽는 것도 이 구조에 의존한다.

**돌의 지형 좌표를 계산할 때 `transform.localPosition`을 읽지 말 것.**
히트 애니메이션(`RockHitAnimator`)이 transform을 흔드는 동안 값이 어긋난다.
`DiggableRock.AnchorLocal`(픽셀 좌표)과 `BaseAngleZ`(세이브)가 배치 확정값이고,
`RockSpawner.SetPlacement()`가 주입한다.

히트 클립은 **원점 기준**(pos 0 / rot 0 / scale 1)으로 만든다. 실제 배치 회전은 코드가 얹는다.
`RockHitAnimator.duration`은 클립 길이에 맞추며 Loop 사고를 막는 하드 타임아웃도 겸한다.

배경: `Assets/Docs/rock-prefab-and-hit-animation.md`

### 18. 엘리베이터 상호작용 구현은 두 갈래 — `IElevatorStop`으로만 다룰 것

`ElevatorController`(구)와 `WorldInteractable`+`ElevatorBehaviour`(통합) 두 갈래가 있고,
**실제 프리팹 `Assets/Prefabs/World/ElevatorPrefab.prefab`에는 후자만 붙어 있다.**
`ElevatorController`를 참조하는 프리팹·씬은 프로젝트에 하나도 없다.

그래서 `is ElevatorController` / `GetComponent<ElevatorController>()`로 판정하면
**조용히 아무 일도 일어나지 않는다.** 실제로 이것 때문에 두 가지가 동시에 죽어 있었다:
- `ElevatorSpawner`가 좌표·layerIndex를 컨트롤러에만 써넣어 → 모든 엘리베이터가 `x=0, layer=0`
- `ElevatorManager.RegisterElevator`가 컨트롤러 타입만 받아 → 등록이 통째로 비어 목록이 전부 '미로드'

또 하나: 프리팹의 상호작용 컴포넌트는 **루트가 아니라 자식 오브젝트 `Elevator`** 에 붙어 있다.
`GetComponent`로 찾으면 null이라 주입·등록이 통째로 조용히 건너뛰어진다 — 반드시
`GetComponentInChildren<T>(true)`를 쓴다. 스포너는 둘 다 못 찾으면 에러 로그를 찍는다.

정류장 좌표·해금·등록은 전부 `IElevatorStop`(`StopDepth`/`StopXChunk`/`StopLayerIndex`/
`StopTransform`/`NotifyReached`)을 통한다. 새 엘리베이터 구현을 만들면 이 인터페이스를 구현하고,
스포너는 `SetElevatorPlacement`로 좌표를 주입한다.

⚠ `GetElevatorAt`의 반환값은 **인터페이스**라 Unity의 `==` 오버로드를 안 탄다 —
파괴된 오브젝트가 `null`로 안 잡히므로 `is Component c && c == null` 체크를 지울 것.

---

### 9. `DiggableRock` 프리팹 직접 배치 시 필수 설정
RockSpawner 없이 프리팹 자식으로 배치된 DiggableRock은 `_mask`가 null → `CountExposedRockPixels()`가 항상 0 반환.
`preExposed=false`(기본값)이면 Start()에서 Renderer·Collider가 비활성화되고 영구 숨김 상태가 된다.
프리팹 자식으로 직접 배치 시: `preExposed=true`, `minExposedPixels=0`, `Rigidbody2D` 없어야 함.

### 15. 보호 좌표(PNG 페인팅 청크)에 광물을 깔 땐 반드시 "페인팅 후"
`ChunkSpawner.DecorateMineralsOnly(..., ignoreProtection: true)`는
`IChunkPostLoadPainter.OnChunkLoaded`에서 `ChunkImagePainter.Paint()` **다음**에만 호출한다.

먼저 깔면 두 가지가 동시에 깨진다:
- `MineralGenerator.IsWellSupported`가 페인팅 전 지형을 봐서 광물이 방 공동에 뜬다
  → `CheckSupport` 탈락 → `WakeUp()` → 루트로 튀어나옴 (2026-07-11 버그 재발)
- `Paint`의 `LoadChunkData`가 PixelInfo를 덮어 광물 마커(3)·수집 마커(4)가 지워진다

폴링(`HasChunk` 감시)으로 시점을 잡으려 하지 말 것 — Phase 1과 Phase 2 사이에 끼어들어
Phase 2 시작의 자식 정리 루프가 방금 만든 광물을 풀 반납한다.
반드시 `SpecialChunkManager.RegisterPostLoadPainter`로 등록해 파이프라인이 부르게 한다.

배경: `Assets/Docs/elevator-landing-room.md` §7

---

## 청크 생성 파이프라인 흐름

```
InfinityMapManager
  └─ ChunkLoadingRunner
       └─ ChunkGenerationPipeline
            └─ ChunkSpawner
                 └─ StandardChunkFactory
                      ├─ ChunkPool.Get()          // 풀에서 재사용
                      └─ Instantiate(chunkPrefab) // 신규 생성
                           └─ Reuse_Step1_Prepare()
                                └─ [나중에] Reuse_Step2_Finalize()
```

### 주의
`colliderUpdateInterval` 같은 non-serialized 필드는 `Instantiate` 시 프리팹에서 복사되지 않는다.
전체 인스턴스에 적용해야 하는 설정은 **static** 패턴을 사용한다.

---

## ProcessDirtyChunksAsync 6단계 파이프라인

```
Step 1: SnapshotScheduleInitJobs()   — Init 잡 스케줄 → yield 1프레임
Step 2: SnapshotScheduleRound1()     — Round 1 Visual 잡 스케줄
Step 3: wait loop + SnapshotCompleteLighting() — Round 1 완료 대기
Step 4: SnapshotScheduleRound2()     — 이웃 경계 동기화 후 Round 2 Visual 스케줄
Step 5: wait loop                    — Round 2 완료 대기
Step 6: SnapshotFinalizeAndApply()   — 텍스처 즉시 업로드 (Visual Job 완료 보장 시점)
```

Step 6에서 `ApplyTexture()`를 직접 호출하는 이유:
드릴 연속 파기 중 이전 코루틴이 LateUpdate 직전에 VisualJob을 재스케줄해
`IsVisualJobCompleted = false` 상태가 유지되어 GPU 스로틀 루프가 차단된다.

---

## 마켓 시스템 (주식·코인)

`Assets/Scripts/Stock/**`(기존 주식 시뮬레이션), `Assets/Scripts/Coin/**`(신규 코인 도박 미니게임), `Assets/Scripts/Market/**`(두 계열 공용)로 구성. 단일 `GameScripts.asmdef` 어셈블리.

| 파일 | 역할 |
|------|------|
| `Assets/Scripts/Market/MarketTheme.cs` | 다크 레트로 팔레트 중앙 정의(static Color). 주식·코인 UI 공유 |
| `Assets/Scripts/Market/MarketSceneController.cs` | 마켓 씬 최상위. 주식/코인 계열 토글, 진입/이탈(Additive), 상단 골드 |
| `Assets/Scripts/Market/MarketUISfx.cs` | 마켓 UI 효과음을 코드로 합성(에셋 불필요, 사인파 차임 계열). `MarketButtonSfx`(컨트롤러가 자동 부착)가 씬 전체 버튼·토글·입력필드에 훅. SoundDataSO에 `market_ui_*` 클립 등록 시 그 클립 우선. 버튼별 변경은 `MarketSfxOverride` |
| `Assets/Scripts/Market/MarketSelectHighlight.cs` | 프리셋 버튼(스테이크 %·배율·소지금 %)의 '선택됨' 하이라이트. 코드 자동 부착(프리팹 세팅 불필요), 주식·코인 공용 |
| `Assets/Scripts/Coin/Core/CoinGameManager.cs` | 코인 두뇌. 하루 1슬롯 잠금 + N판 제한, 베팅·정산(fee·일일 출금 한도·소프트 초과청산 후처리), 상장폐지 교체(동적 로스터), 세이브 |
| `Assets/Scripts/Coin/Systems/CoinPriceEngine.cs` | 승률(부호) 50% 고정 + 비대칭 캡(winCap/lossCap)·레버리지 스케일·하우스 엣지·연승 감쇠 + ±100% 극단. 순수 C# |
| `Assets/Scripts/Coin/Data/CoinTableSO.cs` | 코인 슬롯 라인업(티어) + 교체 이름 풀. 코드 폴백 `DefaultSlots()` 보유 |
| `Assets/StreamingAssets/Data/Coin_Localization.csv` | 코인 UI/플레이버 KR·EN·CN (코인명은 영문 고정, 번역 안 함). 코인 30종 각각 `COIN_FLAVOR_{이름}` 키 보유 — 새 코인 추가 시 이 키도 같이 넣을 것 |
| `Assets/StreamingAssets/Data/Companies.csv` | 주식 95종목. `tier` 컬럼(1~4) = 해금 레벨디자인. 티어표는 design.md §6.3 |
| `Assets/Scripts/Stock/Systems/StockUnlockGate.cs` | 종목 해금 판정(static). **종목 티어 = 채광 레벨(`PlayerStat.MiningLevel`) + 1** — 땅 단계 업그레이드(`UpgradeEffectType.MiningLevel`)로만 오르고, 지형 파기 게이트와 같은 값이라 "그 땅을 팔 수 있게 되면 그 티어 종목이 상장"된다. 잠긴 종목도 **시뮬레이션은 계속 돌고**(`CompanyManager.GetAllCompanies` — 세이브·시세 전용) UI만 `GetUnlockedCompanies()`로 걸러진다. ⚠ 티어 4는 채광 레벨 3 필요 — 3번째 면허 노드가 트리에 아직 없다 |
| `Assets/StreamingAssets/Data/NewsChains.csv` + `NewsTemplates.csv` | 뉴스 체인 13종(찌라시→공식 분기→소멸) + 독립 뉴스 27종. 종목 태그와 뉴스 태그 매칭으로 주가 반영. 로컬라이제이션 텍스트에 쉼표 넣을 땐 반드시 따옴표로 감쌀 것(RFC 4180). **급등주 체인 3종**(`chain_meme_surge`/`chain_resource_squeeze`/`chain_tech_mania`, 매집→폭등→붕괴→소멸)은 `hypeBypass=1` 컬럼으로 표시 — 이 뉴스의 주가 영향은 1틱 변동 상한을 **우회**해 몇 배까지 급등한다(최종 가격은 여전히 기준가×0.1~10 클램프). 뉴스 전반 세기는 `StockPriceEngine.NewsEffectMultiplier`(1.6). 설계: `Assets/Docs/economy/stock-hype-surge-design.md` |

규칙(설계 §7.3/§13): 코인 **승률(부호 50/50)은 전 슬롯 50% 고정**(게임 정체성, 안 건드림). 하우스 유리는 **손익 '크기'의 비대칭**에서 나온다 — 적중은 마진의 일부까지만(`winCap`<1), 빗나감은 마진 이상까지(`lossCap`>1) + **하우스 엣지**(`houseEdge`) + **양방향 베팅세**(`fee`, 승패 무관 매판 레이크) + **연승 보상 감쇠**(`streakPayoutDecay`) + **연승 욕심**(`streakGreed`, streak↑ 시 상폐확률↑) → **EV 항상 마이너스**(고티어일수록 깊음, 수입원 아님). **레버리지 = 위험-보상 다이얼**: `t=(배율−1)/(최대−1)`로 `effWinCap=Lerp(winCap×0.5,winCap,t)`(보상↑)·`effLossCap=Lerp(1,lossCap,t)`(마진 초과청산↑)을 함께 키워 최대 배율=큰 승리+깊은 청산(지배 전략 없음). 적중 `+round(마진×min(move,effWinCap)×(1−edge))`, 빗나감 `−round(마진×min(move,effLossCap))`(`move=변동폭×배율`, `move≥1`이면 `CoinRoundResult.liquidated`). 매니저 후처리: **양방향 fee** + **일일 출금 한도**(`withdrawLimitMultiplier`, 기본 5 — 코인 이익은 오늘 `첫 베팅액×5`까지만, 즉 골드 상한 = `시작 골드 + 첫 베팅액×5`. 첫 베팅 시점의 `DayStartGold`·`DayFirstStake`로 확정·영속, 베팅·손실은 무제한이라 올인 드라마 보존) + **소프트 초과청산 L1**(손실은 지갑 바닥까지만 — **음수 골드/빚 없음**). **전재산 올인은 `unlimitedMax`인 ABYSS만**(하위 코인은 `maxBet`이 베팅 캡). 파라미터: `CoinData.houseEdge/extremeExtraEdge/streakGreed/maxLeverage/winCap/lossCap/streakPayoutDecay` + `CoinTableSO.fee/withdrawLimitMultiplier`. 정산 `CoinPriceEngine.Resolve(dir, stake, streak, leverage)` → `CoinGameManager.TryBet` 후처리. 상장폐지(−100%) 시 그 슬롯에 **새 코인 교체 상장** — 이름은 **슬롯(스테이지)마다 정해진 3개를 A→B→C→A로 순환**(`CoinData.rotationNames`, `RotationNameAt(delistCount+1)`), 순환 목록이 빈 슬롯만 `CoinTableSO.replacementNames` 전역 풀 랜덤. 코인 손익은 골드 시스템(`PlayerStat`) 직접 사용. 세이브는 `PlayerData.coinSave`로 `SaveManager`에 연동(StockSaveData와 동일 패턴). 상세 설계는 `Assets/Docs/market-scene/design.md` 참조.

---

## 성능 최적화 작업 규칙 — 반드시 지킬 것

**성능 최적화 작업을 하면 무조건 `Assets/Docs/performance/`에 기록을 남긴다. 예외 없다.**

이 프로젝트는 UVCS를 쓰고 git 로그를 안 쓰기 때문에, 문서에 안 적으면 **왜 그렇게 바꿨는지가 완전히 사라진다.**
실제로 2026-05-29 / 07-09 최적화는 문서를 안 남겨 세션 밖에서 복원 불가능한 상태였다.

### 작업 시작 전
`Assets/Docs/performance/README.md`를 먼저 읽는다. 특히:
- **§2 미처리 항목** — 이미 검토하고 "이득 대비 비용이 나쁘다"고 판정한 것을 다시 제안하지 않기 위해
- **§3 반복해서 걸린 함정** — Burst 스테일 커널, EditMode가 job safety를 못 잡는 문제, `Idle`은 증상이라는 것 등

### 작업 후
1. `Assets/Docs/performance/`에 상세 문서 작성 (규모가 작으면 생략 가능)
2. **`README.md` §1 완료 이력 표에 한 줄 추가** — 이건 규모와 무관하게 항상
3. 새로 발견한 미처리 항목·함정이 있으면 §2 / §3에 추가
4. 동작이 바뀌었으면 문서에 **⚠ 섹션으로 분리해서** 명시 (회귀 의심 시 여기부터 보게)

### 문서에 반드시 들어갈 것
- **프로파일러 근거** (수치 그대로 — 무엇이 몇 ms, GC Alloc 몇 MB). 추측이 아니라 측정으로 시작했다는 증거
- **왜 위험한가** — 프레임 시간과 할당률은 별개다. "예산 안이라 안 보임"이 "문제없음"이 아니다
- **재사용 버퍼·캐시를 도입했으면 그 불변식** (언제 깨지는지)
- 검토했지만 **안 한 것과 그 이유**

---

## 설계 결정 문서

- `Assets/Docs/performance/README.md` — **성능 최적화 이력 인덱스**. 완료 이력 + 미처리 항목 + 반복해서 걸린 함정. 성능 작업 전후로 반드시 경유
- `Assets/Docs/performance/chunk-load-gc.md` — 청크 로딩 GC 할당 제거(청크당 1~6MB→0). `PixelInfo`가 전부 0이던 버그 수정 포함
- `Assets/Docs/job-pipeline-waste-removal.md` — Job 파이프라인 낭비 제거 설계(Round1/Round2 중복 잡, 전체 격자 dispatch). 계획은 같은 폴더 `-plan.md`
- `Assets/Docs/special-chunk/` — 특수 청크 전체 설계 문서 디렉토리 (compressedtrashwall-spawn-flow.md, prefab-setup.md 등 포함)
- `Assets/Docs/collider-visual-decoupling.md` — 콜라이더/비주얼 분리 배경과 버그 분석
- `Assets/Docs/world-interactable.md` — 월드 상호작용 설계. 상호작용 키를 F로 통일한 단일 원천(`InteractionKeys`), 근접하면 할 수 있는 일이 목록으로 뜨고 휠로 골라 F로 실행하는 구조, `[F]` 아이콘 해석 순서, 지상 씬 오브젝트 세팅표
- `Assets/Docs/player-terrain-sorting.md` — 지하에서 플레이어를 지형 뒤로 보내는 정렬 설계. 정렬 레이어 전체 지도, 지형을 올리지 않고 플레이어를 내린 이유, 던전 예외
- `Assets/Docs/rock-prefab-and-hit-animation.md` — 돌 정의를 구조체 리스트에서 프리팹으로 옮긴 설계 + 히트 흔들림 애니메이션. Animator가 배치 회전을 덮어쓰는 문제를 Update 원점복귀 → LateUpdate 합성으로 푼 이유, 콜라이더를 자식으로 못 내리는 이유, 이 과정에서 발견한 파괴 구멍 오프셋·세이브 오염 버그
- `Assets/Docs/half-res-distance-field.md` — 테두리 거리장 계산을 절반 해상도로 낮춰 청크 로드 chamfer 스파이크를 줄이는 설계(업샘플 방식). distanceField 소비처·매핑·단계별 구현·리스크 정리
- `Assets/Docs/refactoring-plan.md` — 리팩토링 항목 (전체 완료)
- `Assets/Docs/encumbrance-system.md` — 짐 무게 시스템 설계
- `Assets/Docs/equipment-unlock-lock-plan.md` — 장비 잠금/해제 플로우
- `Assets/Docs/stamina-max-reduction-plan.md` — 스태미나 최대치 감소 메커니즘
- `Assets/Docs/terrain-feature-checklist.md` — 지형 신기능 도입 시 체크리스트 (로딩·저장·시각·파기 등 7개 항목)
- `Assets/Docs/linked-chunk-system-design.md` — 링크 피스 시스템 설계 (멀티타일 구조물, Minecraft 방식 역산)
- `Assets/Docs/economy/stock-volatility-gaussian-and-warmup.md` — 평상시 노이즈를 균등→가우시안 로그워크로 전환(평균 일변동 ±11%→±25%), 뉴스를 평상시 상한 밖 별도 상한(±0.7)으로 분리, 데드밴드 ±15→±35%. 새 게임 시작 시 뉴스 없이 20틱 예열(`StockPriceEngine.WarmUp`). 튜닝 다이얼·회귀 주의점 정리
- `Assets/Docs/economy/stock-mean-reversion-ramp.md` — 주가 시간-램프 평균회귀. 이탈 지속 틱 수에 따라 회귀 강도를 램프(뉴스 종료 3~4틱 후 기준가 복귀, 여러 뉴스 겹쳐도 복귀). `ticksAway`는 `PriceHistory`에서 파생(세이브 무변경). 상수·튜닝 수치·밸런스 영향 정리
- `Assets/Docs/market-scene/design.md` — 주식·코인 통합 단말기 씬(NEON 마켓) 다크 레트로 설계. 기존 `Stock.*` 리테마 + 신규 코인 도박 미니게임(단판 베팅, 동적 로스터) + `Coin.*`/`Market.*` 코드 명세
- `Assets/Docs/elevator-landing-room.md` — 구멍 입구 방 / 엘리베이터 착지 방 분리 설계. 텔레포트 착지 낙하 버그(§1-A) 원인 분석 포함
- `Assets/Docs/surface-light-shaft.md` — 구멍 입구 청크의 빛기둥 연출 + E키 지상 복귀 설계. Light2D를 못 쓰는 이유(어둠막 구조), 청크 자식이 아니라 씬 배치인 이유, 씬 세팅 절차
- `Assets/Docs/audio/sound-system-design.md` — 사운드 인프라 설계(3채널·원샷 풀·앰비언스 2레이어·상태 루프) + **효과음 키 39개 ↔ 훅 지점 전체 매핑**. 남은 Unity 수동 작업 목록도 여기
- `Assets/Docs/audio/sound-system-plan.md` — 위 설계의 태스크별 구현 계획(실행 완료)
- `Assets/Docs/bug-report-system.md` — 팀 QA 버그 리포트(F12) 설계. 캡처 순서가 왜 핵심인지, Unity 공식 User Reporting을 안 쓴 이유, `UIState`를 바꾸지 않고 static 플래그로 단축키를 막는 이유. 구현 계획은 같은 폴더 `-plan.md`
- `Assets/Docs/qa/terrain-seam-watchdog.md` — 청크 이음매 통과 감시기 설계. 왜 사후 조사가 불가능한지, 탐지기 4종과 오탐 제외 기준, **콜라이더 윤곽이 픽셀 중심을 이어서 청크 경계마다 구조적으로 생기는 1픽셀 틈**, `PreMarkBoundaryVisited`를 되살리면 청크 전체가 콜라이더를 잃는 이유
- `Assets/Docs/qa/debug-console-and-fixtures.md` — 디버그 콘솔·시간 배속·QA 세이브 픽스처 설계. **`worldData.bin`에 슬롯 인덱스가 없어 5개 슬롯이 지형을 공유한다는 미해결 이슈**도 여기
- `Assets/Docs/upgrade-multi-level.md` — 업그레이드 노드 다단계(레벨) 설계. Lv1을 `unlockedNodeIds`로만 표현해 구버전 세이브·기존 소비처를 그대로 살린 이유, 효과 합산 규칙(합=×N·곱=^N), 계층 게이트(MiningLevel)를 다단계에서 제외한 이유, 노드를 다단계로 바꾸는 절차
- `Assets/Docs/market-scene/ui-build-guide.md` — MarketScene UI **에디터 제작 가이드**. Hierarchy 구조·컴포넌트 부착 위치·전 SerializeField 연결 대상·프리팹 셋업·색표 정리
