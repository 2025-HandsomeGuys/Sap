// @tags: scene, loading, deprecated, ready, state
using UnityEngine;

/// <summary>
/// [Deprecated] 이 스크립트는 더 이상 사용하지 않습니다.
/// LoadingData.IsReady 제어는 InfinityMapManager 내부에서 자동으로 처리됩니다.
/// 씬에 붙어있다면 제거해도 무방합니다.
/// </summary>
public class SceneReadyReporter : MonoBehaviour
{
    [System.Obsolete("더 이상 사용하지 않습니다. InfinityMapManager가 IsReady를 직접 관리합니다.", true)]
    public void ReportReady()
    {
        LoadingData.IsReady = true;
    }
}
