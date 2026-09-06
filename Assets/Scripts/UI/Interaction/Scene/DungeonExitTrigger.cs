using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 던전 클리어/퇴장을 담당하는 트리거 컴포넌트입니다.
/// 플레이어가 닿으면 지정된 메인 씬(지하 등)으로 이동합니다.
/// </summary>
public class DungeonExitTrigger : MonoBehaviour
{
    [Header("복귀 설정")]
    [Tooltip("돌아갈 메인 지하 씬의 이름입니다.")]
    [SerializeField] private string returnSceneName = "DemoUnderground";
    
    [Header("클리어 설정")]
    [Tooltip("던전 클리어 시 골드 보상량")]
    [SerializeField] private int clearRewardGold = 1000;

    private bool _isExiting = false;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_isExiting) return;

        if (other.CompareTag("Player"))
        {
            _isExiting = true;
            ExitDungeon();
        }
    }

    private void ExitDungeon()
    {
        if (string.IsNullOrEmpty(returnSceneName))
        {
            Debug.LogWarning("[DungeonExitTrigger] 복귀할 씬 이름이 설정되지 않았습니다.");
            return;
        }

        if (GameManager.Instance != null && GameManager.Instance.saveManager != null)
        {
            var pData = GameManager.Instance.saveManager.playerData;
            if (pData != null)
            {
                // 보상 지급 (골드)
                if (clearRewardGold > 0)
                {
                    pData.gold += clearRewardGold;
                    Debug.Log($"[DungeonExitTrigger] 던전 클리어 보상 {clearRewardGold} 골드 획득! (현재 골드: {pData.gold})");
                }
                
                // SaveManager를 통해 상태 저장 (isReturningFromDungeon은 PlayerSpawner 등에서 텔레포트 처리 후 false로 변경됨)
                GameManager.Instance.saveManager.Save();
            }
        }

        Debug.Log($"[DungeonExitTrigger] 메인 씬({returnSceneName})으로 복귀를 시작합니다.");
        SceneLoader.LoadScene(returnSceneName);
    }
}
