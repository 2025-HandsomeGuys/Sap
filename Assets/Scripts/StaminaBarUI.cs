using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StaminaBar : MonoBehaviour
{
    public PlayerStats player;

    [Header("UI References")]
    public RectTransform fill;          // 노란색
    public RectTransform used;          // 검정색
    public RectTransform maxReduction;  // 분홍색
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

        // Fill (노란색)
        fill.sizeDelta = new Vector2(currentRatio * usableAreaWidth, fill.sizeDelta.y);

        // Used (검정색)
        used.sizeDelta = new Vector2(usedRatio * usableAreaWidth, used.sizeDelta.y);
        used.anchoredPosition = new Vector2(fill.sizeDelta.x, used.anchoredPosition.y);

        // MaxReduction (분홍색)
        float reductionRatio = 1f - maxRatio;
        maxReduction.sizeDelta = new Vector2(reductionRatio * usableAreaWidth, maxReduction.sizeDelta.y);
        maxReduction.anchoredPosition = new Vector2(fill.sizeDelta.x + used.sizeDelta.x, maxReduction.anchoredPosition.y);

        // 텍스트
        staminaText.text = $"{player.currentStamina} / {player.maxStamina}";
    }
}
