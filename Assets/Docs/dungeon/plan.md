# 던전 입구 시스템 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development 또는
> superpowers:executing-plans 로 태스크 단위 구현. 스텝은 체크박스(`- [ ]`)로 추적.

**Goal:** 오버세계에 확률 스폰되는 1×1 던전 문 특수청크를 E키로 상호작용해 던전 씬으로 진입하고, 문 인스턴스별로 채굴된 rock·수집 보상을 영속 저장하며, 퇴장 시 문 위치로 복귀한다.

**Architecture:** 기존 던전 복귀 스캐폴딩(`SaveManager.PrepareDungeonEntry` · `PlayerSpawner` · `DungeonExitTrigger`)을 재사용하고, 그 위에 **인스턴스별 상태 저장 계층**(`DungeonStateStore` — `CauldronStateStore` 패턴)과 **자체 적용 컴포넌트**(`DungeonRock`/`DungeonRewardPickup`)를 얹는다. 던전은 던전 타입당 실제 씬으로 다루고, 문은 `SpecialChunkManager` 확률 스폰 파이프라인에 등록한다.

**Tech Stack:** Unity 2D, C# (GameScripts asmdef), Unity Test Framework(EditMode/NUnit), JsonUtility 세이브.

## Global Constraints

- **버전 관리: UVCS(Plastic).** git 명령 사용 금지. "커밋" 스텝 없음 — 대신 각 태스크 끝에 **체크포인트**(Unity 에디터에서 임포트/컴파일 확인) 수행.
- **테스트 실행은 사람이 한다.** Claude는 테스트 파일 **작성만** 하고 `mcp__mcp-unity__run_tests` 등 실행 도구를 호출하지 않는다. "테스트 실행" 스텝은 사용자가 Unity Test Runner(EditMode)에서 수행한다.
- **더티 플래그**는 직접 세팅 금지 — `ChunkData.MarkDirty()`/`MarkRenderDirty()` 사용. (이 계획은 지형 픽셀을 건드리지 않으므로 해당 없음)
- **컬렉션 초기화**는 `new List<T>()` 명시형 사용(타깃 타입 `new()` 지양) — 기존 세이브 DTO 컨벤션(`CauldronSaveData`) 준수.
- 신규 스크립트는 모두 **GameScripts 어셈블리**(`Assets/Scripts/**`) 아래 배치 → EditMode 테스트에서 참조 가능.
- **던전 씬 이름은 `"DemoUnderground"`가 아니어야 한다.** `GameManager`/`SaveManager`는 `DemoUnderground`에서 인벤토리 저장을 억제한다(지상 데이터 보호). 던전은 반대로 수집 보상(인벤토리 변경)을 저장해야 하므로, 던전 씬 이름을 그 분기에 넣지 말 것 (설계 §4.3).

## File Structure

| 파일 | 책임 | 종류 |
|------|------|------|
| `Assets/Scripts/_Core/Data/DungeonSaveData.cs` | 인스턴스 저장 DTO | Create |
| `Assets/Scripts/UI/Player/PlayerData.cs` | `dungeonSave` 필드 추가 | Modify |
| `Assets/Scripts/Gameplay/Dungeon/DungeonStateStore.cs` | 좌표→인스턴스상태 런타임 맵 + Capture/Apply | Create |
| `Assets/Scripts/_Core/Managers/SaveManager.cs` | Capture/Apply/Clear 훅 3개 | Modify |
| `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunkManager.cs` | `SpecialChunkType.DungeonDoor` enum 값 | Modify |
| `Assets/Scripts/Gameplay/Dungeon/DungeonDoorChunk.cs` | E키 진입 특수청크 | Create |
| `Assets/Scripts/Gameplay/Dungeon/DungeonRock.cs` | 채굴된 rock 영속(자체 적용) | Create |
| `Assets/Scripts/Gameplay/Dungeon/DungeonRewardPickup.cs` | 수집 보상 영속(자체 적용) | Create |
| `Assets/Tests/EditMode/DungeonSaveDataTests.cs` | DTO 직렬화 테스트 | Create |
| `Assets/Tests/EditMode/DungeonStateStoreTests.cs` | 상태 저장소 로직 테스트 | Create |

