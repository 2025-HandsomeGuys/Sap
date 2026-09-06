# 별자리 한붓그리기 퍼즐 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 기존 「별빛 잇기」(집합 일치) 퍼즐을 정해진 별자리 도형을 펜을 떼지 않고 각 선을 한 번씩만 긋는 **한붓그리기(Euler trail)** 퍼즐로 전환한다.

**Architecture:** 순수 C# 상태 기계 `StarStrokeState`(UnityEngine 비의존)가 펜 위치·그은 선·유효 수 판정·막다른 길 감지를 담당하고, `StarPuzzleManager`(MonoBehaviour)는 버튼 입력을 이 상태 기계에 위임하며 가이드 선·노드 색상 등 시각 표현만 맡는다. 로직/표현 분리로 `StarStrokeState`는 EditMode 단위 테스트가 가능하다.

**Tech Stack:** Unity 2022+ / C# / NUnit(EditMode) / LineRenderer / SpriteRenderer

## Global Constraints

- **버전 관리**: 이 프로젝트는 UVCS를 쓰며 **git 명령을 실행하지 않는다**. 각 Task 끝의 "체크포인트"는 파일 저장 상태를 뜻하며, 실제 커밋은 사람이 UVCS로 수행한다. 에이전트는 `git`/`cm` 명령을 호출하지 않는다.
- **테스트 실행**: Claude는 EditMode 테스트 파일을 **작성만** 하고 Unity Test Runner를 호출하지 않는다. "테스트 실행" 스텝은 **사람이** Test Runner(EditMode)에서 돌리는 게이트 아님 절차다. 에이전트는 테스트 실행 결과를 기다리지 말고 다음 스텝으로 진행한다.
- **네임스페이스**: 신규 런타임 코드는 `Gameplay.Terrain.Tiles.SpecialChunks.SpaceLayer` 네임스페이스에 둔다.
- **테스트 관례**: EditMode 테스트는 `Assets/Tests/EditMode/`에 두고, 네임스페이스 없이 public 클래스 + `[Test]` 메서드로 작성한다(기존 `CauldronResolverTests.cs` 패턴). 테스트 asmdef는 이미 `GameScripts`를 참조하므로 asmdef 수정 불필요.
- **어셈블리**: 신규 런타임 클래스는 `GameScripts` 어셈블리(=`Assets/Scripts/` 하위)에 위치해야 테스트에서 접근 가능하다.
- **더티 플래그/콜라이더 등 지형 규칙**은 이 작업 범위와 무관(별 퍼즐은 지형 파기와 독립).

---

### Task 1: `StarStrokeState` 순수 상태 기계 + EditMode 테스트

한붓그리기 규칙 전체를 UnityEngine 없이 구현하고 단위 테스트한다. 이 Task가 퍼즐 로직의 핵심이다.

**Files:**
- Create: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/SpaceLayer/StarStrokeState.cs`
- Test: `Assets/Tests/EditMode/StarStrokeStateTests.cs`

**Interfaces:**
- Consumes: `StarEdge`(기존 struct, `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/SpaceLayer/StarEdge.cs`) — 생성자가 양끝을 정렬해 `A-B == B-A` 보장, `Equals`/`GetHashCode` 구현됨.
- Produces (Task 4가 의존):
  - `enum MoveKind { Started, Drew, CancelledStart, Ignored }`
  - `struct MoveResult { MoveKind Kind; StarEdge Edge; }` (`Edge`는 `Kind==Drew`일 때만 유효)
  - `class StarStrokeState`
    - `StarStrokeState(IEnumerable<StarEdge> targetEdges)`
    - `string CurrentNodeId { get; }` (펜 위치, 시작 전 null)
    - `int TargetCount { get; }`, `int DrawnCount { get; }`
    - `bool IsSolved { get; }`
    - `IReadOnlyCollection<StarEdge> TargetEdges { get; }`
    - `bool IsEdgeDrawn(StarEdge e)`
    - `MoveResult TryPress(string nodeId)`
    - `bool IsDeadEnd()`
    - `void Reset()`
    - `static int CountOddDegreeNodes(IEnumerable<StarEdge> edges)`
    - `static bool IsOneStrokePossible(IEnumerable<StarEdge> edges)`

- [ ] **Step 1: 실패하는 테스트 작성**

Create `Assets/Tests/EditMode/StarStrokeStateTests.cs`:

```csharp
using NUnit.Framework;
using System.Collections.Generic;
using Gameplay.Terrain.Tiles.SpecialChunks.SpaceLayer;

public class StarStrokeStateTests
{
    // 정사각형 A-B-C-D + 대각선 A-C (봉투 일부). 한붓그리기 가능.
    private static List<StarEdge> Square()
    {
        return new List<StarEdge>
        {
            new StarEdge("A", "B"),
            new StarEdge("B", "C"),
            new StarEdge("C", "D"),
            new StarEdge("D", "A"),
            new StarEdge("A", "C"),
        };
    }

    [Test]
    public void FirstPress_StartsPen()
    {
        var s = new StarStrokeState(Square());
        var r = s.TryPress("A");
        Assert.AreEqual(MoveKind.Started, r.Kind);
        Assert.AreEqual("A", s.CurrentNodeId);
        Assert.AreEqual(0, s.DrawnCount);
    }

    [Test]
    public void SamePress_NoEdgesDrawn_CancelsStart()
    {
        var s = new StarStrokeState(Square());
        s.TryPress("A");
        var r = s.TryPress("A");
        Assert.AreEqual(MoveKind.CancelledStart, r.Kind);
        Assert.IsNull(s.CurrentNodeId);
    }

    [Test]
    public void ValidAdjacent_DrawsEdge_AndMovesPen()
    {
        var s = new StarStrokeState(Square());
        s.TryPress("A");
        var r = s.TryPress("B");
        Assert.AreEqual(MoveKind.Drew, r.Kind);
        Assert.AreEqual(new StarEdge("A", "B"), r.Edge);
        Assert.AreEqual("B", s.CurrentNodeId);
        Assert.AreEqual(1, s.DrawnCount);
        Assert.IsTrue(s.IsEdgeDrawn(new StarEdge("A", "B")));
    }

