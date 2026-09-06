# 가격 데이터 JSON 외부화 설계

작성일: 2026-08-09
관련 문서: [mineral-price-design.md](mineral-price-design.md) · [equipment-relic-price-design.md](equipment-relic-price-design.md) — 이 문서들이 정하는 **값**을 담을 **그릇**을 만드는 작업이다.

---

## 1. 목표

**가격 하나 고치는 데 드는 시간을 줄인다.** 밸런스 값은 원래 자주 바뀌는데 지금 구조는 한 번 만지는 비용이 너무 크다.

### 1.1 착수 시점의 현황 (측정값)

| 가격 종류 | 위치 | 파일 수 |
|---|---|---|
| 광물 가격 23종 | `Assets/GameData/ShopData/MineralPriceDatabase.asset` | 1 |
| 광물 무게 23종 | `Assets/GameData/MineralData/**/*.asset` | **23** |
| 장비·유물·소모품 상점가 58개 | `Assets/GameData/ShopData/ShopItemDatabase.asset` | 1 |
| 유물 강화비 27×2 | `Assets/GameData/Relics/*.asset` | **27** |
| 업그레이드 노드 비용 35개 | `Assets/Scripts/Editor/UpgradeTreeGenerator.cs` → 생성기 실행 → 노드 에셋 35개 재생성 | 1 + 에디터 조작 |

**총 52개 파일 + C# 1개 + 에디터 툴 실행.**

업그레이드 트리가 가장 나쁘다. 코드를 고치고 → 에디터 창을 열어 생성기를 돌리고 → 노드 에셋 35개가 삭제·재생성된다. GUID가 바뀌고, `nodeId`가 바뀐 노드는 세이브의 해금 상태가 풀린다.

### 1.2 이미 절반은 JSON에 있다

`StreamingAssets/`에 `tileData.json`(광물 산출량 `perChunk`), `worldSettings.json`, `toolConfig.json`, `specialChunkSettings.json`, `windPatterns.json`이 있고 `*Loader.cs` 싱글턴 패턴이 잡혀 있다.

즉 **밸런스 데이터의 절반은 이미 JSON이고 가격만 에셋에 갇혀 있다.** 산출량은 JSON인데 가격은 에셋인 것이 오히려 일관성 없는 상태다. 이 작업은 새 패턴을 만드는 게 아니라 있는 관례에 가격을 얹는 것이다.

---

## 2. 설계 — 런타임 오버라이드

**`priceData.json`이 정답이고, 에셋 값은 폴백이다.**

게임 시작 시 `PriceDataLoader`가 JSON을 읽어 ScriptableObject 필드에 직접 써넣는다. 이후 값을 읽는 쪽은 아무것도 몰라도 된다.

### 2.1 게임 코드를 고치지 않아도 되는 이유

호출부를 조사한 결과, 세 값 모두 **필드에 써넣으면 전부 반영된다.**

| 값 | 읽는 방식 | 조치 |
|---|---|---|
| `MineralSO.weight` | 전부 `.Weight` 프로퍼티(`InterfaceInventoryItem.Weight => weight`) 경유. **필드를 직접 읽는 곳 0** | 필드에 쓰면 끝 |
| `UpgradeNodeSO.cost` | `UpgradeManager`·`UpgradeSlotTooltipProvider`·`UpgradeOverlayUI`에서 필드 직접 읽음 (12곳) | 필드에 쓰면 끝 |
| `ShopItemData.price` | `ShopManager`·`ShopOverlayUI`·`ShopUI`에서 직접 읽음 (12곳) | 필드에 쓰면 끝 |
| `MineralPriceDatabase` | `GetPrice(MineralID)` 단일 접근자 | 내부 `_priceMap` 재빌드 |

접근자를 새로 만들거나 호출부 24곳을 고칠 필요가 없다. 게임 코드 변경은 **두 곳뿐**이다.

- **유물 강화비 배선** (§4) — 기존 동작을 바꾸는 유일한 변경. 2줄
- **`MineralPriceDatabase.ApplyPriceOverrides`** (§5) — 새 메서드를 더하는 것이지 기존 동작을 건드리지 않는다

### 2.2 이 방식을 고른 이유

검토한 대안은 셋이었다.

