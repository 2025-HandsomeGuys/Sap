// [임시 디버그용] 사용 후 삭제하세요.
// 아무 씬의 빈 GameObject에 붙이거나 InventoryUI 오브젝트에 임시 추가 후 Play.
// Console에서 "[RaycastDebug]" 로그로 현재 마우스가 감지하는 UI 계층을 확인할 수 있습니다.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class RaycastDebugger : MonoBehaviour
{
    private float _logTimer;
    private const float LOG_INTERVAL = 1f; // 1초마다 로그

    void Update()
    {
        _logTimer += Time.unscaledDeltaTime;
        if (_logTimer < LOG_INTERVAL) return;
        _logTimer = 0f;

        if (EventSystem.current == null)
        {
            Debug.LogError("[RaycastDebug] EventSystem이 없습니다!");
            return;
        }

        var results = new List<RaycastResult>();
        var pointerData = new PointerEventData(EventSystem.current)
        {
            position = Input.mousePosition
        };

        EventSystem.current.RaycastAll(pointerData, results);

        if (results.Count == 0)
        {
            Debug.Log("[RaycastDebug] 마우스 아래 감지된 UI 없음");
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[RaycastDebug] 마우스({Input.mousePosition.x:F0},{Input.mousePosition.y:F0}) 아래 UI 계층 ({results.Count}개):");
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine($"  [{i}] {GetHierarchyPath(r.gameObject)} | sortingOrder={r.sortingOrder} | depth={r.depth}");
        }
        Debug.Log(sb.ToString());
    }

    private string GetHierarchyPath(GameObject go)
    {
        if (go == null) return "(null)";
        string path = go.name;
        Transform t = go.transform.parent;
        int limit = 5;
        while (t != null && limit-- > 0)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }
}
