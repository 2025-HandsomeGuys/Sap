using UnityEngine;
using UnityEngine.SceneManagement; // SceneManager 사용을 위해 추가
using System.Collections; // IEnumerator 사용을 위해 추가

public class SceneTransitionManager : MonoBehaviour
{
    public float delayBeforeTransition = 5.0f; // 씬 전환까지 대기할 시간 (초)
    public string nextSceneName = "SecondScene"; // 전환될 다음 씬의 이름

    void Start()
    {
        // 게임 시작 시 씬 전환 코루틴 시작
        StartCoroutine(TransitionToNextSceneAfterDelay());
    }

    IEnumerator TransitionToNextSceneAfterDelay()
    {
        // 현재 씬이 로드되고 delayBeforeTransition(예: 3초)만큼 기다립니다.
        yield return new WaitForSeconds(delayBeforeTransition);

        // 지정된 시간이 지나면 다음 씬으로 전환합니다.
        SceneManager.LoadScene(nextSceneName);
    }
}