| 방식 | 채택 여부 |
|---|---|
| **런타임 오버라이드** (JSON이 정답, 에셋 무시) | **채택** |
| 에디터 싱크 (버튼으로 JSON → 에셋 반영) | 기각 — 버튼을 잊으면 어긋나고, 빌드에서 수정 불가. 트리 생성기 문제(GUID·세이브)도 그대로 남는다 |
| 완전 이전 (에셋에서 가격 필드 제거) | 기각 — `ShopItemData.price` 등을 참조하는 코드를 전부 고쳐야 한다. 목적(수정 속도) 대비 과하다 |

채택 이유 셋:

1. **프로젝트가 이미 그렇게 돈다.** `tileData.json`의 `perChunk`가 런타임에 읽히고 `worldSettings.json`의 `colliderUpdateInterval`이 `Awake`에서 적용된다.
2. **업그레이드 트리의 진짜 비용이 사라진다.** 앞으로 가격만 바꿀 때는 생성기를 안 돌려도 된다. 생성기는 노드를 추가·삭제할 때만 쓴다.
3. **빌드된 게임에서 고칠 수 있다.** 120일 곡선은 실제로 플레이해야 검증되는데, "이 숫자 좀 낮춰볼까"를 Unity 재컴파일 없이 즉시 시험할 수 있다.

### 2.3 ⚠ 인스펙터가 거짓말을 하게 된다

에셋에 200,000이라 적혀 있는데 게임에선 46,000인 상황이 생긴다. 해당 SO 필드에 "런타임에 `priceData.json`이 덮어씀" 취지의 `[Header]`를 달아 완화한다.

Unity 에디터에서는 플레이 중 ScriptableObject를 수정하면 그 변경이 플레이 종료 후에도 남는 경향이 있다(MonoBehaviour와 다른 점). 그래서 첫 플레이 후 에셋 값이 JSON과 같아져 이 문제가 저절로 사라질 수 있다. **다만 이 동작에 의존하지 않는다** — 설계상 JSON이 정답이고, 에디터가 그 변경을 디스크에 쓰든 말든 게임 동작은 같다.

---

## 3. JSON 스키마

`Assets/StreamingAssets/priceData.json`

**`JsonUtility`는 딕셔너리를 지원하지 않는다.** 배열만 된다. `tileData.json`이 이미 그 제약 아래 쓰여 있으니 같은 방식을 따른다 — ID를 **enum 이름 문자열**로 쓰고 `System.Enum.TryParse`로 읽는다(`MineralRuleJson.mineralType`과 동일한 패턴).

```json
{
  "minerals": [
    { "id": "ScrapMetal", "price": 7,  "weight": 0.3, "layer": "L0_Dirt" },
    { "id": "Iron",       "price": 50, "weight": 1.2, "layer": "L0_Dirt" }
  ],
  "shopItems": [
    { "id": "FrostbiteResist", "price": 200 }
  ],
  "shopEquipment": [
    { "id": "MinerHelmet", "price": 2000, "layer": "L0_Dirt", "unlockNodeId": "PickaxeUnlock_T0_01" }
  ],
  "shopRelics": [
    { "id": "Magnet", "price": 12000 }
  ],
  "relicUpgrades": [
    { "id": "Magnet", "gold": [6000, 18000] }
  ],
  "upgradeNodes": [
    { "id": "MiningSpeed_T0_01", "cost": 700 }
  ]
}
```

### 3.1 항목 수

| 절 | 대상 | 개수 |
|---|---|---|
| `minerals` | `MineralID` — 가격과 무게를 함께 | 23 |
| `shopItems` | `ItemID` — 소모품 | 2 |
| `shopEquipment` | `EquipmentID` | 28 |
| `shopRelics` | `RelicID` | 28 |
| `relicUpgrades` | `RelicID` — `gold` 배열 길이 2 (`maxLevel - 1`) | 27 |
| `upgradeNodes` | `UpgradeNodeSO.nodeId` (문자열, enum 아님) | 35 |
| | | **143** |

`relicUpgrades`가 27인 것은 `TestStatRelic`(4001)을 넣지 않기 때문이다. JSON에 없으면 에셋 값(100/250)이 유지된다.

`upgradeNodes`의 `id`는 enum이 아니라 `nodeId` 문자열이다(`"MiningSpeed_T0_01"` 등). 파싱이 아니라 문자열 비교로 찾는다.

**가격만 다룬다. `isAvailable`·`stock`은 JSON에 넣지 않는다.** 판매 여부는 가격과 성격이 다른 결정이고(예: `TestStatRelic`을 상점에서 빼는 것), 지금 어긋나 있지도 않다. `ShopItemDatabase.asset`에 그대로 둔다.