---

## Task 1: 저장 DTO + PlayerData 필드

**Files:**
- Create: `Assets/Scripts/_Core/Data/DungeonSaveData.cs`
- Test: `Assets/Tests/EditMode/DungeonSaveDataTests.cs`
- Modify: `Assets/Scripts/UI/Player/PlayerData.cs` (기존 `[Header("Dungeon Instance")]` 블록)

**Interfaces:**
- Produces: `DungeonSaveData { List<DungeonInstanceEntry> entries }`, `DungeonInstanceEntry { int x; int y; List<string> collectedRewardIds; List<int> brokenRockIds }`, `PlayerData.dungeonSave` 필드.

- [ ] **Step 1: 실패 테스트 작성** — `Assets/Tests/EditMode/DungeonSaveDataTests.cs`

```csharp
using NUnit.Framework;
using UnityEngine;

/// <summary>DungeonSaveData JsonUtility 왕복 직렬화 검증 (순수 DTO).</summary>
public class DungeonSaveDataTests
{
    [Test]
    public void JsonUtility_RoundTrips_Entries()
    {
        var data = new DungeonSaveData();
        var e = new DungeonInstanceEntry { x = 2, y = -3 };
        e.brokenRockIds.Add(5);
        e.collectedRewardIds.Add("chest");
        data.entries.Add(e);

        string json = JsonUtility.ToJson(data);
        var restored = JsonUtility.FromJson<DungeonSaveData>(json);

        Assert.AreEqual(1, restored.entries.Count);
        Assert.AreEqual(2, restored.entries[0].x);
        Assert.AreEqual(-3, restored.entries[0].y);
        Assert.AreEqual(5, restored.entries[0].brokenRockIds[0]);
        Assert.AreEqual("chest", restored.entries[0].collectedRewardIds[0]);
    }
}
```

- [ ] **Step 2: 테스트 실행(사용자)** — Unity Test Runner(EditMode)에서 `DungeonSaveDataTests` 실행. 예상: **컴파일 실패**(`DungeonSaveData` 미정의).

- [ ] **Step 3: DTO 구현** — `Assets/Scripts/_Core/Data/DungeonSaveData.cs`

```csharp
// @tags: dungeon, save, data-container, dto
using System.Collections.Generic;

[System.Serializable]
public class DungeonSaveData
{
    public List<DungeonInstanceEntry> entries = new List<DungeonInstanceEntry>();
}

[System.Serializable]
public class DungeonInstanceEntry
{
    public int x;   // 문 청크 좌표 X
    public int y;   // 문 청크 좌표 Y
    public List<string> collectedRewardIds = new List<string>(); // 수집한 보상 ID
    public List<int>    brokenRockIds      = new List<int>();     // 채굴로 부서진 rock ID
}
```

- [ ] **Step 4: PlayerData에 필드 추가** — `Assets/Scripts/UI/Player/PlayerData.cs` 의 기존 블록을 수정:

```csharp
        [Header("Dungeon Instance")]
        public bool isReturningFromDungeon = false;
        public Vector3 preDungeonPosition = Vector3.zero;
        public DungeonSaveData dungeonSave = new DungeonSaveData();
```

(마지막 줄만 신규 추가)

- [ ] **Step 5: 테스트 실행(사용자)** — `DungeonSaveDataTests` 실행. 예상: **PASS**.

- [ ] **Step 6: 체크포인트** — Unity 에디터로 전환해 컴파일 에러 없음 확인. (git 커밋 없음)

---

## Task 2: DungeonStateStore (인스턴스 상태 저장소)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/DungeonStateStore.cs`
- Test: `Assets/Tests/EditMode/DungeonStateStoreTests.cs`

