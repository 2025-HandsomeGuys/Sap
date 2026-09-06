using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 미네랄 인벤토리 테스트를 위한 스크립트
/// 버튼을 클릭하거나 키를 눌러 테스트용 광물을 플레이어 인벤토리에 추가합니다.
/// </summary>
public class MineralInventoryTestHelper : MonoBehaviour
{
    [Header("테스트 광물")]
    public MineralSO[] testMinerals; // Inspector에서 테스트용 광물을 드래그하여 설정

    [Header("버튼 (선택사항)")]
    public Button addMineralButton;
    public TextMeshProUGUI buttonText;

    private MineralInventory mineralInventory;

    void Start()
    {
        mineralInventory = FindFirstObjectByType<MineralInventory>();

        // 버튼 클릭 이벤트 연결
        if (addMineralButton != null)
        {
            addMineralButton.onClick.RemoveAllListeners();
            addMineralButton.onClick.AddListener(AddRandomMineralToInventory);

            if (buttonText != null)
            {
                buttonText.text = "인벤토리에 광물 추가 (테스트)";
            }
        }
    }

    void Update()
    {
        // G 키를 누르면 광물 추가 (단축키)
        if (Input.GetKeyDown(KeyCode.G))
        {
            AddRandomMineralToInventory();
        }

        // H 키를 누르면 5개 추가 (대량 테스트)
        if (Input.GetKeyDown(KeyCode.H))
        {
            for (int i = 0; i < 5; i++)
            {
                AddRandomMineralToInventory();
            }
        }

        // J 키를 누르면 인벤토리 비우기
        if (Input.GetKeyDown(KeyCode.J))
        {
            ClearInventory();
        }
    }

    /// <summary>
    /// 랜덤 광물을 인벤토리에 추가
    /// </summary>
    public void AddRandomMineralToInventory()
    {
        if (mineralInventory == null)
        {
            mineralInventory = FindFirstObjectByType<MineralInventory>();
            if (mineralInventory == null)
            {
                Debug.LogError("[MineralInventoryTestHelper] MineralInventory를 찾을 수 없습니다!");
                return;
            }
        }

        if (testMinerals == null || testMinerals.Length == 0)
        {
            Debug.LogWarning("[MineralInventoryTestHelper] 테스트 광물이 설정되지 않았습니다. Inspector에서 광물을 추가하세요.");
            return;
        }

        // 랜덤 광물 선택
        MineralSO randomMineral = testMinerals[Random.Range(0, testMinerals.Length)];
        
        // 랜덤 수량 (1~5)
        int randomQuantity = Random.Range(1, 6);

        // 인벤토리에 추가
        int added = mineralInventory.AddItem(randomMineral, randomQuantity);

        if (added > 0)
        {
            Debug.Log($"[MineralInventoryTestHelper] 인벤토리에 {randomMineral.DisplayName} {added}개 추가됨!");
        }
        else
        {
            Debug.LogWarning($"[MineralInventoryTestHelper] 인벤토리가 가득 찼거나 무게 제한으로 추가 실패: {randomMineral.DisplayName}");
        }
    }

    /// <summary>
    /// 인벤토리 비우기
    /// </summary>
    public void ClearInventory()
    {
        if (mineralInventory == null)
        {
            mineralInventory = FindFirstObjectByType<MineralInventory>();
            if (mineralInventory == null)
            {
                Debug.LogError("[MineralInventoryTestHelper] MineralInventory를 찾을 수 없습니다!");
                return;
            }
        }

        // 모든 광물 제거 (ReadonlyItems 복사본 사용)
        var minerals = new System.Collections.Generic.List<InventorySlot>(mineralInventory.ReadonlyItems);
        
        foreach (var slot in minerals)
        {
             if (slot != null && slot.item != null && slot.item is MineralSO mineralSO)
             {
                 mineralInventory.RemoveItem(mineralSO, slot.quantity);
             }
        }

        Debug.Log($"[MineralInventoryTestHelper] 인벤토리를 비웠습니다.");
    }
}
