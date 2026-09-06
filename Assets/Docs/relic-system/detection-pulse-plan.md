# 탐지파동(DetectionPulse) 유물 구현 계획

> **에이전트 작업자용:** 이 계획은 태스크 단위로 실행한다. 각 태스크는 독립적으로 컴파일·검증 가능한
> 산출물로 끝난다. 스텝은 체크박스(`- [ ]`)로 추적.

**목표:** 로딩 범위 밖 특수청크를 온디맨드 파동으로 탐지해 미니맵·전체지도에 영구 마커로 남기는 액티브 유물(RelicID 4021)을 추가한다.

**아키텍처:** 특수청크 위치는 결정론적(`SpecialChunkSelector.TrySelect`)이므로 로딩 없이 예측한다. 유물이 발동 시 반경 내를 예측 스캔 → `DetectedChunkStore`(단일 진실 소스)에 기록 → 미니맵/전체지도 뷰가 구독해 마커를 그린다. 발견 데이터는 `PlayerData`로 영속. 기존 방향 화살표 나침반은 제거한다.

**기술 스택:** Unity 2D, C#, Unity Burst/NativeArray 청크 시스템, 유물 프레임워크(`Relic.*`), 코드 생성 UI.

## 전역 제약 (Global Constraints)

- **버전 관리는 UVCS** — git 명령 사용 금지. 각 태스크 끝의 "체크인"은 사람이 UVCS로 수행.
- **컴파일·플레이 검증은 사람이 수행** — Claude는 코드/테스트 파일 작성만. Unity 테스트 실행 도구 호출 안 함.
- **EditMode 테스트**는 기존 `Assets/Tests/EditMode/EditModeTests.asmdef`에 편입(별도 asmdef 안 만듦).
- **`InfinityMapManager`는 partial class** — 수정 시 `InfinityMapManager.cs`/`.Data.cs` 양쪽 확인.
- **공유 파일 3종**(`RelicID.cs`, `RelicSliceAssetGenerator.cs`, `RelicDebugGranter.cs`)은 기존 줄 보존하며 추가만.
- **네임스페이스**: 유물 코드는 `namespace Relic`. 탐지 인프라는 글로벌(기존 `Systems/Compass/`와 동일).
- **레벨 파라미터 관례**: `[SerializeField] T[] xxxPerLevel`, `Lv()` 헬퍼(`a[Mathf.Clamp(level-1,0,a.Length-1)]`).
- **SerializeReference 필드 추가/변경 시** 사람이 `Tools > Relic > Generate Slice Assets` 재실행 필요.

**설계 문서:** `Assets/Docs/relic-system/detection-pulse-design.md`

---

## 파일 구조

**신규**
- `Assets/Scripts/Systems/Compass/DetectedChunkStore.cs` — 발견 좌표 단일 진실 소스 + 세이브 타입. 순수 C#.
- `Assets/Scripts/Gameplay/Relics/Behaviours/DetectionPulseRelic.cs` — 유물 훅(예측 스캔·기록·VFX).
- `Assets/Tests/EditMode/DetectionPulseTests.cs` — 순수 로직 EditMode 테스트.

**수정**
- `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunkManager.cs` — `PredictAnchorsInRadius` 공개 API.
- `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs` — `IsChunkVisited` 공개 래퍼.
- `Assets/Scripts/UI/Player/PlayerData.cs` — `detectedChunks` 필드.
- `Assets/Scripts/_Core/Managers/SaveManager.cs` — capture/restore 분기.
- `Assets/Scripts/UI/Player/WorldMapOverlay.cs` — 발견 마커 레이어.
- `Assets/Scripts/UI/Player/UndergroundMinimap.cs` — 발견 마커 레이어(신규 오프센터 변환).
- `Assets/Scripts/Gameplay/Relics/Data/RelicID.cs` — `DetectionPulse = 4021`.
- `Assets/Scripts/Editor/RelicSliceAssetGenerator.cs` — 유물 등록.
- `Assets/Scripts/Gameplay/Relics/Debug/RelicDebugGranter.cs` — 디버그 키.

**삭제(사람이 씬에서 오브젝트도 제거)**
- `Assets/Scripts/Systems/Compass/HeadCompassArrow.cs`
- `Assets/Scripts/Systems/Compass/SpecialChunkCompass.cs`(자동 스캔 폐지)

---

## Task 1: 탐지·방문 조회 API

로딩 없이 특수청크를 예측하는 `PredictAnchorsInRadius`와 방문여부 래퍼 `IsChunkVisited`를 추가한다.

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunkManager.cs`
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/InfinityMapManager.cs`

**Interfaces:**
- Consumes: 기존 `_selector`(SpecialChunkSelector), `_registry`(SubChunkRegistry), `InfinityMapManager.worldSeed`(public int), `TileDataManager.Instance.GetTileTypeAtPosition(int,int)`, `_persistenceSystem.TryGetChunkData(Vector2Int, out ChunkSaveData)`.
- Produces:
  - `void SpecialChunkManager.PredictAnchorsInRadius(Vector2Int origin, int radius, List<(Vector2Int coord, SpecialChunkType type)> results)`
  - `bool InfinityMapManager.IsChunkVisited(Vector2Int coord)`

- [ ] **Step 1: `PredictAnchorsInRadius` 추가**

`SpecialChunkManager.cs`의 `#region Public API — 선택` 안, `GetSpecialChunk` 메서드 **아래**에 추가:

```csharp
    /// <summary>
    /// origin 기준 체비쇼프 반경 radius 내의 특수청크 앵커를 결정론적으로 예측한다.
    /// 로드 여부와 무관 — 청크 생성 파이프라인이 스폰 판정에 쓰는 _selector.TrySelect를 그대로 호출한다.
    /// 나침반/탐지 유물이 로딩 범위 밖을 조회할 때 사용.
    /// </summary>
    public void PredictAnchorsInRadius(
        Vector2Int origin, int radius,
        List<(Vector2Int coord, SpecialChunkType type)> results)
    {
        if (results == null) return;
        results.Clear();
        if (_selector == null) return;

        int seed = InfinityMapManager.Instance != null ? InfinityMapManager.Instance.worldSeed : 0;
        var tdm = TileDataManager.Instance;
        if (tdm == null) return;

        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            var coord = new Vector2Int(origin.x + dx, origin.y + dy);
            TileType layer = tdm.GetTileTypeAtPosition(coord.x, coord.y);
            var def = _selector.TrySelect(coord, layer, seed, _registry);
            if (def != null)
                results.Add((coord, def.Value.chunkType));
        }
    }
```

- [ ] **Step 2: `IsChunkVisited` 추가**

`InfinityMapManager.cs`에서 `_persistenceSystem` 필드가 보이는 영역(파일 상단 `:45` 부근 선언, 사용은 `:249`+)을 확인한 뒤, public 메서드 영역에 추가:

```csharp
    /// <summary>
    /// 해당 앵커 좌표에 저장된 청크 데이터가 있으면(=플레이어가 방문·수정함) true.
    /// 탐지 유물이 발견 마커의 '방문/미방문' 상태를 판정할 때 사용.
    /// </summary>
    public bool IsChunkVisited(Vector2Int coord)
        => _persistenceSystem != null && _persistenceSystem.TryGetChunkData(coord, out _);
```

- [ ] **Step 3: EditMode 테스트 작성 (예측 결정론)**

`Assets/Tests/EditMode/DetectionPulseTests.cs` 신규 생성. 예측 핵심 가정 — 동일 (coord, layer, seed) 입력에 `SpecialChunkSelector.TrySelect`가 결정론적이며, 스폰 확률 100% 정의가 항상 선택됨 — 을 순수 C#으로 검증(싱글톤 불필요):

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class DetectionPulseTests
{
    // prefab=null 이면 GetSize가 chunkSizeX/Y(기본 1)로 폴백 → 단일 청크 앵커로 동작.
    private static SpecialChunkManager.SpecialChunkPool MakePool(TileType layer, float chance)
    {
        return new SpecialChunkManager.SpecialChunkPool
        {
            targetLayer = layer,
            chunks = new List<SpecialChunkManager.SpecialChunkDef>
            {
                new SpecialChunkManager.SpecialChunkDef
                {
                    prefab = null,
                    spawnChance = chance,
                    chunkType = SpecialChunkType.DungeonDoor,
                    chunkSizeX = 1, chunkSizeY = 1,
                }
            }
        };
    }

    [Test]
    public void TrySelect_IsDeterministic_ForSameCoord()
    {
        var pools = new List<SpecialChunkManager.SpecialChunkPool> { MakePool(TileType.Dirt, 100f) };
        var sel = new SpecialChunkSelector(pools, minChunkSpacing: 0, layerBoundarySpacing: 0, layerBoundaryYCoords: new int[0]);
        var reg = new SubChunkRegistry();

        var a = sel.TrySelect(new Vector2Int(3, -5), TileType.Dirt, 12345, reg);
        var b = sel.TrySelect(new Vector2Int(3, -5), TileType.Dirt, 12345, reg);

        Assert.IsTrue(a.HasValue, "chance 100% 정의는 항상 선택돼야 한다");
        Assert.AreEqual(a.Value.chunkType, b.Value.chunkType);
    }

    [Test]
    public void TrySelect_ReturnsNull_ForZeroChance()
    {
        var pools = new List<SpecialChunkManager.SpecialChunkPool> { MakePool(TileType.Dirt, 0f) };
        var sel = new SpecialChunkSelector(pools, 0, 0, new int[0]);
        var reg = new SubChunkRegistry();

        Assert.IsFalse(sel.TrySelect(new Vector2Int(3, -5), TileType.Dirt, 12345, reg).HasValue);
    }
}
```

> **주의:** `TileType` enum 멤버명(`Dirt` 등)이 실제와 다르면 프로젝트의 실제 멤버로 교체. `SpecialChunkSelector` 생성자/필드가 `internal`이면 테스트 asmdef에 `InternalsVisibleTo` 필요 여부 확인(현재 `public class SpecialChunkSelector`이므로 불필요).

- [ ] **Step 4: 컴파일 확인 (사람)** — 에러 없음. EditMode 테스트는 사람이 Test Runner로 실행(선택).

- [ ] **Step 5: 체크인 (사람, UVCS)** — "feat(compass): deterministic special-chunk prediction API".

---

## Task 2: DetectedChunkStore (발견 저장소)

발견 좌표의 단일 진실 소스와 세이브 직렬화 타입을 순수 C#으로 만든다. 뷰가 구독한다.

**Files:**
- Create: `Assets/Scripts/Systems/Compass/DetectedChunkStore.cs`
- Modify: `Assets/Tests/EditMode/DetectionPulseTests.cs` (테스트 추가)

**Interfaces:**
- Produces:
  - `struct DetectedChunk { SpecialChunkType type; bool visited; }`
  - `[Serializable] class DetectedChunkSaveData { bool hasData; List<DetectedChunkEntry> entries; }`
  - `class DetectedChunkStore` (static `Instance`): `IReadOnlyDictionary<Vector2Int, DetectedChunk> All`, `event Action OnChanged`, `void Report(Vector2Int, SpecialChunkType, bool)`, `void MarkVisited(Vector2Int)`, `void Clear()`, `DetectedChunkSaveData Capture()`, `void Apply(DetectedChunkSaveData)`.

- [ ] **Step 1: DetectedChunkStore 작성**

```csharp
// @tags: compass, special-chunk, detection, store, save
using System;
using System.Collections.Generic;
using UnityEngine;