**Interfaces:**
- Consumes: `DungeonSaveData`, `DungeonInstanceEntry` (Task 1).
- Produces: `static class DungeonStateStore`:
  - `Vector2Int CurrentInstance { get; }`
  - `void SetCurrentInstance(Vector2Int coord)`
  - `bool IsRockBroken(Vector2Int coord, int rockId)`
  - `bool IsRewardCollected(Vector2Int coord, string rewardId)`
  - `void MarkRockBroken(int rockId)` (CurrentInstance에 기록)
  - `void MarkRewardCollected(string rewardId)` (CurrentInstance에 기록)
  - `DungeonSaveData Capture()`, `void Apply(DungeonSaveData data)`, `void Clear()`

- [ ] **Step 1: 실패 테스트 작성** — `Assets/Tests/EditMode/DungeonStateStoreTests.cs`

```csharp
using NUnit.Framework;
using UnityEngine;

/// <summary>DungeonStateStore — 인스턴스별 격리·중복방지·Capture/Apply 왕복 검증.</summary>
public class DungeonStateStoreTests
{
    [SetUp]
    public void Setup() => DungeonStateStore.Clear();

    [Test]
    public void MarkRockBroken_UsesCurrentInstance()
    {
        DungeonStateStore.SetCurrentInstance(new Vector2Int(3, -5));
        DungeonStateStore.MarkRockBroken(7);
        Assert.IsTrue(DungeonStateStore.IsRockBroken(new Vector2Int(3, -5), 7));
        Assert.IsFalse(DungeonStateStore.IsRockBroken(new Vector2Int(3, -5), 8));
    }

    [Test]
    public void Reward_IsIsolatedPerInstance()
    {
        DungeonStateStore.SetCurrentInstance(new Vector2Int(1, 1));
        DungeonStateStore.MarkRewardCollected("chest");
        Assert.IsTrue(DungeonStateStore.IsRewardCollected(new Vector2Int(1, 1), "chest"));
        Assert.IsFalse(DungeonStateStore.IsRewardCollected(new Vector2Int(2, 2), "chest"));
    }

    [Test]
    public void MarkRockBroken_NoDuplicates()
    {
        DungeonStateStore.SetCurrentInstance(new Vector2Int(0, 0));
        DungeonStateStore.MarkRockBroken(1);
        DungeonStateStore.MarkRockBroken(1);
        var data = DungeonStateStore.Capture();
        Assert.AreEqual(1, data.entries[0].brokenRockIds.Count);
    }

    [Test]
    public void CaptureThenApply_RoundTrips()
    {
        DungeonStateStore.SetCurrentInstance(new Vector2Int(4, -2));
        DungeonStateStore.MarkRockBroken(9);
        DungeonStateStore.MarkRewardCollected("gold_1");
        var data = DungeonStateStore.Capture();

        DungeonStateStore.Clear();
        Assert.IsFalse(DungeonStateStore.IsRockBroken(new Vector2Int(4, -2), 9));

        DungeonStateStore.Apply(data);
        Assert.IsTrue(DungeonStateStore.IsRockBroken(new Vector2Int(4, -2), 9));
        Assert.IsTrue(DungeonStateStore.IsRewardCollected(new Vector2Int(4, -2), "gold_1"));
    }

    [Test]
    public void Apply_NullData_ClearsState()
    {
        DungeonStateStore.SetCurrentInstance(new Vector2Int(0, 0));
        DungeonStateStore.MarkRockBroken(1);
        DungeonStateStore.Apply(null);
        Assert.IsFalse(DungeonStateStore.IsRockBroken(new Vector2Int(0, 0), 1));
    }
}
```

- [ ] **Step 2: 테스트 실행(사용자)** — EditMode에서 `DungeonStateStoreTests` 실행. 예상: **컴파일 실패**(`DungeonStateStore` 미정의).

- [ ] **Step 3: 구현** — `Assets/Scripts/Gameplay/Dungeon/DungeonStateStore.cs`

