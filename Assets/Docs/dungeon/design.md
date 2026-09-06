# 던전 입구 시스템 설계

> 작성일: 2026-07-02
> 상태: 설계 확정 — 구현 계획(plan) 착수 전

오버세계 지형에 **1×1 던전 문 특수청크**를 확률 스폰하고, 플레이어가 **E키로 상호작용**하면
별도의 **던전 씬**(손맵 점프맵)으로 진입한다. 보상을 수집하고 나오면 원래 문 위치로 복귀하며,
던전의 가변 상태(수집 보상·부서진 rock)는 **문 인스턴스별로 영속 저장**된다.

---

## 1. 배경·방향 결정

### 1.1 상위 방향 전환
- **1×1 특수청크만 월드에 직접 배치**한다. 그보다 큰 콘텐츠(멀티타일 구조물 등)는 전부
  **"던전 형식"**(문 → 별도 씬)으로 전환한다. 기존 `linkedPieces` 멀티타일 시스템의 방향 전환.

### 1.2 채택 접근 (A: 별도 씬 + 인스턴스별 저장)
검토한 대안:
- **A. 별도 씬 + 인스턴스별 저장 (채택)** — 던전을 별도 씬으로 로드. 상태는 문 인스턴스별 저장.
- **B. 단일 저장 파일 + 좌표 오프셋** — 오버세계 저장 파일 오염·개별 리셋 곤란으로 기각.
- **C. 포켓 차원(씬 전환 없이 먼 좌표로 순간이동)** — 코드는 가장 적으나,
  **손맵 점프맵을 무한 월드 좌표 공간에 주입해야 해서 디자인이 오히려 어려움** + 던전 종류별
  분위기 차별화 곤란. 사용자 의도("새로운 씬")와도 배치되어 기각.

**결정 근거:** 던전은 손으로 만드는 점프맵이다. 에디터 씬/프리팹에서 발판을 배치하는 것이
자연스럽고, 던전 종류마다 조명·분위기를 다르게 줄 수 있으며, 인스턴스 리셋이 파일/엔트리 삭제로
간단하다. 이미 `DemoUpground ↔ DemoUnderground` 씬 전환이 A 방식으로 검증되어 있다.

