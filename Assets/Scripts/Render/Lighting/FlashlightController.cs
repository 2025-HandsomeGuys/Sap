using UnityEngine;

/// <summary>
/// 손전등 빛 시스템 - ILightSource를 구현하여 플레이어에서 마우스 방향으로 물리적 광원 데이터 제공
/// (렌더링은 PlayerVisionOverlay 등에서 처리)
/// </summary>
public class FlashlightController : MonoBehaviour, ILightSource
{
    [Header("Settings")]
    [Tooltip("빛의 최대 거리")]
    public float maxLightDistance = 15f;
    
    [Tooltip("빛의 원뿔 각도 (도 단위)")]
    [Range(10f, 90f)]
    public float coneAngle = 30f;
    
    [Tooltip("스텐실 마스크 생성을 위해 실제 시야각보다 확장할 각도 (가장자리 부드러운 처리를 위함)")]
    public float paddingAngle = 45f;

    [Tooltip("빛이 시작되는 내경 (플레이어 주변 시야와 겹침 방지)")]
    public float innerRadius = 2f;
    
    [Header("References")]
    [Tooltip("플레이어 Transform")]
    public Transform playerTransform;
    
    [Header("Layer Settings")]
    [Tooltip("땅으로 인식할 레이어")]
    public LayerMask terrainLayer;

    [Tooltip("그림자를 만들지 않을 레이어 (예: groundmineral, mineral)")]
    public LayerMask excludeFromShadow;

    private Camera mainCamera;
    
    // ILightSource 구현부
    public bool IsActive => enabled && gameObject.activeInHierarchy;
    
    public Vector3 Position 
    {
        get 
        {
            if (playerTransform != null)
                return new Vector3(playerTransform.position.x, playerTransform.position.y, 0f);
            return Vector3.zero;
        }
    }
    
    public Vector3 Direction { get; private set; } = Vector3.right;
    
    public float MaxDistance => maxLightDistance;
    
    public float Angle => coneAngle;

    public float InnerRadius => innerRadius;
    
    public float RaycastAngle => coneAngle + paddingAngle;
    
    public int RayCount => RAY_COUNT;

    private const int RAY_COUNT = 128;
    private float[] rayDistances = new float[RAY_COUNT];

    public float[] GetRayDistances()
    {
        return rayDistances;
    }

    void Start()
    {
        mainCamera = Camera.main;
        
        if (playerTransform == null)
        {
            Debug.LogError("[FlashlightController] Player Transform이 할당되지 않았습니다!");
            enabled = false;
            return;
        }
        
        if (mainCamera == null)
        {
            Debug.LogError("[FlashlightController] Main Camera를 찾을 수 없습니다!");
            enabled = false;
            return;
        }

        // 초기 거리 배열 초기화
        for (int i = 0; i < RAY_COUNT; i++)
        {
            rayDistances[i] = maxLightDistance;
        }
    }

    void Update()
    {
        if (playerTransform == null || mainCamera == null) return;
        
        UpdateFlashlightData();
    }

    void UpdateFlashlightData()
    {
        // 마우스 드 좌표 얻기
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0f;
        
        Vector3 playerPos = Position;
        
        // 플레이어에서 마우스로의 방향
        Direction = (mouseWorldPos - playerPos).normalized;
        if (Direction == Vector3.zero) Direction = Vector3.right;
        
        // 기준 각도 (마우스 방향)
        float baseAngle = Mathf.Atan2(Direction.y, Direction.x) * Mathf.Rad2Deg;
        float halfConeAngle = RaycastAngle / 2f;
        float angleStep = RaycastAngle / (RAY_COUNT - 1);
        
        // 여러 개의 레이를 쏘아 거리 데이터 수집
        for (int i = 0; i < RAY_COUNT; i++)
        {
            float currentAngleDeg = baseAngle - halfConeAngle + (angleStep * i);
            float currentAngleRad = currentAngleDeg * Mathf.Deg2Rad;
            
            Vector3 rayDir = new Vector3(Mathf.Cos(currentAngleRad), Mathf.Sin(currentAngleRad), 0f);
            
            RaycastHit2D hit = Physics2D.Raycast(playerPos, rayDir, maxLightDistance, terrainLayer & ~excludeFromShadow);
            
            if (hit.collider != null)
            {
                rayDistances[i] = hit.distance;
            }
            else
            {
                rayDistances[i] = maxLightDistance;
            }
        }
    }

    void OnDrawGizmos()
    {
        // 에디터에서 디버그용 기즈모
        if (playerTransform != null && mainCamera != null && Application.isPlaying)
        {
            Vector3 playerPos = Position;
            
            // Raycast 중앙선 시각화
            RaycastHit2D hit = Physics2D.Raycast(playerPos, Direction, maxLightDistance, terrainLayer & ~excludeFromShadow);
            
            if (hit.collider != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(playerPos, hit.point);
                Gizmos.DrawWireSphere(hit.point, 0.2f);
            }
            else
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(playerPos, playerPos + Direction * maxLightDistance);
            }
        }
    }
}
