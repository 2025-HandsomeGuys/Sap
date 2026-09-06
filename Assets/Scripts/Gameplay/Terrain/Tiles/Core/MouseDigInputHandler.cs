// @tags: input, mouse, digging, input-handler
using UnityEngine;

/// <summary>
/// 마우스 입력 기반 채굴 핸들러
/// </summary>
public class MouseDigInputHandler : IDigInputHandler
{
    public bool IsDigRequested()
    {
        // 화면 터치/클릭 감지
        return Input.GetMouseButtonDown(0);
    }

    public Vector2 GetTargetPosition()
    {
        if (Camera.main == null) return Vector2.zero;
        return Camera.main.ScreenToWorldPoint(Input.mousePosition);
    }
}
