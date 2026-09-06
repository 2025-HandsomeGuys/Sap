# 암시장(Black Market) 유물 설계

`adding-a-relic.md` 가이드 기반. RelicID `BlackMarket = 4018`.

## 1. 정체성

지하에서 인벤토리 광물을 **전부 즉시 현금화**하는 액티브 유물.
정가보다 싸게(급전 수수료) 팔리고, **지하 1회/하강** 제한. 지상 복귀(=씬 재진입) 시 재충전.

- 타입: **Active** (즉발, `GetDuration()=0`)
- 배율: `sellRatePerLevel = {0.8, 0.9, 1.0}` (Lv1 0.8배 → Lv3 정가)
- 횟수: `usesPerLevel = {1, 1, 1}` (구조만 선반영. 나중에 값만 올리면 N회/하강)
- `maxLevel = 3`

## 2. 동작 흐름

### 발동 게이트 — `CanActivate()`
셋 다 만족해야 발동 가능(불충족 시 발동 소모 안 됨):
1. 현재 씬 == `"DemoUnderground"` (지하)
2. `MineralInventory`에 광물 1개 이상
3. `_usesLeft > 0`

### 판매 — `OnActivate()`
1. `MineralInventory.ReadonlyItems` 순회 → `(MineralSO, qty)` 수집
2. `raw += priceDatabase.GetPrice(m.mineralID) * qty`
3. `gold = Mathf.FloorToInt(raw * sellRate)` (레벨 배율)
4. `ctx.stat.AddGold(gold)` + `DayEarningsLedger.Report(DayEarningsCategory.MineralSale, gold)`
5. 수집한 광물 전량 제거 (`MineralInventory.RemoveAllOf(m)` 반복)
6. `_usesLeft--`
7. (선택) `SoundManager` 있으면 캐시등록음, 없으면 `Debug.Log`

### 1회/하강 락 — `GetCooldown()`
- 이번 사용으로 소진되면(`_usesLeft <= 1`) 센티넬 `999999f` 반환 → 이번 하강 내내 Cooldown 유지
  (퀵슬롯 fill이 꽉 찬 상태 = "소진" 표시. `RelicQuickslotUI`는 `timer/10f` clamp01 → 1)
- 아직 남으면 `0f` → 즉시 재사용 가능 (미래 N회/하강 UX 대비)

### 리셋 — 세이브·씬이벤트 불필요
플레이어는 `DontDestroyOnLoad` 아님 → 씬마다 재생성. 지하 진입 시 `RelicManager`가 세이브에서 재장착 →
behaviour `Clone()` 새로 생성 → `OnEquip`에서 `_usesLeft = usesPerLevel[lv]` 초기화.
`_usesLeft`는 런타임 전용 필드(비직렬화)라 다음 하강마다 자동 리셋.

## 3. 의존성 해석

| 의존 | 경로 |
|------|------|
| `PlayerStat.AddGold` | `ctx.stat` (RelicContext 기본 제공) |
| `MineralInventory` | `OnEquip`에서 `FindFirstObjectByType<MineralInventory>()` 캐시 |
| `MineralPriceDatabase` | `[SerializeField] priceDb` — **생성기가 에디터에서 `AssetDatabase.FindAssets("t:MineralPriceDatabase")`로 자동 연결**. 런타임 폴백: null이면 `FindFirstObjectByType<ShopManager>()?.priceDatabase` |
| 지하 판정 | `SceneManager.GetActiveScene().name == "DemoUnderground"` (GameManager 기준과 동일) |

> 가격 DB는 싱글턴/Resources 접근이 없고 `ShopManager.priceDatabase`로만 접근됨. 지하 씬엔 상점이 없으므로
> behaviour가 직접 SO 참조를 들고 있어야 함.

## 4. 코어 훅 1개 신규 (최소 침습)

현재 `RelicManager.ActivateSlot`은 `st.TryActivate()`(상태기계 소모)를 `b.OnActivate()`보다 **먼저** 호출.
→ 지상/빈가방에서 눌러도 사용이 낭비됨. 이를 막기 위해:

- `RelicBehaviour`: `public virtual bool CanActivate() => true;` 추가
- `RelicManager.ActivateSlot`: `TryActivate` 전에 `if (!b.CanActivate()) return;` 게이트

조건부 액티브 유물 전반에 재사용 가능한 훅.

## 5. 수정 파일

- **신규**: `Assets/Scripts/Gameplay/Relics/Behaviours/BlackMarketRelic.cs`
- **코어**:
  - `Core/RelicBehaviour.cs` — `CanActivate()` 가상 훅 추가
  - `Core/RelicManager.cs` — `ActivateSlot`에 게이트
- **공유(순차 편집)**:
  - `Data/RelicID.cs` — `BlackMarket = 4018`
  - `Editor/RelicSliceAssetGenerator.cs` — 등록 1줄 + `db.allRelics` 추가 + 가격DB 자동연결
  - `Gameplay/Relics/Debug/RelicDebugGranter.cs` — **F2** grant+equip (F5는 Anvil 점유 중)

## 6. 검증 (사용자 인게임)

1. Unity에서 `Tools > Relic > Generate Slice Assets` 재실행 (에셋 + 가격DB 연결 + DB 갱신)
2. 지하 씬에서 광물 채굴 후 F2로 유물 지급·장착, 발동 키(Q/R)로 발동
3. 확인: 골드 = 정가합×0.8(Lv1) 반영, 인벤토리 비워짐, 재발동 불가(소진 표시), 지상 복귀 후 재하강 시 1회 복귀
4. 예외: 지상에서 발동 → 무반응(소모 안 됨), 빈 가방 발동 → 무반응