public struct DetectedChunk
{
    public SpecialChunkType type;
    public bool visited;
}

[Serializable]
public struct DetectedChunkEntry
{
    public int x, y;
    public int type;       // (int)SpecialChunkType
    public bool visited;
}

[Serializable]
public class DetectedChunkSaveData
{
    public bool hasData;
    public List<DetectedChunkEntry> entries = new List<DetectedChunkEntry>();
}

/// <summary>
/// 탐지 유물이 발견한 특수청크 좌표의 단일 진실 소스. 미니맵·전체지도가 구독한다.
/// 순수 C# 싱글톤(앱 수명). 세이브는 SaveManager가 Capture/Apply로 연동.
/// </summary>
public class DetectedChunkStore
{
    private static DetectedChunkStore _instance;
    public static DetectedChunkStore Instance => _instance ??= new DetectedChunkStore();

    private readonly Dictionary<Vector2Int, DetectedChunk> _found = new Dictionary<Vector2Int, DetectedChunk>();

    public IReadOnlyDictionary<Vector2Int, DetectedChunk> All => _found;

    /// <summary>발견 데이터 변경 시 발행. 뷰가 마커를 갱신한다.</summary>
    public event Action OnChanged;

    /// <summary>파동이 특수청크를 발견/재확인했을 때 호출. 좌표 중복은 갱신(visited 최신화).</summary>
    public void Report(Vector2Int coord, SpecialChunkType type, bool visited)
    {
        if (_found.TryGetValue(coord, out var existing) &&
            existing.type == type && existing.visited == visited)
            return; // 변화 없음 — 이벤트 스팸 방지

        _found[coord] = new DetectedChunk { type = type, visited = visited };
        OnChanged?.Invoke();
    }

    /// <summary>이미 발견된 좌표를 방문 상태로 승격(선택적 훅용).</summary>
    public void MarkVisited(Vector2Int coord)
    {
        if (_found.TryGetValue(coord, out var d) && !d.visited)
        {
            d.visited = true;
            _found[coord] = d;
            OnChanged?.Invoke();
        }
    }

    public void Clear()
    {
        if (_found.Count == 0) return;
        _found.Clear();
        OnChanged?.Invoke();
    }

    public DetectedChunkSaveData Capture()
    {
        var save = new DetectedChunkSaveData { hasData = true };
        foreach (var kv in _found)
            save.entries.Add(new DetectedChunkEntry
            {
                x = kv.Key.x, y = kv.Key.y,
                type = (int)kv.Value.type,
                visited = kv.Value.visited,
            });
        return save;
    }

    public void Apply(DetectedChunkSaveData save)
    {
        _found.Clear();
        if (save != null && save.entries != null)
        {
            foreach (var e in save.entries)
                _found[new Vector2Int(e.x, e.y)] =
                    new DetectedChunk { type = (SpecialChunkType)e.type, visited = e.visited };
        }
        OnChanged?.Invoke();
    }
}
```

- [ ] **Step 2: EditMode 테스트 추가**

`DetectionPulseTests.cs`에 추가:

```csharp
    [Test]
    public void Store_Report_DedupesAndRoundTrips()
    {
        var store = new DetectedChunkStore();
        store.Report(new Vector2Int(1, -2), SpecialChunkType.DungeonDoor, false);
        store.Report(new Vector2Int(1, -2), SpecialChunkType.DungeonDoor, false); // 중복
        store.Report(new Vector2Int(5, -9), SpecialChunkType.AntiGravity, true);

        Assert.AreEqual(2, store.All.Count);

        var save = store.Capture();
        var store2 = new DetectedChunkStore();
        store2.Apply(save);

        Assert.AreEqual(2, store2.All.Count);
        Assert.IsTrue(store2.All[new Vector2Int(5, -9)].visited);
        Assert.AreEqual(SpecialChunkType.DungeonDoor, store2.All[new Vector2Int(1, -2)].type);
    }

    [Test]
    public void Store_OnChanged_FiresOnNewButNotOnUnchanged()
    {
        var store = new DetectedChunkStore();
        int fires = 0;
        store.OnChanged += () => fires++;

        store.Report(new Vector2Int(0, 0), SpecialChunkType.Mine, false); // +1
        store.Report(new Vector2Int(0, 0), SpecialChunkType.Mine, false); // 변화 없음 → 0
        Assert.AreEqual(1, fires);
    }
