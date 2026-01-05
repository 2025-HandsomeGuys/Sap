using UnityEngine;

public class FogOfWarController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("플레이어 Transform (비어있으면 자동으로 찾습니다)")]
    public Transform playerTransform; // 플레이어의 Transform
    
    [Tooltip("Fog of War 머티리얼 (비어있으면 자동으로 찾습니다)")]
    public Material fogOfWarMaterial; // 시야 제한 효과에 사용될 머티리얼

    [Header("Settings")]
    [Tooltip("이 Y좌표 위로는 안개가 끼지 않습니다 (월드 좌표)")]
    public float worldYLimit = 100.0f; // 기본값을 높게 설정하여 초기에는 제한이 없도록 함

    [Tooltip("시야각 (손전등 너비) -1 ~ 1 (1에 가까울수록 좁음, 0은 180도, -1은 360도)")]
    [Range(-1f, 1f)]
    public float sightAngle = 0.5f;

    [Tooltip("손전등 사거리 (0-1 범위)")]
    public float sightDistance = 0.5f;

    [Header("Tile Spotlight Settings")]
    [Tooltip("타일 스포트라이트 반경 (월드 단위)")]
    public float tileSpotlightRadius = 10.0f;

    [Tooltip("타일 스포트라이트 부드러움 정도")]
    public float tileSpotlightSoftness = 1.0f;

    [Header("Tile Flashlight Settings")]
    [Tooltip("손전등 활성화 여부")]
    public bool enableFlashlight = true;
    
    [Tooltip("디버그: 원형 spotlight 비활성화 (손전등만 보기)")]
    public bool disableCircularSpotlight = false;

    [Tooltip("손전등 각도 (도 단위, 기본값 60도)")]
    [Range(15f, 180f)]
    public float flashlightAngle = 60f;

    [Tooltip("손전등 거리 (월드 단위)")]
    [Range(1f, 50f)]
    public float flashlightDistance = 15f;

    [Tooltip("손전등 밝기 (0-1, 기본값 1.0)")]
    [Range(0f, 1f)]
    public float flashlightBrightness = 1.0f;

    [Tooltip("손전등 가장자리 부드러움 (월드 단위)")]
    [Range(0.1f, 10f)]
    public float flashlightSoftness = 2f;
    
    private Camera mainCamera;

    void Start()
    {
        mainCamera = GetComponent<Camera>();
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        // 플레이어를 자동으로 찾기
        if (playerTransform == null)
        {
            Player3Controller playerController = FindFirstObjectByType<Player3Controller>();
            if (playerController != null)
            {
                playerTransform = playerController.transform;
                Debug.Log("FogOfWarController: 플레이어를 자동으로 찾았습니다.");
            }
            else
            {
                Debug.LogError("FogOfWarController: 플레이어를 찾을 수 없습니다. PlayerController가 씬에 있는지 확인하세요.");
                this.enabled = false;
                return;
            }
        }

        // 머티리얼을 자동으로 찾기
        if (fogOfWarMaterial == null)
        {
            fogOfWarMaterial = Resources.Load<Material>("FogMaterial");
            if (fogOfWarMaterial == null)
            {
                // Assets/Shader 폴더에서 찾기 시도
                Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
                foreach (Material mat in materials)
                {
                    if (mat.name == "FogMaterial" && mat.shader.name == "Custom/FogOfWar")
                    {
                        fogOfWarMaterial = mat;
                        Debug.Log("FogOfWarController: FogMaterial을 자동으로 찾았습니다.");
                        break;
                    }
                }
            }
            
            if (fogOfWarMaterial == null)
            {
                Debug.LogError("FogOfWarController: Fog of War Material을 찾을 수 없습니다. Assets/Shader/FogMaterial.mat을 할당하거나 씬에 있는지 확인하세요.");
                this.enabled = false;
            }
        }
    }

    void Update()
    {
        if (playerTransform != null && fogOfWarMaterial != null && mainCamera != null)
        {
            // 플레이어의 월드 좌표를 화면 뷰포트 좌표(0-1 범위)로 변환합니다.
            Vector3 screenPos = mainCamera.WorldToViewportPoint(playerTransform.position);
            
            // z 값이 양수인지 확인 (카메라 앞에 있는지)
            // z가 음수면 플레이어가 카메라 뒤에 있거나 시야 밖에 있습니다
            if (screenPos.z > 0)
            {
                // 뷰포트 좌표를 셰이더에 전달
                // WorldToViewportPoint는 (0,0)이 왼쪽 아래, (1,1)이 오른쪽 위를 반환합니다
                // 이는 Blit의 UV 좌표계와 일치합니다
                Vector4 playerScreenPos = new Vector4(screenPos.x, screenPos.y, 0, 0);
                fogOfWarMaterial.SetVector("_PlayerScreenPos", playerScreenPos);
                
                // Global property로도 설정 (더 확실한 동기화)
                Shader.SetGlobalVector("_PlayerScreenPos", playerScreenPos);

                // Y 제한선 계산 및 전달
                // 월드 좌표의 제한선을 화면 좌표(0-1)로 변환
                Vector3 limitScreenPos = mainCamera.WorldToViewportPoint(new Vector3(0, worldYLimit, 0));
                
                // 쉐이더에 전달 (값은 0~1 사이여야 의미가 있음)
                fogOfWarMaterial.SetFloat("_FogYLimit", limitScreenPos.y);
                Shader.SetGlobalFloat("_FogYLimit", limitScreenPos.y);

                // --- 마우스 방향 계산 (손전등 효과) ---
                // 마우스 위치를 화면 좌표(Viewport 0~1)로 변환
                Vector3 mouseViewportPos = mainCamera.ScreenToViewportPoint(Input.mousePosition);
                
                // 플레이어 화면 좌표 (이미 계산됨: screenPos)
                // 방향 벡터 계산 (마우스 - 플레이어)
                Vector2 lookDir = new Vector2(mouseViewportPos.x - screenPos.x, mouseViewportPos.y - screenPos.y);
                
                // 화면 비율 보정 (쉐이더와 동일하게 X축 보정)
                // 안 그러면 화면이 납작해서 원이 찌그러지는 것처럼 방향도 왜곡됨
                float aspectRatio = (float)Screen.width / Screen.height;
                lookDir.x *= aspectRatio;
                
                lookDir.Normalize();

                fogOfWarMaterial.SetVector("_PlayerDir", lookDir);
                fogOfWarMaterial.SetFloat("_SightAngle", sightAngle);
                fogOfWarMaterial.SetFloat("_SightDistance", sightDistance);
                
                Shader.SetGlobalVector("_PlayerDir", lookDir);
                Shader.SetGlobalFloat("_SightAngle", sightAngle);
                Shader.SetGlobalFloat("_SightDistance", sightDistance);
                
                // --- World Space Spotlight for Tiles ---
                Shader.SetGlobalVector("_PlayerWorldPos", playerTransform.position);
                // 디버그 모드: 원형 spotlight 비활성화
                float effectiveRadius = disableCircularSpotlight ? 0 : tileSpotlightRadius;
                Shader.SetGlobalFloat("_SightRadiusWorld", effectiveRadius);
                Shader.SetGlobalFloat("_SightSoftnessWorld", tileSpotlightSoftness);
                
                // 디버그 로그 (1초마다)
                if (Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[Spotlight Debug] Player World Pos: {playerTransform.position}, Radius: {effectiveRadius}, Softness: {tileSpotlightSoftness}");
                }

                // --- Flashlight Direction (World Space) for Tiles ---
                if (enableFlashlight)
                {
                    // 마우스 위치를 월드 좌표로 변환 (2D 게임용)
                    Vector2 mouseWorldPos = (Vector2)mainCamera.ScreenToWorldPoint(Input.mousePosition);

                    // 플레이어 위치에서 마우스로의 방향 벡터 계산 (월드 좌표계)
                    Vector2 playerWorldDir = mouseWorldPos - (Vector2)playerTransform.position;
                    
                    // 디버그 로그 (1초마다)
                    if (Time.frameCount % 60 == 0)
                    {
                        Debug.Log($"[Flashlight Debug] Enable: {enableFlashlight}, Mouse World: {mouseWorldPos}, Player: {playerTransform.position}");
                        Debug.Log($"[Flashlight Debug] Direction (before normalize): {playerWorldDir}, Magnitude: {playerWorldDir.magnitude}");
                        Debug.Log($"[Flashlight Debug] Settings - Angle: {flashlightAngle}, Distance: {flashlightDistance}, Brightness: {flashlightBrightness}, Softness: {flashlightSoftness}");
                    }
                    
                    // 0 벡터 체크 (마우스가 플레이어와 같은 위치에 있을 때)
                    if (playerWorldDir.magnitude > 0.001f)
                    {
                        playerWorldDir.Normalize();

                        // 디버그 로그
                        if (Time.frameCount % 60 == 0)
                        {
                            Debug.Log($"[Flashlight Debug] Direction (normalized): {playerWorldDir}");
                        }

                        // 쉐이더에 전달
                        Shader.SetGlobalVector("_PlayerWorldDir", new Vector4(playerWorldDir.x, playerWorldDir.y, 0, 0));
                        Shader.SetGlobalFloat("_FlashlightAngle", flashlightAngle);
                        Shader.SetGlobalFloat("_FlashlightDistance", flashlightDistance);
                        Shader.SetGlobalFloat("_FlashlightBrightness", flashlightBrightness);
                        Shader.SetGlobalFloat("_FlashlightSoftness", flashlightSoftness);
                        
                        // 쉐이더 프로퍼티 검증 (1초마다)
                        if (Time.frameCount % 60 == 0)
                        {
                            Vector4 shaderDir = Shader.GetGlobalVector("_PlayerWorldDir");
                            float shaderAngle = Shader.GetGlobalFloat("_FlashlightAngle");
                            float shaderDistance = Shader.GetGlobalFloat("_FlashlightDistance");
                            float shaderBrightness = Shader.GetGlobalFloat("_FlashlightBrightness");
                            float shaderSoftness = Shader.GetGlobalFloat("_FlashlightSoftness");
                            
                            Debug.Log($"[Shader Properties] Dir: ({shaderDir.x:F3}, {shaderDir.y:F3}), Angle: {shaderAngle}, Distance: {shaderDistance}, Brightness: {shaderBrightness}, Softness: {shaderSoftness}");
                            
                            // 값이 올바르게 전달되었는지 확인
                            if (Mathf.Abs(shaderAngle - flashlightAngle) > 0.01f)
                                Debug.LogWarning($"[Shader Properties] Angle mismatch! Expected: {flashlightAngle}, Got: {shaderAngle}");
                            if (Mathf.Abs(shaderDistance - flashlightDistance) > 0.01f)
                                Debug.LogWarning($"[Shader Properties] Distance mismatch! Expected: {flashlightDistance}, Got: {shaderDistance}");
                            if (shaderDir.magnitude < 0.001f)
                                Debug.LogWarning("[Shader Properties] Direction vector is zero in shader!");
                        }
                    }
                    else
                    {
                        // 마우스가 플레이어 위치에 있으면 기본 방향 (오른쪽) 사용
                        if (Time.frameCount % 60 == 0)
                        {
                            Debug.LogWarning("[Flashlight Debug] Direction vector is zero, using default direction (1, 0)");
                        }
                        
                        Shader.SetGlobalVector("_PlayerWorldDir", new Vector4(1, 0, 0, 0));
                        Shader.SetGlobalFloat("_FlashlightAngle", flashlightAngle);
                        Shader.SetGlobalFloat("_FlashlightDistance", flashlightDistance);
                        Shader.SetGlobalFloat("_FlashlightBrightness", flashlightBrightness);
                        Shader.SetGlobalFloat("_FlashlightSoftness", flashlightSoftness);
                    }
                }
                else
                {
                    // 손전등 비활성화 시 기본값 설정
                    if (Time.frameCount % 60 == 0)
                    {
                        Debug.Log("[Flashlight Debug] Flashlight is disabled");
                    }
                    
                    Shader.SetGlobalVector("_PlayerWorldDir", Vector4.zero);
                    Shader.SetGlobalFloat("_FlashlightAngle", 0);
                    Shader.SetGlobalFloat("_FlashlightDistance", 0);
                    Shader.SetGlobalFloat("_FlashlightBrightness", 0);
                    Shader.SetGlobalFloat("_FlashlightSoftness", 0);
                }
                
                // 디버깅: Material 속성 확인 (1초마다)
                if (Time.frameCount % 60 == 0)
                {
                    Vector2 matDir = fogOfWarMaterial.GetVector("_PlayerDir");
                    float matAngle = fogOfWarMaterial.GetFloat("_SightAngle");
                    float matDist = fogOfWarMaterial.GetFloat("_SightDistance");
                    // Debug.Log($"[FogOfWarController] Material 속성 - Dir: {matDir}, Angle: {matAngle}, Distance: {matDist}");
                }
            }
            else
            {
                // 플레이어가 카메라 뒤에 있으면 화면 중앙으로 설정
                Vector4 defaultPos = new Vector4(0.5f, 0.5f, 0, 0);
                fogOfWarMaterial.SetVector("_PlayerScreenPos", defaultPos);
                Shader.SetGlobalVector("_PlayerScreenPos", defaultPos);
            }
        }
        else
        {
            // 디버깅: 왜 업데이트가 안 되는지 확인
            if (playerTransform == null)
                Debug.LogWarning("FogOfWarController: playerTransform이 null입니다.");
            if (fogOfWarMaterial == null)
                Debug.LogWarning("FogOfWarController: fogOfWarMaterial이 null입니다.");
            if (mainCamera == null)
                Debug.LogWarning("FogOfWarController: mainCamera가 null입니다.");
        }
    }
}