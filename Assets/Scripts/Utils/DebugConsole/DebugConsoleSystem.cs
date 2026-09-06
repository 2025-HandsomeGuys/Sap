// @tags: debug, console, qa, entry-point, hotkey, timescale
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace DebugTools
{
    /// <summary>
    /// 디버그 콘솔 진입점. RuntimeInitializeOnLoadMethod로 자동 생성 — 씬 배치 불필요.
    /// (BugReportSystem과 같은 구조)
    ///
    /// 단축키가 두 갈래인 이유:
    ///   - 콘솔(`, F9)은 게임을 멈추고 명령을 친다 — 정확한 값 입력용.
    ///   - 배속([ ])은 게임이 도는 채로 즉시 바뀐다 — "이 구간 지루한가"를 눈으로 볼 때.
    /// 밸런싱 중에 실제로 많이 쓰는 건 후자다.
    /// </summary>
    public class DebugConsoleSystem : MonoBehaviour
    {
        public const KeyCode ToggleKey = KeyCode.BackQuote;   // ` / ~
        public const KeyCode AltToggleKey = KeyCode.F9;       // 한글 키보드에서 ` 가 애매할 때

        public const KeyCode SpeedDownKey = KeyCode.LeftBracket;   // [
        public const KeyCode SpeedUpKey = KeyCode.RightBracket;    // ]
        public const KeyCode SpeedResetKey = KeyCode.Backslash;    // \

        private static DebugConsoleSystem s_instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureExists();
            SceneManager.sceneLoaded -= OnSceneLoadedEnsure;
            SceneManager.sceneLoaded += OnSceneLoadedEnsure;
        }

        private static void OnSceneLoadedEnsure(Scene scene, LoadSceneMode mode) => EnsureExists();

        /// <summary>
        /// ⚠ 자가복구: GameManager.OpenMainMenu의 DestroyPersistentObjects()가
        /// DontDestroyOnLoad 씬의 루트를 전부 파괴한다 — 이 오브젝트도 같이 죽는다.
        /// [RuntimeInitializeOnLoadMethod]는 세션당 한 번만 돌아 재생성되지 않으므로,
        /// 메인메뉴를 다녀오면 콘솔(` / F9)과 배속([ ])이 프로세스를 다시 켜기
        /// 전까지 영구히 먹통이었다. static 이벤트 구독은 파괴와 무관하게 살아남으므로
        /// 씬 로드마다 되살린다 (SoundManager·AmbienceDirector와 같은 패턴).
        /// </summary>
        private static void EnsureExists()
        {
            if (s_instance != null) return;

            var go = new GameObject("[DebugConsole]");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<DebugConsoleSystem>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(ToggleKey) || Input.GetKeyDown(AltToggleKey))
            {
                DebugConsoleOverlayUI.Toggle();
                return;
            }

            // 콘솔이 열려 있으면 콘솔이 키를 전부 처리한다.
            if (DebugConsoleOverlayUI.IsOpen) return;

            // 다른 입력창(메모·검색·수량)에 포커스가 있으면 대괄호는 그냥 글자다.
            if (IsTypingSomewhere()) return;

            if (Input.GetKeyDown(SpeedUpKey)) ReportSpeed(DebugTimeScale.Nudge(+1));
            else if (Input.GetKeyDown(SpeedDownKey)) ReportSpeed(DebugTimeScale.Nudge(-1));
            else if (Input.GetKeyDown(SpeedResetKey))
            {
                DebugTimeScale.ResetToNormal();
                ReportSpeed(1f);
            }
        }

        /// <summary>
        /// 콘솔 로그에도 남긴다 — 나중에 "이 측정 구간에 배속이 걸려 있었나"를 되짚을 수 있게.
        /// 화면 표시기는 DebugTimeScaleDriver가 따로 띄운다.
        /// </summary>
        private static void ReportSpeed(float multiplier)
        {
            DebugConsoleOverlayUI.Print($"<color=#F09A3E>배속 x{multiplier:0.##}</color>");
        }

        private static bool IsTypingSomewhere()
        {
            var es = EventSystem.current;
            if (es == null) return false;

            var selected = es.currentSelectedGameObject;
            if (selected == null) return false;

            return selected.GetComponent<TMP_InputField>() != null
                || selected.GetComponent<UnityEngine.UI.InputField>() != null;
        }
    }
}
#endif
