// @tags: scene, surface, upground, audio, ambience, footstep, registry, pure
using UnityEngine.SceneManagement;

/// <summary>
/// "이 씬이 지상인가" 판정의 단일 출처.
///
/// 왜 필요한가: 지상 씬 이름 목록이 코드 곳곳(SceneTransitionTrigger, TravelBehaviours,
/// PortalController, CodeSlotView, AmbienceDirector, FootstepPlayer …)에 복사돼 있었고,
/// 실제 지상 씬 이름이 "DemoUpground"인데 오디오 쪽 두 목록만 그것을 빠뜨려
/// **정산씬(SettlementScene)에서만 밤 풀벌레 앰비언스가 들리고, 지상 씬으로 넘어가
/// BGM이 시작되는 순간 앰비언스가 꺼지는** 버그가 났다.
/// (SettlementSceneController가 SetActiveScene(DemoUpground)를 하는 시점 = BGM 시작 시점)
///
/// 지상 씬을 추가할 때는 이 목록 하나만 고친다.
/// </summary>
public static class SurfaceSceneRegistry
{
    /// <summary>지상으로 취급하는 씬 이름. 정산씬은 지상 위에 얹히는 오버레이 씬이라 포함한다.</summary>
    public static readonly string[] Names = { "UpgroundScene", "DemoUpground", "SettlementScene" };

    public static bool IsSurface(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;

        for (int i = 0; i < Names.Length; i++)
            if (Names[i] == sceneName) return true;

        return false;
    }

    /// <summary>
    /// 추가 목록(인스펙터 지정)까지 함께 본다.
    ///
    /// 프리팹·씬에 이미 직렬화된 목록은 코드 기본값을 고쳐도 갱신되지 않는다
    /// (FanalPlayer.prefab의 FootstepPlayer.surfaceScenes가 그 경우였다).
    /// 그래서 인스펙터 목록은 '대체'가 아니라 '추가'로 취급한다 — 낡은 직렬화 값이
    /// 남아 있어도 정식 목록이 항상 살아 있다.
    /// </summary>
    public static bool IsSurface(string sceneName, string[] extra)
    {
        if (IsSurface(sceneName)) return true;
        if (extra == null || string.IsNullOrEmpty(sceneName)) return false;

        for (int i = 0; i < extra.Length; i++)
            if (extra[i] == sceneName) return true;

        return false;
    }

    public static bool IsActiveSceneSurface(string[] extra = null)
        => IsSurface(SceneManager.GetActiveScene().name, extra);
}
