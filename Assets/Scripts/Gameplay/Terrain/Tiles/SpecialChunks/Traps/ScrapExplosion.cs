// @tags: trap, special-chunk, explosion, damage, loot, digging
using UnityEngine;

/// <summary>
/// Special Chunk: Compressed Trash Wall
/// 파기 시 폭발. HP 소진 → 폭발 피해 + 아이템 파괴 + 광물 드롭.
///
/// SOLID:
///  - SRP: HP 관리·폭발 피해·아이템 파괴만 담당. 드롭은 ILootDropper에 위임.
///  - DIP: ILootDropper 인터페이스만 참조 — ScrapExplosionDropper 구체 타입 미참조.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Collider2D))]
public class ScrapExplosion : MonoBehaviour, IDiggable
{
    private float _explosionRadius = 3f;
    private float _staminaDamage   = 30f;
    private float _maxHp           = 30f;

    [Header("VFX")]
    public GameObject explosionVFXPrefab;

    private float _currentHp;
    private SpriteRenderer _spriteRenderer;
    private ILootDropper _dropper;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _dropper        = GetComponent<ILootDropper>();

        if (SpecialChunkSettingsLoader.Instance != null)
        {
            var s = SpecialChunkSettingsLoader.Instance.Settings.traps.scrapExplosion;
            _explosionRadius = s.explosionRadius;
            _staminaDamage   = s.staminaDamage;
            _maxHp           = s.maxHp;
        }

        _currentHp = _maxHp;
    }

    // ─── IDiggable ────────────────────────────────────────────────
    public void Dig(Vector2 worldPos, float damage, int toolIndex)
    {
        _currentHp -= damage;
        Debug.Log($"[ScrapExplosion] Hit — HP={_currentHp:F1}/{_maxHp}, tool={toolIndex}");

        if (_currentHp <= 0f)
            Explode();
    }

    // ─── 폭발 ─────────────────────────────────────────────────────
    private void Explode()
    {
        Vector3 blastCenter = _spriteRenderer != null
            ? _spriteRenderer.bounds.center
            : transform.position;

        SpawnVFX(blastCenter);
        ProcessBlast(blastCenter);
        _dropper?.Drop(blastCenter);

        Debug.Log("[ScrapExplosion] Destroyed.");
        Destroy(gameObject);
    }

    private void SpawnVFX(Vector3 pos)
    {
        if (explosionVFXPrefab != null)
            Instantiate(explosionVFXPrefab, pos, Quaternion.identity);
    }

    /// <summary>
    /// 단일 OverlapCircleAll로 플레이어 피해와 아이템 파괴를 한 번에 처리한다.
    /// </summary>
    private void ProcessBlast(Vector3 blastCenter)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(blastCenter, _explosionRadius);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            // 플레이어 피해 (IHazardTarget — DIP: PlayerStat 구체 타입 미참조)
            if (hit.CompareTag("Player"))
            {
                IHazardTarget target = hit.GetComponent<IHazardTarget>();
                if (target == null) target = hit.GetComponentInParent<IHazardTarget>();
                if (target != null)
                {
                    target.ApplyHazardDamage(_staminaDamage);
                    Debug.Log($"[ScrapExplosion] 플레이어 피해 -{_staminaDamage}");
                }
                continue;
            }

            // 드롭된 아이템 파괴
            if (hit.GetComponent<MineralItemController>() != null ||
                hit.GetComponent<PickupableItem>()        != null)
            {
                Debug.Log($"[ScrapExplosion] 폭발로 아이템 파괴: {hit.gameObject.name}");
                Destroy(hit.gameObject);
            }
        }
    }
}
