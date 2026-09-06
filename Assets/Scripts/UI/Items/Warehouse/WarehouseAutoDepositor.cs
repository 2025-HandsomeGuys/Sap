using UnityEngine;

/// <summary>
/// 지상 씬 진입 시 자동으로 플레이어 인벤토리를 비우고 창고로 이동시키는 스크립트.
/// 지상 씬의 관리자 오브젝트(예: ShopCanvas 등)에 부착하여 사용합니다.
/// </summary>
public class WarehouseAutoDepositor : MonoBehaviour
{
    private System.Collections.IEnumerator Start()
    {
        // 최적화: 유니티 생명주기 및 비활성화 이슈(비활성화된 캔버스/매니저에서 코루틴 중단 등)를 
        // 완벽하게 방지하기 위해 LoadingSceneController.cs 내부의 TriggerAutoDeposit()에서 직접 동기식으로 일괄 입고 처리하도록 이관하였습니다.
        // 현재 이 스크립트는 하위 호환성을 위해 껍데기만 남겨둡니다. 
        // 씬에서 GameObject에 붙어있더라도 아무 작업도 하지 않습니다.
        yield break;
    }

    private void ApplyEmergencyPenalty(MineralInventory mineralInv)
    {
        if (mineralInv == null) return;

        var items = new System.Collections.Generic.List<MineralSO>();
        foreach (var slot in mineralInv.ReadonlyItems)
        {
            if (slot.item is MineralSO mineral && slot.quantity > 0)
            {
                for (int i = 0; i < slot.quantity; i++)
                {
                    items.Add(mineral);
                }
            }
        }

        int totalCount = items.Count;
        int dropCount = Mathf.FloorToInt(totalCount * 0.6f);

        // Fisher-Yates shuffle 알고리즘으로 무작위 셔플
        for (int i = 0; i < totalCount; i++)
        {
            int r = Random.Range(i, totalCount);
            var temp = items[i];
            items[i] = items[r];
            items[r] = temp;
        }

        // 앞쪽에서부터 60% 분량 제거
        for (int i = 0; i < dropCount; i++)
        {
            mineralInv.RemoveItem(items[i], 1);
        }
        
        Debug.Log($"[WarehouseAutoDepositor] 긴급 탈출 페널티 완료: 총 광물 {totalCount}개 중 {dropCount}개 삭제");
    }
}