```

> **주의:** 테스트는 `DetectedChunkStore.Instance`(전역)가 아닌 `new DetectedChunkStore()`로 격리 인스턴스를 쓴다.

- [ ] **Step 3: 컴파일 확인 (사람)** — 에러 없음.

- [ ] **Step 4: 체크인 (사람, UVCS)** — "feat(compass): DetectedChunkStore + save types".

---

## Task 3: 세이브 연동 (영구 저장)

발견 데이터를 `PlayerData`에 얹고 `SaveManager` 파이프라인(coin/relic 패턴)에 연동한다.

**Files:**
- Modify: `Assets/Scripts/UI/Player/PlayerData.cs`
- Modify: `Assets/Scripts/_Core/Managers/SaveManager.cs`

**Interfaces:**
- Consumes: `DetectedChunkStore.Instance.Capture()/Apply()`(Task 2), `DetectedChunkSaveData`(Task 2).
- Produces: `PlayerData.detectedChunks` 필드.

- [ ] **Step 1: PlayerData 필드 추가**

`PlayerData.cs`의 `coinSave` 선언 아래(relicSave가 있으면 그 아래)에 추가:

```csharp
    // Detection compass (탐지파동 유물)
    [Header("Detection")] public DetectedChunkSaveData detectedChunks = new DetectedChunkSaveData();
```

- [ ] **Step 2: SaveManager 저장 분기 추가**

`SaveManager.cs`의 코인 저장 블록(`data.coinSave = ...`, `:432` 부근) **아래**에 추가:

```csharp
            // 탐지 나침반 발견 데이터 저장
            data.detectedChunks = DetectedChunkStore.Instance.Capture();
```

- [ ] **Step 3: SaveManager 복원 분기 추가**

`SaveManager.cs`의 코인 복원 블록 아래(relic 복원 블록이 있으면 그 아래)에 추가:

```csharp
            // 탐지 나침반 발견 데이터 복원
            if (data.detectedChunks != null && data.detectedChunks.hasData)
                DetectedChunkStore.Instance.Apply(data.detectedChunks);
            else
                DetectedChunkStore.Instance.Clear(); // 신규 게임 = 발견 없음
```

> **주의:** 정확한 삽입 위치는 `SaveManager.cs`에서 `coinSave` 문자열을 찾아 그 저장/복원 블록 바로 아래. 저장은 `Capture()`(약 `:432`), 복원은 대응 `ApplySaveData`/`coinSave` 로드 지점.

- [ ] **Step 4: 컴파일 확인 (사람)** — 에러 없음. 저장→재로드 시 발견 좌표 유지 확인은 Task 4 이후 통합 검증에서.

- [ ] **Step 5: 체크인 (사람, UVCS)** — "feat(save): persist detected special chunks".

---

## Task 4: DetectionPulseRelic (유물 코어)

발동 시 반경 예측 스캔 → Store 기록. VFX 없이 탐지 로직만(디버그 로그로 검증 가능).

**Files:**
- Modify: `Assets/Scripts/Gameplay/Relics/Data/RelicID.cs`
- Create: `Assets/Scripts/Gameplay/Relics/Behaviours/DetectionPulseRelic.cs`
- Modify: `Assets/Scripts/Editor/RelicSliceAssetGenerator.cs`
- Modify: `Assets/Scripts/Gameplay/Relics/Debug/RelicDebugGranter.cs`

**Interfaces:**
- Consumes: `SpecialChunkManager.PredictAnchorsInRadius`(Task 1), `InfinityMapManager.IsChunkVisited`(Task 1), `DetectedChunkStore.Instance.Report`(Task 2), `ChunkCoords.ToChunk(Vector3)`(기존).
- Produces: `Relic.DetectionPulseRelic`, `RelicID.DetectionPulse = 4021`.

- [ ] **Step 1: RelicID 예약값 추가**

`RelicID.cs`의 enum 끝(`Jetpack = 4020` 아래, `// 이후 유물은 4021+` 주석 위치)에 추가:

```csharp
        DetectionPulse = 4021,  // 탐지파동 (액티브: 반경 내 특수청크를 미니맵·지도에 표시)
```

- [ ] **Step 2: DetectionPulseRelic 클래스 작성 (탐지만, VFX는 Task 5)**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Relic
{
    // 탐지파동(액티브·즉발): 발동 시 반경 내 특수청크를 결정론 예측해 DetectedChunkStore에 기록한다.
    // 미니맵·전체지도가 Store를 구독해 마커를 그린다. 파동 VFX는 OnActivate에서 함께 재생(Task 5).
    [Serializable]
    public class DetectionPulseRelic : RelicBehaviour
    {
        [SerializeField] private int[] radiusPerLevel = { 4, 5, 6 }; // 청크 좌표 체비쇼프 반경
        [SerializeField] private float cooldown = 60f;

        // 스캔 결과 재사용 버퍼 (GC 절약)
        private readonly List<(Vector2Int coord, SpecialChunkType type)> _buffer
            = new List<(Vector2Int, SpecialChunkType)>();

        private int Radius() => radiusPerLevel[Mathf.Clamp(level - 1, 0, radiusPerLevel.Length - 1)];

        public override float GetDuration() => 0f;          // 즉발 → 바로 쿨타임
        public override float GetCooldown() => cooldown;

        public override void OnActivate()
        {
            if (ctx?.player == null) return;
            var scm = SpecialChunkManager.Instance;
            if (scm == null) return;

            Vector2Int origin = ChunkCoords.ToChunk(ctx.player.position);
            int radius = Radius();

            scm.PredictAnchorsInRadius(origin, radius, _buffer);

            var imm = InfinityMapManager.Instance;
            foreach (var hit in _buffer)
            {
                bool visited = imm != null && imm.IsChunkVisited(hit.coord);
                DetectedChunkStore.Instance.Report(hit.coord, hit.type, visited);
            }

            // Task 5에서 여기 아래에 파동 VFX 재생 호출 추가.
        }
    }
}
```

- [ ] **Step 3: 생성기 등록**

`RelicSliceAssetGenerator.cs`의 `Generate()`에서 기존 `CreateRelic(...)` 나열 마지막에 한 줄 추가하고, `db.allRelics` 리스트에도 추가(기존 줄 보존):

```csharp
        CreateRelic(RelicID.DetectionPulse, "탐지파동", RelicType.Active, new DetectionPulseRelic());
