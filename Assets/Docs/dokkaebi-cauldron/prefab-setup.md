# 도깨비 가마솥 — 프리팹 제작 가이드

코드(Task 1~8)는 완료. 이 문서는 사람이 Unity 에디터에서 만들 **프리팹·오브젝트 구성**을 정리한다.
프로젝트의 특수청크 컨벤션(`SpecialChunks/World` = 청크 루트, `SpecialChunks/Object` = 자식 엔티티)을 따른다.

> **중요(CLAUDE.md §8):** 특수청크 자식 배치는 반드시 Project 창에서 프리팹을 더블클릭(Prefab Edit 모드)해서 한다.
> 씬 인스턴스에 드래그 후 Apply 하면 local position이 틀어진다. 청크 1칸 = 10유닛, 자식 local 유효범위 X/Y(0~10).

---

## 전체 구조 한눈에

```
[World 특수청크 프리팹]  ← SpecialChunkManager 풀에 등록되는 것 (루트 = TerrainChunk)
  └─ DokkaebiCauldron (Object, 자식)   ← 가마솥 본체 엔티티
       ├─ (SpriteRenderer: 가마솥 이미지)
       ├─ SpawnPoint (빈 Transform)
       └─ BubbleFX (ParticleSystem, 선택)

[CauldronUI]  ← UI 캔버스 아래 (청크 아님)
  └─ Panel
       ├─ SlotContainer (Grid Layout)
       └─ CloseButton

[CauldronSlotButton 프리팹]  ← 광물 슬롯 버튼 (SlotContainer에 동적 생성)
```

---

## A. 가마솥 Object 프리팹 — `SpecialChunks/Object/DokkaebiCauldron.prefab`

가마솥 본체. 가장 빠른 길은 **기존 인터랙티브 Object 프리팹(`Trapbutton.prefab`)을 복제**해서 시작하는 것.

### 루트 GameObject "DokkaebiCauldron"
| 컴포넌트 | 설정 |
|---|---|
| `DokkaebiCauldron` (스크립트) | partial이라 본체+Spawn+Anim 필드가 **한 컴포넌트에 모두** 노출됨 |
| `SpriteRenderer` | 가마솥 이미지. (이 렌더러를 아래 `bodyRenderer`와 `spriteRenderer` 양쪽에 연결) |
| `BoxCollider2D` (선택) | 플레이어가 통과 못 하게 하려면. 상호작용 자체는 거리기반이라 필수 아님 |

### 자식 오브젝트
| 자식 | 용도 |
|---|---|
| `SpawnPoint` (빈 GameObject) | 결과가 튀어나오는 위치. 가마솥 입구쯤에 배치 |
| `BubbleFX` (ParticleSystem, 선택) | 부글부글 파티클. 없으면 색 연출만 |

### Inspector 연결 (DokkaebiCauldron 컴포넌트)
**Cauldron Refs**
- `cauldronUI` ← 비워도 됨(런타임에 씬에서 자동 탐색). 명시 연결하려면 씬의 CauldronUI 드래그
- `spawnPoint` ← 자식 SpawnPoint
- `bodyRenderer` ← 루트 SpriteRenderer (가마솥 이미지)
- `spentColor` ← 식은 색 (기본 회색)

**Cauldron Reward Prefabs**
- `explosiveHazardPrefab` ← **폭발 광물 프리팹** (프로젝트의 `Minerals/Blaststone.prefab`이 후보 — 실제 폭발 광물로 연결). Rigidbody2D 있으면 튀어나오며 흩어짐
- `ashMineral` ← "재" MineralSO (Task 9-1 결정 후. 임시로 ScrapMetal SO 가능)
- `spawnScatter` / `explosiveSpawnArc` ← 기본값 유지

**Cauldron Anim**
- `brewDuration`(1.5) / `brewColor`(연두) / `greatFailColor`(빨강) / `bubbleFx` ← 자식 BubbleFX (선택)

**InteractableBlockBase (상속 필드)**
- `interactionRange` ← 2 정도
- `promptText` ← "E - 광물 제련"
- `priority` ← 5
- `spriteRenderer` ← **bodyRenderer와 동일한 SpriteRenderer** (근접 하이라이트용)
- `idleColor` ← 흰색(White) — 평상시 원래 스프라이트 색 유지
- `nearbyColor` ← 살짝 밝은 색(플레이어 근접 시)

