using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 삽(SapStrategy, toolIndex=1) 차징 중 파기 지점에 UI 이미지를 표시한다.
/// 마우스 방향에 따라 플레이어 주변을 회전하며, 차징 단계에 따라 이미지가 변경된다.
/// </summary>
public class DigRangePreview : MonoBehaviour
{
    [Header("참조")]
    public PlayerMining playerMining;
    public Digger digger;
    public ToolController toolController;

    [Header("UI 이미지")]
    public Image indicatorImage;

    [Header("차징 단계별 이미지 설정")]
    [Tooltip("30% 미만일 때 보여줄 이미지")]
    public Sprite chargeSpriteLow;
    [Tooltip("30% 이상 ~ 60% 미만일 때 보여줄 이미지")]
    public Sprite chargeSpriteMedium;
    [Tooltip("60% 이상 ~ 100% 미만일 때 보여줄 이미지")]
    public Sprite chargeSpriteHigh;
    [Tooltip("100% (풀차징) 시 보여줄 이미지")]
    public Sprite chargeSpriteFull;

    [Header("위치 및 크기 설정")]
    [Tooltip("전체 이미지 크기 배율 (1 = 원본, 2 = 두 배, 0.5 = 절반)")]
    public float imageScaleMultiplier = 1.0f;

    [Tooltip("플레이어 기준으로 이미지가 배치될 고정 거리 (월드 유닛)")]
    [SerializeField] private float fixedPreviewRadius = 1.5f;

    [Tooltip("바라보는 방향을 기준으로 한 미세 조정 (X: 앞/뒤, Y: 좌/우)")]
    public Vector2 imageOffset = Vector2.zero;

    [Tooltip("회전 보간 속도 (높을수록 즉각 반응)")]
    [SerializeField] private float rotationSpeed = 15f;

    [Header("표시 임계값")]
    [Tooltip("차징 진행 중 실시간 사거리가 이 값보다 클 때만 표시됩니다.")]
    [SerializeField] private float minShowThresholdRadius = 0.3f;

    private RectTransform _rectTransform;
    private Canvas _canvas;
    private Vector2 _lockedDirection;
    private bool _wasAttacking;
    private float _currentAngle;
    private bool _isVisible;

    private void Start()
    {
        if (indicatorImage == null)
            indicatorImage = GetComponentInChildren<Image>();

        if (indicatorImage != null)
        {
            _rectTransform = indicatorImage.GetComponent<RectTransform>();
            _canvas = indicatorImage.GetComponentInParent<Canvas>();

            if (_canvas != null && _canvas.renderMode == RenderMode.WorldSpace && _canvas.worldCamera == null)
            {
                Camera mainCam = Camera.main ?? FindFirstObjectByType<Camera>();
                if (mainCam != null) _canvas.worldCamera = mainCam;
            }
        }
        else
        {
            Debug.LogError("[DigRangePreview] indicatorImage가 연결되지 않았습니다.");
        }

        if (playerMining == null) playerMining = GetComponentInParent<PlayerMining>();
        if (digger == null) digger = GetComponentInParent<Digger>();
        if (toolController == null) toolController = GetComponentInParent<ToolController>();

        SetVisible(false);
        _currentAngle = 0f;
    }

    private void Update()
    {
        if (playerMining == null || digger == null) { SetVisible(false); return; }

        int toolIdx = toolController != null ? toolController.currentToolIndex : -1;
        bool isShovelActive = toolController == null || toolIdx == 1;
        bool isCharging = playerMining.IsCharging;
        bool isAttacking = playerMining.IsAttacking;

        bool shouldShow = isShovelActive && (isCharging || isAttacking);

        if (shouldShow)
        {
            if (isAttacking && !_wasAttacking)
            {
                Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                Vector2 playerPos = (Vector2)playerMining.transform.position;
                Vector2 dir = mouseWorld - playerPos;
                _lockedDirection = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.right;
            }
            _wasAttacking = isAttacking;
            _isVisible = true;
        }
        else
        {
            _wasAttacking = false;
            SetVisible(false);
        }
    }

