using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 개별 활성 효과(버프)의 아이콘과 남은 시간을 표시하는 UI 컴포넌트입니다.
/// </summary>
public class ActiveEffectUIItem : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private Image radialFill; // 선택사항: 쿨다운 형태의 연출용

    private float remainingTime;
    private float totalDuration;

    public void Initialize(Sprite icon, float duration)
    {
        if (iconImage != null) iconImage.sprite = icon;
        
        remainingTime = duration;
        totalDuration = duration;
        
        UpdateUI();
    }

    private void Update()
    {
        if (remainingTime > 0)
        {
            remainingTime -= Time.deltaTime;
            UpdateUI();
        }
    }

    private void UpdateUI()
    {
        // 타이머 텍스트 업데이트 (소수점 1자리까지 표시하거나 정수로 표시)
        if (timerText != null)
        {
            if (remainingTime > 60f)
            {
                int minutes = Mathf.FloorToInt(remainingTime / 60f);
                int seconds = Mathf.FloorToInt(remainingTime % 60f);
                timerText.text = $"{minutes}:{seconds:D2}";
            }
            else
            {
                timerText.text = $"{Mathf.Max(0, remainingTime):F1}s";
            }
        }

        // 라디얼 필 업데이트 (선택사항)
        if (radialFill != null && totalDuration > 0)
        {
            radialFill.fillAmount = remainingTime / totalDuration;
        }
    }
}
