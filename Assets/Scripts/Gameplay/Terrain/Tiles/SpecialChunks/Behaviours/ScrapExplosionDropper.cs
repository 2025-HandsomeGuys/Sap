// @tags: loot, drop, special-chunk, explosion, mineral, interface
using UnityEngine;

/// <summary>
/// 압축 쓰레기 벽 폭발 시 광물 드롭을 담당하는 컴포넌트.
///
/// SOLID:
///  - SRP: 드롭 로직만. HP 판정·폭발 피해·VFX와 완전 분리.
///  - DIP: ILootDropper 계약 구현 → ScrapExplosion은 인터페이스만 알면 됨.
///  - OCP: 드롭 항목 변경은 Inspector/JSON 설정으로 — 코드 수정 불필요.
/// </summary>
public class ScrapExplosionDropper : MonoBehaviour, ILootDropper
{
    [Header("확정 드롭 — 고철")]
    public int scrapMin = 3;
    public int scrapMax = 6;

    [Header("확률 드롭")]
    [Range(0f, 1f)] public float copperChance = 0.20f;
    [Range(0f, 1f)] public float ironChance   = 0.15f;

    [Header("드롭 산란")]
    public float scatterX = 0.3f;
    public float scatterY = 0.2f;

    private void Awake()
    {
        if (SpecialChunkSettingsLoader.Instance != null)
        {
            var s = SpecialChunkSettingsLoader.Instance.Settings.traps.scrapExplosion;
            scrapMin      = s.scrapMin;
            scrapMax      = s.scrapMax;
            copperChance  = s.copperChance;
            ironChance    = s.ironChance;
        }
    }

    // ─── ILootDropper ────────────────────────────────────────────
    public void Drop(Vector3 center)
    {
        int scrapCount = Random.Range(scrapMin, scrapMax + 1);
        for (int i = 0; i < scrapCount; i++)
            TryDrop(MineralID.ScrapMetal, center);

        if (Random.value <= copperChance) TryDrop(MineralID.Copper, center);
        if (Random.value <= ironChance)   TryDrop(MineralID.Iron,   center);
    }

    // ─── 내부 ────────────────────────────────────────────────────
    private void TryDrop(MineralID id, Vector3 center)
    {
        if (MineralDatabase.Instance == null)
        {
            Debug.LogWarning("[ScrapExplosionDropper] MineralDatabase.Instance is null.");
            return;
        }

        MineralSO so = MineralDatabase.Instance.GetMineralByID(id);
        if (so == null || so.mineralPrefab == null)
        {
            Debug.LogWarning($"[ScrapExplosionDropper] MineralSO or prefab missing for {id}.");
            return;
        }

        Vector3 dropPos = new Vector3(
            center.x + Random.Range(-scatterX, scatterX),
            center.y + Random.Range(0f, scatterY),
            -1f
        );

        GameObject spawned = Instantiate(so.mineralPrefab, dropPos, Quaternion.identity);
        if (spawned.TryGetComponent<MineralItemController>(out var ctrl))
            ctrl.mineralData = so;

        Debug.Log($"[ScrapExplosionDropper] Dropped {id} at {dropPos}");
    }
}