    private void LateUpdate()
    {
        if (!_isVisible || _rectTransform == null || playerMining == null || digger == null)
            return;

        bool isAttacking = playerMining.IsAttacking;
        float chargeRatio = playerMining.GetCurrentChargeRatio();
        float baseRadius = digger.digRadius;

        // 1. 방향 계산
        Vector2 playerPos = (Vector2)playerMining.transform.position;
        Vector2 direction;

        if (isAttacking)
        {
            direction = _lockedDirection;
            chargeRatio = Mathf.Max(chargeRatio, 1.0f);
        }
        else
        {
            Vector2 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            direction = mouseWorld - playerPos;
            if (direction.sqrMagnitude < 0.001f) direction = Vector2.right;
            direction.Normalize();
        }

        // 2. 각도 보간
        float targetAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        _currentAngle = Mathf.LerpAngle(_currentAngle, targetAngle, isAttacking ? 1f : rotationSpeed * Time.deltaTime);

        // 3. 차징 비율에 따른 이미지 교체 및 원본 비율 맞춤 (찌그러짐 방지)
        if (indicatorImage != null)
        {
            Sprite targetSprite = chargeSpriteLow;

            if (chargeRatio >= 1.0f)
                targetSprite = chargeSpriteFull;
            else if (chargeRatio >= 0.6f)
                targetSprite = chargeSpriteHigh;
            else if (chargeRatio >= 0.3f)
                targetSprite = chargeSpriteMedium;

            if (indicatorImage.sprite != targetSprite)
            {
                indicatorImage.sprite = targetSprite;

                if (targetSprite != null)
                {
                    indicatorImage.SetNativeSize();
                }
            }
        }

        // 4. 표시 여부 설정
        float rawChargeRadius = baseRadius * chargeRatio;
        bool showIndicator = rawChargeRadius >= minShowThresholdRadius;
        if (indicatorImage != null && indicatorImage.enabled != showIndicator)
        {
            bool hasSprite = indicatorImage.sprite != null;
            indicatorImage.enabled = showIndicator && hasSprite;
        }

        // 5. 위치 갱신 (고정 거리 + 미세 조정)
        float previewRadius = fixedPreviewRadius > 0f ? fixedPreviewRadius : baseRadius * 2f;

        Vector2 smoothDirection = new Vector2(
            Mathf.Cos(_currentAngle * Mathf.Deg2Rad),
            Mathf.Sin(_currentAngle * Mathf.Deg2Rad));

        Vector3 rotatedOffset = Quaternion.Euler(0, 0, _currentAngle) * (Vector3)imageOffset;
        Vector3 worldTarget = (Vector3)playerPos + (Vector3)(smoothDirection * previewRadius) + rotatedOffset;

        UpdateUIPosition(worldTarget);

        // 6. 회전 및 크기 적용
        float rotZ = _currentAngle - 180f;
        _rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotZ);

        // ★ [추가] 인스펙터에서 설정한 배율로 크기를 키우거나 줄입니다.
        _rectTransform.localScale = Vector3.one * imageScaleMultiplier;
    }

    private void UpdateUIPosition(Vector3 worldPos)
    {
        if (_canvas == null) return;

        if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay || _canvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            Camera cam = (_canvas.renderMode == RenderMode.ScreenSpaceCamera && _canvas.worldCamera != null) ? _canvas.worldCamera : Camera.main;
            if (cam != null)
                _rectTransform.position = cam.WorldToScreenPoint(worldPos);
        }
        else
        {
            _rectTransform.position = worldPos;
        }
    }

    private void SetVisible(bool visible)
    {
        _isVisible = visible;
        if (indicatorImage != null && indicatorImage.enabled != visible)
            indicatorImage.enabled = visible;
    }
}