### 3.1-A `minerals`·`shopEquipment`의 `layer` — 읽기 전용 주석

`minerals`와 `shopEquipment` 절만 항목마다 `layer`를 하나 더 갖는다(`"L0_Dirt"` / `"L1_Ice"` / `"L2_MagmaRock"` / `"L3_MeteoriteRock"`).
**`PriceApplier`는 이 값을 읽지 않는다.** 143개를 이름순으로 늘어놓으면 "이 층 광물 전체를 올리자"가 눈에 안 들어와서 넣은 밸런싱용 라벨이다.

- 출처는 `tileData.json`이다. `PriceDataExporter`가 층을 `startDepth` 내림차순(0 → -20 → -40 → -60)으로 세우고, 그 순서대로 광물을 훑어 라벨을 붙인다. `minerals` 배열도 같은 순서로 정렬돼 나온다(층 안에서는 `tileData.json`에 적힌 순서 그대로).
- **여러 층에 걸쳐 나오는 광물은 가장 얕은 층 소속으로 본다.** Copper·Iron은 Ice에도 소량 나오지만 `L0_Dirt`, Emerald·Topaz는 `L1_Ice`, Diamond·LavaStone은 `L2_MagmaRock`이다.
- `tileData.json`에 없는 광물(던전 보상 등)은 `"Unlisted"`로 배열 맨 뒤에 이름순으로 모인다.
- 손으로 고쳐도 의미가 없다 — 다음 export에서 `tileData.json` 기준으로 덮어써진다. 광물의 소속 층을 바꾸려면 `tileData.json`을 고쳐야 한다.

**`shopEquipment`도 같은 라벨을 쓴다.** 층별로 묶여 나오고, **층 안에서는 가격 오름차순**이다(광물이 층 안에서 `tileData.json` 순서인 것과 다른 점). 층 하나가 세트 셋인 구간이 있어 가격순으로 세워야 "이 층에서 뭐부터 사나"가 그대로 보인다.

- 층 배정의 출처는 데이터가 아니라 [equipment-relic-price-design.md](equipment-relic-price-design.md) §4의 표다. `PriceDataExporter.EquipmentSetLayer`에 그 표가 그대로 적혀 있다 — 광물의 `tileData.json` 같은 원본이 장비에는 없기 때문이다(`EquipmentSO.level`은 27종 전부 0이라 쓸 수 없다). 세트를 옮기려면 문서와 그 표를 같이 고친다.
- 매칭은 **id의 세트 이름 접두사**(`Miner*`, `Winter*`, …)로 한다. 표에 없는 레거시 장비(`LeatherHelmet` 등)는 광물과 마찬가지로 `"Unlisted"`로 맨 뒤에 모인다.
- 라벨 문자열은 광물과 같은 함수에서 나온다. `tileData.json`의 층 이름을 바꾸면 두 절이 함께 바뀐다.

### 3.1-B `shopEquipment`의 `unlockNodeId` — 역시 읽기 전용 주석

장비 항목은 `unlockNodeId`도 함께 갖는다. **그 장비가 상점에 뜨려면 먼저 사야 하는 업그레이드 노드**다(`"PickaxeUnlock_T0_01"` 등). 비어 있으면 처음부터 판다.

가격만 있으면 "이 값을 감당할 때쯤 상점에 떠 있기는 한가"를 알 수 없다. 판정 자체는 `ShopItemData.unlockNodeId`(에셋)에만 있어서 튜닝할 때마다 상점 에셋을 따로 열어봐야 했다 — 그 한 줄을 가격 옆으로 옮겨 적는다.

- **`PriceApplier`는 이 값도 읽지 않는다.** 실제 해금 판정은 `ShopUnlockGate`가 에셋 값으로 한다. JSON을 고쳐도 게임은 안 바뀌고, 다음 export에서 에셋 기준으로 덮어써진다. 해금을 바꾸려면 `ShopItemDatabase.asset`을 고쳐야 한다.
- 노드 id는 `upgradeNodes` 절의 `id`와 같은 문자열이라 그 절에서 가격을 바로 찾을 수 있다.
- 소모품·유물 절에는 넣지 않았다. 상점 행에 값이 안 채워져 있어 전부 빈 문자열이 될 뿐이다(유물 해금은 `RelicSO.unlockNodeId` 쪽에 있다).

### 3.2 없는 항목과 모르는 항목