### 1.3 던전 내부는 청크 시스템을 쓰지 않는다
초기에는 던전도 오버세계와 같은 청크 시스템(픽셀 파괴)을 검토했으나,
**던전에서는 땅파기를 거의 쓰지 않기로** 하여 `InfinityMapManager` 계열을 던전에서 전부 제외한다.
- 던전 지형·발판은 **프리팹에 손배치**.
- rock은 **직접 배치 프리팹**으로 처리(청크 불필요, CLAUDE.md 규칙 #9).
- 결과적으로 던전 씬은 매우 가볍다(플레이어·카메라·Exit 정도).

---

## 2. 전체 흐름

> **조율 노트:** 코드베이스에 던전 복귀 왕복이 **이미 부분 구현**되어 있다
> (`PlayerData.isReturningFromDungeon`/`preDungeonPosition`, `SaveManager.PrepareDungeonEntry`,
> `PlayerSpawner`, `DungeonExitTrigger`). 따라서 설계 초안의 `DungeonSession` 정적 컨텍스트는
> **폐기**하고 기존 `PlayerData` 기반 메커니즘을 재사용한다. 던전은 **던전 종류(타입)당 실제 씬**으로
> 다루며(제네릭 호스트/프리팹 레지스트리 방식 폐기), 님이 만든 프리팹은 그 씬 안에 직접 배치한다.

```
[오버세계] DungeonDoorChunk 앞 → E키
   → DungeonStateStore.SetCurrentInstance(문좌표)          // 인스턴스 ID = 자기 청크 좌표
   → SaveManager.PrepareDungeonEntry(플레이어위치)          // [기존] 복귀 위치 기록 + isReturningFromDungeon=true + Save
   → SceneLoader.LoadScene(dungeonSceneName)               // [기존] LoadingScene 인프라 재사용

[던전 씬] (던전 타입당 실제 씬. 프리팹이 씬에 배치돼 있음)
   → 각 DungeonRock/DungeonRewardPickup가 Start()에서 자기 상태를 자체 적용
        · DungeonStateStore.IsRockBroken(현재, rockId) 이면 → Destroy(자신)  (이미 채굴된 rock 숨김)
        · DungeonStateStore.IsRewardCollected(현재, rewardId) 이면 → Destroy(자신)
   → PlayerSpawner가 플레이어를 씬 스폰 지점에 배치 (복귀 아님 → 기본 스폰)

[플레이어] 점프맵 진행. rock 채굴 → 광물 드롭(기존 DiggableRock) → 인벤토리 수집
   (광물은 오버세계와 동일하게 60초 미수집 시 소멸 → 바닥 광물은 저장 안 함)
   · rock 채굴 파괴 시: DungeonRock.OnDestroy가 DungeonStateStore.MarkRockBroken(rockId)
   · 보상 수집 시: DungeonRewardPickup가 DungeonStateStore.MarkRewardCollected(rewardId)

[출구] DungeonExitTrigger (플레이어 트리거 접촉)   // [기존, 재사용]
   → GameManager.saveManager.Save() → SaveManager가 DungeonStateStore.Capture()로 인스턴스 상태 확정
   → SceneLoader.LoadScene(returnSceneName)

[복귀 씬] PlayerSpawner가 isReturningFromDungeon 감지     // [기존]
   → preDungeonPosition(문 위치)으로 스폰 후 플래그 해제 + Save
```

---

## 3. 컴포넌트

### 3.1 신규 (이번에 구현)
| 컴포넌트 | 종류 | 역할 |
|---------|------|------|
| **DungeonDoorChunk** | MonoBehaviour (`InteractableBlockBase`, `IChunkInitializer`) | 1×1 던전 문 특수청크. `DokkaebiCauldron` 패턴. E키 진입: 자기 좌표를 `DungeonStateStore.SetCurrentInstance`, `SaveManager.PrepareDungeonEntry`, `SceneLoader.LoadScene(dungeonSceneName)`. `SerializeField`: `dungeonSceneName`, 프롬프트 |
| **DungeonStateStore** | static 클래스 | 좌표→인스턴스상태 런타임 맵. `CauldronStateStore` 패턴. `CurrentInstance`, `IsRockBroken/MarkRockBroken`, `IsRewardCollected/MarkRewardCollected`, `Capture/Apply/Clear` |
| **DungeonSaveData / DungeonInstanceEntry** | `[Serializable]` DTO | `PlayerData.dungeonSave`. `CauldronSaveData` 패턴 |
| **DungeonRock** | MonoBehaviour (`DiggableRock`와 같은 GameObject) | `int rockId`. Start: 이미 부서졌으면 자기 파괴. OnDestroy: 형제 `DiggableRock.CurrentHp<=0`이면(=채굴됨) `MarkRockBroken`. **DiggableRock 수정 불필요** |
| **DungeonRewardPickup** | MonoBehaviour | `string rewardId`. 플레이어 접촉/상호작용 → 인벤토리 지급 + `MarkRewardCollected` + 자기 파괴. Start: 이미 수집됐으면 자기 파괴 |

### 3.2 재사용 (기존 코드, 변경 최소)
| 기존 컴포넌트 | 역할 | 변경 |
|--------------|------|------|
| `SaveManager.PrepareDungeonEntry(Vector3)` | 진입 직전 복귀 위치 기록 + Save | 변경 없음 |
| `SaveManager.Save()/Load()` | 세이브 파이프라인 | `DungeonStateStore.Capture/Apply/Clear` 훅 3줄 추가 (Cauldron 라인 인접) |
| `PlayerSpawner` | 복귀 시 문 위치로 스폰 | 변경 없음 |
| `DungeonExitTrigger` | 트리거 퇴장 → Save → 복귀 씬 로드 | 변경 없음 (Save가 Capture를 유발) |
| `SpecialChunkManager` (`pools`/`SpecialChunkType`) | 확률 스폰 등록 | enum에 `DungeonDoor` 추가 + Inspector에 `SpecialChunkDef` 등록(에디터 작업) |
| `DiggableRock` | rock 채굴·광물 드롭 (직접 배치 `preExposed=true`) | 변경 없음 |

---

## 4. 저장 모델

`DokkaebiCauldron`의 좌표맵 직렬화 패턴(`CauldronSaveData` = `List<CauldronEntry>{x,y,value}`)을 그대로 재사용한다.

### 4.1 데이터 구조 (`PlayerData`에 추가)
```csharp
[System.Serializable]
public class DungeonSaveData
{
    public List<DungeonInstanceEntry> entries = new();
}

[System.Serializable]
public class DungeonInstanceEntry
{
    public int x;                          // 문 청크 좌표
    public int y;
    public List<string> collectedRewardIds = new();  // 먹은 보상/수집물 ID
    public List<int>    brokenRockIds     = new();   // 채굴로 부서진 rock ID
}
```

> **rock 상태 단순화:** 초안의 `RockState{hp, broken}`(부분 HP 저장)는 던전 rock에는 과설계다.
> 던전 rock은 "온전 또는 채굴됨" 둘 중 하나이므로 **부서진 rock ID 집합**만 저장한다.
> 채굴 도중 나갔다 오면 rock은 온전 상태로 리셋(경미). 부분 HP 저장이 필요해지면 확장한다.

- `PlayerData`에 `public DungeonSaveData dungeonSave;` 추가.
- `SaveManager.Save()`/`Load()`에 캡처/적용 훅 추가 (Stock/Coin/Cauldron과 동일 위치·패턴).

### 4.2 저장 범위
- **저장함**: 수집한 보상 ID 목록, rock 부서짐/HP.
- **저장 안 함**: 바닥에 떨어진 미수집 광물 — 오버세계와 동일하게 60초 후 소멸(`MineralLifetime` 유지).
  재입장 시 던전은 프리팹에서 새로 생성되고, 이미 부서진 rock만 제거되므로
  "판 rock은 부서진 채, 안 주운 광물은 사라진 채"로 일관되게 복원된다.

### 4.3 인벤토리 처리 — DemoUnderground와 반대 (핵심 주의점)
`DemoUnderground`는 지상 데이터 보호를 위해 **인벤토리를 저장하지 않는다**(`ClearInventoriesForUnderground`).
그러나 던전은 **수집한 보상이 인벤토리를 통해 오버세계로 전달되어야 하므로**, 던전 퇴장 저장 시
**인벤토리 변경을 정상 저장한다**. 던전 씬을 `GameManager`의 `IsUndergroundScene()`·
`ClearInventoriesForUnderground()` 분기에 **포함시키지 않도록** 주의한다.

---

## 5. 복귀 처리 (기존 메커니즘 재사용)

- 진입 시 `SaveManager.PrepareDungeonEntry(플레이어위치)`가 `preDungeonPosition` 기록 +
  `isReturningFromDungeon=true` + Save. (기존)
- 던전 퇴장 시 `DungeonExitTrigger`가 Save 후 `SceneLoader.LoadScene(returnSceneName)`. (기존)
- 복귀 씬 로드 시 `PlayerSpawner`가 `isReturningFromDungeon`을 감지해 `preDungeonPosition`(문 위치)에
  스폰 후 플래그 해제 + Save. (기존)
- `returnSceneName`은 문이 위치한 지형 씬(`DemoUnderground` 등)으로 던전 씬 인스펙터에서 설정한다.

---

## 6. 스폰·인스턴스 ID

- **스폰 방식**: 다른 특수청크처럼 월드 생성 시 깊이별 확률 스폰(minDepth/maxDepth 필터).
- **인스턴스 ID**: 문이 스폰된 **청크 좌표(`Vector2Int`)**. 월드 생성이 시드 기반 결정론적이므로
  같은 좌표의 문은 항상 같은 자리에 생성 → 안정적 고유 ID. 저장 엔트리 키로 사용.
- **인스턴스별 독립 상태**: 같은 종류(같은 `dungeonPrefabId`)의 문이 여러 개 스폰돼도,
  좌표가 다르므로 각자 독립된 저장 엔트리를 갖는다. A문 클리어가 B문에 영향 없음.

---

## 7. 재사용·통합 지점

| 재사용 | 대상 |
|--------|------|
| 씬 전환 인프라 | `SceneLoader` → `LoadingScene` → `LoadingSceneController` |
| E키 상호작용 | `IInteractable` + `MarketTerminalInteractable`/`InteractableBlockBase` 패턴 |
| 정적 씬간 컨텍스트 | `LoadingData` 패턴(→ `DungeonSession`) |
| 좌표맵 저장 직렬화 | `CauldronSaveData`/`CauldronEntry` 패턴 |
| rock 직접 배치 | `DiggableRock` (`preExposed=true`, `Rigidbody2D` 없음, CLAUDE.md #9) |
| 저장 훅 위치 | `SaveManager.Save()/Load()`의 Stock·Coin·Cauldron 블록 인접 |

---

## 8. 설계 제약·주의점

1. **던전은 청크 매니저 없음** — `InfinityMapManager` 관련 부트스트랩 전부 제외. 씬은 가볍게 유지.
2. **rock 직접 배치 규칙(CLAUDE.md #9)** — `preExposed=true`, `minExposedPixels=0`, `Rigidbody2D` 없음.
   안 지키면 rock이 영구 숨김된다.
3. **인벤토리 저장 분기(4.3)** — 던전을 언더그라운드 인벤토리 보호 분기에 넣지 말 것.
4. **인스턴스 ID 안정성(6)** — 좌표 기반. 스폰 결정론이 깨지면 저장이 어긋난다.
5. **던전 내 강제종료 처리** — 퇴장 시점 캡처+저장이 기본. 던전 중간 강제종료 시 동작은
   가마솥/`DemoUnderground` 종료 로직을 참고해 구현 단계에서 확정(미결).
6. **MonoBehaviour 파일 규모** — 새 던전 스크립트는 단일 책임으로 분리(Bootstrap/Exit/State/Door 각각).

---

## 9. 미결 항목 (구현 계획에서 확정)

- 던전 중간 강제종료(앱 quit/pause) 시 저장 정책.
- rock 고유 ID 부여 방식(프리팹 내 인덱스 vs 직렬화 필드).
- 보상 수집물의 ID 체계(기존 아이템 SO ID 재사용 여부).
- 던전 입구 스폰 지점·Exit 배치의 프리팹 규약.
- 던전별 조명/카메라 세팅을 프리팹에 넣을지 호스트 씬에 둘지.
