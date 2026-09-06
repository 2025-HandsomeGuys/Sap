using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>바닥에서 주기적으로 튀어나오는 가시. 돌출 중 접촉 시 데미지.
    /// linkControlled=true면 주기 무시하고 압력판 Activate 동안만 돌출.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class RetractingSpike : MonoBehaviour, ILinkTarget
    {
        [SerializeField] private float period = 2f;
        [SerializeField] private float activeFraction = 0.4f;
        [SerializeField] private float damage = 20f;
        [SerializeField] private bool linkControlled = false;
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private Transform visual; // 돌출/수납 시 보이기용(없어도 됨)

        private Collider2D _col;
        private bool _extended;
        private bool _linkActive;
        private TrapCycle _cycle;

        private void Reset()
        {
            var c = GetComponent<Collider2D>();
            if (c != null) c.isTrigger = true;
        }

        private void Awake()
        {
            _col = GetComponent<Collider2D>();
            _cycle = new TrapCycle { period = period, activeFraction = activeFraction };
        }

        public void Activate() => _linkActive = true;
        public void Deactivate() => _linkActive = false;

        private void Update()
        {
            bool extend = linkControlled ? _linkActive : _cycle.IsActive(Time.time);
            if (extend != _extended) SetExtended(extend);
        }

        private void SetExtended(bool on)
        {
            _extended = on;
            _col.enabled = on;
            if (visual != null) visual.gameObject.SetActive(on);
        }

        private void OnTriggerEnter2D(Collider2D other) => TryDamage(other);
        private void OnTriggerStay2D(Collider2D other) => TryDamage(other);

        private void TryDamage(Collider2D other)
        {
            if (!_extended || !other.CompareTag(playerTag)) return;
            TrapDamage.ApplyInjury(other, damage); // 부상 → MaxStamina 감소
        }
    }
}