- **JSON에 없는 ID는 건너뛴다.** 에셋 값을 그대로 쓴다. 일부만 실험적으로 덮어쓸 수 있고, 새 광물을 추가했을 때 JSON을 안 고쳐도 게임이 돈다.
- **모르는 ID는 경고를 남기고 무시한다.** 오타나 삭제된 항목이 조용히 넘어가면 "왜 안 바뀌지"로 시간을 날린다. `Debug.LogWarning`에 어느 절의 어느 id인지 찍는다.

---

## 4. 유물 강화비 배선

`RelicSO.upgradeCosts`는 **현재 읽는 코드가 없다.** 실제 강화비는 `EquipmentUpgradeTable.cs:86`의 `RelicGoldCost(int currentLevel) => Math.Max(1, currentLevel) * 2222` 고정 공식이고, `EquipmentUpgradeOverlayUI.cs`의 1156행(표시)과 1339행(차감)이 그것을 쓴다. `EquipmentUpgradeTable.cs:83`에 "유물은 자체 upgradeCosts를 쓰지 않고 이 공식으로 통일한다"는 주석이 있다.

**이번에 배선한다.** 두 줄이 `_selRelicSo.upgradeCosts[level - 1].gold`를 읽도록 바꾼다.

배선하지 않으면 JSON에 "고쳐도 아무 일이 안 일어나는 27개 항목"이 생기고, 다음에 이 파일을 여는 사람이 정확히 같은 함정에 빠진다.

**방어**: `upgradeCosts`가 null이거나 길이가 부족하면 기존 `RelicGoldCost` 공식으로 폴백한다. 에셋이 덜 채워진 유물이 생겨도 강화가 막히지 않아야 한다.

**인덱싱**: `upgradeCosts[0]`이 Lv1→2, `[1]`이 Lv2→3이다. 현재 레벨 `L`에 대해 인덱스는 `L - 1`인데, `RelicGoldCost`가 `Math.Max(1, currentLevel)`로 방어하는 걸 보면 `L`이 0일 수 있다. `Mathf.Clamp(L - 1, 0, upgradeCosts.Length - 1)`로 잡는다.

### 4.1 ⚠ "임시 테스트 값" 배지는 남긴다

`EquipmentUpgradeOverlayUI.cs:1137-1138`의 "⚠ 임시 테스트 값 (밸런스 미확정)" 배지는 **골드만 경고하는 게 아니다.**

같은 화면의 **재료 광물**도 여전히 임시값이다 — 수량은 `EquipmentUpgradeFormula.RelicMaterialCount(level) = level`이고, 종류는 `EquipmentUpgradeStore.ResolveMaterial(null)`이 반환하는 "DB 첫 광물(임시 강화석)"이다. 이번 작업은 **골드만** 배선하므로 재료 쪽 근거는 그대로 남는다.

배지를 지우면 아직 미확정인 재료 비용이 확정된 것처럼 보인다. **남긴다.** 유물 강화 재료 설계는 별도 작업이다(§8).

---

## 5. 로더 동작

`Assets/Scripts/_Core/Data/PriceDataLoader.cs` — `ToolConfigLoader`와 같은 골격.

```
[DefaultExecutionOrder(-250)]
```

현재 가장 이른 것이 `ToolConfigLoader`·`WorldSettingsLoader`의 -200이므로 **-250은 비어 있다.** 다른 매니저가 가격을 읽기 전에 끝나야 한다.

동작 순서:

1. `Application.streamingAssetsPath`에서 `priceData.json`을 읽는다
2. 파일이 없으면 `Debug.LogWarning` 후 종료 — 에셋 값 그대로
3. 파싱에 실패하면 `Debug.LogError` 후 종료 — 에셋 값 그대로
4. 절별로 대상 SO를 찾아 필드에 써넣는다
5. 적용 개수와 무시된 id를 `Debug.Log`로 한 줄 요약

**대상 SO를 어떻게 찾는가.** 런타임이므로 `AssetDatabase`를 못 쓴다. 기존 데이터베이스 SO를 경유한다.

| 절 | 경로 |
|---|---|
| `minerals` (price) | `MineralPriceDatabase` |
| `minerals` (weight) | `MineralDatabase` → `MineralSO` |
| `shopItems`·`shopEquipment`·`shopRelics` | `ShopItemDatabase.shopItems` |
| `relicUpgrades` | `RelicDatabase` → `RelicSO` |
| `upgradeNodes` | `Assets/GameData/UpgradeData/_UpgradeTree.asset` → `UpgradeNodeSO` |

