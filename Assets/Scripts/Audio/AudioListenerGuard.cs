// @tags: sound, audio, listener, guard, static, scene, dedup
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 어느 씬이든 "활성 AudioListener가 항상 정확히 1개"임을 보장하는 전역 가드.
///
/// 배경: 지상씬(DemoUpground)은 AudioListener를 가진 카메라가 2개 들어 있다
///  - "Main Camera"(MainCamera 태그, Cinemachine/CameraFollow): 리스너가 씬 파일에서 꺼져 있음
///  - "Camera"(Untagged): 리스너가 켜져 있음
/// 정상 상태가 "씬에 저장된 수동 플래그 하나"에만 의존해서, 그 플래그가 한 번 뒤집히면
/// 리스너가 2개로 남고 → 유니티가 오디오 처리를 못 해 무음 + "There are 2 audio listeners" 경고.
///
/// 기존 중복 제거 로직(LoadingSceneController/SettlementSceneController/MarketSceneController)은
/// "자기 전환·오버레이 씬의 리스너만" 끄고 "목적지 씬은 이미 1개"라고 가정하기 때문에
/// 목적지 씬 자체가 2개를 들고 오는 상황은 아무도 교정하지 못했다.
///
/// ⚠ 이 가드는 반드시 "GameObject 없는 순수 static 구독"이어야 한다.
/// MonoBehaviour(DontDestroyOnLoad)로 만들면 GameManager.OpenMainMenu의
/// DestroyPersistentObjects()가 가드 오브젝트까지 파괴해 버리고, RuntimeInitializeOnLoadMethod는
/// 세션당 한 번만 도므로 재생성되지 않는다 → 메인메뉴 왕복 후 가드가 사라진다(과거 버그).
/// static 이벤트 구독은 씬/DDOL 파괴와 무관하게 세션 내내 살아 있다.
///
/// - 유지 우선순위: Camera.main(=활성 MainCamera 태그 카메라) → 이미 켜진 리스너 → 첫 리스너
/// - AudioListener.pause(전역 음소거 플래그)는 건드리지 않는다(로딩씬 음소거 연출 보존).
/// </summary>
public static class AudioListenerGuard
{
    private static bool _hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_hooked) return;
        _hooked = true;

        SceneManager.sceneLoaded += (_, __) => EnforceSingle();
        SceneManager.sceneUnloaded += _ => EnforceSingle();       // 로딩씬 언로드로 Camera.main이 바뀔 때 재정리
        SceneManager.activeSceneChanged += (_, __) => EnforceSingle();

        // 부트스트랩 직후(첫 씬)에도 한 번 정리한다.
        EnforceSingle();
    }

    /// <summary>
    /// 모든 씬을 통틀어 활성 AudioListener를 정확히 1개만 남긴다.
    ///
    /// 핵심 원칙: <b>리스너를 옮기지 않는다.</b> 씬이 켜둔 리스너를 그대로 유지하고
    /// 여분만 끈다. 억지로 Camera.main으로 옮기면 두 카메라의 위치 차이 때문에 3D 사운드가
    /// 달라져 "원래 소리 대신 이상한 소리"가 난다(지상씬은 Main Camera와 aux "Camera"가
    /// ~11유닛 떨어져 있고, 오디오는 aux "Camera" 기준으로 튜닝돼 있다).
    /// </summary>
    public static void EnforceSingle()
    {
        // 비활성 GameObject의 리스너는 셀 필요 없다(유니티도 그것들은 경고 대상이 아님).
        var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (listeners.Length == 0) return;

        var mainCam = Camera.main;
        AudioListener mainListener = mainCam != null ? mainCam.GetComponent<AudioListener>() : null;

        AudioListener keeper = null;

        // 1순위: 이미 켜져 있는 리스너를 유지한다(리스너 위치를 바꾸지 않음).
        //         여러 개가 켜져 있으면(=중복 버그) Main Camera에 붙은 것을 우선 버리고
        //         나머지를 남긴다 — 지상씬은 aux "Camera" 리스너가 정상이고 Main Camera 것은
        //         꺼져 있어야 하는 설계이기 때문.
        for (int i = 0; i < listeners.Length; i++)
        {
            if (!listeners[i].enabled) continue;
            if (listeners[i] == mainListener) continue; // Main Camera 것은 유지 후보에서 뒤로 미룸
            keeper = listeners[i];
            break;
        }

        // 켜진 게 Main Camera 것뿐이면 그거라도 유지(끊김 방지).
        if (keeper == null)
            for (int i = 0; i < listeners.Length; i++)
                if (listeners[i].enabled) { keeper = listeners[i]; break; }

        // 2순위: 아무것도 안 켜져 있으면 하나 켠다(폴백) — Main Camera 우선, 없으면 첫 번째.
        if (keeper == null) keeper = mainListener != null ? mainListener : listeners[0];

        for (int i = 0; i < listeners.Length; i++)
        {
            bool shouldEnable = (listeners[i] == keeper);
            if (listeners[i].enabled != shouldEnable)
                listeners[i].enabled = shouldEnable;
        }
    }
}