```csharp
// @tags: dungeon, save, state, store
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 던전 문 좌표 → 인스턴스 상태(부서진 rock·수집 보상) 런타임 맵.
/// SaveManager가 Capture/Apply로 PlayerData.dungeonSave와 동기화 (CauldronStateStore 패턴).
/// CurrentInstance는 진입 시 DungeonDoorChunk가 세팅하고, 던전 씬 컴포넌트가 Mark로 기록한다.
/// </summary>
public static class DungeonStateStore
{
    private static readonly Dictionary<Vector2Int, DungeonInstanceEntry> _map
        = new Dictionary<Vector2Int, DungeonInstanceEntry>();

    public static Vector2Int CurrentInstance { get; private set; }

    public static void SetCurrentInstance(Vector2Int coord) => CurrentInstance = coord;

    private static DungeonInstanceEntry GetOrCreate(Vector2Int coord)
    {
        if (!_map.TryGetValue(coord, out var e))
        {
            e = new DungeonInstanceEntry { x = coord.x, y = coord.y };
            _map[coord] = e;
        }
        return e;
    }

    public static bool IsRockBroken(Vector2Int coord, int rockId)
        => _map.TryGetValue(coord, out var e) && e.brokenRockIds.Contains(rockId);

    public static bool IsRewardCollected(Vector2Int coord, string rewardId)
        => _map.TryGetValue(coord, out var e) && e.collectedRewardIds.Contains(rewardId);

    public static void MarkRockBroken(int rockId)
    {
        var e = GetOrCreate(CurrentInstance);
        if (!e.brokenRockIds.Contains(rockId)) e.brokenRockIds.Add(rockId);
    }

    public static void MarkRewardCollected(string rewardId)
    {
        if (string.IsNullOrEmpty(rewardId)) return;
        var e = GetOrCreate(CurrentInstance);
        if (!e.collectedRewardIds.Contains(rewardId)) e.collectedRewardIds.Add(rewardId);
    }

    public static DungeonSaveData Capture()
    {
        var data = new DungeonSaveData();
        foreach (var kv in _map) data.entries.Add(kv.Value);
        return data;
    }

    public static void Apply(DungeonSaveData data)
    {
        _map.Clear();
        if (data?.entries == null) return;
        foreach (var e in data.entries)
            _map[new Vector2Int(e.x, e.y)] = e;
    }

    public static void Clear() => _map.Clear();
}
```

- [ ] **Step 4: 테스트 실행(사용자)** — `DungeonStateStoreTests` 5개 전부 **PASS** 확인.

- [ ] **Step 5: 체크포인트** — 컴파일 에러 없음 확인.

---

## Task 3: SaveManager 세이브 훅 연결

**Files:**
- Modify: `Assets/Scripts/_Core/Managers/SaveManager.cs` (3곳 — Cauldron 훅 인접)

**Interfaces:**
- Consumes: `DungeonStateStore.Capture/Apply/Clear` (Task 2), `PlayerData.dungeonSave` (Task 1).

- [ ] **Step 1: Save() 훅 추가** — `data.cauldronSave = CauldronStateStore.Capture();` 바로 아래에 추가:

```csharp
        data.cauldronSave = CauldronStateStore.Capture();
        data.dungeonSave = DungeonStateStore.Capture();
```

- [ ] **Step 2: Load() 훅 추가** — `CauldronStateStore.Apply(data.cauldronSave);` 바로 아래에 추가:

```csharp
        CauldronStateStore.Apply(data.cauldronSave);
        DungeonStateStore.Apply(data.dungeonSave);
```

- [ ] **Step 3: NewGame/초기화 훅 추가** — `CauldronStateStore.Clear();` 바로 아래에 추가:

```csharp
        CauldronStateStore.Clear();
        DungeonStateStore.Clear();
```