이 데이터베이스들은 `DatabaseLoader`(`Assets/Scripts/_Core/Items/DatabaseLoader.cs`)가 인스펙터 참조로 메모리에 올린다. `PriceDataLoader`도 같은 방식으로 인스펙터에서 참조를 받는다. 업그레이드 트리는 `DatabaseLoader`에 없으므로 `PriceDataLoader`가 직접 참조 필드를 갖는다.

**`MineralPriceDatabase`는 `prices`가 `[SerializeField] private`이고 `_priceMap`도 private**이라 외부에서 못 고친다. `public void ApplyPriceOverrides(...)`를 추가한다(§2.1의 두 예외 중 하나).

### 5.1 ⚠ 배선 오류가 조용히 무시되는 지점

`PriceDataLoader`가 참조하는 5개 SO 중 **3개는 배선을 잘못해도 아무도 모른다.**

- `MineralPriceDatabase`·`ShopItemDatabase`·`UpgradeTreeSO`는 단순 인스펙터 참조다. 게임 쪽도 마찬가지로 `ShopManager.priceDatabase`·`ShopManager.shopItemDatabase`·`UpgradeManager.upgradeTree` 필드가 인스펙터 참조다. `PriceDataLoader`와 이 매니저들이 **서로 다른 에셋 파일**(예: 중복 저장본)을 가리키면, 로더는 "적용 143건" 성공 로그를 남기지만 게임은 안 덮어써진 원본을 본다. 예외도 경고도 없다.
- `MineralDatabase`는 `Resources.Load` 싱글턴이라 어느 경로로 로드하든 같은 인스턴스로 수렴한다 — 안전.
- `RelicDatabase`는 `OnEnable`에서 자신을 `Instance`로 굳힌다 — 마찬가지로 안전.

체크리스트는 `price-data-json-plan.md`의 "사람이 Unity에서 해야 할 일" 5번에 있다.

---

## 6. 초기 JSON 생성 — 에디터 툴

`Tools/Economy/Export Prices to JSON`

**143개 숫자를 손으로 전사하면 오타가 난다.** 그런데 테스트가 JSON을 읽으므로(§7) **전사 오류를 테스트가 못 잡는다** — 자기 자신을 검증하는 꼴이 된다. 그래서 현재 에셋 값을 그대로 뽑는 툴을 만든다.

- 노드 비용은 `Assets/GameData/UpgradeData/Node/*.asset`에서 읽는다. `UpgradeTreeGenerator.cs`를 파싱할 필요가 없다.
- 일회용이 아니다. 나중에 에셋 쪽에서 값을 만졌을 때 다시 뽑을 수 있다.
- 덮어쓰기 전에 확인 다이얼로그를 띄운다. JSON이 정답이 된 뒤에 실수로 실행하면 에셋의 낡은 값이 JSON을 덮어쓴다.

---

## 7. 검증

### 7.1 기존 테스트를 JSON 읽도록 전환한다

`MineralBalanceTests`(15개)와 `ShopPriceTests`(11개)는 지금 **에셋을 읽어** 밸런스를 검증한다. JSON이 정답이 되면 이 테스트들은 **게임이 실제로 쓰는 값을 보지 않게 된다.**

두 파일을 JSON에서 읽도록 바꾼다. **불변식은 그대로 유지하고 입력 출처만 바꾼다.**

- 선별 편향 ≤ 1.2 (설계 §3.3)
- 층 배수 4.0~5.0
- 2층 진입 매출 ≥ 1층 만재 × 2.0
- 유물 풀강 = 구매가 × 3 = 같은 티어 장비 한 세트
- 장비 27종 총액 1,728,000

### 7.2 로더 테스트를 새로 만든다

`PriceDataLoaderTests` — 파싱과 적용 로직을 검증한다.

- 정상 JSON을 파싱하면 각 절의 항목 수가 맞는가
- 없는 id는 건너뛰는가 (기존 값 보존)
- 모르는 id는 경고만 남기고 넘어가는가 (예외를 던지지 않는가)
- 파일이 없을 때 예외 없이 폴백하는가
- 깨진 JSON일 때 예외 없이 폴백하는가
- `relicUpgrades`의 `gold` 배열 길이가 2가 아닐 때 안전한가

