using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>
    /// 마리오 쿵쿵이(Thwomp)식 압쇄 블록.
    /// 위에서 부르르 떨며 대기 → 아래에 플레이어 감지 → 빠르게 낙하 → 바닥에서 잠깐 정지 → 천천히 복귀.
    /// 데미지는 '낙하 중'에만 준다.
    /// transform 이동이라 충돌 이벤트를 받으려면 Rigidbody2D(Kinematic)가 필요(프리팹에 포함됨).
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class CrusherBlock : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("아래로 떨어지는 거리(유닛)")]
        [SerializeField] private float dropDistance = 3f;
        [Tooltip("낙하 속도(빠름)")]
        [SerializeField] private float dropSpeed = 22f;
        [Tooltip("복귀 상승 속도(느림)")]
        [SerializeField] private float riseSpeed = 2.5f;
        [Tooltip("바닥에서 멈추는 시간")]
        [SerializeField] private float bottomPause = 0.4f;
        [Tooltip("복귀 후 재감지까지 대기(연속 발동 방지)")]
        [SerializeField] private float rearmDelay = 0.4f;

        [Header("Detection (플레이어가 아래 가까이)")]
        [Tooltip("감지 박스 가로 폭")]
        [SerializeField] private float detectWidth = 1f;
        [Tooltip("아래로 감지하는 거리")]
        [SerializeField] private float detectRange = 3.5f;
        [SerializeField] private string playerTag = "Player";

        [Header("Idle Tremble (부르르)")]
        [SerializeField] private float trembleAmp = 0.06f;
        [SerializeField] private float trembleFreq = 32f;

        [Header("Damage")]
        [SerializeField] private float damage = 40f;

        private enum State { Idle, Dropping, Bottom, Rising }
        private State _state = State.Idle;
        private Vector3 _top;   // 시작(위) 위치 = 복귀 지점
        private float _timer;   // Bottom 정지 / Idle 재장전 겸용

        private void Awake() => _top = transform.position;

        private void Update()
        {
            switch (_state)
            {
                case State.Idle:     TickIdle();     break;
                case State.Dropping: TickDropping(); break;
                case State.Bottom:   TickBottom();   break;
                case State.Rising:   TickRising();   break;
            }
        }

        private void TickIdle()
        {
            // 부르르 떨기(가로 미세 진동)
            float x = Mathf.Sin(Time.time * trembleFreq) * trembleAmp;
            transform.position = _top + new Vector3(x, 0f, 0f);

            if (_timer > 0f) { _timer -= Time.deltaTime; return; } // 재장전 중엔 감지 안 함

            if (PlayerBelow())
            {
                transform.position = _top; // 진동 리셋
                _state = State.Dropping;
            }
        }

        private void TickDropping()
        {
            Vector3 bottom = _top + Vector3.down * dropDistance;
            transform.position = Vector3.MoveTowards(transform.position, bottom, dropSpeed * Time.deltaTime);
            if ((transform.position - bottom).sqrMagnitude < 0.0001f)
            {
                _timer = bottomPause;
                _state = State.Bottom;
            }
        }

        private void TickBottom()
        {
            _timer -= Time.deltaTime;
            if (_timer <= 0f) _state = State.Rising;
        }

        private void TickRising()
        {
            transform.position = Vector3.MoveTowards(transform.position, _top, riseSpeed * Time.deltaTime);
            if ((transform.position - _top).sqrMagnitude < 0.0001f)
            {
                transform.position = _top;
                _timer = rearmDelay;
                _state = State.Idle;
            }
        }

        // 대기 위치(_top) 기준으로 아래쪽 박스 안에 플레이어가 있는지.
        private bool PlayerBelow()
        {
            Vector2 center = (Vector2)_top + Vector2.down * (detectRange * 0.5f);
            var hits = Physics2D.OverlapBoxAll(center, new Vector2(detectWidth, detectRange), 0f);
            foreach (var h in hits)
                if (h != null && h.CompareTag(playerTag)) return true;
            return false;
        }

        private void OnCollisionEnter2D(Collision2D c) => TryDamage(c.collider);
        private void OnTriggerEnter2D(Collider2D other) => TryDamage(other);

        private void TryDamage(Collider2D other)
        {
            if (_state != State.Dropping || !other.CompareTag(playerTag)) return; // 낙하 중에만 타격
            TrapDamage.ApplyInjury(other, damage); // 부상 → MaxStamina 감소
        }

        private void OnDrawGizmosSelected()
        {
            // 감지 범위 시각화(에디터 배치용)
            Vector3 anchor = Application.isPlaying ? _top : transform.position;
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
            Gizmos.DrawWireCube(anchor + Vector3.down * (detectRange * 0.5f), new Vector3(detectWidth, detectRange, 0f));
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
            Gizmos.DrawLine(anchor, anchor + Vector3.down * dropDistance);
        }
    }
}
