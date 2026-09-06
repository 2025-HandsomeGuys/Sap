// @tags: bug-report, qa, entry-point, hotkey, screenshot
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BugReport
{
    /// <summary>
    /// 버그 리포트 진입점. RuntimeInitializeOnLoadMethod로 자동 생성 — 씬 배치 불필요.
    /// 로그 버퍼가 씬 전환에서 끊기면 안 되므로 DontDestroyOnLoad로 유지된다.
    ///
    /// ⚠ 순서가 이 클래스의 전부다.
    ///   스크린샷과 상태 수집이 오버레이보다 '먼저' 끝나야 한다.
    ///   뒤집히면 리포트에 자기 UI가 찍히고, 일시정지 이후의 값이 수집되어
    ///   "버그가 난 그 시점"이 아니게 된다.
    /// </summary>
    public class BugReportSystem : MonoBehaviour
    {
        public const KeyCode HotKey = KeyCode.F12;

        private static BugReportSystem s_instance;
        private bool _busy;

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
        /// 메인메뉴를 다녀오면 F12가 프로세스를 다시 켜기 전까지 영구히 먹통이었다.
        /// static 이벤트 구독은 파괴와 무관하게 살아남으므로 씬 로드마다 되살린다
        /// (SoundManager·AmbienceDirector와 같은 패턴).
        /// </summary>
        private static void EnsureExists()
        {
            if (s_instance != null) return;

            BugReportLogBuffer.Install();   // 첫 프레임 로그부터 잡는다 (중복 호출은 무해)

            var go = new GameObject("[BugReportSystem]");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<BugReportSystem>();
        }

        /// <summary>코드에서 직접 리포트를 띄우고 싶을 때.</summary>
        public static void Capture()
        {
            if (s_instance == null || s_instance._busy) return;
            s_instance.StartCoroutine(s_instance.CaptureRoutine());
        }

        private void Update()
        {
            if (!_busy && Input.GetKeyDown(HotKey)) Capture();
        }

        private IEnumerator CaptureRoutine()
        {
            _busy = true;

            // ── 1. 스크린샷: 오버레이를 띄우기 전에 찍는다.
            //    WaitForEndOfFrame 없이 호출하면 렌더가 끝나지 않아 실패한다.
            yield return new WaitForEndOfFrame();

            Texture2D shot = null;
            byte[] png = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                png = shot.EncodeToPNG();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BugReport] 스크린샷 실패: {e.Message}");
            }

            // ── 2. 상태 수집: 여전히 오버레이 전. 여기서 값이 확정된다.
            BugReportData data = BugReportCollector.Collect();
            string saveJson = BugReportCollector.CollectSaveJson();
            DateTime when = DateTime.Now;

            // ── 3. 일시정지. PauseOverlayUI와 같은 방식 — 1f로 하드코딩하지 않는다.
            //    일시정지 메뉴 위에서 F12를 눌렀을 때 게임이 멋대로 재개되면 안 된다.
            float prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;

            // 메모 입력 중 M(지도)·Tab(인벤토리) 같은 전역 단축키는
            // UIStateManager가 BugReportOverlayUI.IsOpen을 보고 차단한다.
            // UIState를 바꾸지 않는 이유: SetState는 열려 있던 오버레이를 CloseStatic으로
            // 닫아버려서, 리포트 대상인 상태 자체를 바꿔버린다.

            // ── 4. 오버레이.
            BugReportOverlayUI.Show(shot,
                memo => Finish(data, png, saveJson, when, shot, prevTimeScale, memo),
                () => Finish(data, null, null, when, shot, prevTimeScale, null));
        }

        private void Finish(BugReportData data, byte[] png, string saveJson, DateTime when,
                            Texture2D shot, float prevTimeScale, string memo)
        {
            Time.timeScale = prevTimeScale;

            if (memo != null)   // null이면 취소
            {
                data.memo = memo;
                var result = BugReportWriter.Write(data, png, saveJson, null, when);

                if (result.success)
                {
                    Debug.Log($"[BugReport] 저장됨: {result.folderPath}");
                    BugReportToast.Show($"버그 리포트 저장됨\n{result.folderPath}", result.folderPath);
                }
                else
                {
                    Debug.LogError($"[BugReport] 저장 실패: {result.error}");
                    BugReportToast.Show($"저장 실패: {result.error}", null);
                }
            }

            if (shot != null) Destroy(shot);   // CaptureScreenshotAsTexture는 수동 해제 대상
            _busy = false;
        }
    }
}
#endif