```

> `db.allRelics.Add(...)` 형태로 DB에 넣는 기존 패턴을 그대로 따를 것(파일 내 다른 유물 등록 줄 참고).

- [ ] **Step 4: 디버그 키 바인딩**

`RelicDebugGranter.cs`의 `DefaultBindings`(또는 `bindings[]`) 배열에 **빈 F키**로 추가. `F1`(플라즈마)·`F3`(도박꾼)·`F5`(모루)·`F6~F12`·`F9/F10`(Collider)은 피할 것 → **`F2`** 사용(현재 바인딩과 충돌 없는지 파일에서 확인). 기존 유물의 grant+equip 줄을 복사해 `RelicID.DetectionPulse`, 슬롯 지정(Q 발동 슬롯), 키 `KeyCode.F2`로 바꿔 한 줄 추가.

- [ ] **Step 5: 컴파일 확인 (사람)** — 에러 없음.

- [ ] **Step 6: 슬라이스 에셋 재생성 (사람)** — `Tools > Relic > Generate Slice Assets` 실행, DatabaseLoader에 RelicDatabase 할당 확인.

- [ ] **Step 7: 탐지 동작 검증 (사람, 임시 로그)** — F2로 장착 후 Q 발동. `OnActivate`의 `foreach` 안에 임시 `Debug.Log($"[DetectionPulse] {hit.coord} {hit.type} visited={visited}");`를 넣어(검증 후 제거) 로드 안 된 먼 특수청크가 잡히는지 콘솔로 확인. `DetectedChunkStore.Instance.All.Count` 증가 확인.

- [ ] **Step 8: 체크인 (사람, UVCS)** — "feat(relic): DetectionPulse detection core (4021)".

---

## Task 5: 파동 VFX (월드 링 + 청크 핑)

발동 시 플레이어 중심 링이 퍼지고, 링이 발견 청크 거리를 지날 때 그 위에 핑을 띄운다. 순수 연출.

**Files:**
- Modify: `Assets/Scripts/Gameplay/Relics/Behaviours/DetectionPulseRelic.cs`

**Interfaces:**
- Consumes: `ctx.runner`(RelicManager, `StartCoroutine`), `ctx.player`, `_buffer`(Task 4), `ChunkCoords.ToWorld`/`WorldSize`(기존).

- [ ] **Step 1: VFX 파라미터 + OnActivate 호출 추가**

`DetectionPulseRelic`의 필드부에 추가:

```csharp
        [SerializeField] private float pulseDuration = 0.8f;
        [SerializeField] private Color ringColor = new Color(0.5f, 0.85f, 1f, 0.85f);
        [SerializeField] private float ringWidth = 0.18f;
        [SerializeField] private Color pingColor = new Color(1f, 0.9f, 0.4f, 1f);
```

`OnActivate`의 `// Task 5에서 ...` 주석을 다음으로 교체:

```csharp
            // 파동 VFX (월드 링 + 발견 청크 핑). 순수 연출.
            if (ctx.runner != null)
            {
                float worldRadius = radius * ChunkCoords.WorldSize;
                var pings = new List<Vector2>(_buffer.Count);
                foreach (var hit in _buffer)
                    pings.Add(ChunkCenterWorld(hit.coord));
                ctx.runner.StartCoroutine(PulseVFX(ctx.player.position, worldRadius, pings));
            }
```

- [ ] **Step 2: VFX 코루틴 + 헬퍼 추가**

클래스 안에 추가(스테로이드 `ShockwaveRing` 패턴 확장 — 확장 링 + 거리 도달 시 핑 팝):