- [ ] **Step 4: 검증(사용자, 플레이 모드)** — 게임 실행 → 저장 파일에 `dungeonSave` JSON 노드가 생성되는지 확인(빈 배열이라도 무방). 콘솔 에러 없음 확인.

- [ ] **Step 5: 체크포인트** — 컴파일 에러 없음 확인.

---

## Task 4: DungeonDoorChunk (E키 진입 특수청크)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/DungeonDoorChunk.cs`
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunkManager.cs` (`SpecialChunkType` enum)

**Interfaces:**
- Consumes: `InteractableBlockBase`, `IChunkInitializer`, `TerrainChunk.Coord`, `SaveManager.PrepareDungeonEntry(Vector3)`, `SceneLoader.LoadScene(string)`, `DungeonStateStore.SetCurrentInstance` (Task 2).
- Produces: `DungeonDoorChunk` (특수청크 프리팹에 부착), `SpecialChunkType.DungeonDoor`.

- [ ] **Step 1: enum 값 추가** — `SpecialChunkManager.cs` 의 `SpecialChunkType` 마지막 값 아래에 추가:

```csharp
        MagmaJump,      // 용암 점프맵 — 벽타기 불가, 용암 바닥(화상 피해), 가라앉는 기둥 기믹
        DungeonDoor,    // 던전 입구 — E키 상호작용 시 던전 씬으로 진입
    }
```

- [ ] **Step 2: 구현** — `Assets/Scripts/Gameplay/Dungeon/DungeonDoorChunk.cs`

```csharp
// @tags: dungeon, door, special-chunk, interactable, scene, entry
using UnityEngine;

/// <summary>
/// 던전 문 1×1 특수청크. 플레이어가 E키로 상호작용하면 던전 씬으로 진입한다.
/// 인스턴스 ID = 자기 청크 좌표 (시드 결정론 → 안정적). DokkaebiCauldron과 동일한
/// InteractableBlockBase + IChunkInitializer 패턴.
/// 진입: 좌표를 DungeonStateStore.CurrentInstance로 세팅 → SaveManager.PrepareDungeonEntry(복귀위치 기록)
///       → SceneLoader.LoadScene(dungeonSceneName). 복귀는 기존 PlayerSpawner가 처리.
/// </summary>
public class DungeonDoorChunk : InteractableBlockBase, IChunkInitializer
{
    [Header("Dungeon Door")]
    [Tooltip("진입할 던전 씬 이름 (Build Settings에 등록되어 있어야 함)")]
    [SerializeField] private string dungeonSceneName = "Dungeon_Test";

    private Vector2Int _coord;
    private bool _initialized;
    private bool _entering;

    public int InitializationOrder => 0;

    public void Initialize(Transform parent)
    {
        var tc = GetComponentInParent<TerrainChunk>();
        _coord = tc != null ? tc.Coord : Vector2Int.zero;
        _initialized = true;
    }

    protected override void Start()
    {
        base.Start();
        // 특수청크 스폰 경로를 안 탄 직접 배치(테스트 등) 폴백 초기화.
        if (!_initialized) Initialize(transform.parent);
    }

    protected override void HandleInteraction(GameObject interactor)
    {
        if (_entering) return;
        if (string.IsNullOrEmpty(dungeonSceneName))
        {
            Debug.LogWarning("[DungeonDoor] dungeonSceneName이 설정되지 않았습니다.");
            return;
        }
        _entering = true;

        // 인스턴스 지정 → 던전 씬의 rock/보상이 이 좌표의 저장 상태를 읽는다.
        DungeonStateStore.SetCurrentInstance(_coord);

        // 복귀 위치(플레이어 현재 위치) 기록 + isReturningFromDungeon=true + Save. (기존 API)
        var sm = GameManager.Instance != null ? GameManager.Instance.saveManager : null;
        if (sm != null) sm.PrepareDungeonEntry(interactor.transform.position);
        else Debug.LogWarning("[DungeonDoor] SaveManager 없음 — 복귀 위치 미기록");

        SceneLoader.LoadScene(dungeonSceneName);
    }
}
```

- [ ] **Step 3: 검증(사용자, 에디터)** — 컴파일 확인 후, 임시 확인용으로 씬에 빈 GameObject를 만들어 `DungeonDoorChunk` + `BoxCollider2D`(선택) 부착 → 인스펙터에 `dungeonSceneName` 필드와 프롬프트가 노출되는지 확인. (프리팹·스폰 등록은 Task 7)

- [ ] **Step 4: 체크포인트** — 컴파일 에러 없음 확인.

---

## Task 5: DungeonRock (채굴된 rock 영속)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/DungeonRock.cs`

