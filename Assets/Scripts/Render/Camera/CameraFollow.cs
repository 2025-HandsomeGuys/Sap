using UnityEngine;
using Unity.Cinemachine; // Cinemachine 3.0 기능을 사용하기 위해 필수 추가

public class CameraFollow : MonoBehaviour
{
    public Transform target; // 따라갈 대상 (플레이어)

    // 카메라 이동 제한 범위 (Inspector에서 설정)
    public float minX; // 왼쪽 끝
    public float maxX; // 오른쪽 끝
    public float minY; // 아래쪽 끝 (필요 없으면 무시)
    public float maxY; // 위쪽 끝 (필요 없으면 무시)

    [Header("드릴 줌")]
    public float normalOrthoSize = 2.5f;   // 평상시 orthographicSize (사용자 설정에 맞춰 2.5로 조정)
    public float drillOrthoSize  = 1.8f;   // 드릴 대시 중 줌 인 크기 (비율 유지)
    public float uiZoomOrthoSize = 2.0f;   // UI 오픈 시 줌 인 크기 (사용자 설정에 맞춰 수정)
    public float zoomSmoothTime  = 0.25f; // 줌 전환 속도

    [Header("Cinemachine")]
    public CinemachineCamera virtualCamera; // 제어할 가상 카메라

    [Header("반중력 카메라 플립")]
    [Tooltip("반중력(역중력) 위상에서 카메라를 180° 롤(Dutch)로 뒤집는 전환 속도")]
    public float flipSmoothTime = 0.25f;

    private Camera  _cam;
    private float   _zoomVelocity;
    private float   _targetOrthoSize;
    private bool    _hasCinemachineInScene; // 씬 내에 시네머신이 존재하는지 여부

    // 반중력 카메라 롤: 목표 0°(정상) 또는 180°(뒤집힘). Lens.Dutch로 보간 적용.
    private float   _targetDutch;
    private float   _dutchVelocity;

    // [추가] 줌 상태 플래그
    private bool    _isUIZoomActive;
    private bool    _isDrillZoomActive;

    // FindVirtualCamera 반복 호출 방지
    private int     _findCameraFailCount;
    private const int MaxFindRetries = 3;

    public static CameraFollow Instance { get; private set; }

    void Awake()
    {
        // 싱글톤 인스턴스 설정
        if (Instance == null) Instance = this;
        
        _cam = GetComponent<Camera>();
        if (_cam == null) _cam = Camera.main;

        // 가상 카메라 찾기 시도
        FindVirtualCamera();

        // 초기 타겟 설정
        _targetOrthoSize = normalOrthoSize;
    }

    private void FindVirtualCamera()
    {
        if (virtualCamera == null)
        {
            virtualCamera = GetComponent<CinemachineCamera>();
            if (virtualCamera == null)
            {
                virtualCamera = FindFirstObjectByType<CinemachineCamera>();
            }
        }
        
        // 씬 내에 시네머신 가상 카메라가 하나라도 있으면 수동 위치 제어를 포기합니다.
        _hasCinemachineInScene = (virtualCamera != null) || (FindFirstObjectByType<CinemachineCamera>() != null);
    }

    // DrillStrategy에서 호출: 드릴 대시 시작/종료 시 줌 전환
    public void SetDrillZoom(bool active)
    {
        _isDrillZoomActive = active;
        UpdateTargetOrthoSize();
    }

    // AntiGravityHandler에서 호출: 역중력 위상 진입/이탈 시 카메라 180° 롤 전환.
    // inverted=true → Dutch 180°(화면 뒤집힘), false → 0°(정상). 플레이어 flip과 동기.
    public void SetGravityFlip(bool inverted)
    {
        _targetDutch = inverted ? 180f : 0f;
    }

    // UIStateManager에서 호출: UI 오픈/클로즈 시 줌 전환
    public void SetUIZoom(bool active)
    {
        _isUIZoomActive = active;
        UpdateTargetOrthoSize();
    }

    /// <summary>
    /// 우선순위에 따라 최종 목표 줌 크기를 결정합니다. (UI > 드릴 > 평상시)
    /// </summary>
    private void UpdateTargetOrthoSize()
    {
        if (_isUIZoomActive)
        {
            _targetOrthoSize = uiZoomOrthoSize;
        }
        else if (_isDrillZoomActive)
        {
            _targetOrthoSize = drillOrthoSize;
        }
        else
        {
            _targetOrthoSize = normalOrthoSize;
        }
    }

    void LateUpdate()
    {
        // 런타임에 가상 카메라가 새로 생기거나 바뀌었을 경우를 대비한 보험 (최대 3회까지만 재시도)
        if (virtualCamera == null)
        {
            if (_findCameraFailCount < MaxFindRetries)
            {
                FindVirtualCamera();
                if (virtualCamera == null) _findCameraFailCount++;
            }
        }

        // 1. 현재 ortho 크기·Dutch(롤) 읽기
        float currentOrthoSize;
        float currentDutch;
        if (virtualCamera != null)
        {
            currentOrthoSize = virtualCamera.Lens.OrthographicSize;
            currentDutch     = virtualCamera.Lens.Dutch;
        }
        else if (_cam != null)
        {
            currentOrthoSize = _cam.orthographicSize;
            currentDutch     = _cam.transform.eulerAngles.z;
        }
        else
        {
            HandleManualCameraMove();
            return;
        }

        // 2. 줌·롤 둘 다 목표에 도달했으면 setter 호출을 건너뜀 (Self 비용의 주요 원인)
        bool zoomSettled  = Mathf.Abs(currentOrthoSize - _targetOrthoSize) < 0.001f
                         && Mathf.Abs(_zoomVelocity) < 0.001f;
        bool dutchSettled = Mathf.Abs(Mathf.DeltaAngle(currentDutch, _targetDutch)) < 0.05f
                         && Mathf.Abs(_dutchVelocity) < 0.05f;
        if (zoomSettled && dutchSettled)
        {
            HandleManualCameraMove();
            return;
        }

        // 3. 줌·롤 보간 계산 (SmoothDamp)
        float nextSize  = Mathf.SmoothDamp(currentOrthoSize, _targetOrthoSize, ref _zoomVelocity, zoomSmoothTime);
        float nextDutch = Mathf.SmoothDampAngle(currentDutch, _targetDutch, ref _dutchVelocity, flipSmoothTime);

        // 4. 값 적용 (Cinemachine 우선)
        if (virtualCamera != null)
        {
            // LensSettings 구조체 복사-수정-대입
            var lens = virtualCamera.Lens;
            lens.OrthographicSize = nextSize;
            lens.Dutch            = nextDutch;
            virtualCamera.Lens = lens;
        }
        else if (_cam != null)
        {
            _cam.orthographicSize = nextSize;
            var e = _cam.transform.eulerAngles;
            e.z = nextDutch;
            _cam.transform.eulerAngles = e;
        }

        HandleManualCameraMove();
    }

    /// <summary>
    /// 카메라 수동 이동 (씬 내에 시네머신이 전혀 없는 경우에만 수행)
    /// </summary>
    private void HandleManualCameraMove()
    {
        if (!_hasCinemachineInScene && target != null)
        {
            Vector3 targetPos = target.position;
            float clampedX = (minX == 0f && maxX == 0f) ? targetPos.x : Mathf.Clamp(targetPos.x, minX, maxX);
            float clampedY = (minY == 0f && maxY == 0f) ? targetPos.y : Mathf.Clamp(targetPos.y, minY, maxY);
            transform.position = new Vector3(clampedX, clampedY, transform.position.z);
        }
    }
}