using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// UI 클릭 디버거 - 버튼에 붙여서 클릭 이벤트가 제대로 전달되는지 확인
/// </summary>
public class UIClickDebugger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerClickHandler
{
    public void OnPointerEnter(PointerEventData eventData)
    {
        Debug.Log($"[UIClickDebugger] Mouse ENTERED: {gameObject.name}");
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Debug.Log($"[UIClickDebugger] Mouse EXITED: {gameObject.name}");
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        Debug.Log($"[UIClickDebugger] Mouse DOWN on: {gameObject.name}");
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"[UIClickDebugger] Mouse CLICKED: {gameObject.name} ✅");
    }

    void Update()
    {
        // 마우스가 이 오브젝트 위에 있는지 매 프레임 체크
        if (RectTransformUtility.RectangleContainsScreenPoint(
            GetComponent<RectTransform>(), 
            Input.mousePosition, 
            null))
        {
            if (Input.GetMouseButtonDown(0))
            {
                Debug.LogWarning($"[UIClickDebugger] Raw mouse click detected on {gameObject.name} but event system might not be triggering!");
            }
        }
    }
}