**Interfaces:**
- Consumes: `DiggableRock.CurrentHp` (기존 게터), `DungeonStateStore.IsRockBroken/MarkRockBroken/CurrentInstance` (Task 2).
- Produces: `DungeonRock` (DiggableRock와 같은 GameObject에 부착, `int rockId`).

- [ ] **Step 1: 구현** — `Assets/Scripts/Gameplay/Dungeon/DungeonRock.cs`

```csharp
// @tags: dungeon, rock, save, persistence, diggable
using UnityEngine;

/// <summary>
/// 던전 안에 직접 배치된 DiggableRock의 채굴 상태를 인스턴스별로 영속화한다.
/// DiggableRock을 수정하지 않고 같은 GameObject에 함께 부착한다.
///  - Start: 이 인스턴스에서 이미 채굴된 rock이면 자기 자신을 파괴(재입장 시 숨김).
///  - OnDestroy: 형제 DiggableRock의 HP가 0 이하(=채굴로 파괴됨)일 때만 부서짐으로 기록.
///    (씬 언로드/앱 종료로 파괴될 때는 HP>0 → 기록하지 않음)
/// rockId는 한 던전 프리팹 안에서 유일해야 한다(수동 부여).
/// </summary>
[RequireComponent(typeof(DiggableRock))]
public class DungeonRock : MonoBehaviour
{
    [Tooltip("이 던전 프리팹 안에서 유일한 rock 식별자")]
    [SerializeField] private int rockId;

    private DiggableRock _rock;

    private void Awake() => _rock = GetComponent<DiggableRock>();

    private void Start()
    {
        if (DungeonStateStore.IsRockBroken(DungeonStateStore.CurrentInstance, rockId))
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_rock != null && _rock.CurrentHp <= 0f)
            DungeonStateStore.MarkRockBroken(rockId);
    }
}
```

- [ ] **Step 2: 검증(사용자, 에디터)** — 컴파일 확인. DiggableRock이 있는 GameObject에 `DungeonRock` 추가 시 인스펙터에 `rockId` 노출 확인. (전체 동작 검증은 Task 7 통합 검증)

- [ ] **Step 3: 체크포인트** — 컴파일 에러 없음 확인.

---

## Task 6: DungeonRewardPickup (수집 보상 영속)

**Files:**
- Create: `Assets/Scripts/Gameplay/Dungeon/DungeonRewardPickup.cs`

**Interfaces:**
- Consumes: `DungeonStateStore.IsRewardCollected/MarkRewardCollected/CurrentInstance` (Task 2), `GameManager.Instance.saveManager.playerData.gold` (기존).
- Produces: `DungeonRewardPickup` (`string rewardId`, `int rewardGold`).

> **범위 노트:** 보상은 우선 **골드 지급**으로 구현한다(인벤토리 API 의존 없이 완결, `DungeonExitTrigger.clearRewardGold`와 동일 패턴). 광물/아이템 보상이 필요해지면 이 컴포넌트를 확장한다. 던전의 주 수집 루프(rock→광물)는 기존 `DiggableRock` + 인벤토리 저장으로 이미 커버된다.

- [ ] **Step 1: 구현** — `Assets/Scripts/Gameplay/Dungeon/DungeonRewardPickup.cs`

