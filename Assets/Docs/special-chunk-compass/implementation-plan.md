# 특수청크 나침반 구현 계획

> **For agentic workers:** 이 계획은 태스크 단위로 구현한다. 스텝은 체크박스(`- [ ]`)로 추적.

**Goal:** 플레이어에게 가장 가까운 특수청크 방향을 머리 위 화살표로 표시하고, 이후 미니맵이 재사용할 view-agnostic 탐색 코어를 제공한다.

**Architecture:** 순수 C# 탐색 코어(`DeterministicRingLocator`, 좌표 조회 델리게이트 주입) ↔ MonoBehaviour 허브(`SpecialChunkCompass`, 주기 스캔 + 이벤트) ↔ 표시 뷰(`HeadCompassArrow`). 특수청크는 결정론적 배치라 청크 로드 없이 좌표 평가만으로 탐색한다.

**Tech Stack:** Unity 2D, C#, `GameScripts.asmdef` 단일 어셈블리, Unity Test Runner(EditMode, NUnit).

## Global Constraints

- **UVCS 프로젝트**: git 명령 사용 금지. commit은 사람이 UVCS로 수행. 계획의 "Commit" 스텝은 논리적 구획 표시일 뿐 Claude가 git을 호출하지 않는다.
- **테스트 실행은 사람이 수행**: `mcp__mcp-unity__run_tests` 등 호출 금지. Claude는 테스트 파일 작성만.
- 청크 1칸 = 10 world units (`ChunkCoords.WorldSize`), 피벗 좌하단.
- 파일 위치: 신규 코드는 `Assets/Scripts/Systems/Compass/` 아래. 테스트는 `Assets/Scripts/Utils/Tests/`(기존 테스트 위치 관례 따름 — 실제 위치는 Task 0에서 확인).
- 기존 API만 사용: `TileDataManager.Instance.GetTileTypeAtPosition(x,y)`, `SpecialChunkManager.Instance.GetSpecialChunk(coord,layer,seed)`, `ChunkCoords.ToWorld/ToChunk`, `InfinityMapManager.Instance.worldSeed`.

---

## File Structure

- `Assets/Scripts/Systems/Compass/CompassTarget.cs` — 결과 데이터 struct + `ISpecialChunkLocator` 인터페이스
- `Assets/Scripts/Systems/Compass/DeterministicRingLocator.cs` — 순수 C# 링 스캔 탐색기
- `Assets/Scripts/Systems/Compass/SpecialChunkCompass.cs` — MonoBehaviour 허브(주기 스캔 + 이벤트)
- `Assets/Scripts/Systems/Compass/HeadCompassArrow.cs` — 머리 위 화살표 뷰
- `Assets/Scripts/Systems/Compass/SpecialChunkQuery.cs` — `SpecialChunkManager`/`TileDataManager`를 델리게이트로 감싸는 어댑터(테스트 격리용)
- 테스트: `<테스트폴더>/DeterministicRingLocatorTests.cs`

---

## Task 0: 사전 확인 (코드 없음)

**Files:**
- 확인만: `Assets/Scripts/Utils/Tests/` 존재 여부, EditMode asmdef 위치, `SpecialChunkManager.GetSpecialChunk` 시그니처, `SpecialChunkType` 접근성.

- [ ] **Step 1: 테스트 폴더/asmdef 확인**

`Grep`/`Glob`로 기존 EditMode 테스트 asmdef를 찾는다: `Glob("**/*Tests*.asmdef")` 또는 `Grep("TestRunner", "Assets")`. 테스트 파일을 둘 폴더와 참조할 asmdef 이름을 확정한다. 없으면 이 계획의 테스트 asmdef를 Task 1에서 생성.

- [ ] **Step 2: 프리팹→종류 조회 필요성 확인**

`SpecialChunkManager`에 좌표의 `SpecialChunkType`를 반환하는 public 메서드가 없음을 확인(현재 `GetSpecialChunk`는 프리팹만 반환). Task 4에서 얇은 헬퍼를 추가할지 결정. 1차엔 방향만 쓰므로 type은 optional.

---

## Task 1: 탐색 코어 데이터 + 인터페이스