```csharp
        private static Vector2 ChunkCenterWorld(Vector2Int coord)
        {
            Vector3 w = ChunkCoords.ToWorld(coord);
            float half = ChunkCoords.WorldSize * 0.5f;
            return new Vector2(w.x + half, w.y + half);
        }

        private System.Collections.IEnumerator PulseVFX(Vector2 center, float maxR, List<Vector2> pings)
        {
            // 확장 링
            var ringGo = new GameObject("RelicDetectionRing");
            var lr = ringGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            const int seg = 64;
            lr.positionCount = seg;
            lr.widthMultiplier = ringWidth;
            lr.numCapVertices = 4;
            var mat = new Material(Shader.Find("Sprites/Default"));
            lr.material = mat;
            lr.sortingOrder = 115;

            // 핑 상태: 각 발견 청크의 플레이어 거리 + 발사 여부
            var pingObjs = new List<GameObject>(pings.Count);
            var pingMats = new List<Material>(pings.Count);
            var pingDist = new List<float>(pings.Count);
            var pingFired = new List<bool>(pings.Count);
            foreach (var p in pings)
            {
                pingDist.Add(Vector2.Distance(center, p));
                pingFired.Add(false);
                pingObjs.Add(null);
                pingMats.Add(null);
            }

            float t = 0f;
            while (t < pulseDuration)
            {
                float k = t / pulseDuration;
                float r = Mathf.Lerp(0.2f, maxR, k);
                Color c = ringColor; c.a = ringColor.a * (1f - k);
                lr.startColor = c; lr.endColor = c;
                for (int i = 0; i < seg; i++)
                {
                    float a = (i / (float)seg) * Mathf.PI * 2f;
                    lr.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * r, center.y + Mathf.Sin(a) * r, 0f));
                }

                // 링 반경이 핑 거리를 지나면 핑 팝
                for (int i = 0; i < pings.Count; i++)
                {
                    if (pingFired[i] || r < pingDist[i]) continue;
                    pingFired[i] = true;
                    var go = new GameObject("RelicDetectionPing");
                    go.transform.position = pings[i];
                    var plr = go.AddComponent<LineRenderer>();
                    plr.useWorldSpace = true; plr.loop = true;
                    plr.positionCount = 20; plr.widthMultiplier = 0.12f; plr.numCapVertices = 4;
                    var pmat = new Material(Shader.Find("Sprites/Default"));
                    plr.material = pmat; plr.sortingOrder = 116;
                    plr.startColor = plr.endColor = pingColor;
                    for (int s = 0; s < 20; s++)
                    {
                        float aa = (s / 20f) * Mathf.PI * 2f;
                        plr.SetPosition(s, pings[i] + new Vector2(Mathf.Cos(aa), Mathf.Sin(aa)) * 0.6f);
                    }
                    pingObjs[i] = go; pingMats[i] = pmat;
                }

                t += Time.deltaTime;
                yield return null;
            }

            // 핑 짧게 페이드 후 정리
            float ft = 0f;
            while (ft < 0.35f)
            {
                float a = 1f - ft / 0.35f;
                for (int i = 0; i < pingObjs.Count; i++)
                {
                    if (pingObjs[i] == null) continue;
                    var plr = pingObjs[i].GetComponent<LineRenderer>();
                    Color pc = pingColor; pc.a = a;
                    plr.startColor = plr.endColor = pc;
                }
                ft += Time.deltaTime;
                yield return null;
            }

            for (int i = 0; i < pingObjs.Count; i++)
            {
                if (pingMats[i] != null) UnityEngine.Object.Destroy(pingMats[i]);
                if (pingObjs[i] != null) UnityEngine.Object.Destroy(pingObjs[i]);
            }
            UnityEngine.Object.Destroy(mat);
            UnityEngine.Object.Destroy(ringGo);
        }
```

> `radius` 변수는 Step 1의 OnActivate 지역변수(`int radius = Radius();`). VFX 호출이 그 스코프 안(스캔 직후)에 있는지 확인.

- [ ] **Step 3: 컴파일 확인 (사람)** — 에러 없음.

- [ ] **Step 4: 연출 검증 (사람)** — Q 발동 시 청록 링이 퍼지고, 특수청크 있는 방향에서 노란 핑이 순차로 튀는지 확인.

- [ ] **Step 5: 체크인 (사람, UVCS)** — "feat(relic): DetectionPulse wave VFX".

---

## Task 6: 전체지도 마커 (WorldMapOverlay)

M키 전체지도에 발견 특수청크를 영구 마커로 표시. 기존 플레이어 마커의 월드→로컬 변환을 재사용.

**Files:**
- Modify: `Assets/Scripts/UI/Player/WorldMapOverlay.cs`

**Interfaces:**
- Consumes: `DetectedChunkStore.Instance.All`(Task 2), 기존 `_panelRt`, `panelWidth`, `_worldWidth`, `_center`, `_generated`.

- [ ] **Step 1: 마커 풀 필드 + 스프라이트 추가**

`WorldMapOverlay.cs` 내부 상태 필드부에 추가:

```csharp
    private readonly List<RectTransform> _detectMarkers = new List<RectTransform>();
    private Sprite _detectSprite;
```

- [ ] **Step 2: `UpdateDetectedMarkers()` 추가 + Update에서 호출**

`UpdateMarker()` 메서드 아래에 추가:

```csharp
    private void UpdateDetectedMarkers()
    {
        var store = DetectedChunkStore.Instance;
        var all = store.All;

        // 스프라이트 lazy 생성 (기존 MakeDiamondSprite 재사용)
        if (_detectSprite == null) _detectSprite = MakeDiamondSprite(32);

        float scale = panelWidth / _worldWidth; // 월드 유닛당 로컬 px (UpdateMarker와 동일)
        float half = ChunkCoords.WorldSize * 0.5f;
        float halfW = panelWidth * 0.5f - 6f;
        float halfH = panelHeight * 0.5f - 6f;

        int idx = 0;
        foreach (var kv in all)
        {
            Vector3 w = ChunkCoords.ToWorld(kv.Key);
            Vector2 world = new Vector2(w.x + half, w.y + half);
            float lx = (world.x - _center.x) * scale;
            float ly = (world.y - _center.y) * scale;
            if (lx < -halfW || lx > halfW || ly < -halfH || ly > halfH) continue; // 화면 밖 스킵

            RectTransform rt = GetOrCreateDetectMarker(idx++);
            rt.gameObject.SetActive(true);
            rt.anchoredPosition = new Vector2(lx, ly);
            var img = rt.GetComponent<Image>();
            // 미방문 밝게 / 클리어 흐리게
            img.color = kv.Value.visited
                ? new Color(0.7f, 0.7f, 0.7f, 0.5f)
                : new Color(1f, 0.85f, 0.35f, 1f);
        }

        for (int i = idx; i < _detectMarkers.Count; i++)
            _detectMarkers[i].gameObject.SetActive(false);
    }

    private RectTransform GetOrCreateDetectMarker(int i)
    {
        if (i < _detectMarkers.Count) return _detectMarkers[i];
        var marker = new GameObject($"DetectMarker{i}").AddComponent<Image>();
        marker.transform.SetParent(_panelRt, false);
        marker.sprite = _detectSprite;
        marker.raycastTarget = false;
        var rt = marker.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(16f, 16f);
        _generated.Add(marker);
        _detectMarkers.Add(rt);
        return rt;
    }
```

