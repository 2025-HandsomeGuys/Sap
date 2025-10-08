using UnityEngine;
using TMPro;
using UnityEngine.Events;

public class QuantityPrompt : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI titleText; 
    [SerializeField] private TextMeshProUGUI infoText; // 선택(없으면 null 허용)
    [SerializeField] private TMP_InputField inputField;
    private int min = 1;
    private int max = 1;
    private UnityAction<int> onConfirm;

    public void Show(string title, int minValue, int maxValue, UnityAction<int> confirmCallback, string info = null)
    {
        gameObject.SetActive(true);

        titleText.text = title;
        if (infoText != null) infoText.text = info ?? string.Empty;

        min = Mathf.Max(1, minValue);
        max = Mathf.Max(min, maxValue);

        inputField.contentType = TMP_InputField.ContentType.IntegerNumber;
        inputField.text = max.ToString(); // 기본값: 최대
        onConfirm = confirmCallback;

        // 첫 키 입력 편하도록 포커스
        inputField.Select();
        inputField.ActivateInputField();
    }

    public void OnClickConfirm()
    {
        int value;
        if (!int.TryParse(inputField.text, out value))
            value = min;

        value = Mathf.Clamp(value, min, max);

        onConfirm?.Invoke(value);
        Close();
    }

    public void OnClickCancel()
    {
        Close();
    }

    private void Close()
    {
        gameObject.SetActive(false);
        onConfirm = null;
    }
}
