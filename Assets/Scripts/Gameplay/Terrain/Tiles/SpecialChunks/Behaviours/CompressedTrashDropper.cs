// @tags: loot, drop, special-chunk, mineral, explosion, interface
using UnityEngine;

/// <summary>
/// 압축 쓰레기 벽 파괴 시 드롭을 처리하는 컴포넌트.
///
/// 드롭 목록:
///   - 쓰레기 아이템 (ScrapMetal, GarbageBag, PETBottle) — 3~6개 랜덤
///   - 희귀광물 (Copper) — 20% 확률
///
/// 폭발 산란: 드롭된 광물 오브젝트에 Rigidbody2D 임펄스를 가해
///           쓰레기가 사방으로 튀어나오는 연출을 구현.
///
/// SOLID — SRP: 드롭 로직만 담당.
/// </summary>
public class CompressedTrashDropper : MonoBehaviour, ILootDropper
{
    [Header("쓰레기 드롭 설정")]
    [Tooltip("드롭할 쓰레기 아이템 최솟값")]
    public int trashMin = 3;

    [Tooltip("드롭할 쓰레기 아이템 최댓값")]
    public int trashMax = 6;

    [Header("희귀광물 설정")]
    [Range(0f, 1f)]
    [Tooltip("희귀광물(구리) 드롭 확률. 기획: 20%")]
    public float rareMineralChance = 0.20f;

    [Header("폭발 산란 설정")]
    [Tooltip("산란 수평 최대 속도 (world units/s)")]
    public float scatterSpeedX = 4f;

    [Tooltip("산란 수직 속도 (위로 튀는 힘)")]
    public float scatterSpeedY = 5f;

    // ─── ILootDropper ─────────────────────────────────────────────
    /// <summary>파괴 중심점에서 아이템을 폭발적으로 산란시킨다.</summary>
    public void Drop(Vector3 center)
    {
        if (MineralDatabase.Instance == null)
        {
            Debug.LogWarning("[CompressedTrashDropper] MineralDatabase.Instance is null.");
            return;
        }

        // 쓰레기 아이템 산란 드롭
        int count = Random.Range(trashMin, trashMax + 1);
        for (int i = 0; i < count; i++)
        {
            MineralID trashId = PickRandomTrash();
            SpawnWithBlast(trashId, center);
        }

        // 희귀광물 20% 확률 드롭
        if (Random.value < rareMineralChance)
            SpawnWithBlast(MineralID.Copper, center);
    }

    // ─── 내부 ────────────────────────────────────────────────────

    /// <summary>쓰레기 종류를 균등 확률로 하나 고른다.</summary>
    private MineralID PickRandomTrash()
    {
        // ScrapMetal 50%, GarbageBag 30%, PETBottle 20%
        float r = Random.value;
        if (r < 0.50f) return MineralID.ScrapMetal;
        if (r < 0.80f) return MineralID.GarbageBag;
        return MineralID.PETBottle;
    }

    /// <summary>중심점에서 광물을 스폰하고 랜덤 방향 임펄스를 가한다.</summary>
    private void SpawnWithBlast(MineralID id, Vector3 center)
    {
        MineralSO so = MineralDatabase.Instance.GetMineralByID(id);
        if (so == null || so.mineralPrefab == null)
        {
            Debug.LogWarning($"[CompressedTrashDropper] MineralSO/prefab 없음: {id}");
            return;
        }

        // 스폰 위치: 중심에서 살짝 랜덤 오프셋
        Vector3 spawnPos = center + new Vector3(
            Random.Range(-0.2f, 0.2f),
            Random.Range(-0.1f, 0.2f),
            -1f
        );

        GameObject spawned = Instantiate(so.mineralPrefab, spawnPos, Quaternion.identity);

        if (spawned.TryGetComponent<MineralItemController>(out var ctrl))
            ctrl.mineralData = so;

        // Rigidbody2D 임펄스 — 쓰레기가 사방으로 튀어나오는 연출
        if (spawned.TryGetComponent<Rigidbody2D>(out var rb))
        {
            Vector2 blastDir = new Vector2(
                Random.Range(-1f, 1f),   // 좌우 랜덤
                Random.Range(0.3f, 1f)   // 위쪽 방향 편향
            ).normalized;

            float speed = Random.Range(scatterSpeedX * 0.5f, scatterSpeedX);
            rb.AddForce(new Vector2(blastDir.x * speed, scatterSpeedY), ForceMode2D.Impulse);
        }
    }
}
