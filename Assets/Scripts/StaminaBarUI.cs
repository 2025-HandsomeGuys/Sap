using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StaminaBar : MonoBehaviour
{
    public PlayerStats player;

    [Header("UI References")]
    public RectTransform fill;          // �����
    public RectTransform used;          // ������
    public RectTransform maxReduction;  // ��ȫ��
    public TextMeshProUGUI staminaText;

    private float usableAreaWidth;

    void Start()
    {
        usableAreaWidth = fill.parent.GetComponent<RectTransform>().rect.width;
    }

    void Update()
    {
        UpdateUI();
    }

    void UpdateUI()
    {
        float maxRatio = player.maxStamina / player.originalMaxStamina;
        float currentRatio = player.currentStamina / player.originalMaxStamina;
        float usedRatio = (player.maxStamina - player.currentStamina) / player.originalMaxStamina;

        // Fill (�����)
        fill.sizeDelta = new Vector2(currentRatio * usableAreaWidth, fill.sizeDelta.y);

        // Used (������)
        used.sizeDelta = new Vector2(usedRatio * usableAreaWidth, used.sizeDelta.y);
        used.anchoredPosition = new Vector2(fill.sizeDelta.x, used.anchoredPosition.y);

        // MaxReduction (��ȫ��)
        float reductionRatio = 1f - maxRatio;
        maxReduction.sizeDelta = new Vector2(reductionRatio * usableAreaWidth, maxReduction.sizeDelta.y);
        maxReduction.anchoredPosition = new Vector2(fill.sizeDelta.x + used.sizeDelta.x, maxReduction.anchoredPosition.y);

        // �ؽ�Ʈ
        staminaText.text = $"{player.currentStamina} / {player.maxStamina}";
    }
}