**Files:**
- Create: `Assets/Scripts/Systems/Compass/CompassTarget.cs`

**Interfaces:**
- Produces:
  - `struct CompassTarget { Vector2Int coord; Vector2 worldPos; SpecialChunkType type; }`
  - `interface ISpecialChunkLocator { bool TryFindNearest(Vector2Int origin, out CompassTarget target); }`

- [ ] **Step 1: 파일 작성**

```csharp
// @tags: compass, special-chunk, locator, interface
using UnityEngine;

/// <summary>나침반 탐색 결과 — 가장 가까운 특수청크 앵커.</summary>
public struct CompassTarget
{
    public Vector2Int coord;      // 앵커 청크 좌표
    public Vector2 worldPos;      // 앵커 청크 중심 월드 좌표
    public SpecialChunkType type; // 특수청크 종류 (미확정 시 None)
}

/// <summary>특수청크 방향 탐색 코어. 뷰(화살표·미니맵)를 모른다.</summary>
public interface ISpecialChunkLocator
{
    /// <summary>origin 청크에서 가장 가까운 특수청크 앵커를 찾는다. 없으면 false.</summary>
    bool TryFindNearest(Vector2Int origin, out CompassTarget target);
}
```

- [ ] **Step 2: 컴파일 확인**

Unity 에디터에서 컴파일 에러 없음(사람 확인). `SpecialChunkType`은 전역 enum이라 참조 가능.

- [ ] **Step 3: Commit** (사람이 UVCS로)

---

## Task 2: DeterministicRingLocator (순수 C#, TDD)

**Files:**
- Create: `Assets/Scripts/Systems/Compass/DeterministicRingLocator.cs`
- Test: `<Task0에서 확정한 테스트폴더>/DeterministicRingLocatorTests.cs`

**Interfaces:**
- Consumes: `CompassTarget`, `ISpecialChunkLocator` (Task 1)
- Produces:
  - `delegate bool CoordProbe(Vector2Int coord, out SpecialChunkType type);` — 좌표에 특수청크 앵커가 있으면 true + 종류 out.
  - `class DeterministicRingLocator : ISpecialChunkLocator`
    - `DeterministicRingLocator(int maxRadius, CoordProbe probe, System.Predicate<SpecialChunkType> filter = null)`
    - `bool TryFindNearest(Vector2Int origin, out CompassTarget target)`

> 설계 근거: 실제 `SpecialChunkManager`/`TileDataManager` 대신 `CoordProbe` 델리게이트를 주입 → EditMode 테스트에서 목 없이 순수 로직 검증 가능. 프로덕션 배선은 Task 4.

- [ ] **Step 1: 실패하는 테스트 작성**

