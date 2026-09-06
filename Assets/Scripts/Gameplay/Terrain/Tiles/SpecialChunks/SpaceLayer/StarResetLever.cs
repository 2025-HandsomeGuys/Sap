using UnityEngine;

namespace Gameplay.Terrain.Tiles.SpecialChunks.SpaceLayer
{
    /// <summary>
    /// 별자리 한붓그리기 퍼즐의 수동 리셋 레버. 곡괭이(toolIndex=2)로 타격하면
    /// 연결된 StarPuzzleManager의 진행을 전부 초기화한다. (이미 클리어된 퍼즐은 무시됨.)
    /// 같은 GameObject에 Collider2D가 있어야 Digger가 GetComponent&lt;IDiggable&gt;로 찾는다.
    /// </summary>
    public class StarResetLever : MonoBehaviour, IPuzzleDiggable
    {
        [Header("Reset Target")]
        [Tooltip("초기화할 별자리 퍼즐 매니저")]
        public StarPuzzleManager puzzleManager;

        [Header("Hit")]
        [Tooltip("연속 타격 중복 방지 쿨다운(초)")]
        public float hitCooldown = 0.2f;

        private float _lastHitTime = float.NegativeInfinity;

        // ─── IDiggable: 곡괭이 타격 = 리셋 ───
        public void Dig(Vector2 worldPos, float damage, int toolIndex)
        {
            if (toolIndex != 2) return; // 곡괭이 전용
            if (Time.time - _lastHitTime < hitCooldown) return;
            _lastHitTime = Time.time;

            if (puzzleManager == null)
            {
                Debug.LogWarning($"[StarResetLever] '{gameObject.name}'에 puzzleManager가 연결되지 않았습니다.");
                return;
            }
            puzzleManager.ResetAllConnections();
        }
    }
}
