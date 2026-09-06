using UnityEngine;

/// <summary>
/// FlashlightController의 Raycast 데이터를 바탕으로
/// 스텐실 마스크용 폴리곤 메쉬를 동적으로 생성하는 클래스.
/// 생성된 메쉬는 'Custom/FlashlightFOVMask' 셰이더를 통해 스텐실 버퍼에 1을 기록하며,
/// 이후 그려지는 RadialVignette 셰이더가 이 영역을 제외하고 어둠을 그립니다.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class FlashlightFOVBuilder : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Raycast 데이터를 제공할 조명 컨트롤러")]
    public FlashlightController flashlightController;
    
    [Tooltip("Custom/FlashlightFOVMask 가 할당된 머티리얼")]
    public Material fovMaskMaterial;

    private Mesh _mesh;
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;

    private Vector3[] _vertices;
    private int[] _triangles;

    void Start()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();
        
        _mesh = new Mesh();
        _mesh.name = "FlashlightFOVMesh";
        _mesh.MarkDynamic(); // 매 프레임 업데이트 빈도가 높으므로 Dynamic 마킹
        _meshFilter.mesh = _mesh;

        if (fovMaskMaterial != null)
        {
            _meshRenderer.material = fovMaskMaterial;
            _meshRenderer.sortingLayerName = "Default";
            _meshRenderer.sortingOrder = 900; // UI Canvas(999) 보다 먼저 그려지도록 보장
        }
        else
        {
            Debug.LogWarning("[FlashlightFOVBuilder] FOV Mask Material이 할당되지 않았습니다. Inspector에서 할당해주세요.");
        }

        if (flashlightController == null)
        {
            flashlightController = GetComponentInParent<FlashlightController>();
            if (flashlightController == null)
                flashlightController = FindFirstObjectByType<FlashlightController>();
        }
    }

    void LateUpdate()
    {
        if (flashlightController == null || !flashlightController.IsActive)
        {
            _meshRenderer.enabled = false;
            return;
        }

        _meshRenderer.enabled = true;
        BuildMesh();
    }

    void BuildMesh()
    {
        int rayCount = flashlightController.RayCount;
        float[] distances = flashlightController.GetRayDistances();
        
        if (_vertices == null || _vertices.Length != rayCount + 1)
        {
            _vertices = new Vector3[rayCount + 1];
            _triangles = new int[(rayCount - 1) * 3];
        }

        Vector3 origin = flashlightController.Position;
        
        // 메쉬의 World Position을 정확하게 그리기 위해, GameObject의 Transform은 월드 중심(0,0,0)으로 고정하되,
        // Canvas(Z=10)와 Camera(Z=-10 등) 사이에서 확실히 렌더링되게 하기 위해 Z값을 5로 설정합니다.
        transform.position = new Vector3(0, 0, 5f);
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        float angle = flashlightController.Angle;
        Vector3 dir = flashlightController.Direction;
        float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float halfAngle = angle / 2f;
        float angleStep = angle / (rayCount - 1);

        // 원점 (플레이어/손전등의 위치)
        _vertices[0] = origin;

        for (int i = 0; i < rayCount; i++)
        {
            float currentAngleDeg = baseAngle - halfAngle + (angleStep * i);
            float currentAngleRad = currentAngleDeg * Mathf.Deg2Rad;
            
            float dist = Mathf.Max(0f, distances[i]);
            
            Vector3 point = origin + new Vector3(Mathf.Cos(currentAngleRad), Mathf.Sin(currentAngleRad), 0f) * dist;
            _vertices[i + 1] = point;
            
            if (i < rayCount - 1)
            {
                _triangles[i * 3] = 0;
                _triangles[i * 3 + 1] = i + 1;
                _triangles[i * 3 + 2] = i + 2;
            }
        }

        _mesh.Clear();
        _mesh.vertices = _vertices;
        _mesh.triangles = _triangles;
        
        // 카메라 범위 판정을 위한 Bounds 갱신
        _mesh.RecalculateBounds(); 
    }
}