```csharp
// @tags: compass, locator, test, editmode
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class DeterministicRingLocatorTests
{
    // 지정 좌표 집합에만 특수청크가 있다고 응답하는 probe 생성 헬퍼
    static CoordProbe ProbeFrom(Dictionary<Vector2Int, SpecialChunkType> map)
    {
        return (Vector2Int c, out SpecialChunkType t) =>
        {
            return map.TryGetValue(c, out t);
        };
    }

    [Test]
    public void ReturnsFalse_WhenNothingInRadius()
    {
        var probe = ProbeFrom(new Dictionary<Vector2Int, SpecialChunkType>());
        var locator = new DeterministicRingLocator(4, probe);

        bool found = locator.TryFindNearest(Vector2Int.zero, out _);

        Assert.IsFalse(found);
    }

    [Test]
    public void FindsNearest_AmongMultiple()
    {
        var map = new Dictionary<Vector2Int, SpecialChunkType>
        {
            { new Vector2Int(3, 0), SpecialChunkType.Mine },   // 거리 3
            { new Vector2Int(1, 1), SpecialChunkType.DungeonDoor }, // 거리 ~1.41 (가장 가까움)
        };
        var locator = new DeterministicRingLocator(8, ProbeFrom(map));

        bool found = locator.TryFindNearest(Vector2Int.zero, out var target);

        Assert.IsTrue(found);
        Assert.AreEqual(new Vector2Int(1, 1), target.coord);
        Assert.AreEqual(SpecialChunkType.DungeonDoor, target.type);
    }

    [Test]
    public void RespectsMaxRadius()
    {
        var map = new Dictionary<Vector2Int, SpecialChunkType>
        {
            { new Vector2Int(10, 0), SpecialChunkType.Mine },
        };
        var locator = new DeterministicRingLocator(4, ProbeFrom(map));

        bool found = locator.TryFindNearest(Vector2Int.zero, out _);

        Assert.IsFalse(found); // 반경 4 밖
    }

    [Test]
    public void Filter_SkipsUnwantedTypes()
    {
        var map = new Dictionary<Vector2Int, SpecialChunkType>
        {
            { new Vector2Int(1, 0), SpecialChunkType.Mine },
            { new Vector2Int(3, 0), SpecialChunkType.DungeonDoor },
        };
        // DungeonDoor만 원함 → 더 가까운 Mine은 무시하고 (3,0) 채택
        var locator = new DeterministicRingLocator(
            8, ProbeFrom(map), t => t == SpecialChunkType.DungeonDoor);

        bool found = locator.TryFindNearest(Vector2Int.zero, out var target);

        Assert.IsTrue(found);
        Assert.AreEqual(new Vector2Int(3, 0), target.coord);
    }

    [Test]
    public void WorldPos_IsChunkCenter()
    {
        var map = new Dictionary<Vector2Int, SpecialChunkType>
        {
            { new Vector2Int(2, 3), SpecialChunkType.Mine },
        };
        var locator = new DeterministicRingLocator(8, ProbeFrom(map));

        locator.TryFindNearest(Vector2Int.zero, out var target);

        // ToWorld(2,3) = (20,30), 중심 = +5,+5 = (25,35)
        Assert.AreEqual(25f, target.worldPos.x, 0.001f);
        Assert.AreEqual(35f, target.worldPos.y, 0.001f);
    }
}
```

- [ ] **Step 2: 테스트 실패 확인** (사람이 Test Runner 실행)

기대: `DeterministicRingLocator` / `CoordProbe` 미정의로 컴파일 실패.

- [ ] **Step 3: 최소 구현 작성**

