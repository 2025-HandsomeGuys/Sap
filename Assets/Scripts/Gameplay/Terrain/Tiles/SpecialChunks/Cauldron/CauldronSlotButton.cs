// @tags: cauldron, ui, slot, button
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 가마솥 투입 UI의 광물 슬롯 버튼. 아이콘·수량 표시 + 클릭 콜백.
/// </summary>
public class CauldronSlotButton : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI quantityText;
    [SerializeField] private Button button;

    public void Bind(MineralSO mineral, int quantity, Action onClick)
    {
        if (icon != null) icon.sprite = mineral.Icon;
        if (quantityText != null) quantityText.text = quantity.ToString();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
        }
    }
}
