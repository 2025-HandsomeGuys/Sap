using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class FlashlightFOVGraphic : Graphic
{
    public FlashlightController flashlightController;
    
    // UI 요소는 RectTransform 중심을 기준으로 그려집니다.
    // 플레이어 좌표를 Canvas 로컬 좌표계로 맞추는 것이 필요합니다.

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (flashlightController == null || !flashlightController.IsActive)
            return;

        UIVertex vert = UIVertex.simpleVert;
        int rayCount = flashlightController.RayCount;
        float[] distances = flashlightController.GetRayDistances();
        
        Vector3 origin = flashlightController.Position;
        
        // 캔버스 내에서의 월드 좌표를 로컬로 변환
        Vector3 localOrigin = transform.InverseTransformPoint(origin);
        localOrigin.z = 0f;

        float angle = flashlightController.Angle;
        if (flashlightController is FlashlightController fc)
        {
            angle = fc.RaycastAngle;
        }
        Vector3 dir = flashlightController.Direction;
        float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float halfAngle = angle / 2f;
        float angleStep = angle / (rayCount - 1);
        
        // 중심점 추가
        vert.position = localOrigin;
        vert.color = color;
        vh.AddVert(vert);

        for (int i = 0; i < rayCount; i++)
        {
            float currentAngleDeg = baseAngle - halfAngle + (angleStep * i);
            float currentAngleRad = currentAngleDeg * Mathf.Deg2Rad;
            
            float dist = Mathf.Max(0f, distances[i]);
            
            Vector3 worldPoint = origin + new Vector3(Mathf.Cos(currentAngleRad), Mathf.Sin(currentAngleRad), 0f) * dist;
            Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
            localPoint.z = 0f;
            
            vert.position = localPoint;
            vert.color = color;
            vh.AddVert(vert);
            
            if (i < rayCount - 1)
            {
                // 중심점(0)과 두 정점을 연결하여 삼각형 생성
                vh.AddTriangle(0, i + 1, i + 2);
            }
        }
    }

    void Update()
    {
        // 매 프레임 메쉬 재구축
        SetVerticesDirty();
    }
}
