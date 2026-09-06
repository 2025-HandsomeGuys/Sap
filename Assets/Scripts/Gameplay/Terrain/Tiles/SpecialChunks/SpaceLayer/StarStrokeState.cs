using System.Collections.Generic;

namespace Gameplay.Terrain.Tiles.SpecialChunks.SpaceLayer
{
    /// <summary>버튼 입력 1회의 판정 결과 종류.</summary>
    public enum MoveKind { Started, Drew, CancelledStart, Rewound, Ignored }

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
        // 펜이 지나온 정점 순서(트레일). _drawn과 항상 동기: 인접 쌍(_path[i],_path[i+1])이 그은 선.
        // 되돌리기(부분 취소)는 순서 정보가 필요하므로 HashSet과 별도로 유지한다.
        private readonly List<string> _path = new List<string>();

        public string CurrentNodeId => _path.Count > 0 ? _path[_path.Count - 1] : null;
        public int TargetCount => _targets.Count;
        public int DrawnCount => _drawn.Count;
        public bool IsSolved => TargetCount > 0 && DrawnCount == TargetCount;
        public IReadOnlyCollection<StarEdge> TargetEdges => _targets;
        public IReadOnlyCollection<StarEdge> DrawnEdges => _drawn;
        public IReadOnlyList<string> Path => _path;

        public StarStrokeState(IEnumerable<StarEdge> targetEdges)
        {
            if (targetEdges == null) return;
            foreach (var raw in targetEdges)
            {
                if (string.IsNullOrEmpty(raw.NodeAId) || string.IsNullOrEmpty(raw.NodeBId)) continue;
                if (raw.NodeAId == raw.NodeBId) continue; // self-loop 제외
                // Unity 인스펙터 직렬화는 정렬 생성자를 거치지 않으므로 여기서 정규화한다.
                var e = new StarEdge(raw.NodeAId, raw.NodeBId);
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
            if (_path.Count == 0)
            {
                _path.Add(nodeId);
                return MoveResult.Of(MoveKind.Started);
            }

            string current = CurrentNodeId;

            // 현재 펜 별 재선택
            if (nodeId == current)
            {
                if (DrawnCount == 0)
                {
                    _path.Clear();
                    return MoveResult.Of(MoveKind.CancelledStart);
                }
                return MoveResult.Of(MoveKind.Ignored);
            }

            // 다른 별 → 새 선을 그을 수 있으면 긋기(진행 우선)
            var edge = new StarEdge(current, nodeId);
            if (_targets.Contains(edge) && !_drawn.Contains(edge))
            {
                _drawn.Add(edge);
                _path.Add(nodeId);
                return MoveResult.Drawn(edge);
            }

            // 못 그으면 → 지나온 정점이면 거기까지 되돌리기(부분 취소).
            // 여러 번 방문한 정점은 가장 최근 방문 지점까지만(LastIndexOf) 되돌린다.
            int back = _path.LastIndexOf(nodeId);
            if (back >= 0)
            {
                for (int j = back; j < _path.Count - 1; j++)
                    _drawn.Remove(new StarEdge(_path[j], _path[j + 1]));
                _path.RemoveRange(back + 1, _path.Count - (back + 1));
                return MoveResult.Of(MoveKind.Rewound);
            }

            return MoveResult.Of(MoveKind.Ignored);
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
            _path.Clear();
        }

        /// <summary>도형의 홀수 차수 꼭짓점 수. 0 또는 2여야 한붓그리기 가능.</summary>
        public static int CountOddDegreeNodes(IEnumerable<StarEdge> edges)
        {
            var degree = new Dictionary<string, int>();
            var seen = new HashSet<StarEdge>();
            if (edges != null)
            {
                foreach (var raw in edges)
                {
                    if (string.IsNullOrEmpty(raw.NodeAId) || string.IsNullOrEmpty(raw.NodeBId)) continue;
                    if (raw.NodeAId == raw.NodeBId) continue;
                    var e = new StarEdge(raw.NodeAId, raw.NodeBId); // 인스펙터 역순 입력 정규화
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
