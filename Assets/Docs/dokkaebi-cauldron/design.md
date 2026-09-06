# 도깨비 가마솥 — 설계 문서

작성일: 2026-06-30

광물을 제련(승급)하는 1회성 특수청크. 광물 1개를 투입하면 부글부글 끓는 연출 후 확률에 따라
상위 광물·재·폭발 광물이 산출된다. 같은 가마솥은 최대 3회 사용할 수 있고, 소진되면 비활성(소진) 상태가 된다.

---

## 1. 정체 / 배치

- **땅속 1회성 특수청크.** 파괴 불가(Indestructible), E키 상호작용(`InteractableBlockBase` 상속).
- 기존 특수청크 파이프라인(`IChunkInitializer` → `ApplyBorderDataOnly` → `RestoreSavedPixels`) 위에 구현.
- 스폰은 `specialChunkSettings.json`의 `spawnChances`에 `DokkaebiCauldron` 항목 추가로 제어.

### 사용 흐름

```
플레이어 접근 → E키
  → CauldronUI 열림 (인벤토리 광물 목록)
  → 광물 1개 선택 + 투입 확정
  → UI 닫힘
  → 부글부글 끓는 애니메이션 (대실패는 붉은 글로우)
  → 결과 1개가 가마솥에서 월드로 튀어나옴
  → 남은 횟수 -1 (3 → 2 → 1 → 0)
  → 0이 되면 가마솥 비활성(소진) 상태로 전환 — 오브젝트는 그대로 남고 상호작용만 막힘
```

- 매 시도는 결과(성공/실패 무관) **3회 중 1회를 소모**한다.
- 투입 가능한 광물은 인벤토리에 보유한 광물 중 1개.

---

## 2. 컴포넌트 분해

연출·스폰을 담당하는 MonoBehaviour와 규칙·확률을 담당하는 순수 C# 클래스를 분리한다.
순수 C# 부분은 Unity 의존 없이 단위 테스트가 가능하다.

| 컴포넌트 | 종류 | 책임 | 의존 |
|---|---|---|---|
| `DokkaebiCauldron` | MonoBehaviour : `InteractableBlockBase` | E키→UI 열기, 남은횟수 관리, 연출 코루틴, 결과 스폰, 소진 시 자기소멸 | `CauldronUI`, `CauldronResolver` |
| `CauldronUI` | MonoBehaviour | 인벤토리 광물 표시 → 1개 선택·투입 확정 → 가마솥에 선택 `MineralID` 콜백 | `MineralInventory` |
| `MineralUpgradeLadder` | 순수 C# (static/싱글톤) | tileData.json에서 8칸 승급 사다리 구축. `GetRung`, `Resolve(input, steps)` | `TileDatabaseJson` |
| `CauldronResolver` | 순수 C# | 입력 `MineralID` + RNG → `CauldronResult`(결과 종류 + 보상 페이로드). 확률 가중 추첨 | `MineralUpgradeLadder` |
| `CauldronResult` | 순수 C# struct | 결과 종류(enum) + 보상 데이터(결과 MineralID / 재 / 폭발 / 이스터에그 플래그) + `rewardType` | — |

### 책임 분리 이유
- `CauldronResolver`/`MineralUpgradeLadder`는 Unity 비의존 → EditMode 테스트로 확률·승급 로직 검증.
- `DokkaebiCauldron`은 "결과를 받아 연출·스폰"만 → 규칙 변경이 MonoBehaviour를 건드리지 않음.

---

## 3. 결과 테이블

| 결과 | 확률 | 효과 |
|---|---|---|
| 대성공 (GreatSuccess) | 15% | 승급 사다리 **+2칸** 광물 1개 (최상위면 §6 이스터에그 hook) |
| 성공 (Success) | 45% | 승급 사다리 **+1칸** 광물 1개 |
| 실패 (Fail) | 25% | **재(Ash)** — 쓸모없는 아이템 1개 |
| 대실패 (GreatFail) | 15% | **폭발 광물 N개** 마구 스폰 + 가마솥 붉은 글로우 연출 |

- 확률값(15/45/25/15)은 `specialChunkSettings.json`에 노출하여 튜닝 가능하게 한다.
- 결과 광물은 `MineralItemController`(기존 월드 광물) 재사용으로 가마솥 위치에서 살짝 튀어나오게 스폰.
- 대실패 폭발은 `ExplosiveMineralReactor`가 쓰는 `projectileHazardPrefab`(폭발 위험 오브젝트)을 N개 스폰.

---

## 4. 승급 사다리 (MineralUpgradeLadder)

`tileData.json`의 층(`tiles[]`)·등급(`rarity`)을 깊이순으로 나열한 8칸 순서표.

```
[1] Dirt 일반   (ScrapMetal, GarbageBag, PETBottle, Coal)
[2] Dirt 희귀   (Copper, Iron)
[3] Ice 일반    (Meteorite, Fossil, Silver, Sapphire)
[4] Ice 희귀    (Emerald, Topaz)
[5] Magma 일반  (Obsidian, Quartz, Gold, Ruby)
[6] Magma 희귀  (Diamond, LavaStone)
[7] 운석층 일반 (Mithril, Gravitonium, Uranium)
[8] 운석층 희귀 (VoidStone, StarFragment)
```

