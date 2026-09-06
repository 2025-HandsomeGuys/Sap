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
            // 씬에 놓인 소스는 믹서 그룹이 비어 있다 → 설정의 효과음 슬라이더가 안 먹는다.
            AudioRouting.Route(sfxSource, AudioChannel.SFX);

            // 하위 정점(버튼) 등록·구독
            _vertices.Clear();
            _vertices.AddRange(GetComponentsInChildren<StarButtonTile>());
            foreach (var v in _vertices)
            {
                if (string.IsNullOrEmpty(v.nodeId))
                    v.nodeId = v.gameObject.name;
                if (_nodeMap.ContainsKey(v.nodeId))
                    Debug.LogWarning($"[StarPuzzleManager] 중복 nodeId '{v.nodeId}' — '{v.gameObject.name}'가 기존 정점을 덮어씁니다. 각 정점의 nodeId를 고유하게 지정하세요.");
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

                case MoveKind.Rewound:
                    // 여러 선이 한 번에 취소되고 펜이 옮겨가므로, 상태 기준으로 시각 전체 갱신.
                    RefreshVisualsFromState();
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
                        if (previewLine != null) previewLine.enabled = false;
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

        /// <summary>현재 StarStrokeState를 기준으로 선·펜·연결색 시각을 전부 다시 맞춘다(되돌리기 등 다중 변경용).</summary>
        private void RefreshVisualsFromState()
        {
            // 선: 그은 것만 표시
            foreach (var kv in _lineMap)
                if (kv.Value != null) kv.Value.SetDrawn(_state.IsEdgeDrawn(kv.Key));

            // 연결(밝은) 상태: 남아 있는 그은 선의 끝점만
            var connected = new HashSet<string>();
            foreach (var e in _state.DrawnEdges)
            {
                connected.Add(e.NodeAId);
                connected.Add(e.NodeBId);
            }

            string pen = _state.CurrentNodeId;
            foreach (var v in _vertices)
            {
                v.SetConnected(connected.Contains(v.nodeId));
                v.SetIsPen(v.nodeId == pen);
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
