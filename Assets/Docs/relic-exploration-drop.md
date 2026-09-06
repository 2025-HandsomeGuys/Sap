# 유물 탐험 획득 — 돌 드롭 + 던전 상자

## 1. 무엇을 바꿨나

유물 27종은 상점에서 골드로 사는 물건이었다. 원래 의도는 **탐험으로 찾는 것**이었는데
획득 경로가 상점 하나뿐이라 "깊이 내려갈 이유"가 광물 매출에만 걸려 있었다.

- 상점에서 유물을 **전부 뺀다** (`ShopItemDatabase.asset`의 `itemType: 2` 28행 → `isAvailable: 0`)
- 돌을 완파하면 확률로 유물이 **바닥에 떨어진다** → E키로 줍는다
- 던전 보상 상자가 골드·광물에 더해 확률로 유물을 **상자 앞에 떨어뜨린다**

업그레이드 트리로 주는 유물(엘리베이터 신호기, `RelicSO.unlockNodeId`)은 그대로 둔다 —
그건 "탐험 보상"이 아니라 트리의 일부다.

## 2. 어느 지층에서 무엇이 나오나

`Assets/StreamingAssets/relicDropSettings.json` **한 파일이 답한다.** 티어별 유물 목록과
지층별 가중치가 모두 여기 있어, 코드를 읽지 않고도 "3층에서 뭐가 나오지"에 답할 수 있다.

티어 배정 근거는 `economy/equipment-relic-price-design.md` §6.1(구매가 12,000 / 46,000 / 200,000G).

| 지층 (tileData.json) | 1티어 | 2티어 | 3티어 |
|---|---|---|---|
| Dirt (1층) | **100** | 0 | 0 |
| Ice (2층) | **100** | 8 | 0 |
| MagmaRock (3층) | 25 | **100** | 8 |
| MeteoriteRock (4층) | 10 | 25 | **100** |

의도한 겹침이다.

- **자기 층 티어가 압도적**이라 "지금 층에 맞는 유물"이 주로 나온다
- **아래 티어가 0이 아니라서** 얕은 층에서 놓친 유물을 깊은 층에서 회수할 수 있다 —
  유물은 종당 1개뿐이라 회수 경로가 없으면 그 회차는 영영 미완성이 된다
- **위 티어가 살짝 열려 있어**(Ice→2티어 8, Magma→3티어 8) 가끔 "이른 대박"이 난다

1티어가 Dirt·Ice 두 층의 주력인 건 층은 4개인데 유물 티어는 3개이기 때문이다.
가격 설계의 "1티어 풀강 = 2층 한 세트"와도 어긋나지 않는다.

### 2.1 이미 가진 유물은 후보에서 빠진다

유물엔 수량 개념이 없어 중복 드롭이 곧 꽝이다. 더해서 **후보가 하나도 없는 티어는 가중치에서
통째로 뺀다.** 안 그러면 1티어를 다 모은 순간부터 마그마층 드롭의 25/133이 조용히 허탕이 되어
체감 드롭률이 설정값보다 낮아진다. (`RelicDropTable.TryPick`)

## 3. 확률과 피티

| 항목 | 기본값 | 의미 |
|---|---|---|
| `rock.baseChance` | 0.0035 | 일반 돌 1개 완파당 |
| `rock.mineralRockMultiplier` | 2.0 | 광물돌은 2배 — 캘 만한 돌을 노린 보상 |
| `rock.pityRocks` | 400 | 유물 없이 이만큼 깨면 다음 돌은 확정 |
| `dungeonChest.chance` | 0.35 | 유물 가능 상자를 열었을 때 |
| `dungeonChest.pityChests` | 6 | 연속 빈손 이만큼이면 다음 상자 확정 |

**피티(pity)가 있는 이유.** 순수 확률만 두면 운이 나쁜 플레이어는 한 회차 내내 유물 0개로
끝난다. 상점이라는 대체 경로를 없앤 이상 이건 "운 나쁨"이 아니라 콘텐츠 차단이다.

피티 카운터는 `RelicSaveData.rockDropPity` / `chestDropPity`에 실려 세이브에 남는다.
구버전 세이브엔 필드가 없지만 JsonUtility가 0으로 채우므로 그대로 호환된다.

**후보가 없어서 못 준 경우엔 피티를 소모하지 않는다.** 그래야 새 티어에 처음 발을 들인 순간
쌓아 둔 피티가 바로 터진다.

## 4. 왜 바닥에 떨어뜨리나 (즉시 지급이 아니라)

돌을 깨자마자 인벤토리에 꽂으면 유물이 어디서 나왔는지 화면에 안 보이고, 광물과 획득 방식이
갈라져 손에 안 익는다. 광물과 똑같이 **떨어뜨리고 E로 줍게** 했다.

던전 상자도 같은 이유로 상자 앞에 떨어뜨린다 — 골드·광물은 즉시 지급인데 유물만 다르다.

`WorldRelicPickup`은 프리팹이 없다. 유물 28종마다 스프라이트만 다른 껍데기를 만들 이유가
없어서 `WorldRelicPickup.Create()`가 `RelicSO.icon`을 읽어 런타임에 조립한다.

