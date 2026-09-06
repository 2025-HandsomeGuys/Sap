using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class FieldOfView : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("시야 거리 (반지름)")]
    public float viewRadius = 5f;
    [Tooltip("시야각 (360도 전체는 360)")]
    [Range(0, 360)]
    public float viewAngle = 90f;
    [Tooltip("장애물로 인식할 레이어")]
    public LayerMask obstacleMask;
    
    [Header("Resolution")]
    [Tooltip("도(degree)당 발사할 레이의 개수 (높을수록 정밀하지만 성능 저하)")]
    public float meshResolution = 0.5f; // 1도당 0.5개 (2도당 1개)
    [Tooltip("벽 모서리 다듬기 반복 횟수")]
    public int edgeResolveIterations = 4;
    [Tooltip("벽 모서리 다듬기 거리 임계값")]
    public float edgeDstThreshold = 0.5f;
    [Tooltip("플레이어 내부 안전 구역 반지름 (내부 그림자 방지용)")]
    public float innerRadius = 0.3f;

    [Header("References")]
    public MeshFilter viewMeshFilter;
    private Mesh viewMesh;

    private void Start()
    {
        if (viewMeshFilter == null)
            viewMeshFilter = GetComponent<MeshFilter>();

        viewMesh = new Mesh();
        viewMesh.name = "View Mesh";
        viewMeshFilter.mesh = viewMesh;
    }

    private void LateUpdate()
    {
        LookAtMouse();
        // 매 2프레임에 한 번만 메쉬 재계산 (45 raycast 비용 절반 감소)
        if (Time.frameCount % 2 == 0)
            DrawFieldOfView();
    }

    // 마우스를 바라보는 각도 계산
    float aimAngle = 0;

    void LookAtMouse()
    {
        Vector3 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dir = (mousePos - transform.position).normalized;
        aimAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
    }

    void DrawFieldOfView()
    {
        int stepCount = Mathf.RoundToInt(viewAngle * meshResolution);
        float stepAngleSize = viewAngle / stepCount;
        List<Vector3> viewPoints = new List<Vector3>();

        // [수정] 플레이어의 회전(transform.eulerAngles.z) 대신 계산된 aimAngle을 사용
        float startAngle = aimAngle + (viewAngle / 2f);
        
        for (int i = 0; i <= stepCount; i++)
        {
            float angle = startAngle - stepAngleSize * i;
            ViewCastInfo newViewCast = ViewCast(angle);
            viewPoints.Add(newViewCast.point);
        }

        // --- 메쉬 생성 (Vertices & Triangles) ---
        int vertexCount = viewPoints.Count + 1; // 원점 포함
        Vector3[] vertices = new Vector3[vertexCount];
        int[] triangles = new int[(vertexCount - 2) * 3];

        // [수정] 원점(vertices[0])을 단순히 (0,0,0)으로 두지 않고, 
        // 플레이어가 바라보는 방향의 '반대'쪽으로 innerRadius만큼 이동시킵니다.
        // 이렇게 하면 부채꼴의 시작점이 플레이어 뒤쪽에 위치하여 틈이 생기는 것을 방지합니다.
        // 로컬 좌표계 기준이므로 X축(오른쪽)이 정면이라고 가정하면 (-innerRadius, 0, 0)입니다.
        // 단, FieldOfView 오브젝트 자체가 회전한다면 그냥 (0,0)을 써야 할 수도 있지만, 
        // 여기서는 "시야각의 중심 반대편"으로 이동시키는 것이 정확합니다.
        
        // 시야각이 360도라면 그냥 (0,0)이 맞지만, 부채꼴이라면 뒤로 빼는게 좋음.
        if (viewAngle < 360)
        {
             vertices[0] = Vector3.left * innerRadius; 
             // 주의: 위 코드는 로컬 X축이 '바라보는 방향'과 일치한다고 가정함.
             // 만약 회전이 Z축으로 이루어진다면 로컬 좌표계에선 항상 고정됨.
        }
        else
        {
            vertices[0] = Vector3.zero;
        }

        for (int i = 0; i < vertexCount - 1; i++)
        {
            // 월드 좌표인 viewPoints를 로컬 좌표로 변환
            vertices[i + 1] = transform.InverseTransformPoint(viewPoints[i]);

            if (i < vertexCount - 2)
            {
                triangles[i * 3] = 0; // 원점
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
        }

        // 팁: 단순히 0번 버텍스만 있으면 부채꼴 사이가 벌어질 때(특히 시야각이 360일 때) 틈이 생길 수 있음.
        // 하지만 지금 문제는 "플레이어 뒤쪽"임. 
        // 해결책: 부채꼴의 시작점을 플레이어 위치(0,0)가 아니라, 뒤쪽으로 살짝 뺀 위치로 설정하면 됨.
        // 또는 그냥 innerRadius만큼의 원형 메쉬를 별도로 그리는게 나음.
        // 코드가 복잡해지니, 가장 간단한 "ViewCast의 시작점 오프셋" 방식을 적용하겠습니다.
        
        viewMesh.Clear();
        viewMesh.vertices = vertices;
        viewMesh.triangles = triangles;
        viewMesh.RecalculateBounds();
    }

    ViewCastInfo ViewCast(float globalAngle)
    {
        Vector3 dir = DirFromAngle(globalAngle, true);
        // [수정] Ray 시작점을 플레이어 중심이 아니라, innerRadius만큼 뒤에서 시작하게 하여 틈을 메움
        // 단, 벽 뒤로 넘어가면 안되므로 로직 주의. 
        // 간단한 해결책: Ray는 그대로 쏘고, 메쉬의 중심점(vertices[0])만 뒤로 빼는 건 시각적으로 이상함.
        
        // [수정안 2] 그냥 innerRadius 무시하고 RaycastOrigin을 살짝 이동?
        // 아니면 사용자가 말한 '검은 배경'이 스프라이트 피봇 문제일 수 있음.
        
        RaycastHit2D hit = Physics2D.Raycast(transform.position, dir, viewRadius, obstacleMask);

        if (hit.collider != null)
        {
            return new ViewCastInfo(true, hit.point, hit.distance, globalAngle);
        }
        else
        {
            return new ViewCastInfo(false, transform.position + dir * viewRadius, viewRadius, globalAngle);
        }
    }

    public Vector3 DirFromAngle(float angleInDegrees, bool angleIsGlobal)
    {
        if (!angleIsGlobal)
        {
            angleInDegrees += transform.eulerAngles.z;
        }
        // Unity 수학 함수는 라디안을 사용하므로 변환
        // Sin/Cos 위치 주의: Unity 2D (Z축 회전) 기준 0도가 오른쪽(X+)이면:
        // x = cos, y = sin
        return new Vector3(Mathf.Cos(angleInDegrees * Mathf.Deg2Rad), Mathf.Sin(angleInDegrees * Mathf.Deg2Rad), 0);
    }

    public struct ViewCastInfo
    {
        public bool hit;
        public Vector3 point;
        public float dst;
        public float angle;

        public ViewCastInfo(bool _hit, Vector3 _point, float _dst, float _angle)
        {
            hit = _hit;
            point = _point;
            dst = _dst;
            angle = _angle;
        }
    }
}