> `_busy`(연출 중)·소진 상태에선 근접 색상변경이 자동 억제되도록 코드에서 `UpdateVisuals()`를 오버라이드해 둠.

---

## B. World 호스트 특수청크 프리팹 — `SpecialChunks/World/DokkaebiCauldronChunk.prefab`

가마솥은 TerrainChunk에 얹혀야 지형 통합(파괴불가 지형·콜라이더·저장)이 된다.
**기존 1칸 World 특수청크 프리팹(예: `SpecialChunks/World/Oxidized1.prefab` 또는 `HybridChunk.prefab`)을 복제**해서 시작.

1. Prefab Edit 모드로 연다 (루트에 `TerrainChunk`/`LargeStaticTerrainChunk` + 파괴불가 오버레이 초기화 컴포넌트가 이미 있음 — 그대로 둠).
2. **A의 DokkaebiCauldron Object를 자식으로 배치** (local position을 청크 안쪽 0~10 범위, 바닥에 닿게).
3. 가마솥이 파묻혀 있게 하려면 청크 지형 픽셀은 그대로 두고, 가마솥 스프라이트가 보이도록 sorting order 조정.

> 가마솥을 **파내야 등장**시키려면: 기존 특수청크처럼 파괴 가능한 지형 안에 묻고, 플레이어가 주변을 파면 노출되는 구조. 그냥 빈 공간에 두려면 청크 중앙을 비운(cavity) World 프리팹을 템플릿으로.

---

## C. CauldronUI — UI 캔버스 아래

청크가 아니라 일반 UI. 기존 인벤토리 UI 캔버스 계층에 패널로 추가.

### 계층
```
CauldronUI (빈 GameObject + CauldronUI 스크립트)
  └─ Panel (panelRoot — 열기/닫기 토글 대상)
       ├─ Title (TMP, "도깨비 가마솥")
       ├─ SlotContainer (Grid Layout Group)   ← slotContainer
       └─ CloseButton (Button)                 ← closeButton
```

### Inspector 연결 (CauldronUI 스크립트)
- `panelRoot` ← Panel
- `slotContainer` ← SlotContainer
- `slotButtonPrefab` ← D의 CauldronSlotButton 프리팹
- `closeButton` ← CloseButton

> `Awake`에서 panelRoot를 자동 비활성화하므로, 씬에선 켜둔 채로 둬도 됨.

---

## D. CauldronSlotButton 프리팹 — `UI/.../CauldronSlotButton.prefab`

광물 1종을 표시하는 버튼. 인벤토리 슬롯 버튼과 비슷. `InventorySlot.prefab` 복제 추천.

### 계층
```
CauldronSlotButton (Button + CauldronSlotButton 스크립트)
  ├─ Icon (Image)            ← icon
  └─ Quantity (TextMeshProUGUI)  ← quantityText
```

### Inspector 연결 (CauldronSlotButton 스크립트)
- `icon` ← Icon (Image)
- `quantityText` ← Quantity (TMP)
- `button` ← 루트 Button

---

## E. 스폰 등록

1. `SpecialChunkManager` 인스펙터 → 가마솥을 스폰할 레이어 풀의 `chunks` 리스트에 **B의 World 프리팹**을 `SpecialChunkDef`로 추가.
   - `prefab` ← DokkaebiCauldronChunk (World)
   - `spawnChance` ← 테스트용으로 크게 (예: 50)
   - `minDepth`/`maxDepth` ← 등장 깊이
2. `specialChunkSettings.json`의 `DokkaebiCauldron` chance도 테스트값으로 올림(현재 0.0).

---

## F. 통합 PlayMode 체크리스트

- [ ] 가마솥 청크 스폰, 가마솥 보임
- [ ] 다가가면 prompt 표시 → E키 → CauldronUI 열림
- [ ] 인벤토리 광물 목록 표시 → 클릭 시 1개 차감 + UI 닫힘
- [ ] 부글부글(성공계열) / 붉은 글로우(대실패) 후 결과 산출
- [ ] 광물(상위)/재/폭발 결과별 정상 스폰
- [ ] 3회 사용 후 소진 색 + 상호작용 차단
- [ ] 세이브→로드 후 남은횟수 유지, 소진 가마솥 복원
