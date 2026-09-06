using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>직선 투사체. 플레이어 접촉 시 데미지, 수명 후 소멸.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class DartProjectile : MonoBehaviour
    {
        private Vector2 _dir;
        private float _speed, _damage, _life, _age;
        private string _playerTag = "Player";

        public void Launch(Vector2 dir, float speed, float damage, float life)
        {
            _dir = dir.normalized; _speed = speed; _damage = damage; _life = life;
            var c = GetComponent<Collider2D>(); if (c != null) c.isTrigger = true;
        }

        private void Update()
        {
            transform.position += (Vector3)(_dir * (_speed * Time.deltaTime));
            _age += Time.deltaTime;
            if (_age >= _life) Destroy(gameObject);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.CompareTag(_playerTag))
            {
                TrapDamage.ApplyInjury(other, _damage); // 부상 → MaxStamina 감소
                Destroy(gameObject);
            }
        }
    }
}
