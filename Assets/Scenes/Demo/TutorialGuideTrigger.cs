using System.Collections; // 추가
using UnityEngine;

public class TutorialGuideTrigger : MonoBehaviour
{
    [SerializeField] private string tutorialGuideId = "tutorial_basic";

    private IEnumerator Start()
    {
        // 로딩 씬이 끝날 때까지 대기
        yield return new WaitUntil(() => !LoadingData.IsLoading);

        // 로딩창 사라지고 1초 뒤에 뜨게 대기 (타임스케일 영향 안 받게 Realtime 사용)
        yield return new WaitForSecondsRealtime(1.0f);

        SaveManager saveManager = GameManager.Instance.saveManager;
        if (saveManager == null) yield break;

        PlayerData data = saveManager.playerData;

        // SceneLoader에서 isTutorialCompleted를 먼저 true로 만들어버리는 경우가 있어서
        // GuideManager의 시청 기록(IsSeen)을 기준으로 한 번도 안 봤다면 띄웁니다.
        if (data != null && !GuideManager.IsSeen(tutorialGuideId))
        {
            Debug.Log($"[TutorialGuideTrigger] 1.0초 대기 후 가이드 호출: {tutorialGuideId}");

            GuideManager.Trigger(tutorialGuideId);
        }
    }
}