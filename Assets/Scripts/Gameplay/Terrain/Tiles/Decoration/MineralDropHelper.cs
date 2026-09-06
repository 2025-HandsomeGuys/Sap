// @tags: mineral, drop, helper, spawn
using UnityEngine;

/// <summary>
/// 월드에 광물 픽업 오브젝트를 1개 생성하고 MineralLifetime(자동소멸)을 부착하는 공용 헬퍼.
/// DiggableRock의 랜덤 드롭과 MineralRock의 확정 드롭이 공유한다 (DRY).
/// </summary>
public static class MineralDropHelper
{
    /// <summary>MineralSO 직접 드롭.</summary>
    public static void Drop(MineralSO so, Vector3 worldPos)
    {
        if (so == null) { Debug.LogWarning("[MineralDropHelper] MineralSO 가 null"); return; }
        if (so.mineralPrefab == null) { Debug.LogWarning($"[MineralDropHelper] mineralPrefab 없음: {so.name}"); return; }

        GameObject spawned = Object.Instantiate(so.mineralPrefab, worldPos, Quaternion.identity);
        if (spawned == null) return;

        spawned.AddComponent<MineralLifetime>();

        // 돌이 부서져 나온 광물은 살짝 위로 튀었다가 내려온다.
        // 땅속 광물이 지지를 잃어 그냥 흘러내리는 것과 구별되고,
        // 돌 조각이 튀는 동안 위쪽으로 올라와 무엇이 나왔는지가 바로 보인다.
        if (spawned.TryGetComponent<MineralItemController>(out var mineral))
            mineral.PopOut();
    }

    /// <summary>MineralID로 MineralDatabase에서 조회 후 드롭.</summary>
    public static void Drop(MineralID id, Vector3 worldPos)
    {
        if (MineralDatabase.Instance == null) { Debug.LogWarning("[MineralDropHelper] MineralDatabase.Instance 가 null"); return; }
        Drop(MineralDatabase.Instance.GetMineralByID(id), worldPos);
    }
}
