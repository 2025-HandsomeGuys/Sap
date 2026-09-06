// @tags: scene, loader, transition, loading, utility
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneLoader
{
    /// <summary>
    /// 로딩씬을 거쳐 지정된 씬으로 이동합니다.
    /// </summary>
    /// <param name="sceneName">이동할 씬의 이름</param>
    public static void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[SceneLoader] 전환할 씬 이름이 비어있습니다. 중단합니다.");
            return;
        }

        // Additive 언로드를 위해 현재 씬 이름 저장
        LoadingData.PreviousSceneName = SceneManager.GetActiveScene().name;
        LoadingData.NextSceneName = sceneName;

        SceneManager.LoadScene("LoadingScene");
    }

    /// <summary>
    /// 정산씬을 거쳐 지정된 씬으로 이동합니다.
    /// </summary>
    /// <param name="sceneName">이동할 씬의 이름</param>
    public static void LoadSettlementScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[SceneLoader] 전환할 씬 이름이 비어있습니다. 중단합니다.");
            return;
        }

        LoadingData.PreviousSceneName = SceneManager.GetActiveScene().name;
        LoadingData.NextSceneName = sceneName;

        SceneManager.LoadScene("SettlementScene");
    }
}