    [Test]
    public void NonAdjacent_IsIgnored()
    {
        var s = new StarStrokeState(Square());
        s.TryPress("A");
        var r = s.TryPress("D"); // A-D는 존재하지만 유효; B-D 없음으로 테스트
        // A에서 D는 실제로 존재하므로, 존재하지 않는 쌍으로 다시 확인:
        s.Reset();
        s.TryPress("B");
        var r2 = s.TryPress("D"); // B-D 선은 도형에 없음
        Assert.AreEqual(MoveKind.Ignored, r2.Kind);
        Assert.AreEqual("B", s.CurrentNodeId); // 펜 안 움직임
        Assert.AreEqual(0, s.DrawnCount);
    }

    [Test]
    public void AlreadyDrawn_IsIgnored()
    {
        var s = new StarStrokeState(Square());
        s.TryPress("A");
        s.TryPress("B"); // A-B 그음, 펜 B
        s.TryPress("A"); // B-A 이미 그은 선 → 무시
        Assert.AreEqual(1, s.DrawnCount);
        Assert.AreEqual("B", s.CurrentNodeId);
    }

    [Test]
    public void DeadEnd_WhenStuckWithRemainingEdges()
    {
        // 경로: A-B, B-C, C-D, D-A → 펜 A. 남은 선 A-C 하나. A에 인접 미사용 A-C 있으므로 막다른길 아님.
        var s = new StarStrokeState(Square());
        s.TryPress("A"); s.TryPress("B"); s.TryPress("C"); s.TryPress("D"); s.TryPress("A");
        Assert.IsFalse(s.IsDeadEnd());
        // A-C까지 그으면 완성
        s.TryPress("C");
        Assert.IsTrue(s.IsSolved);
        Assert.IsFalse(s.IsDeadEnd());
    }

    [Test]
    public void DeadEnd_True_WhenPenHasNoUndrawnEdges()
    {
        // 삼각형 A-B-C 세 변 + 꼬리 C-D. 잘못된 순서로 D에서 끝나면 막다른 길.
        var edges = new List<StarEdge>
        {
            new StarEdge("A","B"), new StarEdge("B","C"),
            new StarEdge("C","A"), new StarEdge("C","D"),
        };
        var s = new StarStrokeState(edges);
        // A→B→C→D 로 가면 D에서 막힘(A-C, ... 남았지만 D엔 미사용 선 없음)
        s.TryPress("A"); s.TryPress("B"); s.TryPress("C"); s.TryPress("D");
        Assert.IsFalse(s.IsSolved);
        Assert.IsTrue(s.IsDeadEnd());
    }

    [Test]
    public void FullTraversal_Solves()
    {
        var s = new StarStrokeState(Square());
        // 한붓 경로: B-A-C-B... 홀수차수 노드는 B,D(각 3? 계산) — 실제 가능한 경로 하나:
        // 차수: A=3(B,D,C), B=2(A,C), C=3(B,D,A), D=2(C,A) → 홀수 A,C 2개 → A 또는 C에서 시작
        s.TryPress("A"); // start
        s.TryPress("B"); // A-B
        s.TryPress("C"); // B-C
        s.TryPress("A"); // C-A
        s.TryPress("D"); // A-D
        s.TryPress("C"); // D-C
        Assert.IsTrue(s.IsSolved);
        Assert.AreEqual(5, s.DrawnCount);
    }

    [Test]
    public void Reset_ClearsDrawnAndPen()
    {
        var s = new StarStrokeState(Square());
        s.TryPress("A"); s.TryPress("B");
        s.Reset();
        Assert.AreEqual(0, s.DrawnCount);
        Assert.IsNull(s.CurrentNodeId);
        Assert.IsFalse(s.IsSolved);
    }

    [Test]
    public void OddDegree_SolvableFigures()
    {
        Assert.IsTrue(StarStrokeState.IsOneStrokePossible(Square())); // 홀수 2개
        var triangle = new List<StarEdge>
        {
            new StarEdge("A","B"), new StarEdge("B","C"), new StarEdge("C","A"),
        };
        Assert.AreEqual(0, StarStrokeState.CountOddDegreeNodes(triangle)); // 전부 차수2
        Assert.IsTrue(StarStrokeState.IsOneStrokePossible(triangle));
    }

    [Test]
    public void OddDegree_UnsolvableFigure()
    {
        // 완전그래프 K4: 모든 정점 차수3 → 홀수 4개 → 불가
        var k4 = new List<StarEdge>
        {
            new StarEdge("A","B"), new StarEdge("A","C"), new StarEdge("A","D"),
            new StarEdge("B","C"), new StarEdge("B","D"), new StarEdge("C","D"),
        };
        Assert.AreEqual(4, StarStrokeState.CountOddDegreeNodes(k4));
        Assert.IsFalse(StarStrokeState.IsOneStrokePossible(k4));
    }
}
```

- [ ] **Step 2: 테스트가 실패하는지 확인** *(사람이 Test Runner에서 실행 — 게이트 아님)*

`StarStrokeState` 미존재로 컴파일 에러/전체 실패 예상. 에이전트는 대기하지 말고 Step 3 진행.

- [ ] **Step 3: `StarStrokeState` 구현**

Create `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/SpaceLayer/StarStrokeState.cs`:

```csharp
using System.Collections.Generic;

namespace Gameplay.Terrain.Tiles.SpecialChunks.SpaceLayer
{
    /// <summary>버튼 입력 1회의 판정 결과 종류.</summary>
    public enum MoveKind { Started, Drew, CancelledStart, Ignored }

    /// <summary>TryPress 결과. Edge는 Kind==Drew일 때만 유효.</summary>
    public struct MoveResult
    {
        public MoveKind Kind;
        public StarEdge Edge;

        public static MoveResult Of(MoveKind kind) => new MoveResult { Kind = kind };
        public static MoveResult Drawn(StarEdge e) => new MoveResult { Kind = MoveKind.Drew, Edge = e };
    }