```csharp
// @tags: dungeon, reward, pickup, save, persistence
using UnityEngine;

/// <summary>
/// 던전 안의 1회성 보상 오브젝트. 플레이어가 접촉하면 골드를 지급하고 인스턴스별로 수집 처리한다.
///  - Start: 이 인스턴스에서 이미 수집됐으면 자기 파괴(재입장 시 숨김).
///  - OnTriggerEnter2D(Player): 골드 지급 + 수집 기록 + 자기 파괴.
/// 트리거용 Collider2D(isTrigger=true)가 같은 GameObject에 필요하다.
/// rewardId는 한 던전 프리팹 안에서 유일해야 한다(수동 부여).
/// </summary>
public class DungeonRewardPickup : MonoBehaviour
{
    [Tooltip("이 던전 프리팹 안에서 유일한 보상 식별자")]
    [SerializeField] private string rewardId = "reward_1";

    [Tooltip("수집 시 지급할 골드")]
    [SerializeField] private int rewardGold = 500;

    private bool _collected;

    private void Start()
    {
        if (DungeonStateStore.IsRewardCollected(DungeonStateStore.CurrentInstance, rewardId))
            Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_collected || !other.CompareTag("Player")) return;
        _collected = true;

        var sm = GameManager.Instance != null ? GameManager.Instance.saveManager : null;
        if (sm != null && sm.playerData != null)
        {
            sm.playerData.gold += rewardGold;
            Debug.Log($"[DungeonReward] '{rewardId}' 수집 → +{rewardGold} 골드 (현재: {sm.playerData.gold})");
        }

        DungeonStateStore.MarkRewardCollected(rewardId);
        Destroy(gameObject);
    }
}
```

- [ ] **Step 2: 검증(사용자, 에디터)** — 컴파일 확인. GameObject에 `DungeonRewardPickup` + `Collider2D(isTrigger)` 부착 시 인스펙터 필드 노출 확인.

- [ ] **Step 3: 체크포인트** — 컴파일 에러 없음 확인.

---

## Task 7: 에디터 통합 — 프리팹·던전 씬·스폰 등록·엔드투엔드 검증

