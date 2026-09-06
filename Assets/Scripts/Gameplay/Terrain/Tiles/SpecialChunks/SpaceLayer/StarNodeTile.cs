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
