using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 창고 테스트를 위한 스크립트
/// 버튼을 클릭하면 테스트용 광물을 창고에 추가합니다.
/// </summary>
public class WarehouseTestHelper : MonoBehaviour
{
    [Header("테스트 광물")]
    [Header("테스트 광물")]
    public MineralSO[] testMinerals; // Inspector에서 테스트용 광물을 드래그하여 설정
    
    [Header("테스트 장비")]
    public EquipmentSO[] testEquipments; // Inspector에서 테스트용 장비를 드래그하여 설정

    [Header("버튼 (선택사항)")]
    public Button addMineralButton;
    public TextMeshProUGUI buttonText;

    void Start()
    {
        // 버튼 클릭 이벤트 연결
        if (addMineralButton != null)
        {
            addMineralButton.onClick.RemoveAllListeners();
            addMineralButton.onClick.AddListener(AddRandomMineralToWarehouse);

            if (buttonText != null)
            {
                buttonText.text = "광물 추가 (테스트)";
            }
        }
    }

    void Update()
    {
        // T 키를 누르면 광물 추가 (단축키)
        if (Input.GetKeyDown(KeyCode.T))
        {
            AddRandomMineralToWarehouse();
        }

        // Y 키를 누르면 5개 추가 (대량 테스트)
        if (Input.GetKeyDown(KeyCode.Y))
        {
            for (int i = 0; i < 5; i++)
            {
                AddRandomMineralToWarehouse();
            }
        }

        // U 키를 누르면 창고 비우기
        if (Input.GetKeyDown(KeyCode.U))
        {
            ClearWarehouse();
        }

        // 장비 추가(상호작용 키와 겹치지 않게 O로 옮김)
        if (Input.GetKeyDown(KeyCode.O))
        {
            AddRandomEquipmentToWarehouse();
        }
    }

    /// <summary>
    /// 랜덤 광물을 창고에 추가
    /// </summary>
    public void AddRandomMineralToWarehouse()
    {
        if (WarehouseManager.Instance == null)
        {
            Debug.LogError("[WarehouseTestHelper] WarehouseManager를 찾을 수 없습니다!");
            return;
        }

        if (testMinerals == null || testMinerals.Length == 0)
        {
            Debug.LogWarning("[WarehouseTestHelper] 테스트 광물이 설정되지 않았습니다. Inspector에서 광물을 추가하세요.");
            return;
        }

        // 랜덤 광물 선택
        MineralSO randomMineral = testMinerals[Random.Range(0, testMinerals.Length)];
        
        // 랜덤 수량 (1~10)
        int randomQuantity = Random.Range(1, 11);

        // 창고에 추가
        WarehouseManager.Instance.AddMineral(randomMineral, randomQuantity);

        Debug.Log($"[WarehouseTestHelper] 창고에 {randomMineral.DisplayName} {randomQuantity}개 추가됨!");
    }

    /// <summary>
    /// 특정 광물을 창고에 추가
    /// </summary>
    public void AddMineralToWarehouse(MineralSO mineral, int quantity = 1)
    {
        if (WarehouseManager.Instance == null)
        {
            Debug.LogError("[WarehouseTestHelper] WarehouseManager를 찾을 수 없습니다!");
            return;
        }

        if (mineral == null)
        {
            Debug.LogWarning("[WarehouseTestHelper] 광물이 null입니다.");
            return;
        }

        WarehouseManager.Instance.AddMineral(mineral, quantity);
        WarehouseManager.Instance.AddMineral(mineral, quantity);
        Debug.Log($"[WarehouseTestHelper] 창고에 {mineral.DisplayName} {quantity}개 추가됨!");
    }

    /// <summary>
    /// 랜덤 장비를 창고에 추가
    /// </summary>
    public void AddRandomEquipmentToWarehouse()
    {
        if (WarehouseManager.Instance == null)
        {
            Debug.LogError("[WarehouseTestHelper] WarehouseManager를 찾을 수 없습니다!");
            return;
        }

        if (testEquipments == null || testEquipments.Length == 0)
        {
            Debug.LogWarning("[WarehouseTestHelper] 테스트 장비가 설정되지 않았습니다. Inspector에서 장비를 추가하세요.");
            return;
        }

        // 랜덤 장비 선택
        EquipmentSO randomEquipment = testEquipments[Random.Range(0, testEquipments.Length)];
        
        // 장비는 1개씩 추가 (수량 1)
        int quantity = 1;

        // 창고에 추가
        WarehouseManager.Instance.AddEquipment(randomEquipment, quantity);

        Debug.Log($"[WarehouseTestHelper] 창고에 {randomEquipment.DisplayName} {quantity}개 추가됨!");
    }

    /// <summary>
    /// 창고 비우기
    /// </summary>
    public void ClearWarehouse()
    {
        if (WarehouseManager.Instance == null)
        {
            Debug.LogError("[WarehouseTestHelper] WarehouseManager를 찾을 수 없습니다!");
            return;
        }

        // 창고의 광물 리스트 가져오기
        var minerals = WarehouseManager.Instance.StoredMinerals;
        int count = minerals.Count;

        // 모든 광물 제거 (역순으로)
        for (int i = count - 1; i >= 0; i--)
        {
            var slot = minerals[i];
            if (slot != null && slot.item != null && slot.item is MineralSO mineralSO)
            {
                WarehouseManager.Instance.RemoveMineral(mineralSO, slot.quantity);
            }
        }

        Debug.Log($"[WarehouseTestHelper] 창고를 비웠습니다. ({count}개 슬롯 제거)");
    }

    /// <summary>
    /// 창고 상태 로그 출력
    /// </summary>
    public void LogWarehouseStatus()
    {
        if (WarehouseManager.Instance == null)
        {
            Debug.LogError("[WarehouseTestHelper] WarehouseManager를 찾을 수 없습니다!");
            return;
        }

        Debug.Log("===== 창고 상태 =====");
        Debug.Log($"광물: {WarehouseManager.Instance.StoredMinerals.Count}개 슬롯");
        Debug.Log($"아이템: {WarehouseManager.Instance.StoredItems.Count}개 슬롯");
        Debug.Log($"장비: {WarehouseManager.Instance.StoredEquipments.Count}개 슬롯");
        Debug.Log("====================");
    }
}