    /// <summary>
    /// 별자리 한붓그리기 퍼즐의 순수 상태 기계. UnityEngine에 의존하지 않아 EditMode 단위 테스트가 가능하다.
    /// 펜(현재 위치)에서 이어진 미사용 가이드 선만 그을 수 있으며, 모든 선을 그으면 IsSolved.
    /// </summary>
    public class StarStrokeState
    {
        private readonly HashSet<StarEdge> _targets = new HashSet<StarEdge>();
        private readonly Dictionary<string, List<StarEdge>> _adjacency = new Dictionary<string, List<StarEdge>>();
        private readonly HashSet<StarEdge> _drawn = new HashSet<StarEdge>();

        public string CurrentNodeId { get; private set; }
        public int TargetCount => _targets.Count;
        public int DrawnCount => _drawn.Count;
        public bool IsSolved => TargetCount > 0 && DrawnCount == TargetCount;
        public IReadOnlyCollection<StarEdge> TargetEdges => _targets;

        public StarStrokeState(IEnumerable<StarEdge> targetEdges)
        {
            if (targetEdges == null) return;
            foreach (var e in targetEdges)
            {
                if (string.IsNullOrEmpty(e.NodeAId) || string.IsNullOrEmpty(e.NodeBId)) continue;
                if (e.NodeAId == e.NodeBId) continue; // self-loop 제외
                if (!_targets.Add(e)) continue;        // 중복 간선 제거
                AddAdjacency(e.NodeAId, e);
                AddAdjacency(e.NodeBId, e);
            }
        }

        private void AddAdjacency(string nodeId, StarEdge e)
        {
            if (!_adjacency.TryGetValue(nodeId, out var list))
            {
                list = new List<StarEdge>();
                _adjacency[nodeId] = list;
            }
            list.Add(e);
        }

        public bool IsEdgeDrawn(StarEdge e) => _drawn.Contains(e);

        /// <summary>버튼(별) 1회 입력 처리. 규칙 위반 입력은 상태를 바꾸지 않고 Ignored 반환.</summary>
        public MoveResult TryPress(string nodeId)
        {
            // 도형에 등장하지 않는 별은 무시
            if (string.IsNullOrEmpty(nodeId) || !_adjacency.ContainsKey(nodeId))
                return MoveResult.Of(MoveKind.Ignored);

            // 펜 없음 → 시작점 배치
            if (CurrentNodeId == null)
            {
                CurrentNodeId = nodeId;
                return MoveResult.Of(MoveKind.Started);
            }

            // 현재 펜 별 재선택
            if (nodeId == CurrentNodeId)
            {
                if (DrawnCount == 0)
                {
                    CurrentNodeId = null;
                    return MoveResult.Of(MoveKind.CancelledStart);
                }
                return MoveResult.Of(MoveKind.Ignored);
            }

            // 다른 별 → 유효한 선인지 판정
            var edge = new StarEdge(CurrentNodeId, nodeId);
            if (!_targets.Contains(edge) || _drawn.Contains(edge))
                return MoveResult.Of(MoveKind.Ignored);

            _drawn.Add(edge);
            CurrentNodeId = nodeId;
            return MoveResult.Drawn(edge);
        }

        /// <summary>현재 펜 별에서 그을 수 있는 미사용 선이 없고, 아직 미완성이면 true.</summary>
        public bool IsDeadEnd()
        {
            if (CurrentNodeId == null || IsSolved) return false;
            if (!_adjacency.TryGetValue(CurrentNodeId, out var list)) return true;
            foreach (var e in list)
                if (!_drawn.Contains(e)) return false;
            return true;
        }

        public void Reset()
        {
            _drawn.Clear();
            CurrentNodeId = null;
        }

        /// <summary>도형의 홀수 차수 꼭짓점 수. 0 또는 2여야 한붓그리기 가능.</summary>
        public static int CountOddDegreeNodes(IEnumerable<StarEdge> edges)
        {
            var degree = new Dictionary<string, int>();
            var seen = new HashSet<StarEdge>();
            if (edges != null)
            {
                foreach (var e in edges)
                {
                    if (string.IsNullOrEmpty(e.NodeAId) || string.IsNullOrEmpty(e.NodeBId)) continue;
                    if (e.NodeAId == e.NodeBId) continue;
                    if (!seen.Add(e)) continue;
                    degree[e.NodeAId] = degree.TryGetValue(e.NodeAId, out var a) ? a + 1 : 1;
                    degree[e.NodeBId] = degree.TryGetValue(e.NodeBId, out var b) ? b + 1 : 1;
                }
            }
            int odd = 0;
            foreach (var kv in degree)
                if ((kv.Value & 1) == 1) odd++;
            return odd;
        }

