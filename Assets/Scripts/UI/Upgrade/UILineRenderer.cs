using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 두 UI 요소 사이를 연결하는 선을 그립니다.
/// RectTransform을 회전하고 크기를 조절하여 구현합니다.
/// </summary>
public class UILineRenderer : MonoBehaviour
{
    public float lineWidth = 5f;
    public Color lineColor = Color.white;
    private Image _lineImage;
    private RectTransform _rectTransform;

    void Awake()
    {
        _lineImage = GetComponent<Image>();
        if (_lineImage == null)
        {
            _lineImage = gameObject.AddComponent<Image>();
        }
        _rectTransform = GetComponent<RectTransform>();
    }

    public void SetColor(Color color)
    {
        if (_lineImage != null) _lineImage.color = color;
    }

    /// <summary>
    /// 두 점(Screen Space가 아닌 Local/AnchoredPosition 기준) 사이를 연결합니다.
    /// 부모 RectTransform 기준의 좌표를 받아 그려야 정확합니다.
    /// </summary>
    public void DrawLine(Vector2 startPos, Vector2 endPos)
    {
        if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();
        
        Vector2 dir = (endPos - startPos).normalized;
        float distance = Vector2.Distance(startPos, endPos);

        // 위치 설정 (중간 지점)
        _rectTransform.anchoredPosition = startPos + dir * distance * 0.5f;
        
        // 크기 설정 (너비는 거리, 높이는 선 두께)
        _rectTransform.sizeDelta = new Vector2(distance, lineWidth);
        
        // 회전 설정
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        _rectTransform.localRotation = Quaternion.Euler(0, 0, angle);
    }
}
