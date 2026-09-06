using UnityEngine;

namespace Gameplay.Terrain.Tiles.SpecialChunks.SpaceLayer
{
    /// <summary>
    /// 별자리 한붓그리기 퍼즐의 정점(별). 곡괭이(toolIndex=2)로 타격하면 자신의 nodeId를 매니저에 발신한다.
    /// HP 없음 — 한 번 타격 = 한 번 상호작용(쿨다운으로 중복 방지). 정점은 부서지지 않는다.
    /// 같은 GameObject에 Collider2D가 있어야 Digger가 GetComponent&lt;IDiggable&gt;로 찾는다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class StarButtonTile : MonoBehaviour, IPuzzleDiggable
    {
        [Header("Star Vertex Settings")]
        [Tooltip("이 정점(별)의 고유 ID. 비어 있으면 매니저가 오브젝트 이름으로 채운다.")]
        public string nodeId;

        [Header("Visual Feedback")]
        public SpriteRenderer spriteRenderer;
        [Tooltip("기본 색상")]
        public Color idleColor = Color.white;
        [Tooltip("현재 펜이 놓인(다음 선을 그을 기준) 색상")]
        public Color penColor = Color.cyan;
        [Tooltip("이미 그은 선에 포함된 색상")]
        public Color connectedColor = Color.green;

        [Header("Hit")]
        [Tooltip("연속 타격 중복 방지 쿨다운(초)")]
        public float hitCooldown = 0.2f;

        // Manager가 구독할 이벤트: 이 정점의 nodeId를 전달
        public delegate void ButtonInteractedAction(string nodeId, GameObject interactor);
        public event ButtonInteractedAction OnButtonInteracted;

        private bool _isPen = false;
        private bool _isConnected = false;
        private float _lastHitTime = float.NegativeInfinity;

        private void Awake()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void Start()
        {
            UpdateVisuals();
        }

        // ─── IDiggable: 곡괭이 타격 = 상호작용 ───
        public void Dig(Vector2 worldPos, float damage, int toolIndex)
        {
            if (toolIndex != 2) return; // 곡괭이 전용
            if (Time.time - _lastHitTime < hitCooldown) return;
            _lastHitTime = Time.time;

            if (string.IsNullOrEmpty(nodeId))
            {
                Debug.LogWarning($"[StarButtonTile] '{gameObject.name}'의 nodeId가 비어있습니다.");
                return;
            }
            OnButtonInteracted?.Invoke(nodeId, null);
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

        /// <summary>우선순위 pen > connected > idle 로 색상 갱신.</summary>
        private void UpdateVisuals()
        {
            if (spriteRenderer == null) return;

            if (_isPen)
                spriteRenderer.color = penColor;
            else if (_isConnected)
                spriteRenderer.color = connectedColor;
            else
                spriteRenderer.color = idleColor;
        }
    }
}
