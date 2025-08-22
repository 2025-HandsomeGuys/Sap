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
        // 부모 바의 전체 폭 (originalMaxStamina 기준)
        //usableAreaWidth = fill.parent.GetComponent<RectTransform>().rect.width;

        // 각 비율 계산
        float currentRatio = player.currentStamina / player.originalMaxStamina;
        float usedRatio = (player.maxStamina - player.currentStamina) / player.originalMaxStamina;
        float reductionRatio = (player.originalMaxStamina - player.maxStamina) / player.originalMaxStamina;

        // Fill (노란색: 현재 사용 가능)
        fill.sizeDelta = new Vector2(currentRatio * usableAreaWidth, fill.sizeDelta.y);
        fill.anchoredPosition = new Vector2(0, fill.anchoredPosition.y);

        // Used (검정색: 임시 회복 가능)
        used.sizeDelta = new Vector2(usedRatio * usableAreaWidth, used.sizeDelta.y);
        used.anchoredPosition = new Vector2(fill.sizeDelta.x, used.anchoredPosition.y);

        // MaxReduction (보라색: 절대 회복 불가)
        maxReduction.sizeDelta = new Vector2(reductionRatio * usableAreaWidth, maxReduction.sizeDelta.y);
        maxReduction.anchoredPosition = new Vector2(fill.sizeDelta.x + used.sizeDelta.x, maxReduction.anchoredPosition.y);

        // 텍스트
        staminaText.text = $"{player.currentStamina} / {player.maxStamina}";
    }
}
