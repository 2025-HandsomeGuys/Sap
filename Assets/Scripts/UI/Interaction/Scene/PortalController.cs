using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 포탈 및 씬 이동을 담당하는 컴포넌트입니다.
/// </summary>
public class PortalController : MonoBehaviour
{
    [Header("이동 설정")]
    public string nextSceneName;

    private void Update()
    {
        // 테스트용: P 키를 누르면 즉시 이동
        if (Input.GetKeyDown(KeyCode.P))
        {
            Debug.Log("[PortalController] P 키 입력으로 이동을 시도합니다.");
            MoveToScene();
        }
    }

    private void OnMouseDown()
    {
        // 테스트용: 마우스로 클릭하면 즉시 이동
        Debug.Log("[PortalController] 마우스 클릭으로 이동을 시도합니다.");
        MoveToScene();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // 충돌 주체가 플레이어인지 확인
        if (other.CompareTag("Player"))
        {
            MoveToScene();
        }
    }

    public void MoveToScene()
    {
        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogWarning("[PortalController] 이동할 씬 이름이 설정되지 않았습니다.");
            return;
        }

        // 씬 전환 직전 세이브 처리
        if (GameManager.Instance != null && GameManager.Instance.saveManager != null)
        {
            if (nextSceneName == "UpgroundScene" || nextSceneName == "DemoUpground" || nextSceneName == "SettlementScene")
            {
                // 지하→지상: 인벤토리를 창고에 병합 후 저장
                Debug.Log("[PortalController] 지상/정산 씬으로 이동 전 인벤토리를 창고에 병합 후 저장합니다.");
                GameManager.Instance.saveManager.MergeInventoriesToWarehouse();
            }
            else
            {
                // 지상→지하: 지상 데이터 확정 저장 (강제종료 시 복구 기준)
                Debug.Log("[PortalController] 지하 씬으로 이동 전 지상 데이터를 확정 저장합니다.");
                GameManager.Instance.saveManager.PrepareUndergroundEntry();
            }
        }

        Debug.Log($"[PortalController] {nextSceneName} 씬으로 이동을 시작합니다.");
        SceneLoader.LoadScene(nextSceneName);
    }
}
