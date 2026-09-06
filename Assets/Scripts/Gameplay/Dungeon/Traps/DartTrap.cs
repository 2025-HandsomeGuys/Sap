using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>
    /// 로컬 +X(transform.right) 방향으로 다트를 발사하는 함정.
    /// 기본 모드(PlayerProximity): 앞쪽 감지 영역에 플레이어가 들어오면 '한 발' 빠르게 발사 후 쿨다운.
    /// PlateLinked: 압력판(PressurePlate) Activate 시에만 한 발. Periodic: 일정 주기 반복.
    /// 발사 방향은 zRotation으로 정한다(예: 180 = 왼쪽).
    /// </summary>
    public class DartTrap : MonoBehaviour, ILinkTarget
    {
        public enum TriggerMode { PlayerProximity, PlateLinked, Periodic }

        [SerializeField] private DartProjectile projectilePrefab;

        [Header("Projectile")]
        [Tooltip("다트 속도(빠르게)")]
        [SerializeField] private float speed = 16f;
        [SerializeField] private float damage = 15f;
        [SerializeField] private float projectileLife = 4f;

        [Header("Trigger")]
        [SerializeField] private TriggerMode mode = TriggerMode.PlayerProximity;
        [Tooltip("발사 후 재장전 시간(연사 방지)")]
        [SerializeField] private float cooldown = 1.2f;
        [Tooltip("Periodic 모드 발사 주기")]
        [SerializeField] private float period = 1.5f;

        [Header("Detection (PlayerProximity 모드)")]
        [Tooltip("앞쪽(로컬 +X)으로 감지하는 길이")]
        [SerializeField] private float detectLength = 6f;
        [Tooltip("감지 박스 두께")]
        [SerializeField] private float detectThickness = 1.2f;
        [SerializeField] private string playerTag = "Player";

        private float _timer;        // Periodic용
        private float _cooldownLeft;
        private bool _wasInFront;    // PlayerProximity 엣지 트리거용

        private void Update()
        {
            if (_cooldownLeft > 0f) _cooldownLeft -= Time.deltaTime;

            switch (mode)
            {
                case TriggerMode.Periodic:
                    _timer += Time.deltaTime;
                    if (_timer >= period) { _timer = 0f; Fire(); }
                    break;

                case TriggerMode.PlayerProximity:
                {
                    // 엣지 트리거: 감지 영역에 '들어오는 순간' 딱 한 발.
                    // 계속 서 있어도 재발사 없음 — 벗어났다가 다시 들어와야 발사.
                    bool inFront = PlayerInFront();
                    if (inFront && !_wasInFront && _cooldownLeft <= 0f) Fire();
                    _wasInFront = inFront;
                    break;
                }

                case TriggerMode.PlateLinked:
                    break; // Activate()에서만 발사
            }
        }

        /// <summary>압력판 연동 발사(쿨다운 중이면 무시).</summary>
        public void Activate()
        {
            if (_cooldownLeft <= 0f) Fire();
        }

        public void Deactivate() { }

        // 로컬 +X 방향 앞쪽 박스 안에 플레이어가 있는지.
        private bool PlayerInFront()
        {
            Vector2 dir = transform.right;
            Vector2 center = (Vector2)transform.position + dir * (detectLength * 0.5f);
            var hits = Physics2D.OverlapBoxAll(center, new Vector2(detectLength, detectThickness), transform.eulerAngles.z);
            foreach (var h in hits)
                if (h != null && h.CompareTag(playerTag)) return true;
            return false;
        }

        private void Fire()
        {
            if (projectilePrefab == null) return;
            _cooldownLeft = cooldown;

            var dart = Instantiate(projectilePrefab, transform.position, transform.rotation);
            dart.Launch(transform.right, speed, damage, projectileLife);
        }

        private void OnDrawGizmosSelected()
        {
            // 감지 영역 + 발사 방향 시각화(에디터 배치용)
            Vector3 dir = transform.right;
            Vector3 center = transform.position + dir * (detectLength * 0.5f);
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(detectLength, detectThickness, 0f));
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, transform.position + dir * detectLength);
        }
    }
}