```csharp
// @tags: compass, special-chunk, locator, ring-scan, deterministic
using System;
using UnityEngine;

/// <summary>좌표에 특수청크 앵커가 있으면 true + 종류 반환.</summary>
public delegate bool CoordProbe(Vector2Int coord, out SpecialChunkType type);

/// <summary>
/// origin 청크에서 링(반경 0→maxRadius)을 바깥으로 스캔해
/// 가장 가까운 특수청크 앵커를 찾는 순수 C# 탐색기.
/// 실제 매니저 대신 CoordProbe 델리게이트를 주입받아 테스트·재사용이 쉽다.
/// </summary>
public class DeterministicRingLocator : ISpecialChunkLocator
{
    private readonly int _maxRadius;
    private readonly CoordProbe _probe;
    private readonly Predicate<SpecialChunkType> _filter;

    public DeterministicRingLocator(int maxRadius, CoordProbe probe,
        Predicate<SpecialChunkType> filter = null)
    {
        _maxRadius = Mathf.Max(0, maxRadius);
        _probe = probe;
        _filter = filter;
    }

    public bool TryFindNearest(Vector2Int origin, out CompassTarget target)
    {
        target = default;
        if (_probe == null) return false;

        // 반경 r 링을 안쪽부터 확장. 어떤 링에서 후보가 나오면
        // 그 링(및 이미 지난 안쪽)이 최소 거리 후보 → 최소 제곱거리 선택 후 종료.
        // 단, r 링 채택 전에 r+1 링 코너가 더 가까울 수 있으므로
        // 안전하게 "첫 후보 발견 반경 rf" 이후 rf 링까지만 비교하면 충분:
        //   체비쇼프 거리 r 링의 최소 유클리드 거리 = r, 최대 = r*sqrt(2).
        //   r+1 링 최소 거리 = r+1 > r 이므로, rf 링 내부 최소거리 후보가
        //   rf+1 링 어떤 점보다 항상 가깝지는 않다(코너 vs 변).
        //   → 정확성 위해: 후보를 처음 발견한 반경 rf에서 rf 링 전체를 모으고,
        //     추가로 rf 링 최소거리와 rf+1 링 최소거리(rf+1)를 비교해
        //     rf+1 링도 필요한 경우만 1링 더 스캔한다.
        bool haveBest = false;
        float bestSqr = float.MaxValue;
        CompassTarget best = default;

        for (int r = 0; r <= _maxRadius; r++)
        {
            ScanRing(origin, r, ref haveBest, ref bestSqr, ref best);

            // 다음 링의 이론적 최소 거리(r+1)가 현재 best보다 크면 종료.
            if (haveBest && (r + 1) * (r + 1) > bestSqr)
                break;
        }

        if (haveBest) { target = best; return true; }
        return false;
    }

    // 체비쇼프 반경 r의 테두리 좌표만 순회 (r=0은 중심 1개)
    private void ScanRing(Vector2Int origin, int r,
        ref bool haveBest, ref float bestSqr, ref CompassTarget best)
    {
        if (r == 0) { Consider(origin, ref haveBest, ref bestSqr, ref best); return; }

        for (int dx = -r; dx <= r; dx++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue; // 테두리만
                Consider(origin + new Vector2Int(dx, dy),
                    ref haveBest, ref bestSqr, ref best);
            }
        }
    }

    private void Consider(Vector2Int coord,
        ref bool haveBest, ref float bestSqr, ref CompassTarget best)
    {
        if (!_probe(coord, out var type)) return;
        if (_filter != null && !_filter(type)) return;

        Vector2Int d = coord;
        // origin 대비 제곱거리 (origin은 Consider 호출부에서 이미 offset 반영됨 → coord 절대좌표)
        // 제곱거리는 아래 TryFindNearest 종료조건과 동일 좌표계(청크 단위)여야 하므로
        // origin을 기준으로 재계산: 호출자가 절대 coord를 넘기므로 여기서 origin 필요.
        // → best 비교는 청크 단위 제곱거리 사용.
        // (origin은 필드가 아니므로 델타를 넘기도록 재작성) — 아래 주: 실제 구현은 델타 기반.
        float sqr = SqrChunkDist(coord);
        if (sqr < bestSqr)
        {
            bestSqr = sqr;
            best = new CompassTarget
            {
                coord = coord,
                worldPos = ChunkCenterWorld(coord),
                type = type,
            };
            haveBest = true;
        }
    }

    // origin 기준 제곱거리가 필요하므로 origin을 인스턴스 스코프로 잡는다.
    private Vector2Int _origin;
    private float SqrChunkDist(Vector2Int coord)
    {
        int dx = coord.x - _origin.x;
        int dy = coord.y - _origin.y;
        return dx * dx + dy * dy;
    }

    private static Vector2 ChunkCenterWorld(Vector2Int coord)
    {
        Vector3 w = ChunkCoords.ToWorld(coord);
        float half = ChunkCoords.WorldSize * 0.5f;
        return new Vector2(w.x + half, w.y + half);
    }
}
```

> **주의(구현자용):** 위 스니펫에서 `_origin`을 `Consider`가 참조한다. `TryFindNearest` 진입 시 `_origin = origin;`을 **가장 먼저 대입**하도록 추가할 것. 종료조건의 `(r+1)*(r+1)`은 청크 단위 제곱거리와 같은 좌표계이므로 정합한다.

- [ ] **Step 4: 종료조건 정합성 확인 (구현자 자체 점검)**

`bestSqr`는 청크 단위 유클리드 제곱거리, `(r+1)^2`도 청크 단위. 링 r까지 스캔 후 다음 링 최소거리 r+1이 best보다 멀면 종료 → 가장 가까운 것 보장. `FindsNearest_AmongMultiple` 테스트가 이를 검증.

- [ ] **Step 5: `_origin` 대입 추가**

`TryFindNearest` 본문 첫 줄:
```csharp
target = default;
_origin = origin;
if (_probe == null) return false;
```

- [ ] **Step 6: 테스트 통과 확인** (사람이 Test Runner 실행)

기대: 5개 테스트 모두 PASS.

- [ ] **Step 7: Commit** (사람이 UVCS로)

---

## Task 3: SpecialChunkQuery 어댑터 (프로덕션 probe)

