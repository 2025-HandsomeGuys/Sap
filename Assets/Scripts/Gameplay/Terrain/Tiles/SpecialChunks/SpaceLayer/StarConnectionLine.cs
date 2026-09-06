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