> 코드가 아닌 **Unity 에디터 작업**이다. 각 서브스텝은 사용자가 수행하고 결과를 확인한다.
> (CLAUDE.md 규칙: 특수청크 자식 배치는 반드시 프리팹 편집 모드에서, DiggableRock 직접 배치는 규칙 #9 준수)

- [ ] **Step 1: 던전 문 특수청크 프리팹** — 기존 특수청크(예: `DokkaebiCauldron`) 프리팹 구조를 참고해 1×1 `TerrainChunk` 기반 문 프리팹을 만들고 `DungeonDoorChunk`를 부착. `dungeonSceneName`을 던전 씬 이름으로 설정.

- [ ] **Step 2: 스폰 등록** — `SpecialChunkManager`(씬 인스턴스)의 `pools`에서 원하는 지층 풀에 `SpecialChunkDef` 추가: `prefab`=던전 문 프리팹, `chunkType`=`DungeonDoor`, `spawnChance`(예: 3~5%), `minDepth/maxDepth`(원하는 깊이), `chunkSizeX/Y`=1. (선택) `specialChunkSettings.json`의 `spawning.spawnChances`에 `{"chunkTypeName":"DungeonDoor","chance":...}` 추가로 확률 오버라이드.

- [ ] **Step 3: 던전 씬 제작** — ⚠️ **빈 씬에서 만들지 말고 잘 도는 `DemoUnderground` 씬을 복제**해서 시작(플레이어 리그·매니저·레이어·조명을 완비된 상태로 상속). 그다음 청크 시스템(`InfinityMapManager`·`SpecialChunkManager`·`ChunkPool`·TerrainGenerator 등)을 **제거**하고 손배치 지형으로 대체. Build Settings에 등록(씬 이름 `DemoUnderground` 금지). 씬 구성:
  - ⚠️ 청크 매니저를 제거하므로 rock 채굴은 `Digger.TryDigRockOnly` 경로로 동작한다(mapManager==null 시 IDiggable만 채굴). 곡괭이/드릴만 rock을 캘 수 있다.
  - ⚠️ 발판(Square 등) **Layer = Ground**(플레이어 `PlayerController.groundLayer` 포함 레이어) — 아니면 `isGrounded=false`라 점프 불가.
  - 플레이어 + 카메라 + **`DungeonPlayerSpawner`**(스폰 지점 설정) + EventSystem/라이팅.
    ⚠️ 오버세계용 `PlayerSpawner`를 쓰지 말 것 — 그것은 `InfinityMapManager`(청크) 로드를 기다리며 플레이어를 Kinematic으로 잡아두는데, 던전엔 청크 매니저가 없어 타임아웃 → 플레이어가 영구 Kinematic(프리즈)이 되고, `isReturningFromDungeon` 플래그도 잘못 소비해 복귀가 깨진다.
  - 던전 문 상호작용 오브젝트에는 **Collider2D(BoxCollider2D, Is Trigger)** 필수 — `PlayerInteractor`가 `Collider2D.Overlap`으로만 대상을 탐지한다.
  - 님의 점프맵 프리팹 배치.
  - 출구 배치: **`DungeonExitInteractable`**(E키 상호작용, Collider2D+IsTrigger 필요) 또는 `DungeonExitTrigger`(접촉 방식) 중 택1. `returnSceneName` = 문이 있던 지형 씬(예: `DemoUnderground`).
  - ⚠️ **던전 씬 이름 자체는 `DemoUnderground`가 아니어야 한다**(Global Constraints 참조). 그래야 던전에서 먹은 광물/보상이 인벤토리에 저장돼 오버세계로 전달된다.
  - 곳곳에 `DiggableRock`(규칙 #9: `preExposed=true`, `minExposedPixels=0`, `Rigidbody2D` 없음) + `DungeonRock`(유일 `rockId`).
  - (선택) `DungeonRewardPickup` + 트리거 콜라이더.

- [ ] **Step 4: 엔드투엔드 검증(사용자, 플레이 모드)** — 다음 시나리오를 순서대로 확인:
  1. 오버세계에서 던전 문 앞 → E → 던전 씬 진입.
  2. 던전에서 rock 채굴 → 광물 드롭·인벤토리 수집. 보상 픽업 → 골드 증가.
  3. `DungeonExitTrigger` 접촉 → 지형 씬 복귀 → **문 위치에 스폰**되는지 확인.
  4. 인벤토리에 던전에서 먹은 광물이 유지되는지 확인(오버세계로 전달).
  5. **같은 문으로 재입장** → 채굴했던 rock이 사라진 채, 먹은 보상이 사라진 채로 나오는지 확인.
  6. **다른 위치의 같은 종류 문**으로 진입 → 독립된(온전한) 상태인지 확인.
  7. 저장 후 재시작 → 5·6의 상태가 유지되는지 확인.

- [ ] **Step 5: 체크포인트** — 시나리오 1~7 모두 통과. 콘솔 에러 없음.

---

## 미결 항목 (구현 후 별도 처리)

- **던전 중간 강제종료** 시 정책: 현재는 저장 파일의 `isReturningFromDungeon=true` + `preDungeonPosition`이 남아 있어, 재시작 시 `PlayerSpawner`가 오버세계 문 위치로 복귀시킨다(던전 중도 진행은 버려짐). 문제 없으면 그대로 둔다.
- rock 부분 HP 저장(중도 채굴 유지)이 필요해지면 `brokenRockIds` → HP 맵으로 확장.
- 보상을 광물/아이템으로 지급하려면 `DungeonRewardPickup` 확장(인벤토리 API 연결).
- `returnSceneName`을 진입 시점에 동적으로 기록(문이 여러 지형 씬에 존재할 경우). 현재는 던전 씬 인스펙터 고정값.