`Update()`의 `UpdateMarker();` 호출 **아래**에 `UpdateDetectedMarkers();` 추가.

> **주의:** 마커는 플레이어 마커(`PlayerMarker`)보다 먼저 생성돼 뒤에 깔릴 수 있음. 겹침이 거슬리면 `_playerMarker.SetAsLastSibling()`을 `UpdateMarker` 끝에 추가.

- [ ] **Step 3: 컴파일 확인 (사람)** — 에러 없음.

- [ ] **Step 4: 검증 (사람)** — Q로 탐지 후 M으로 지도 열기 → 발견 청크에 마커, 이미 판 곳은 흐리게. 재접속 후에도 유지(Task 3 세이브).

- [ ] **Step 5: 체크인 (사람, UVCS)** — "feat(map): detected special-chunk markers on world map".

---

## Task 7: 미니맵 마커 (UndergroundMinimap)

플레이어 중앙 고정 미니맵에 발견 청크를 오프센터 마커로. 신규 월드→로컬 변환 필요.

**Files:**
- Modify: `Assets/Scripts/UI/Player/UndergroundMinimap.cs`

**Interfaces:**
- Consumes: `DetectedChunkStore.Instance.All`(Task 2), 미니맵의 월드→텍셀 스케일(코드에서 도출), `_root`, `_generated`, 플레이어 Transform.

- [ ] **Step 1: 미니맵 스케일 확정 (코드 확인 완료)**

`RenderMap(tracker, playerPos)`(`:405`)의 월드→텍셀 매핑은:
```csharp
float pxPerWorld = TEX / ((viewRadius * 2 + 1) * cellSize);   // 텍셀/월드
```
텍스처(`TEX`×`TEX`)는 `_mapSurface`에서 `diameter` px로 표시되므로 **화면 로컬 px/월드**는:
```
PX_PER_WORLD = pxPerWorld_texel × (diameter / TEX)
             = diameter / ((viewRadius * 2 + 1) * cellSize)
```
필드: `viewRadius`(미니맵 필드), `cellSize = DigPathTracker.Instance.cellSize`, `diameter`(미니맵 필드), `TEX`(상수). Y는 플립 없음(RenderMap이 `center + (wy - player.y)*pxPerWorld`, RawImage row0=하단=+Y). 마커는 `_root`(중앙 피벗) 자식이라 `anchoredPosition = (world - player) * PX_PER_WORLD`.

- [ ] **Step 2: 마커 필드 + 갱신 메서드 추가**

필드부(미니맵엔 이미 `playerTr` 필드가 있으니 재사용):

```csharp
    private readonly List<RectTransform> _detectMarkers = new List<RectTransform>();
    private Sprite _detectSprite;
```

갱신 메서드 추가:

```csharp
    private void UpdateDetectedMarkers()
    {
        if (_root == null || playerTr == null) return;
        var tracker = DigPathTracker.Instance;
        if (tracker == null) return;

        if (_detectSprite == null) _detectSprite = MakeDiamondSprite(24);

        float cellSize = Mathf.Max(0.01f, tracker.cellSize);
        float PX_PER_WORLD = diameter / ((viewRadius * 2 + 1) * cellSize);
        float radiusPx = diameter * 0.5f * 0.9f; // 콘텐츠 감쇠(0.8R)와 정합, 원형 안쪽만

        Vector2 pc0 = playerTr.position;
        var all = DetectedChunkStore.Instance.All;
        float half = ChunkCoords.WorldSize * 0.5f;

        int idx = 0;
        foreach (var kv in all)
        {
            Vector3 w = ChunkCoords.ToWorld(kv.Key);
            Vector2 world = new Vector2(w.x + half, w.y + half);
            Vector2 local = (world - pc0) * PX_PER_WORLD;
            if (local.magnitude > radiusPx) continue; // 원형 밖 스킵(주변만 표시)

            RectTransform rt = GetOrCreateDetectMarker(idx++);
            rt.gameObject.SetActive(true);
            rt.anchoredPosition = local;
            var img = rt.GetComponent<Image>();
            img.color = kv.Value.visited
                ? new Color(0.7f, 0.7f, 0.7f, 0.5f)
                : new Color(1f, 0.85f, 0.35f, 1f);
        }
        for (int i = idx; i < _detectMarkers.Count; i++)
            _detectMarkers[i].gameObject.SetActive(false);
    }

    private RectTransform GetOrCreateDetectMarker(int i)
    {
        if (i < _detectMarkers.Count) return _detectMarkers[i];
        var marker = new GameObject($"MiniDetect{i}").AddComponent<Image>();
        marker.transform.SetParent(_root, false);
        marker.sprite = _detectSprite;
        marker.raycastTarget = false;
        var rt = marker.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(10f, 10f);
        _generated.Add(marker);
        _detectMarkers.Add(rt);
        return rt;
    }
```

