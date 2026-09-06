using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 씬 이동 테스트를 위한 헬퍼 스크립트.
/// 버튼을 누르거나 키를 입력하여 지정된 씬으로 이동합니다.
/// </summary>
public class SceneTransitionTestHelper : MonoBehaviour
{
    [Header("설정")]
    public string targetSceneName = "UpgroundScene"; // 이동할 씬 이름

    [Header("UI 연결 (선택사항)")]
    public Button moveSceneButton;

    void Start()
    {
        if (moveSceneButton != null)
        {
            moveSceneButton.onClick.RemoveAllListeners();
            moveSceneButton.onClick.AddListener(MoveToTargetScene);
        }
    }

    void Update()
    {
        // P 키를 누르면 씬 이동 (테스트 편의성)
        if (Input.GetKeyDown(KeyCode.P))
        {
            MoveToTargetScene();
        }
    }

    public void MoveToTargetScene()
    {
        Debug.Log($"[SceneTransitionHelper] {targetSceneName} 씬으로 이동합니다. 저장 후 이동합니다...");
        
        // 이동 전 저장 (지하에서의 데이터를 파일에 기록)
        SaveManager sm = FindFirstObjectByType<SaveManager>();
        if (sm != null)
        {
            sm.Save();
        }

        SceneLoader.LoadScene(targetSceneName);
    }
}
