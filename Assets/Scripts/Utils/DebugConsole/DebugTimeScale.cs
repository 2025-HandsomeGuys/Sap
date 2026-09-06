// @tags: debug, console, qa, timescale, fast-forward, balance, tuning
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DebugTools
{
    /// <summary>
    /// 디버그 시간 배속의 단일 소유자.
    ///
    /// ⚠ 이 클래스가 존재하는 이유는 "매 프레임 다시 쓴다"는 것 하나다.
    ///   이 프로젝트는 오버레이·씬 전환 20여 곳에서 <c>Time.timeScale = 1f</c>를 하드코딩한다
    ///   (PauseOverlayUI, ShopOverlayUI, GameManager, EmergencyEscapeSequenceUI ...).
    ///   한 번만 세팅하는 방식이면 상점을 한 번 열었다 닫는 순간 배속이 조용히 풀리고,
    ///   그걸 모른 채 측정한 밸런스 수치가 전부 오염된다.
    ///   그래서 <see cref="DebugTimeScaleDriver"/>가 LateUpdate에서 매번 되돌려 놓는다.
    ///
    /// 일시정지(timeScale == 0)는 건드리지 않는다. 각 오버레이가 <c>_prevTimeScale</c>로
    /// 복원하는 흐름을 그대로 두고, 복원된 뒤 프레임에 우리가 다시 덮어쓴다.
    /// </summary>
    public static class DebugTimeScale
    {
        /// <summary>단축키로 오르내리는 배속 사다리.</summary>
        public static readonly float[] Ladder = { 0.1f, 0.25f, 0.5f, 1f, 2f, 4f, 8f, 16f };

        public const float MinMultiplier = 0.05f;
        public const float MaxMultiplier = 20f;

        /// <summary>
        /// fixedDeltaTime을 배속만큼 키우는 데에 두는 상한.
        ///
        /// 물리 스텝 수/실시간 = timeScale / fixedDeltaTime 이다.
        /// 배속만큼 fixedDeltaTime을 그대로 키우면 물리 부하는 그대로지만 한 스텝에
        /// 플레이어가 훨씬 멀리 이동해 지형을 뚫는다(이 게임은 픽셀 지형 콜라이더라 특히 취약).
        /// 반대로 안 키우면 8배속에서 물리 스텝이 8배가 되어 프레임이 무너지고,
        /// 그 프레임 저하 자체가 측정값을 왜곡한다.
        /// 절충으로 2배까지만 늘린다 — 8배속에서 물리 부하 4배, 스텝 간 이동거리 2배.
        /// </summary>
        private const float MaxFixedStepFactor = 2f;

        public static float Multiplier { get; private set; } = 1f;

        /// <summary>1배가 아니면 true. 화면 표시기와 QA 로그가 이 값을 본다.</summary>
        public static bool IsActive => !Mathf.Approximately(Multiplier, 1f);

        private static float s_baseFixedDelta = 0.02f;
        private static bool s_booted;
        private static DebugTimeScaleDriver s_driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Enter Play Mode Options로 도메인 리로드를 꺼두면 static이 살아남는다.
            // 그때 s_baseFixedDelta를 다시 읽으면 '이전 세션이 늘려둔 값'을 기준값으로 굳혀버린다.
            if (s_booted) Time.fixedDeltaTime = s_baseFixedDelta;
            else
            {
                s_baseFixedDelta = Time.fixedDeltaTime;
                s_booted = true;
            }

            Multiplier = 1f;

            // 에디터는 Play 세션 사이에 Time.timeScale을 초기화하지 않는다.
            // 이전 세션에서 오버레이(일시정지·상점·인벤토리 등)를 연 채 Stop하면
            // timeScale=0이 다음 Play로 넘어와 게임 전체가 얼어붙는다(입력만 살아 있음).
            // Apply()는 timeScale<=0이면 '일시정지는 오버레이의 몫'이라며 손을 떼므로
            // 자동 복구되지 않는다 → 아직 어떤 오버레이도 안 열린 이 시점에 1로 되보장한다.
            Time.timeScale = 1f;

            EnsureDriver();
            SceneManager.sceneLoaded -= OnSceneLoadedEnsure;
            SceneManager.sceneLoaded += OnSceneLoadedEnsure;
        }

        private static void OnSceneLoadedEnsure(Scene scene, LoadSceneMode mode) => EnsureDriver();

        /// <summary>
        /// ⚠ 자가복구: GameManager.OpenMainMenu의 DestroyPersistentObjects()가
        /// DontDestroyOnLoad 씬의 루트를 전부 파괴해 드라이버까지 가져간다.
        /// 드라이버가 없으면 배속이 한 번 세팅되고 끝이라 상점을 한 번 여닫는 순간 풀린다
        /// (이 클래스 주석 참조). 그래서 씬 로드마다 되살린다.
        ///
        /// 위 Bootstrap의 일회성 초기화(s_baseFixedDelta 캡처·timeScale=1 보장)는
        /// 여기 넣지 않는다 — 씬 로드마다 돌면 오버레이가 멈춰둔 timeScale을 풀어버린다.
        /// </summary>
        private static void EnsureDriver()
        {
            if (s_driver != null) return;

            var go = new GameObject("[DebugTimeScale]");
            Object.DontDestroyOnLoad(go);
            s_driver = go.AddComponent<DebugTimeScaleDriver>();
        }

        public static void Set(float multiplier)
        {
            Multiplier = Mathf.Clamp(multiplier, MinMultiplier, MaxMultiplier);
            Apply();
        }

        public static void ResetToNormal() => Set(1f);

        /// <summary>사다리를 따라 한 칸 이동. dir &gt; 0 이면 빨라진다.</summary>
        public static float Nudge(int dir)
        {
            int nearest = 0;
            float bestGap = float.MaxValue;
            for (int i = 0; i < Ladder.Length; i++)
            {
                float gap = Mathf.Abs(Ladder[i] - Multiplier);
                if (gap < bestGap) { bestGap = gap; nearest = i; }
            }

            int next = Mathf.Clamp(nearest + (dir > 0 ? 1 : -1), 0, Ladder.Length - 1);
            Set(Ladder[next]);
            return Multiplier;
        }

        /// <summary>매 프레임 호출된다. 일시정지 중에는 아무것도 하지 않는다.</summary>
        internal static void Apply()
        {
            if (Time.timeScale <= 0f) return;   // 일시정지 — 재개는 오버레이의 몫

            if (!Mathf.Approximately(Time.timeScale, Multiplier))
                Time.timeScale = Multiplier;

            float target = s_baseFixedDelta * Mathf.Min(Multiplier, MaxFixedStepFactor);
            if (!Mathf.Approximately(Time.fixedDeltaTime, target))
                Time.fixedDeltaTime = target;
        }
    }

}
#endif
