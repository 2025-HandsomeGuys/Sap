using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SaveSlotItemUI : MonoBehaviour
{
    public Button slotButton;
    public Button deleteButton; // <--- 삭제 버튼 추가
    
    [Header("Text References")]
    public TextMeshProUGUI slotNameText;
    public TextMeshProUGUI dayText;
    public TextMeshProUGUI timeText;
    public TextMeshProUGUI goldText;
    
    [Header("Empty State")]
    public GameObject emptyStateGroup;
    public GameObject dataStateGroup;

    private void Awake()
    {
        // 누락된 참조가 있다면 자동으로 탐색
        if (slotButton == null) slotButton = GetComponent<Button>();
        
        // 이름에 특정 키워드가 포함된 자식을 탐색 (복제 시 숫자가 붙는 경우 대비)
        foreach (Transform child in transform)
        {
            if (emptyStateGroup == null && child.name.Contains("EmptyStateGroup")) emptyStateGroup = child.gameObject;
            if (dataStateGroup == null && child.name.Contains("DataStateGroup")) dataStateGroup = child.gameObject;
            
            if (slotNameText == null && child.name.Contains("SlotNameText")) slotNameText = child.GetComponent<TextMeshProUGUI>();
            if (dayText == null && child.name.Contains("DayText")) dayText = child.GetComponent<TextMeshProUGUI>();
            if (timeText == null && child.name.Contains("TimeText")) timeText = child.GetComponent<TextMeshProUGUI>();
            if (goldText == null && child.name.Contains("GoldText")) goldText = child.GetComponent<TextMeshProUGUI>();
            
            if (deleteButton == null && child.name.Contains("DeleteButton")) deleteButton = child.GetComponent<Button>();
        }
    }

    public void Setup(int slotIndex, SaveSlotSummary summary)
    {
        if (slotNameText != null)
        {
            slotNameText.text = $"Slot {slotIndex + 1}";
        }

        if (summary.hasData)
        {
            if (emptyStateGroup != null) emptyStateGroup.SetActive(false);
            if (dataStateGroup != null) dataStateGroup.SetActive(true);
            if (deleteButton != null) deleteButton.gameObject.SetActive(true); // 데이터 있을 때만 삭제 버튼 활성화

            if (dayText != null) dayText.text = $"Day {summary.day}";
            if (timeText != null) timeText.text = $"Time: {summary.time}";
            if (goldText != null) goldText.text = $"Gold: {summary.gold}";
        }
        else
        {
            if (emptyStateGroup != null) emptyStateGroup.SetActive(true);
            if (dataStateGroup != null) dataStateGroup.SetActive(false);
            if (deleteButton != null) deleteButton.gameObject.SetActive(false); // 데이터 없으면 삭제 버튼 비활성화
        }
    }
}
