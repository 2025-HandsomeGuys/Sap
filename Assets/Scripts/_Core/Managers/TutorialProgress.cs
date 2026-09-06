// @tags: tutorial, progress, unlock, gate, save, static
using UnityEngine;

/// <summary>
/// 튜토리얼 완료 여부의 <b>단일 창구</b>(순수 static).
///
/// 값 자체는 세이브에 있다(<c>PlayerData.isTutorialCompleted</c>). 이 플래그는 원래
/// <see cref="SaveSlotUI"/>가 "이어하기 → 튜토리얼 씬으로 강제 분기"에만 쓰던 것이라
/// <b>true로 찍는 코드가 어디에도 없었다</b>. 작업대처럼 "튜토리얼을 끝내야 열리는" 시설이
/// 생기면서 읽는 쪽이 늘었으므로, 읽기·쓰기를 여기 하나로 모은다.
///
/// 세이브를 못 찾으면 <b>완료로 본다</b> — 테스트 씬에서 시설이 통째로 잠겨버리는 쪽이
/// 더 나쁘다(<c>WorldInteractable.IsUnlocked</c>·<c>ToolController.IsToolUnlocked</c>와 같은 규칙).
/// </summary>
public static class TutorialProgress
{
    /// <summary>튜토리얼 씬 이름 — 이 이름을 아는 코드의 단일 원천.</summary>
    public const string SceneName = "TutorialScene";

    /// <summary>튜토리얼을 끝냈는가. 세이브가 없으면 true(잠그지 않는다).</summary>
    public static bool IsCompleted
    {
        get
        {
            SaveManager sm = SaveManager.Instance;
            if (sm == null || sm.playerData == null) return true;
            return sm.playerData.isTutorialCompleted;
        }
    }

    /// <summary>
    /// 튜토리얼 완료를 기록하고 저장한다. 처음 찍혔으면 true(연출·토스트 트리거용).
    /// 튜토리얼 씬을 벗어나는 지점에서 한 번 부른다.
    /// </summary>
    public static bool MarkCompleted()
    {
        SaveManager sm = SaveManager.Instance;
        if (sm == null || sm.playerData == null)
        {
            Debug.LogWarning("[TutorialProgress] SaveManager가 없어 완료를 기록하지 못했다");
            return false;
        }

        if (sm.playerData.isTutorialCompleted) return false;

        sm.playerData.isTutorialCompleted = true;
        sm.Save();
        Debug.Log("[TutorialProgress] 튜토리얼 완료 기록");
        return true;
    }

    /// <summary>
    /// 튜토리얼 씬을 떠나는 전환이면 완료로 기록한다. <see cref="SceneLoader"/>가 부르는
    /// <b>유일한</b> 훅 — 튜토리얼 씬에서 나가는 길(지하행 트리거들)이 전부 SceneLoader를 지난다.
    /// 메인메뉴 복귀는 SceneManager를 직접 쓰므로 여기 걸리지 않는다(중도 이탈은 완료가 아니다).
    /// </summary>
    public static void MarkCompletedIfLeavingTutorial(string fromScene, string toScene)
    {
        if (fromScene != SceneName || toScene == SceneName) return;
        MarkCompleted();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
    /// <summary>디버그 콘솔 전용. 완료 플래그를 켜고 끈다(저장까지).</summary>
    public static bool DebugSet(bool completed)
    {
        SaveManager sm = SaveManager.Instance;
        if (sm == null || sm.playerData == null) return false;

        sm.playerData.isTutorialCompleted = completed;
        sm.Save();
        return true;
    }
#endif
}
