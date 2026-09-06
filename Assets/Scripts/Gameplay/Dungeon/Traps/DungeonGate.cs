using UnityEngine;

namespace Gameplay.Dungeon.Traps
{
    /// <summary>압력판에 연동되는 문. Activate=열림(통과 가능), Deactivate=닫힘.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class DungeonGate : MonoBehaviour, ILinkTarget
    {
        [SerializeField] private bool startOpen = false;
        private Collider2D _col;
        private SpriteRenderer[] _renderers;

        private void Awake()
        {
            _col = GetComponent<Collider2D>();
            _renderers = GetComponentsInChildren<SpriteRenderer>();
            SetOpen(startOpen);
        }

        public void Activate() => SetOpen(true);
        public void Deactivate() => SetOpen(false);

        private void SetOpen(bool open)
        {
            if (_col != null) _col.enabled = !open;
            foreach (var r in _renderers) r.enabled = !open;
        }
    }
}