### 4.1 놓치면 어떻게 되나

`MineralLifetime`(60초 소멸)을 붙이지 않는다. 씬 전환·종료로 사라질 수는 있는데, 그때도
**보유 처리가 안 된 상태**라 다음 추첨 후보에 그대로 남는다 — 영구 손실은 없다.

## 5. 코드 지도

| 파일 | 역할 |
|---|---|
| `StreamingAssets/relicDropSettings.json` | 확률·지층 가중치·티어별 유물 목록 (단일 진실) |
| `_Core/Data/RelicDropSettingsData.cs` | 위 JSON의 DTO + 조회 |
| `_Core/Data/RelicDropSettingsLoader.cs` | static 지연 로더 |
| `Relics/Drop/RelicDropTable.cs` | 순수 추첨 로직 (EditMode 테스트 대상) |
| `Relics/Drop/RelicDropRoller.cs` | 런타임 진입점 — 지층 해석·확률·피티·스폰 |
| `Relics/Drop/WorldRelicPickup.cs` | 바닥 유물, E키 획득 |
| `Relics/Debug/RelicDebugCommands.cs` | F9 콘솔 `relic` — 지급·회수·드롭 표 확인 |
| `Tests/EditMode/RelicDropTableTests.cs` | 추첨 규칙 + 배포 JSON 검증 |

호출부는 둘뿐이다.

- `DiggableRock.DestroyRock()` — 광물 드롭 **다음에** 별도로 판정한다.
  `IRockDropOverride`로 만들면 광물돌(`MineralRock`)의 확정 광물 드롭을 잡아먹는다.
- `DungeonRewardPickup.GrantRelic()`

## 6. 함정 — 반드시 알아야 할 것

### 6.1 로더가 MonoBehaviour가 아니다

`ToolConfigLoader`·`SpecialChunkSettingsLoader`는 씬에 오브젝트를 심어야 한다. 유물 드롭은
무한맵·정적청크·던전 어디서든 불리는데, 씬 하나에 로더를 빠뜨리면 **그 씬에서만 조용히 드롭이
사라진다.** 그래서 `RelicDropSettingsLoader`는 최초 접근 시 1회 읽는 static 캐시로 뒀다.

### 6.2 던전 안의 돌은 청크에 속하지 않는다

던전은 지형이 생성되지 않는 먼 좌표에 통째로 놓이므로 그 안의 `DiggableRock`은 `_chunk`가
null이고 chunkY가 0으로 잡힌다. 그대로 두면 **최하층 던전에서 1티어 유물이 나온다.**
`RelicDropRoller.ResolveTileType`이 `DungeonOverlayController.IsInDungeon`일 때
`DungeonStateStore.CurrentInstance.y`(던전 문 깊이)로 갈아끼운다.

### 6.3 드롭 판정에 UnityEngine.Random을 쓰지 않는다

지형 생성이 같은 스트림을 쓴다. 유물 판정이 그 스트림을 소모하면 청크 생성 결과가 유물 운에
따라 흔들린다. `RelicDropRoller`는 전용 `System.Random`을 들고 있다.

### 6.4 드롭 표에 없는 유물은 영영 못 얻는다

상점을 없앤 이상 이 JSON이 유일한 경로다. 새 유물을 추가하면 반드시 티어에 넣어야 한다 —
`RelicDropTableTests.ShippedJson_CoversEveryObtainableRelic`이 검사한다
(예외는 `TestStatRelic`, `ElevatorTracker`).

## 7. 남은 것

| 항목 | 내용 |
|---|---|
| **드롭률 실측** | 0.0035는 "한 층 탐사에 1~2개"를 노린 책상 값이다. 층당 돌 파괴 수를 재서 조정해야 한다 |
| **유물 아이콘 20종 미제작** | `RelicSO.icon`이 빈 유물이 20종이다(Anvil·XRay·Trident·Jetpack 등). `WorldRelicPickup`이 유물 색 마름모로 대체해 획득은 되지만, 바닥에 떨어진 게 다 똑같이 보인다. 상점을 닫은 지금은 이게 유물을 알아보는 유일한 화면이라 우선순위가 올라갔다 |
| **티어 배정 재검토** | `equipment-relic-price-design.md` §6.1이 이름만 보고 추정한 값 그대로다. 실제 강도를 아는 사람이 조정할 것 |
| **유물 강화 재화** | 상점에서 유물을 뺐지만 강화는 여전히 골드다(`EquipmentUpgradeTable.RelicGoldCost`, 레벨×2,222G 임시값). 탐험으로 얻은 유물을 골드로 키우는 게 맞는지는 미정 |
| **미획득 유물 힌트** | "이 층에 아직 안 나온 유물이 있다"를 도감에서 보여줄지 |
| **디버그 지급기** | `RelicDebugGranter`가 F1~F12로 유물 즉시 지급 + 풀강을 한다. 빌드 씬에 노출돼 있어 드롭 밸런스를 무의미하게 만든다 (기존 이슈, `equipment-relic-price-design.md` §10에도 기록) |
