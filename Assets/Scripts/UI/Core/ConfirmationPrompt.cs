using UnityEngine;
using TMPro;
using UnityEngine.Events;

/// <summary>
/// 확인/취소 선택을 제공하는 프롬프트
/// 예: 판매 확인, 삭제 확인 등
/// </summary>
public class ConfirmationPrompt : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI titleText; 
    [SerializeField] private TextMeshProUGUI messageText; // 확인 메시지
    private UnityAction onConfirm;
    private UnityAction onCancel;

    /// <summary>
    /// 확인 프롬프트 표시
    /// </summary>
    /// <param name="title">프롬프트 제목</param>
    /// <param name="message">확인 메시지</param>
    /// <param name="confirmCallback">확인 버튼 클릭 시 실행될 콜백</param>
    /// <param name="cancelCallback">취소 버튼 클릭 시 실행될 콜백 (선택사항)</param>
    public void Show(string title, string message, UnityAction confirmCallback, UnityAction cancelCallback = null)
    {
        gameObject.SetActive(true);

        titleText.text = title;
        messageText.text = message;
        onConfirm = confirmCallback;
        onCancel = cancelCallback;
    }

    public void OnClickConfirm()
    {
        UnityAction callback = onConfirm;
        Close();
        callback?.Invoke();
    }

    public void OnClickCancel()
    {
        onCancel?.Invoke();
        Close();
    }

    private void Close()
    {
        gameObject.SetActive(false);
        onConfirm = null;
        onCancel = null;
    }
}