### 광물 소속 판정 (canonical home)
한 광물이 여러 층에 등장하므로(예: Copper는 Dirt 희귀이자 Ice 일반으로 재등장) 충돌을 막기 위해
**가장 얕은 층에서 처음 등장하는 위치**를 그 광물의 소속 칸으로 정의한다.
- Copper → Dirt 희귀(`[2]`), Emerald → Ice 희귀(`[4]`). bleed-over(다음 층 일반 재등장)는 소속 판정에서 무시.

### Resolve 규칙
- 입력 광물의 칸 번호 `i`를 찾는다.
- 성공: `i+1` 칸에서 랜덤 1개. 대성공: `i+2` 칸에서 랜덤 1개.
- "일반→같은 층 희귀, 희귀→다음 층 일반"이 이 +1/+2로 자동 표현됨.
- **상한 처리**: `i+steps`가 8칸을 넘으면 §6 이스터에그 분기. (당장은 클램프하여 최상위 광물 지급)

### 데이터 소스 일관성
사다리는 런타임에 `tileData.json`을 파싱해 구축한다. 디자이너가 tileData.json의 광물 배치를 바꾸면
가마솥 승급도 자동으로 따라간다. (하드코딩 금지)

---

## 5. 상태 / 저장 / 연출

- **남은 사용횟수(0~3)**는 특수청크 저장 데이터에 포함하여 재로드 시 유지한다
  (`RestoreSavedPixels` 경로 — CLAUDE.md §11 참고). 구현 시 저장 필드 위치는 플랜 단계에서 확정.
- 횟수 0 → 가마솥은 **비활성(소진) 상태**로 전환. 오브젝트/특수청크는 그대로 남고 상호작용(E키·UI)만 차단된다.
  일반 청크로 변환하지 않는다.
- 소진 상태도 저장 데이터에 반영되어, 재로드 시 비활성 가마솥으로 복원된다(이미 다 쓴 가마솥이 다시 살아나지 않음).
- 소진 시 시각 피드백(예: 불 꺼진/식은 가마솥 스프라이트 또는 색 변화)으로 사용 불가를 알린다.
- 연출:
  - 성공계열: 부글부글 끓는 루프 애니메이션 후 결과 스폰.
  - 대실패: 가마솥이 붉게 달아오르는 글로우 후 폭발 광물 산란.

---

## 6. 보류 항목 (hook만 남기고 구현은 추후)

설계상 분기점만 마련하고, 실제 구현/에셋은 나중에 채운다.

1. **대성공 유물·골드 보상**: `CauldronResult.rewardType`에 분기점만 둔다.
   현재는 대성공 = +2칸 광물로만 동작.
2. **최상위 광물 이스터에그 (유니크 보상, C안)**: 8칸 광물(VoidStone/StarFragment)을 넣고
   성공/대성공이 떠 더 올라갈 칸이 없을 때 발동. 컨셉은 "게임에 하나뿐인 특별 보상".
   지금은 분기점만 두고 **임시로 최상위 광물을 그대로 지급(클램프)**. 실제 유니크 보상은 추후.
3. **신규 에셋 필요**: 실패 결과의 "재(Ash)" 아이템 — 기존에 없으므로 신규 추가 필요.
   임시로 기존 잡템(ScrapMetal 등)으로 대체할지, 새 `MineralID.Ash`를 추가할지는 플랜 단계에서 결정.

---

## 7. 신규/재사용 자산 정리

| 구분 | 항목 |
|---|---|
| 신규 코드 | `DokkaebiCauldron`, `CauldronUI`, `MineralUpgradeLadder`, `CauldronResolver`, `CauldronResult` |
| 재사용 | `InteractableBlockBase`(상호작용), `MineralItemController`/`MineralGenerator`(결과 스폰), `ExplosiveMineralReactor.projectileHazardPrefab`(대실패 폭발), `MineralInventory`(투입 UI), tileData.json(승급 사다리) |
| 신규 에셋 | 가마솥 프리팹(특수청크), 부글부글·붉은글로우 연출, CauldronUI 프리팹, "재(Ash)" 아이템 |
| 설정 | `specialChunkSettings.json`에 `DokkaebiCauldron` 스폰 확률 + 결과 확률(15/45/25/15) |

---

## 8. 테스트 포인트 (EditMode)

- `MineralUpgradeLadder.Resolve`: 각 칸 입력에 대한 +1/+2 결과가 기대 칸에 속하는지.
- 상한: 7·8칸 입력 + 대성공(+2) 시 클램프/이스터에그 분기 동작.
- 광물 소속 판정: bleed-over 광물(Copper, Emerald, Diamond 등)이 얕은 층 기준으로 분류되는지.
- `CauldronResolver`: 고정 시드 RNG로 확률 분포(15/45/25/15)가 맞는지.
- 사용횟수: 3회 소모 후 소진 상태 전이.