- [ ] **Step 3: Update 루프에서 호출**

미니맵 `Update()`(`:264`)의 `AnimateFX();` 호출 **아래**에 `UpdateDetectedMarkers();` 추가(지형 렌더는 스로틀되지만 마커는 플레이어 이동에 매 프레임 따라가야 정합). 마커 생성 순서상 베젤(`Frame`)에 가려지면 `UpdateDetectedMarkers` 안에서 마커 생성 시 `_root`의 `Frame` 앞으로 오도록 확인 — 필요 시 마커 `rt.SetAsLastSibling()` 대신 베젤을 먼저 그리는 현 순서 유지하고 마커 sizeDelta를 작게(10px).

- [ ] **Step 4: 컴파일 확인 (사람)** — 에러 없음.

- [ ] **Step 5: 검증 (사람)** — 탐지 후 미니맵에서 발견 청크가 지형과 정렬돼 표시되는지, 플레이어 이동 시 상대 위치가 맞는지 확인. 어긋나면 Step 1의 `worldViewWidth` 재도출.

- [ ] **Step 6: 체크인 (사람, UVCS)** — "feat(minimap): detected special-chunk markers".

---

## Task 8: 방향 화살표 나침반 제거

기존 항상-켜짐 자동 화살표를 폐지한다(미니맵/지도 마커로 대체됨).

**Files:**
- Delete: `Assets/Scripts/Systems/Compass/HeadCompassArrow.cs`
- Delete: `Assets/Scripts/Systems/Compass/SpecialChunkCompass.cs`
- (유지) `CompassPoi.cs`, `CompassTarget.cs`, `ISpecialChunkLocator`, `ActiveAnchorLocator.cs` — 재사용 자산.

**Interfaces:**
- 확인: `HeadCompassArrow`/`SpecialChunkCompass`를 참조하는 코드가 없어야 함(그레프로 검증됨: 5파일 자기완결).

- [ ] **Step 1: 참조 없음 재확인**

`HeadCompassArrow`, `SpecialChunkCompass` 문자열을 전체 검색해 위 2파일 외 참조가 없는지 확인. `CompassPoi`는 `SpecialChunkManager`가 계속 사용하므로 유지.

- [ ] **Step 2: 두 파일 삭제**

`Assets/Scripts/Systems/Compass/HeadCompassArrow.cs`, `SpecialChunkCompass.cs` 삭제(.meta 파일도 함께).

- [ ] **Step 3: 컴파일 확인 (사람)** — 에러 없음. 씬에 부착된 `HeadCompassArrow`/`SpecialChunkCompass` 컴포넌트가 있으면 사람이 씬에서 오브젝트/컴포넌트 제거(Missing Script 정리).

- [ ] **Step 4: 체크인 (사람, UVCS)** — "refactor(compass): remove directional arrow, replaced by map markers".

---

## 통합 검증 (전체 태스크 후, 사람)

- [ ] F2로 탐지파동 장착 → Q 발동. 청록 파동 + 핑 재생.
- [ ] 로딩 범위 밖(카메라에 안 보이는 먼 곳) 특수청크가 미니맵/지도에 잡히는지 (반경 4칸).
- [ ] 이미 판 특수청크는 흐리게(방문), 미방문은 밝게.
- [ ] 쿨타임 60초 동안 재발동 불가.
- [ ] 저장 → 게임 재시작 → 발견 마커 유지.
- [ ] 방향 화살표 나침반이 더 이상 표시되지 않음.

---

## 자체 검토 메모 (작성자)

- **스펙 커버리지**: 예측(§3)=T1, 방문(§4)=T1, Store/저장(§5)=T2·T3, VFX(§7)=T5, 지도마커(§6-1)=T6, 미니맵마커(§6-2)=T7, 화살표제거(§9)=T8, 파라미터(§8)=T4. 전 항목 대응.
- **타입 일관성**: `PredictAnchorsInRadius`/`IsChunkVisited`/`DetectedChunkStore.Report`/`Capture`/`Apply` 시그니처가 T1·T2 정의와 T4·T3·T6·T7 소비처에서 일치.
- **미니맵 스케일**: 코드 확인 완료 — `PX_PER_WORLD = diameter / ((viewRadius*2+1) * cellSize)`로 확정(T7 Step1). placeholder 없음.
- **API 실재 검증**: `ChunkCoords.ToChunk/ToWorld/WorldSize`, `InfinityMapManager.worldSeed`(public), `TileDataManager.GetTileTypeAtPosition`, `SpecialChunkSelector.TrySelect`, `MakeDiamondSprite`(양 UI에 존재), 미니맵 `playerTr`/`viewRadius`/`diameter`/`TEX`, `RelicBehaviour.OnActivate/GetCooldown/GetDuration`+`ctx.player/runner` — 전부 코드 대조 완료.
- **버전 관리**: UVCS — 모든 커밋/삭제는 사람이 수행. git 미사용.
