using UnityEngine;

public class FogOfWarController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("플레이어 Transform (비어있으면 자동으로 찾습니다)")]
    public Transform playerTransform; // 플레이어의 Transform
    
    [Tooltip("Fog of War 머티리얼 (비어있으면 자동으로 찾습니다)")]
    public Material fogOfWarMaterial; // 시야 제한 효과에 사용될 머티리얼
    
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
            PlayerController playerController = FindFirstObjectByType<PlayerController>();
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