파싱·적용 로직을 `MonoBehaviour`에서 분리해 **순수 클래스**로 두면 테스트가 쉬워진다(`MineralEconomy`와 같은 방식). `PriceDataLoader`는 파일 읽기와 SO 참조만 담당하고, 실제 적용은 순수 함수가 한다.

---

## 8. 범위 밖

| 항목 | 이유 |
|---|---|
| `Tools/Economy/Sync Prices to Assets` (JSON → 에셋 역방향) | 인스펙터를 진실로 만드는 편의 기능이다. 이번 목적인 수정 속도에 기여하지 않는다. 필요해지면 그때 만든다 |
| 광물 산출량(`perChunk`)·상태이상을 `priceData.json`으로 합치기 | 이미 `tileData.json`에 있다. 성격이 다르고(지형 정의 vs 가격표) 파일이 커지면 오히려 찾기 어렵다 |
| 장비 `statModifiers` | 가격이 아니다. 아직 비어 있고 별도 작업이다 |
| **유물 강화 재료(광물) 비용** | 수량 `RelicMaterialCount(level) = level`, 종류 `ResolveMaterial(null)` = "DB 첫 광물" 둘 다 임시값이다. 골드와 달리 담을 필드(`RelicUpgradeCost.material`·`materialCount`)는 있지만 **무엇을 요구할지가 아직 설계되지 않았다.** §4.1의 배지가 남는 이유 |
| 치트 방지 | §10 참조 |

---

## 9. 변경 대상 파일

| 파일 | 변경 |
|---|---|
| `Assets/Scripts/_Core/Data/PriceData.cs` | 신규 — JSON DTO + 순수 적용 로직 |
| `Assets/Scripts/_Core/Data/PriceDataLoader.cs` | 신규 — 싱글턴 로더, 실행 순서 -250 |
| `Assets/Scripts/Editor/PriceDataExporter.cs` | 신규 — `Tools/Economy/Export Prices to JSON` |
| `Assets/StreamingAssets/priceData.json` | 신규 — 143개 항목, 툴로 생성 |
| `Assets/Scripts/UI/Items/Minerals/Mineral/MineralPriceDatabase.cs` | `ApplyPriceOverrides` 메서드 추가 |
| `Assets/Scripts/UI/Upgrade/EquipmentUpgradeOverlayUI.cs` | 1156·1339행 유물 강화비 배선 + 임시값 배지 제거 |
| `Assets/Tests/EditMode/MineralBalanceTests.cs` | 입력 출처를 에셋 → JSON |
| `Assets/Tests/EditMode/ShopPriceTests.cs` | 입력 출처를 에셋 → JSON |
| `Assets/Tests/EditMode/PriceDataLoaderTests.cs` | 신규 |
| 각 가격 SO 필드 | `[Header]`로 "런타임에 덮어씀" 표기 |

---

## 10. 알려진 트레이드오프

**`priceData.json`은 빌드에 그대로 실려 나간다.** `StreamingAssets`는 빌드 결과물에 평문으로 포함되므로 플레이어가 텍스트 에디터로 열어 가격을 고칠 수 있다.

싱글플레이 게임이라 지금은 치명적이지 않고, `tileData.json`·`worldSettings.json`도 이미 같은 상태다. 이것만 다르게 할 이유가 없다.

**나중에 스팀 도전과제나 랭킹을 붙이면 문제가 된다.** 그때는 이 파일 하나가 아니라 밸런스 JSON 전부를 함께 다뤄야 한다 — 릴리즈 빌드에서 `Resources`로 옮기거나 암호화하는 식으로. 출시가 가까워졌을 때 한꺼번에 결정할 사안이다.

---

## 11. 검증 방법

| 항목 | 기대 |
|---|---|
| JSON 숫자 하나를 고치고 재실행 | Unity 재컴파일·생성기 실행 없이 즉시 반영된다 |
| 업그레이드 노드 비용 변경 | 트리 생성기를 돌리지 않아도 상점·툴팁·구매에 반영된다 |
| `priceData.json`을 지우고 실행 | 경고 한 줄 뒤 에셋 값으로 정상 동작한다 |
| JSON에 없는 광물 추가 | 그 광물만 에셋 값을 쓰고 나머지는 JSON을 쓴다 |
| 오타난 id | 경고에 어느 절의 어느 id인지 찍힌다 |
| 유물 강화 | 차감액이 티어별 값(6,000 / 23,000 / 100,000 …)이다. 2,222G 고정이 아니다 |
| 빌드에서 JSON 수정 | 재빌드 없이 값이 바뀐다 |
