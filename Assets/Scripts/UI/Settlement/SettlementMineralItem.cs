using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 정산 UI 리스트에서 개별 광물 항목을 표시하는 스크립트.
/// </summary>
public class SettlementMineralItem : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI countText;

    /// <summary>
    /// 광물 데이터를 UI에 반영합니다.
    /// </summary>
    /// <param name="id">광물 ID</param>
    /// <param name="count">획득 수량</param>
    public void SetData(MineralID id, int count)
    {
        // MineralDatabase에서 광물 정보를 가져옵니다.
        if (MineralDatabase.Instance != null)
        {
            MineralSO mineralSO = MineralDatabase.Instance.GetMineralByID(id);
            if (mineralSO != null)
            {
                if (iconImage != null) iconImage.sprite = mineralSO.Icon;
                if (nameText != null) nameText.text = mineralSO.DisplayName;
            }
            else
            {
                if (nameText != null) nameText.text = id.ToString();
            }
        }
        else
        {
            if (nameText != null) nameText.text = id.ToString();
        }

        if (countText != null) countText.text = $"{count}개";
    }
}