        /// <summary>홀수 차수 꼭짓점이 0 또는 2개면 한붓그리기 가능(연결성은 별도 가정).</summary>
        public static bool IsOneStrokePossible(IEnumerable<StarEdge> edges)
        {
            int odd = CountOddDegreeNodes(edges);
            return odd == 0 || odd == 2;
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인** *(사람이 Test Runner에서 실행 — 게이트 아님)*

`StarStrokeStateTests`의 12개 테스트 전부 PASS 기대. 에이전트는 코드가 위 명세대로 작성됐음을 확인하고 진행.

- [ ] **Step 5: 체크포인트** — `StarStrokeState.cs`, `StarStrokeStateTests.cs` 저장 완료. (커밋은 사람이 UVCS로)

---

### Task 2: `StarConnectionLine` 가이드/점등 상태 전환

가이드 선을 흐리게 미리 표시하고, 그었을 때 밝게 점등하는 상태 전환을 추가한다.

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/SpaceLayer/StarConnectionLine.cs`

**Interfaces:**
- Consumes: `StarEdge`, `LineRenderer`(RequireComponent).
- Produces (Task 4가 의존):
  - `void Init(StarEdge edge, Vector3 posA, Vector3 posB)` — 위치 설정 후 dim 상태로 초기화(기존 시그니처 유지).
  - `void SetDrawn(bool drawn)` — dim(가이드) ↔ lit(점등) 색·굵기 전환.
  - `StarEdge Edge { get; }` (기존 유지).

- [ ] **Step 1: `StarConnectionLine.cs` 수정**

기존 파일 전체를 아래로 교체:

```csharp
using UnityEngine;

namespace Gameplay.Terrain.Tiles.SpecialChunks.SpaceLayer
{
    /// <summary>
    /// 두 개의 StarNodeTile을 잇는 시각적인 선(LineRenderer)을 관리합니다.
    /// 그리기 전에는 흐린 가이드(dim), 그은 뒤에는 밝게 점등(lit)됩니다.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class StarConnectionLine : MonoBehaviour
    {
        [Header("Draw State")]
        [Tooltip("아직 안 그은 가이드 상태 색상")]
        public Color dimColor = new Color(1f, 1f, 1f, 0.25f);
        [Tooltip("그은 뒤 점등 색상")]
        public Color litColor = new Color(1f, 0.92f, 0.4f, 1f);
        [Tooltip("가이드 상태 선 굵기")]
        public float dimWidth = 0.05f;
        [Tooltip("점등 상태 선 굵기")]
        public float litWidth = 0.12f;

        private LineRenderer _lineRenderer;

        /// <summary>이 시각 선이 나타내는 실제 데이터 연결 정보</summary>
        public StarEdge Edge { get; private set; }

        private void Awake()
        {
            _lineRenderer = GetComponent<LineRenderer>();
            _lineRenderer.positionCount = 2;
        }

        /// <summary>초기화 시 두 노드의 위치를 연결하고 흐린 가이드 상태로 시작합니다.</summary>
        public void Init(StarEdge edge, Vector3 posA, Vector3 posB)
        {
            Edge = edge;
            if (_lineRenderer == null) _lineRenderer = GetComponent<LineRenderer>();
            _lineRenderer.positionCount = 2;
            _lineRenderer.SetPosition(0, posA);
            _lineRenderer.SetPosition(1, posB);
            SetDrawn(false);
        }

        /// <summary>그림(lit)/가이드(dim) 상태를 전환합니다.</summary>
        public void SetDrawn(bool drawn)
        {
            if (_lineRenderer == null) _lineRenderer = GetComponent<LineRenderer>();
            Color c = drawn ? litColor : dimColor;
            float w = drawn ? litWidth : dimWidth;
            _lineRenderer.startColor = c;
            _lineRenderer.endColor = c;
            _lineRenderer.startWidth = w;
            _lineRenderer.endWidth = w;
        }
    }
}
```

- [ ] **Step 2: 수동 확인 (에디터)** *(사람 검증 — 게이트 아님)*

LineRenderer의 Material이 정점 컬러(Sprites/Default 등)를 반영해야 색 전환이 보인다. 프리팹의 LineRenderer Material을 확인. 에이전트는 코드가 명세대로임을 확인하고 진행.

- [ ] **Step 3: 체크포인트** — `StarConnectionLine.cs` 저장 완료.

---

### Task 3: `StarNodeTile` 펜 위치 시각 상태

노드 색상 상태를 pen > connected > idle 우선순위로 바꾸고, 기존 selected 개념을 pen으로 대체한다.

**Files:**
- Modify: `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/SpaceLayer/StarNodeTile.cs`

**Interfaces:**
- Produces (Task 4가 의존):
  - `void SetIsPen(bool isPen)` — 현재 펜 위치 표시.
  - `void SetConnected(bool connected)` (기존 유지) — 그은 선에 포함된 별.
  - `string nodeId` (기존 public 필드 유지).
- 제거: `void SetSelected(bool)`, `Color selectedColor` (Task 4에서 더 이상 호출 안 함 — 사전 grep으로 StarPuzzleManager 외 참조 없음 확인됨).

- [ ] **Step 1: `StarNodeTile.cs` 수정**

기존 파일 전체를 아래로 교체:

```csharp
using UnityEngine;

namespace Gameplay.Terrain.Tiles.SpecialChunks.SpaceLayer
{
    /// <summary>
    /// 별자리 한붓그리기 퍼즐의 시각적인 노드(별) 블록입니다.
    /// 플레이어와 직접 상호작용하지 않고, 매니저에 의해 시각적 상태만 변경됩니다.
    /// </summary>
    public class StarNodeTile : MonoBehaviour
    {
        [Header("Star Node Settings")]
        [Tooltip("이 별을 식별하기 위한 고유 ID")]
        public string nodeId;

        [Header("Visual Feedback")]
        public SpriteRenderer spriteRenderer;

        [Tooltip("기본 상태 색상")]
        public Color idleColor = Color.white;
        [Tooltip("현재 펜이 놓인(다음 선을 그을 기준) 별 색상")]
        public Color penColor = Color.cyan;
        [Tooltip("이미 그은 선에 포함된 별 색상")]
        public Color connectedColor = Color.green;

        private bool _isPen = false;
        private bool _isConnected = false;

        private void Start()
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                nodeId = gameObject.name;
            }
            UpdateVisuals();
        }

        /// <summary>매니저가 이 노드를 현재 펜 위치로 지정/해제할 때 호출합니다.</summary>
        public void SetIsPen(bool isPen)
        {
            _isPen = isPen;
            UpdateVisuals();
        }

        /// <summary>매니저가 이 노드가 그은 선에 포함/제외될 때 호출합니다.</summary>
        public void SetConnected(bool connected)
        {
            _isConnected = connected;
            UpdateVisuals();
        }

        /// <summary>상태 우선순위 pen > connected > idle 로 색상을 갱신합니다.</summary>
        private void UpdateVisuals()
        {
            if (spriteRenderer == null) return;

            if (_isPen)
                spriteRenderer.color = penColor;      // 펜 위치가 최우선
            else if (_isConnected)
                spriteRenderer.color = connectedColor;
            else
                spriteRenderer.color = idleColor;
        }
    }
}
```

- [ ] **Step 2: 수동 확인 (에디터)** *(사람 검증 — 게이트 아님)*

기존 씬/프리팹의 StarNodeTile 인스펙터에서 `selectedColor`가 사라지고 `penColor`가 추가됨. 필요 시 penColor 값 재지정. 에이전트는 코드 확인 후 진행.

- [ ] **Step 3: 체크포인트** — `StarNodeTile.cs` 저장 완료.

---

### Task 4: `StarPuzzleManager` 한붓그리기 방식으로 재작성

버튼 입력을 `StarStrokeState`에 위임하고, 가이드 선 생성·펜/연결 시각 갱신·막다른 길 자동 리셋·완성 판정을 연결한다.

**Files:**
- Modify (전체 재작성): `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/SpaceLayer/StarPuzzleManager.cs`

**Interfaces:**
- Consumes:
  - `StarStrokeState`, `MoveKind`, `MoveResult` (Task 1)
  - `StarConnectionLine.Init(...)`, `.SetDrawn(bool)`, `.Edge` (Task 2)
  - `StarNodeTile.SetIsPen(bool)`, `.SetConnected(bool)`, `.nodeId` (Task 3)
  - `StarButtonTile.OnButtonInteracted` 이벤트, `StarEdge`
- Produces: `public void ResetAllConnections()` (수동 리셋 레버가 호출하는 기존 공개 API 유지), `public UnityEvent onPuzzleSolved` (유지), `public List<StarEdge> targetConnections` (인스펙터 할당 유지).

- [ ] **Step 1: `StarPuzzleManager.cs` 전체 교체**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Gameplay.Terrain.Tiles.SpecialChunks.SpaceLayer
{
    /// <summary>
    /// 별자리 한붓그리기(Euler trail) 퍼즐 매니저.
    /// 정해진 도형(targetConnections)을 펜을 떼지 않고 각 선을 한 번씩만 그으면 클리어됩니다.
    /// 규칙 판정은 순수 상태 기계 StarStrokeState에 위임하고, 여기서는 시각 표현만 담당합니다.
    /// </summary>
    public class StarPuzzleManager : MonoBehaviour
    {
        [Header("Puzzle Settings")]
        [Tooltip("그려야 할 별자리 도형 = 정답 = 가이드 선 (인스펙터에서 할당)")]
        public List<StarEdge> targetConnections = new List<StarEdge>();

        [Header("Prefabs")]
        [Tooltip("노드를 연결할 때 표시할 시각적인 선 프리팹")]
        public StarConnectionLine connectionLinePrefab;

        [Header("Feedback (선택)")]
        [Tooltip("무효한 입력/막다른 길 실패 시 재생할 사운드 (없으면 로그만)")]
        public AudioSource sfxSource;
        public AudioClip invalidClip;
        public AudioClip failResetClip;

        [Header("Events")]
        public UnityEvent onPuzzleSolved;

        private readonly List<StarNodeTile> _allNodes = new List<StarNodeTile>();
        private readonly List<StarButtonTile> _allButtons = new List<StarButtonTile>();
        private readonly Dictionary<string, StarNodeTile> _nodeMap = new Dictionary<string, StarNodeTile>();
        private readonly Dictionary<StarEdge, StarConnectionLine> _lineMap = new Dictionary<StarEdge, StarConnectionLine>();

        private StarStrokeState _state;
        private bool _isSolved = false;

        private void Start()
        {
            // 하위 노드 등록
            _allNodes.Clear();
            _allNodes.AddRange(GetComponentsInChildren<StarNodeTile>());
            foreach (var node in _allNodes)
            {
                if (string.IsNullOrEmpty(node.nodeId))
                    node.nodeId = node.gameObject.name;
                _nodeMap[node.nodeId] = node;
            }

            // 하위 버튼 등록·구독
            _allButtons.Clear();
            _allButtons.AddRange(GetComponentsInChildren<StarButtonTile>());
            foreach (var button in _allButtons)
                button.OnButtonInteracted += HandleButtonInteracted;

            // 상태 기계 구성
            _state = new StarStrokeState(targetConnections);

            // 한붓그리기 가능성 검증(디자이너 실수 방지)
            if (!StarStrokeState.IsOneStrokePossible(targetConnections))
            {
                int odd = StarStrokeState.CountOddDegreeNodes(targetConnections);
                Debug.LogWarning($"[StarPuzzleManager] 이 별자리 도형은 한붓그리기 불가! 홀수 차수 꼭짓점={odd}개 (0 또는 2여야 함).");
            }

            BuildGuideLines();
        }

        private void OnDestroy()
        {
            foreach (var button in _allButtons)
                if (button != null)
                    button.OnButtonInteracted -= HandleButtonInteracted;
        }

        /// <summary>targetConnections 전부에 대해 흐린 가이드 선을 생성한다.</summary>
        private void BuildGuideLines()
        {
            if (connectionLinePrefab == null)
            {
                Debug.LogWarning("[StarPuzzleManager] connectionLinePrefab이 할당되지 않았습니다! 가이드 선을 표시할 수 없습니다.");
                return;
            }

            foreach (var edge in _state.TargetEdges)
            {
                if (_lineMap.ContainsKey(edge)) continue;

                Vector3 posA = _nodeMap.TryGetValue(edge.NodeAId, out var a) ? a.transform.position : Vector3.zero;
                Vector3 posB = _nodeMap.TryGetValue(edge.NodeBId, out var b) ? b.transform.position : Vector3.zero;

                StarConnectionLine line = Instantiate(connectionLinePrefab, transform);
                line.Init(edge, posA, posB);
                _lineMap[edge] = line;
            }
        }

        /// <summary>플레이어가 지상의 버튼을 눌렀을 때 호출됩니다.</summary>
        private void HandleButtonInteracted(string targetNodeId, GameObject interactor)
        {
            if (_isSolved) return;

            if (!_nodeMap.ContainsKey(targetNodeId))
            {
                Debug.LogWarning($"[StarPuzzleManager] targetNodeId '{targetNodeId}'에 해당하는 별 노드가 없습니다.");
                return;
            }

            string prevPen = _state.CurrentNodeId;
            MoveResult result = _state.TryPress(targetNodeId);

            switch (result.Kind)
            {
                case MoveKind.Started:
                    SetPen(targetNodeId, true);
                    break;

                case MoveKind.CancelledStart:
                    SetPen(prevPen, false);
                    break;

                case MoveKind.Drew:
                    // 펜 이동
                    SetPen(prevPen, false);
                    SetPen(targetNodeId, true);
                    // 양끝 연결 표시
                    SetConnected(result.Edge.NodeAId, true);
                    SetConnected(result.Edge.NodeBId, true);
                    // 가이드 선 점등
                    if (_lineMap.TryGetValue(result.Edge, out var line))
                        line.SetDrawn(true);

                    if (_state.IsSolved)
                    {
                        _isSolved = true;
                        Debug.Log("[StarPuzzleManager] 한붓그리기 성공!");
                        onPuzzleSolved?.Invoke();
                    }
                    else if (_state.IsDeadEnd())
                    {
                        Debug.Log("[StarPuzzleManager] 막다른 길 — 전체 리셋.");
                        PlaySfx(failResetClip);
                        ResetAllConnections();
                    }
                    break;

                case MoveKind.Ignored:
                    PlaySfx(invalidClip);
                    break;
            }
        }

        private void SetPen(string nodeId, bool isPen)
        {
            if (nodeId != null && _nodeMap.TryGetValue(nodeId, out var node))
                node.SetIsPen(isPen);
        }

        private void SetConnected(string nodeId, bool connected)
        {
            if (nodeId != null && _nodeMap.TryGetValue(nodeId, out var node))
                node.SetConnected(connected);
        }

        private void PlaySfx(AudioClip clip)
        {
            if (sfxSource != null && clip != null)
                sfxSource.PlayOneShot(clip);
        }

        /// <summary>외부(리셋 레버) 또는 막다른 길에서 호출하는 전체 초기화.</summary>
        public void ResetAllConnections()
        {
            if (_isSolved) return;
            if (_state == null) return;

            _state.Reset();

            foreach (var kv in _lineMap)
                if (kv.Value != null) kv.Value.SetDrawn(false);

            foreach (var node in _allNodes)
            {
                node.SetConnected(false);
                node.SetIsPen(false);
            }
        }
    }
}
```

- [ ] **Step 2: 컴파일·수동 검증 (에디터)** *(사람 검증 — 게이트 아님)*

에디터에서 컴파일 에러가 없는지, 씬의 StarPuzzleManager 인스펙터에 `targetConnections`/`connectionLinePrefab`이 유지되는지 확인. 플레이 시:
1. 시작 시 도형이 흐린 가이드 선으로 보임
2. 버튼으로 시작 별 선택 → 펜 색
3. 이어진 별 선택 → 선 점등·펜 이동
4. 안 이어진 별/이미 그은 선 → 무시(경고음)
5. 잘못된 순서로 막히면 자동 전체 리셋
6. 전부 그으면 `onPuzzleSolved` 발동

- [ ] **Step 3: 체크포인트** — `StarPuzzleManager.cs` 저장 완료.

---

## Self-Review

**1. Spec coverage** (design.md 대비):
- §1 규칙 (펜/유효수/무효무시/막다른길 자동리셋/완성) → Task 1 상태기계 + Task 4 배선 ✓
- §2 인터랙션 흐름 (Started/CancelledStart/Drew/Ignored 분기) → Task 1 `TryPress` + Task 4 `switch` ✓
- §3 파일별 변경 (Manager/Line/Node) → Task 4/2/3 ✓, StarEdge·StarButtonTile 무변경 ✓
- §4 안전장치 (홀수 차수 검증 경고, 수동 리셋 유지) → Task 1 `IsOneStrokePossible` + Task 4 Start 경고·`ResetAllConnections` 공개 유지 ✓
- §5 유지 항목 (버튼 매핑, 이벤트 구독/해제, `_isSolved` 잠금, targetConnections 인스펙터, onPuzzleSolved) → Task 4에 전부 보존 ✓
- §6 미결 (실패 연출/사운드, 순서 저장 불필요) → Task 4에 선택적 AudioSource 훅으로 최소 구현, 순서 미저장 ✓

**2. Placeholder scan:** 모든 코드 스텝에 완성 코드 포함. "TBD"/"적절히 처리" 없음 ✓

**3. Type consistency:**
- `MoveResult.Kind`/`.Edge`, `MoveKind.{Started,Drew,CancelledStart,Ignored}` — Task 1 정의 = Task 4 사용 일치 ✓
- `StarStrokeState.{TryPress,IsSolved,IsDeadEnd,Reset,TargetEdges,CurrentNodeId,IsOneStrokePossible,CountOddDegreeNodes}` — Task 1 정의 = Task 4 사용 일치 ✓
- `StarConnectionLine.{Init,SetDrawn,Edge}` — Task 2 정의 = Task 4 사용 일치 ✓
- `StarNodeTile.{SetIsPen,SetConnected,nodeId}` — Task 3 정의 = Task 4 사용 일치, `SetSelected` 호출 제거됨 ✓

---

# 이터레이션 2 — "걸어서 잇기" + 플레이어 추적 고무줄 선

**Goal:** 지상 버튼(입력)과 하늘 별(시각)을 하나의 물리 정점으로 통합하고, 진행 중인 선이 플레이어를 실시간 추적하게 한다. 로직 코어 `StarStrokeState`는 변경 없음.

**설계 근거:** design.md §7.

### Task 5: `StarButtonTile`을 정점(별)으로 통합

`StarButtonTile`(이미 `InteractableBlockBase` 상속)에 `StarNodeTile`의 시각 상태를 흡수하고, `targetNodeId`를 자기 id `nodeId`로 의미 변경한다.

**Files:**
- Modify (전체 교체): `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/SpaceLayer/StarButtonTile.cs`

**Interfaces:**
- Consumes: `InteractableBlockBase`(base — `spriteRenderer`, `idleColor`, `nearbyColor`, `isPlayerNearby`, `HandleInteraction`, `UpdateVisuals` virtual)
- Produces (Task 6 의존): `public string nodeId`, `public void SetIsPen(bool)`, `public void SetConnected(bool)`, `event ButtonInteractedAction OnButtonInteracted(string nodeId, GameObject)`

- [ ] **Step 1: `StarButtonTile.cs` 전체 교체**

```csharp
using UnityEngine;

namespace Gameplay.Terrain.Tiles.SpecialChunks.SpaceLayer
{
    /// <summary>
    /// 별자리 한붓그리기 퍼즐의 정점(별) 겸 상호작용 버튼.
    /// 플레이어가 근처에서 E로 상호작용하면 자신의 nodeId를 매니저에 발신하고,
    /// 매니저가 펜/연결 시각 상태를 갱신한다.
    /// </summary>
    public class StarButtonTile : InteractableBlockBase
    {
        [Header("Star Vertex Settings")]
        [Tooltip("이 정점(별)의 고유 ID. 비어 있으면 매니저가 오브젝트 이름으로 채운다.")]
        public string nodeId;

        [Header("Vertex State Colors")]
        [Tooltip("현재 펜이 놓인(다음 선을 그을 기준) 색상")]
        public Color penColor = Color.cyan;
        [Tooltip("이미 그은 선에 포함된 색상")]
        public Color connectedColor = Color.green;

        // Manager가 구독할 이벤트: 이 정점의 nodeId를 전달
        public delegate void ButtonInteractedAction(string nodeId, GameObject interactor);
        public event ButtonInteractedAction OnButtonInteracted;

        private bool _isPen = false;
        private bool _isConnected = false;

        protected override void HandleInteraction(GameObject interactor)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                Debug.LogWarning($"[StarButtonTile] '{gameObject.name}'의 nodeId가 비어있습니다.");
                return;
            }
            OnButtonInteracted?.Invoke(nodeId, interactor);
        }

        /// <summary>매니저가 이 정점을 현재 펜 위치로 지정/해제할 때 호출.</summary>
        public void SetIsPen(bool isPen)
        {
            _isPen = isPen;
            UpdateVisuals();
        }

        /// <summary>매니저가 이 정점이 그은 선에 포함/제외될 때 호출.</summary>
        public void SetConnected(bool connected)
        {
            _isConnected = connected;
            UpdateVisuals();
        }

        /// <summary>우선순위 pen > connected > nearby(근접) > idle 로 색상 갱신.</summary>
        protected override void UpdateVisuals()
        {
            if (spriteRenderer == null) return;

            if (_isPen)
                spriteRenderer.color = penColor;
            else if (_isConnected)
                spriteRenderer.color = connectedColor;
            else
                spriteRenderer.color = isPlayerNearby ? nearbyColor : idleColor;
        }
    }
}
```

- [ ] **Step 2: 수동 확인 (에디터)** — 컴파일 확인. 기존 씬의 StarButtonTile 인스펙터에서 `targetNodeId`가 사라지고 `nodeId`가 생김(직렬화 키 변경으로 값 유실 → 재입력 필요). `penColor`/`connectedColor` 노출 확인. 정점 오브젝트에 `SpriteRenderer` 연결 필요.

- [ ] **Step 3: 체크포인트** — `StarButtonTile.cs` 저장 완료.

---

### Task 6: `StarPuzzleManager` — 통합 정점 + 고무줄 프리뷰 선

`_nodeMap`을 정점(StarButtonTile) 기준으로 바꾸고, 플레이어를 추적하는 프리뷰 LineRenderer를 추가한다.

**Files:**
- Modify (전체 교체): `Assets/Scripts/Gameplay/Terrain/Tiles/SpecialChunks/SpaceLayer/StarPuzzleManager.cs`

**Interfaces:**
- Consumes: `StarButtonTile.{nodeId, SetIsPen, SetConnected, OnButtonInteracted}`(Task 5), `StarStrokeState`(Task 1, 불변), `StarConnectionLine.{Init,SetDrawn}`(Task 2, 불변), `LineRenderer`(Unity)
- Produces: 공개 표면 `targetConnections`/`onPuzzleSolved`/`ResetAllConnections()` 유지

- [ ] **Step 1: `StarPuzzleManager.cs` 전체 교체**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Gameplay.Terrain.Tiles.SpecialChunks.SpaceLayer
{
    /// <summary>
    /// 별자리 한붓그리기(Euler trail) 퍼즐 매니저 — "걸어서 잇기" 버전.
    /// 플레이어가 정점(StarButtonTile)에 걸어가 E로 잇고, 진행 중인 선은 플레이어를 추적한다.
    /// 규칙 판정은 순수 상태 기계 StarStrokeState에 위임하고, 여기서는 시각/입력만 담당한다.
    /// </summary>
    public class StarPuzzleManager : MonoBehaviour
    {
        [Header("Puzzle Settings")]
        [Tooltip("그려야 할 별자리 도형 = 정답 = 가이드 선 (인스펙터에서 할당)")]
        public List<StarEdge> targetConnections = new List<StarEdge>();

        [Header("Prefabs")]
        [Tooltip("정점을 연결할 때 표시할 시각적인 선 프리팹")]
        public StarConnectionLine connectionLinePrefab;

        [Header("Live Stroke")]
        [Tooltip("펜에서 플레이어까지 실시간으로 따라오는 고무줄 선 (씬의 LineRenderer 할당)")]
        public LineRenderer previewLine;

        [Header("Feedback (선택)")]
        [Tooltip("무효한 입력/막다른 길 실패 시 재생할 사운드 (없으면 로그만)")]
        public AudioSource sfxSource;
        public AudioClip invalidClip;
        public AudioClip failResetClip;

        [Header("Events")]
        public UnityEvent onPuzzleSolved;

        private readonly List<StarButtonTile> _vertices = new List<StarButtonTile>();
        private readonly Dictionary<string, StarButtonTile> _nodeMap = new Dictionary<string, StarButtonTile>();
        private readonly Dictionary<StarEdge, StarConnectionLine> _lineMap = new Dictionary<StarEdge, StarConnectionLine>();

        private StarStrokeState _state;
        private Transform _player;
        private bool _isSolved = false;

        private void Start()
        {
            // 하위 정점(버튼) 등록·구독
            _vertices.Clear();
            _vertices.AddRange(GetComponentsInChildren<StarButtonTile>());
            foreach (var v in _vertices)
            {
                if (string.IsNullOrEmpty(v.nodeId))
                    v.nodeId = v.gameObject.name;
                _nodeMap[v.nodeId] = v;
                v.OnButtonInteracted += HandleButtonInteracted;
            }

            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) _player = playerObj.transform;

            _state = new StarStrokeState(targetConnections);

            // 한붓그리기 가능성 검증(디자이너 실수 방지)
            int odd = StarStrokeState.CountOddDegreeNodes(targetConnections);
            if (odd != 0 && odd != 2)
                Debug.LogWarning($"[StarPuzzleManager] 이 별자리 도형은 한붓그리기 불가! 홀수 차수 꼭짓점={odd}개 (0 또는 2여야 함).");

            BuildGuideLines();

            if (previewLine != null)
            {
                previewLine.positionCount = 2;
                previewLine.enabled = false;
            }
        }

        private void OnDestroy()
        {
            foreach (var v in _vertices)
                if (v != null)
                    v.OnButtonInteracted -= HandleButtonInteracted;
        }

        private void Update()
        {
            if (previewLine == null) return;

            // 펜 활성 + 미완성 + 플레이어 존재일 때만 고무줄 표시
            if (_isSolved || _state == null || _player == null || _state.CurrentNodeId == null
                || !_nodeMap.TryGetValue(_state.CurrentNodeId, out var penVertex))
            {
                if (previewLine.enabled) previewLine.enabled = false;
                return;
            }

            previewLine.enabled = true;
            previewLine.positionCount = 2;
            previewLine.SetPosition(0, penVertex.transform.position);
            previewLine.SetPosition(1, _player.position);
        }

        /// <summary>targetConnections 전부에 대해 흐린 가이드 선을 생성한다.</summary>
        private void BuildGuideLines()
        {
            if (connectionLinePrefab == null)
            {
                Debug.LogWarning("[StarPuzzleManager] connectionLinePrefab이 할당되지 않았습니다! 가이드 선을 표시할 수 없습니다.");
                return;
            }

            foreach (var edge in _state.TargetEdges)
            {
                if (_lineMap.ContainsKey(edge)) continue;

                bool hasA = _nodeMap.TryGetValue(edge.NodeAId, out var a);
                bool hasB = _nodeMap.TryGetValue(edge.NodeBId, out var b);
                if (!hasA || !hasB)
                    Debug.LogWarning($"[StarPuzzleManager] 가이드 선의 정점을 찾을 수 없습니다: '{edge.NodeAId}'-'{edge.NodeBId}'. nodeId 오타를 확인하세요.");

                Vector3 posA = hasA ? a.transform.position : Vector3.zero;
                Vector3 posB = hasB ? b.transform.position : Vector3.zero;

                StarConnectionLine line = Instantiate(connectionLinePrefab, transform);
                line.Init(edge, posA, posB);
                _lineMap[edge] = line;
            }
        }

        /// <summary>플레이어가 정점에 E로 상호작용했을 때 호출됩니다.</summary>
        private void HandleButtonInteracted(string targetNodeId, GameObject interactor)
        {
            if (_isSolved) return;

            if (!_nodeMap.ContainsKey(targetNodeId))
            {
                Debug.LogWarning($"[StarPuzzleManager] targetNodeId '{targetNodeId}'에 해당하는 정점이 없습니다.");
                return;
            }

            string prevPen = _state.CurrentNodeId;
            MoveResult result = _state.TryPress(targetNodeId);

            switch (result.Kind)
            {
                case MoveKind.Started:
                    SetPen(targetNodeId, true);
                    break;

                case MoveKind.CancelledStart:
                    SetPen(prevPen, false);
                    break;

                case MoveKind.Drew:
                    SetPen(prevPen, false);
                    SetPen(targetNodeId, true);
                    SetConnected(result.Edge.NodeAId, true);
                    SetConnected(result.Edge.NodeBId, true);
                    if (_lineMap.TryGetValue(result.Edge, out var line))
                        line.SetDrawn(true);

                    if (_state.IsSolved)
                    {
                        _isSolved = true;
                        Debug.Log("[StarPuzzleManager] 한붓그리기 성공!");
                        onPuzzleSolved?.Invoke();
                    }
                    else if (_state.IsDeadEnd())
                    {
                        Debug.Log("[StarPuzzleManager] 막다른 길 — 전체 리셋.");
                        PlaySfx(failResetClip);
                        ResetAllConnections();
                    }
                    break;

                case MoveKind.Ignored:
                    PlaySfx(invalidClip);
                    break;
            }
        }

        private void SetPen(string nodeId, bool isPen)
        {
            if (nodeId != null && _nodeMap.TryGetValue(nodeId, out var v))
                v.SetIsPen(isPen);
        }

        private void SetConnected(string nodeId, bool connected)
        {
            if (nodeId != null && _nodeMap.TryGetValue(nodeId, out var v))
                v.SetConnected(connected);
        }

        private void PlaySfx(AudioClip clip)
        {
            if (sfxSource != null && clip != null)
                sfxSource.PlayOneShot(clip);
        }

        /// <summary>외부(리셋 레버) 또는 막다른 길에서 호출하는 전체 초기화.</summary>
        public void ResetAllConnections()
        {
            if (_isSolved) return;
            if (_state == null) return;

            _state.Reset();

            foreach (var kv in _lineMap)
                if (kv.Value != null) kv.Value.SetDrawn(false);

            foreach (var v in _vertices)
            {
                v.SetConnected(false);
                v.SetIsPen(false);
            }

            if (previewLine != null) previewLine.enabled = false;
        }
    }
}
```

- [ ] **Step 2: 컴파일·플레이 검증 (에디터, 사람)** — 정점에 걸어가 E → 고무줄이 플레이어를 따라옴, 다음 정점 E → 선 고정·점등·펜 이동, 막다른 길 자동 리셋, 완성 시 프리뷰 사라지고 `onPuzzleSolved`. `previewLine`의 LineRenderer는 정점 컬러 머티리얼 + Use World Space 필요.

- [ ] **Step 3: 체크포인트** — `StarPuzzleManager.cs` 저장 완료.

### 이터레이션 2 Self-Review
- design.md §7.1 UX → Task 6 Update() 고무줄 + HandleButtonInteracted ✓
- §7.2 정점 통합(nodeId 의미변경, 시각상태 흡수, UpdateVisuals 우선순위) → Task 5 ✓
- §7.3 프리뷰 선(펜→플레이어, 조건부 표시) → Task 6 Update() ✓
- §7.4 매니저 변경(_nodeMap 타입, 통합 루프, 위치소스, Update) → Task 6 ✓
- §7.5 불변(StarStrokeState/Tests/StarConnectionLine/StarEdge, 공개표면) → Task 5·6에서 미변경 ✓
- Type consistency: `StarButtonTile.{nodeId,SetIsPen,SetConnected,OnButtonInteracted}` Task 5 정의 = Task 6 사용 일치 ✓; `previewLine`(LineRenderer) 선언=사용 일치 ✓
