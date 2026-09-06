using UnityEngine;
using UnityEngine.UI;

public class MiningChargeBarUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("PlayerMining 컴포넌트 연결 (비워두면 부모에서 찾음)")]
    public PlayerMining playerMining;
    
    [Tooltip("게이지 바 전체를 감싸는 부모 오브젝트 (활성/비활성 제어용)")]
    public GameObject barContainer; 
    
    [Tooltip("게이지 바 배경 이미지")]
    public Image backgroundImage;
    
    [Tooltip("충전량을 표시하고 색상을 변경할 Fill 이미지 (Image Type을 Filled로 설정)")]
    public Image fillImage;

    [Header("Settings")]
    public Color validColor = Color.green;   // 채굴 가능 (>= 30%)
    public Color invalidColor = Color.red;   // 채굴 불가 (< 30%)
    public float validThreshold = 0.3f;      // PickaxeStrategy와 동일하게 설정

    [Header("Full Charge Settings")]
    [Tooltip("풀 차징(100%) 시 바뀔 색상")]
    public Color fullChargeColor = new Color(1f, 0.8f, 0f, 1f); // 황금/노란색
    [Tooltip("반짝임 속도 (숫자가 클수록 빠름)")]
    public float blinkSpeed = 10f;
    [Tooltip("반짝임 강도 (1에 가까울수록 하얀색에 가까워짐)")]
    public float blinkIntensity = 0.6f;

    [Header("Position Settings")]
    [Tooltip("마우스가 왼쪽(플레이어가 왼쪽 바라봄) 일 때 플레이어 기준 오프셋.\nX 양수 = 오른쪽, 마우스가 오른쪽이면 X가 자동 반전되어 왼쪽에 배치됩니다.")]
    public Vector3 worldOffset = new Vector3(0.5f, 0.1f, 0);
    
    [Tooltip("활성화 시 플레이어 위치를 매 프레임 따라다닙니다. (Canvas가 Player의 자식이 아닐 때 사용)")]
    public bool followPlayer = true;

    private Transform _targetTransform;
    private RectTransform _rectTransform;
    private Canvas _canvas;
    
    // 마우스가 오른쪽을 향하는지 캐싱 (LateUpdate에서 위치와 반전 동시 처리)
    private bool _isMouseRight;

    void Start()
    {
        _rectTransform = GetComponent<RectTransform>();
        
        if (_rectTransform == null)
        {
            Debug.LogError($"MiningChargeBarUI: '{gameObject.name}'에 RectTransform이 없습니다.");
            return;
        }

        _canvas = GetComponentInParent<Canvas>();
        
        // Canvas를 찾지 못하더라도 일단 진행하도록 허용 (Update에서 널 체크 추가됨)
            
        if (_canvas.renderMode == RenderMode.WorldSpace)
        {
            if (_canvas.worldCamera == null)
            {
                Camera mainCamera = Camera.main ?? FindFirstObjectByType<Camera>();
                if (mainCamera != null)
                {
                    _canvas.worldCamera = mainCamera;
                    Debug.Log($"MiningChargeBarUI: World Space Canvas에 Main Camera를 설정했습니다.");
                }
                else
                {
                    Debug.LogWarning($"MiningChargeBarUI: World Space Canvas를 사용하지만 Camera를 찾을 수 없습니다.");
                }
            }
            
            if (_canvas.GetComponent<GraphicRaycaster>() == null)
                Debug.LogWarning($"MiningChargeBarUI: Canvas에 GraphicRaycaster가 없습니다.");
        }

        if (playerMining == null) 
            playerMining = GetComponentInParent<PlayerMining>();
            
        if (playerMining != null)
            _targetTransform = playerMining.transform;

        if (barContainer != null) 
            barContainer.SetActive(false);
    }

    void Update()
    {
        if (playerMining == null) return;

        bool isCharging = playerMining.IsCharging;
        
        if (isCharging)
        {
            float ratio = playerMining.GetCurrentChargeRatio();
            
            if (barContainer != null && !barContainer.activeSelf) 
            {
                barContainer.SetActive(true);
            }

            // 마우스가 플레이어 기준 오른쪽인지 매 프레임 갱신
            if (_targetTransform != null)
            {
                _isMouseRight = IsMouseOnRight();
            }

            if (fillImage != null)
            {
                fillImage.fillAmount = ratio;

                if (ratio >= 1.0f)
                {
                    float pingPong = Mathf.PingPong(Time.time * blinkSpeed, blinkIntensity);
                    fillImage.color = Color.Lerp(fullChargeColor, Color.white, pingPong);
                }
                else
                {
                    fillImage.color = (ratio >= validThreshold) ? validColor : invalidColor;
                }
            }
        }
        else
        {
            if (barContainer != null && barContainer.activeSelf) 
                barContainer.SetActive(false);
        }
    }

    void LateUpdate()
    {
        if (!followPlayer || _rectTransform == null || _targetTransform == null || barContainer == null || !barContainer.activeSelf)
            return;

        // 마우스 방향에 따라 오프셋 X 반전:
        //   마우스가 왼쪽 → 플레이어 왼편(기본 오프셋)
        //   마우스가 오른쪽 → 플레이어 오른편(X 반전)
        Vector3 offset = worldOffset;
        float scaleX;

        if (_isMouseRight)
        {
            offset.x = -worldOffset.x;   // 반대편으로 이동
            scaleX = -1f;                 // 바 이미지 자체도 좌우 반전
        }
        else
        {
            scaleX = 1f;
        }

        // barContainer 좌우 반전 적용
        Vector3 containerScale = barContainer.transform.localScale;
        containerScale.x = scaleX;
        barContainer.transform.localScale = containerScale;

        // 위치 계산 (캔버스 모드에 따라 분기)
        if (_canvas != null && (_canvas.renderMode == RenderMode.ScreenSpaceOverlay || _canvas.renderMode == RenderMode.ScreenSpaceCamera))
        {
            Camera cam = (_canvas.renderMode == RenderMode.ScreenSpaceCamera && _canvas.worldCamera != null)
                ? _canvas.worldCamera
                : Camera.main;

            if (cam != null)
            {
                Vector3 worldPos = _targetTransform.position + offset;
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                _rectTransform.position = screenPos;
            }
        }
        else
        {
            // World Space Canvas
            _rectTransform.position = _targetTransform.position + offset;
        }
    }

    /// <summary>
    /// 마우스 월드 좌표가 플레이어보다 오른쪽에 있으면 true
    /// </summary>
    private bool IsMouseOnRight()
    {
        Camera cam = Camera.main;
        if (cam == null) return false;

        Vector3 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
        return mouseWorld.x > _targetTransform.position.x;
    }
}