**Files:**
- Create: `Assets/Scripts/Systems/Compass/SpecialChunkQuery.cs`

**Interfaces:**
- Consumes: `CoordProbe` (Task 2), `SpecialChunkManager`, `TileDataManager`, `InfinityMapManager`
- Produces: `static class SpecialChunkQuery { static CoordProbe CreateProbe(); }`

> `GetSpecialChunk`는 프리팹만 반환하고 종류를 안 준다. 1차엔 `type`을 `SpecialChunkType.None`으로 채운다(방향만 사용). 종류가 필요해지면 Task 4에서 `SpecialChunkManager`에 헬퍼를 추가하고 여기서 배선.

- [ ] **Step 1: 파일 작성**

```csharp
// @tags: compass, special-chunk, probe, adapter
using UnityEngine;

/// <summary>
/// DeterministicRingLocator가 쓸 프로덕션 CoordProbe를 만든다.
/// 매니저 싱글턴을 감싸 좌표→(특수청크 유무) 판정으로 변환한다.
/// </summary>
public static class SpecialChunkQuery
{
    /// <summary>매니저 준비 안 됐으면 항상 false를 반환하는 안전 probe.</summary>
    public static CoordProbe CreateProbe()
    {
        return Probe;
    }

    private static bool Probe(Vector2Int coord, out SpecialChunkType type)
    {
        type = SpecialChunkType.None;

        var scm = SpecialChunkManager.Instance;
        var tdm = TileDataManager.Instance;
        var imm = InfinityMapManager.Instance;
        if (scm == null || tdm == null || imm == null) return false;

        TileType layer = tdm.GetTileTypeAtPosition(coord.x, coord.y);
        MonoBehaviour prefab = scm.GetSpecialChunk(coord, layer, imm.worldSeed);
        // 1차: 종류 미해석. 방향 표시만 하므로 None 유지.
        return prefab != null;
    }
}
```

- [ ] **Step 2: 컴파일 확인** (사람)

`GetSpecialChunk` 반환형이 `MonoBehaviour`임을 Task 0에서 확인한 시그니처와 대조. 다르면 반환형만 맞춘다.

- [ ] **Step 3: Commit** (사람이 UVCS로)

---

## Task 4: SpecialChunkCompass (MonoBehaviour 허브)

**Files:**
- Create: `Assets/Scripts/Systems/Compass/SpecialChunkCompass.cs`

**Interfaces:**
- Consumes: `DeterministicRingLocator`, `SpecialChunkQuery.CreateProbe()`, `CompassTarget`, `ChunkCoords.ToChunk`
- Produces:
  - `CompassTarget? CurrentTarget { get; }`
  - `event Action<CompassTarget?> OnTargetChanged`

- [ ] **Step 1: 파일 작성**

```csharp
// @tags: compass, special-chunk, hub, monobehaviour, scan
using System;
using UnityEngine;

/// <summary>
/// 주기적으로 가장 가까운 특수청크를 탐색하는 view-agnostic 허브.
/// 화살표·미니맵 등 모든 뷰는 CurrentTarget / OnTargetChanged 만 구독한다.
/// </summary>
public class SpecialChunkCompass : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private float scanInterval = 0.5f;
    [SerializeField] private int maxRadius = 8;

    public CompassTarget? CurrentTarget { get; private set; }
    public event Action<CompassTarget?> OnTargetChanged;

    private DeterministicRingLocator _locator;
    private float _timer;

    private void Awake()
    {
        _locator = new DeterministicRingLocator(maxRadius, SpecialChunkQuery.CreateProbe());
    }

    private void Update()
    {
        if (player == null) return;

        _timer += Time.deltaTime;
        if (_timer < scanInterval) return;
        _timer = 0f;

        Scan();
    }

    private void Scan()
    {
        Vector2Int origin = ChunkCoords.ToChunk(player.position);

        CompassTarget? next = _locator.TryFindNearest(origin, out var t)
            ? t : (CompassTarget?)null;

        // 좌표 기준으로 변경 감지 (없음↔있음, 다른 앵커)
        bool changed =
            (next.HasValue != CurrentTarget.HasValue) ||
            (next.HasValue && CurrentTarget.HasValue && next.Value.coord != CurrentTarget.Value.coord);

        CurrentTarget = next;
        if (changed) OnTargetChanged?.Invoke(next);
    }
}
```

