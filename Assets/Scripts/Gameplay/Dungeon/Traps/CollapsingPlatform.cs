using System.Collections;
using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>밟으면 잠깐 뒤 사라졌다가 복구되는 발판.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class CollapsingPlatform : MonoBehaviour
    {
        [SerializeField] private float collapseDelay = 0.3f;
        [SerializeField] private float respawnDelay = 2f;
        [SerializeField] private string playerTag = "Player";

        private Collider2D _col;
        private SpriteRenderer _sr;
        private bool _triggered;

        private void Awake()
        {
            _col = GetComponent<Collider2D>();
            _sr = GetComponent<SpriteRenderer>();
        }

        private void OnCollisionEnter2D(Collision2D c)
        {
            if (_triggered || !c.collider.CompareTag(playerTag)) return;
            _triggered = true;
            StartCoroutine(CollapseRoutine());
        }

        private IEnumerator CollapseRoutine()
        {
            yield return new WaitForSeconds(collapseDelay);
            SetVisible(false);
            yield return new WaitForSeconds(respawnDelay);
            SetVisible(true);
            _triggered = false;
        }

        private void SetVisible(bool on)
        {
            if (_col != null) _col.enabled = on;
            if (_sr != null) _sr.enabled = on;
        }

        // 코루틴 도중 비활성화(풀링/청크 재로드 등)되면 _triggered가 true로 고착돼
        // 재활성화 후 영영 안 무너지는 상태가 된다. 여기서 상태를 복구해 방지.
        private void OnDisable()
        {
            StopAllCoroutines();
            _triggered = false;
            SetVisible(true);
        }
    }
}
