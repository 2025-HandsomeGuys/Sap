// @tags: cauldron, spawn, reward, mineral, explosion
using UnityEngine;

public partial class DokkaebiCauldron
{
    [Header("Cauldron Reward Prefabs")]
    [SerializeField] private GameObject explosiveHazardPrefab; // ExplosiveMineralReactor가 쓰는 것과 동일
    [SerializeField] private MineralSO ashMineral;             // 실패 보상 "재" (Task 9에서 에셋 결정)
    [SerializeField] private float spawnScatter = 0.4f;
    [SerializeField] private int explosiveSpawnArc = 60;       // 분산 각도(±deg)

    private void SpawnReward(CauldronResult result)
    {
        Vector3 origin = (spawnPoint != null ? spawnPoint : transform).position;

        switch (result.rewardType)
        {
            case CauldronRewardType.Mineral:
            case CauldronRewardType.EasterEgg: // 현재는 최상위 광물 그대로 지급 (유니크 보상은 추후)
                SpawnMineral(result.resultMineral, origin);
                break;

            case CauldronRewardType.Ash:
                if (ashMineral != null) SpawnMineralSO(ashMineral, origin);
                else Debug.LogWarning("[Cauldron] ashMineral 미할당 — 재 스폰 생략");
                break;

            case CauldronRewardType.Explosion:
                SpawnExplosives(result.explosiveCount, origin);
                break;
        }
    }

    private void SpawnMineral(MineralID id, Vector3 origin)
    {
        var so = MineralDatabase.Instance?.GetMineralByID(id);
        if (so == null) { Debug.LogWarning($"[Cauldron] MineralSO 없음: {id}"); return; }
        SpawnMineralSO(so, origin);
    }

    private void SpawnMineralSO(MineralSO so, Vector3 origin)
    {
        if (so.mineralPrefab == null) { Debug.LogWarning($"[Cauldron] mineralPrefab 없음: {so.name}"); return; }
        Vector3 pos = origin + (Vector3)(Random.insideUnitCircle * spawnScatter) + Vector3.up * 0.3f;
        var obj = Instantiate(so.mineralPrefab, pos, Quaternion.Euler(0, 0, Random.Range(0f, 360f)));
        // 살짝 튀어오르는 느낌: Rigidbody2D 있으면 위로 임펄스
        var rb = obj.GetComponent<Rigidbody2D>();
        if (rb != null) rb.AddForce(new Vector2(Random.Range(-1.5f, 1.5f), Random.Range(3f, 5f)), ForceMode2D.Impulse);
    }

    private void SpawnExplosives(int count, Vector3 origin)
    {
        if (explosiveHazardPrefab == null) { Debug.LogWarning("[Cauldron] explosiveHazardPrefab 미할당"); return; }
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = origin + (Vector3)(Random.insideUnitCircle * spawnScatter) + Vector3.up * 0.3f;
            var obj = Instantiate(explosiveHazardPrefab, pos, Quaternion.identity);
            var rb = obj.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                float ang = (90f + Random.Range(-explosiveSpawnArc, explosiveSpawnArc)) * Mathf.Deg2Rad;
                rb.AddForce(new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(4f, 7f), ForceMode2D.Impulse);
            }
        }
    }
}