- [ ] **Step 2: 컴파일 확인** (사람)

- [ ] **Step 3: 씬 배치** (사람)

플레이어(또는 매니저) 오브젝트에 `SpecialChunkCompass` 부착, `player` 필드에 플레이어 Transform 연결. `scanInterval=0.5`, `maxRadius=8`.

- [ ] **Step 4: Commit** (사람이 UVCS로)

---

## Task 5: HeadCompassArrow (표시 뷰)

**Files:**
- Create: `Assets/Scripts/Systems/Compass/HeadCompassArrow.cs`

**Interfaces:**
- Consumes: `SpecialChunkCompass.CurrentTarget`, `CompassTarget.worldPos`

- [ ] **Step 1: 파일 작성**

```csharp
// @tags: compass, arrow, view, head-indicator
using UnityEngine;

/// <summary>
/// 플레이어 머리 위에서 가장 가까운 특수청크 방향을 가리키는 화살표.
/// SpecialChunkCompass의 현재 타겟만 읽는다(탐색 로직 없음).
/// 화살표 스프라이트는 기본 "위(+Y)" 방향 기준으로 가정, angleOffset으로 보정.
/// </summary>
public class HeadCompassArrow : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private SpecialChunkCompass compass;
    [SerializeField] private Transform arrow;        // 회전시킬 스프라이트 루트
    [SerializeField] private GameObject arrowVisual; // 표시/숨김 대상(스프라이트 오브젝트)
    [SerializeField] private float headOffsetY = 1.2f;
    [SerializeField] private float angleOffset = -90f; // +Y 기준 스프라이트 보정

    private void LateUpdate()
    {
        if (player == null || compass == null || arrow == null) return;

        // 머리 위 따라다니기
        Vector3 head = player.position + new Vector3(0f, headOffsetY, 0f);
        arrow.position = head;

        var target = compass.CurrentTarget;
        if (!target.HasValue)
        {
            if (arrowVisual != null) arrowVisual.SetActive(false);
            return;
        }

        if (arrowVisual != null) arrowVisual.SetActive(true);

        Vector2 dir = target.Value.worldPos - (Vector2)player.position;
        if (dir.sqrMagnitude < 0.0001f) return; // 같은 위치면 회전 유지

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + angleOffset;
        arrow.rotation = Quaternion.Euler(0f, 0f, angle);
    }
}
```

- [ ] **Step 2: 컴파일 확인** (사람)

- [ ] **Step 3: 씬 배치** (사람)

- 월드 스페이스 화살표 오브젝트 생성(플레이어 자식 아님 — 회전 독립).
  - 루트에 `HeadCompassArrow` 부착, `arrow`=루트, `arrowVisual`=화살표 스프라이트 자식.
  - `player`, `compass` 참조 연결.
- 화살표 스프라이트는 "위쪽" 기준 에셋 사용, 기울면 `angleOffset` 조정.

- [ ] **Step 4: 실행 검증** (사람)

플레이어 이동 시: 근처 특수청크 방향으로 화살표 회전, 반경 밖이면 화살표 숨김.

- [ ] **Step 5: Commit** (사람이 UVCS로)

---

## Self-Review 결과

- **스펙 커버리지:** 탐색 코어(Task 1-2), 프로덕션 배선(Task 3), 허브+이벤트(Task 4), 머리 위 화살표(Task 5), 확장점(filter=Task 2, CurrentTarget 구독=Task 4) 모두 태스크 존재. ✅
- **확장 로드맵(종류 지정·미니맵·다중 타겟):** 지금 미구현이 설계 의도 — Task 2 `filter`, Task 4 이벤트로 훅 열림. ✅
- **타입 정합성:** `CompassTarget{coord,worldPos,type}`, `CoordProbe(coord, out type)`, `TryFindNearest(origin, out target)` 전 태스크 일관. ✅
- **주의점:** Task 2의 `_origin` 대입(Step 5)과 `GetSpecialChunk` 반환형(Task 0/3) 두 곳만 구현 시 확인 필요 — 계획에 명시함.
