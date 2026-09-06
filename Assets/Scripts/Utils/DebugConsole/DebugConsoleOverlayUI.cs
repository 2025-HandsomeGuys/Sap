// @tags: debug, console, qa, ui, overlay, code-generated
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DebugTools
{
    /// <summary>
    /// 명령 입력 콘솔. CodeUI 키트로 전부 코드 생성 — 씬 세팅 불필요.
    ///
    /// 열려 있는 동안 게임은 <c>timeScale = 0</c>으로 멈춘다.
    /// 안 멈추면 타이핑하는 WASD가 그대로 플레이어에게 들어가고,
    /// 명령을 치는 몇 초 동안 스태미나·시간이 흘러 밸런스 측정이 오염된다.
    /// 배속을 눈으로 보면서 조절하고 싶으면 콘솔을 닫고 <c>[</c> / <c>]</c> 단축키를 쓴다.
    ///
    /// IMGUI(OnGUI)를 쓰지 않는 이유는 BugReportOverlayUI와 같다 — 빌드에서 한글 IME가 깨진다.
    /// </summary>
    public class DebugConsoleOverlayUI : MonoBehaviour
    {
        /// <summary>UIStateManager가 전역 단축키를 차단하는 데 쓴다.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>닫힌 그 프레임의 ESC가 일시정지 토글로 새어나가는 것을 막는다.</summary>
        public static bool ClosedThisFrame => Time.frameCount == s_closedFrame;
        private static int s_closedFrame = -1;

        private static DebugConsoleOverlayUI s_instance;

        // 로그와 히스토리는 콘솔을 닫아도 유지된다 — 껐다 켤 때마다 날아가면 쓸모가 없다.
        private static readonly List<string> s_log = new List<string>();
        private static readonly List<string> s_history = new List<string>();
        private const int MaxLogLines = 300;

        private TMP_InputField _input;
        private TextMeshProUGUI _logText;
        private ScrollRect _scroll;
        private int _historyCursor = -1;
        private float _prevTimeScale = 1f;
        private bool _closed;
        private bool _focusRequested;

        // ───────────────────────────────────────────────────────────
        // 외부 진입점
        // ───────────────────────────────────────────────────────────

        public static void Toggle()
        {
            if (IsOpen) s_instance?.Close();
            else Open();
        }

        public static void Open()
        {
            if (IsOpen) return;

            CodeUI.EnsureEventSystem();

            var root = new GameObject("DebugConsoleOverlay",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(root);

            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;   // 다른 모든 UI 위 (버그 리포트와 같은 층)

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            s_instance = root.AddComponent<DebugConsoleOverlayUI>();
            s_instance.Build(root.transform);

            IsOpen = true;

            // 일시정지. PauseOverlayUI와 같은 방식 — 1f로 하드코딩하지 않는다.
            // 이미 멈춰 있는 화면(상점·일시정지) 위에서 열었다가 닫을 때 게임이 멋대로 재개되면 안 된다.
            s_instance._prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        public static void CloseStatic() => s_instance?.Close();

        /// <summary>콘솔이 닫혀 있어도 로그를 남길 수 있다(치트가 비동기로 결과를 알릴 때).</summary>
        public static void Print(string line)
        {
            if (string.IsNullOrEmpty(line)) return;

            s_log.Add(line);
            if (s_log.Count > MaxLogLines) s_log.RemoveRange(0, s_log.Count - MaxLogLines);

            s_instance?.RefreshLog(true);
        }

        public static void ClearLog()
        {
            s_log.Clear();
            s_instance?.RefreshLog(false);
        }

        // ───────────────────────────────────────────────────────────
        // 조립
        // ───────────────────────────────────────────────────────────

        private void Build(Transform parent)
        {
            // 암막 — 뒤쪽 클릭을 전부 먹는다. 화면 위쪽 절반은 게임이 보이도록 얕게.
            var dim = CodeUI.CreateImage(parent, "Dim", new Color(0f, 0f, 0f, 0.35f), null, null, false);
            CodeUI.StretchFull(dim.rectTransform);

            var panel = CodeUI.CreateImage(parent, "Panel", CodeUI.PanelBg);
            var prt = panel.rectTransform;
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(1f, 0f);
            prt.pivot = new Vector2(0.5f, 0f);
            prt.offsetMin = new Vector2(40f, 40f);
            prt.offsetMax = new Vector2(-40f, 620f);

            BuildHeader(panel.transform);
            BuildLogView(panel.transform);
            BuildInput(panel.transform);

            RefreshLog(true);
        }

        private void BuildHeader(Transform panel)
        {
            var title = CodeUI.CreateText(panel, "Title", 26f, FontStyles.Bold,
                CodeUI.LabelColor, TextAlignmentOptions.Left);
            title.text = "디버그 콘솔   <color=#7B86A0>Enter 실행 · ↑↓ 히스토리 · Tab 자동완성 · ESC 닫기</color>";
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(24f, -52f);
            trt.offsetMax = new Vector2(-24f, -16f);
        }

        private void BuildLogView(Transform panel)
        {
            _scroll = CodeUI.CreateScrollView(panel, "LogScroll", out var content);
            var srt = (RectTransform)_scroll.transform;
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(1f, 1f);
            srt.offsetMin = new Vector2(24f, 86f);
            srt.offsetMax = new Vector2(-24f, -56f);

            // content에는 ContentSizeFitter(PreferredSize)가 이미 달려 있다.
            // 레이아웃 그룹을 얹어야 자식 TMP의 preferredHeight가 content 높이로 전달된다.
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _logText = CodeUI.CreateText(content, "Log", 22f, FontStyles.Normal,
                CodeUI.LabelColor, TextAlignmentOptions.TopLeft);
            _logText.textWrappingMode = TextWrappingModes.Normal;   // CodeUI 기본값은 NoWrap
            _logText.richText = true;
        }

        private void BuildInput(Transform panel)
        {
            var box = CodeUI.CreateImage(panel, "InputBox", CodeUI.BoxBg);
            var brt = box.rectTransform;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.offsetMin = new Vector2(24f, 18f);
            brt.offsetMax = new Vector2(-24f, 74f);

            var viewport = CodeUI.CreateRect(box.transform, "TextArea");
            CodeUI.StretchFull(viewport);
            viewport.offsetMin = new Vector2(14f, 6f);
            viewport.offsetMax = new Vector2(-14f, -6f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var text = CodeUI.CreateText(viewport, "Text", 24f, FontStyles.Normal,
                Color.white, TextAlignmentOptions.Left);
            CodeUI.StretchFull(text.rectTransform);

            var placeholder = CodeUI.CreateText(viewport, "Placeholder", 24f, FontStyles.Italic,
                CodeUI.MutedColor, TextAlignmentOptions.Left);
            placeholder.text = "명령 입력  (help)";
            CodeUI.StretchFull(placeholder.rectTransform);

            _input = box.gameObject.AddComponent<TMP_InputField>();
            _input.textViewport = viewport;
            _input.textComponent = text;
            _input.placeholder = placeholder;
            _input.lineType = TMP_InputField.LineType.SingleLine;
            _input.onSubmit.AddListener(Submit);
        }

        // ───────────────────────────────────────────────────────────
        // 동작
        // ───────────────────────────────────────────────────────────

        private void Submit(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                _input.ActivateInputField();
                return;
            }

            Print($"<color=#7B8FD4>> {line}</color>");

            if (s_history.Count == 0 || s_history[s_history.Count - 1] != line)
                s_history.Add(line);
            _historyCursor = -1;

            string result = DebugCommandRegistry.Execute(line);
            if (!string.IsNullOrEmpty(result)) Print(result);

            _input.text = "";
            _input.ActivateInputField();   // 연속 입력 — 포커스를 놓지 않는다
        }

        private void RefreshLog(bool scrollToBottom)
        {
            if (_logText == null) return;

            var sb = new StringBuilder();
            for (int i = 0; i < s_log.Count; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(s_log[i]);
            }
            _logText.text = sb.ToString();

            if (scrollToBottom && _scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                _scroll.verticalNormalizedPosition = 0f;
            }
        }

        private void Update()
        {
            if (_closed) return;

            // 생성 직후 한 프레임 뒤에 포커스를 준다 — 같은 프레임에 주면 놓치는 경우가 있다.
            if (!_focusRequested && _input != null)
            {
                _focusRequested = true;
                _input.ActivateInputField();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

            if (Input.GetKeyDown(KeyCode.UpArrow)) StepHistory(-1);
            else if (Input.GetKeyDown(KeyCode.DownArrow)) StepHistory(1);
            else if (Input.GetKeyDown(KeyCode.Tab)) AutoComplete();
        }

        private void StepHistory(int dir)
        {
            if (s_history.Count == 0 || _input == null) return;

            if (_historyCursor < 0) _historyCursor = s_history.Count;
            _historyCursor = Mathf.Clamp(_historyCursor + dir, 0, s_history.Count);

            _input.text = _historyCursor >= s_history.Count ? "" : s_history[_historyCursor];
            _input.caretPosition = _input.text.Length;
            _input.ActivateInputField();
        }

        private void AutoComplete()
        {
            if (_input == null) return;

            string typed = _input.text ?? "";

            // 첫 토큰은 명령 이름, 그 뒤는 그 명령이 내놓는 인자 후보
            // (명령이 ArgCompleter를 안 걸었으면 후보가 비어 아무 일도 안 일어난다).
            List<string> matches;
            string prefix;

            int lastSpace = typed.LastIndexOf(' ');
            if (lastSpace < 0)
            {
                prefix = typed;
                matches = DebugCommandRegistry.Complete(prefix);
            }
            else
            {
                prefix = typed.Substring(lastSpace + 1);

                // 지금 타이핑 중인 토큰은 빼고 넘긴다 — 명령은 '몇 번째 인자 차례인가'로 후보를 고른다.
                string[] tokens = typed.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0) return;   // 공백만 친 경우

                int confirmed = Mathf.Max(0, tokens.Length - (prefix.Length > 0 ? 2 : 1));
                var argsSoFar = new string[confirmed];
                for (int i = 0; i < confirmed; i++) argsSoFar[i] = tokens[i + 1];

                matches = DebugCommandRegistry.CompleteArgs(tokens[0], argsSoFar, prefix);
            }

            if (matches.Count == 0) return;

            if (matches.Count == 1)
            {
                _input.text = typed.Substring(0, typed.Length - prefix.Length) + matches[0] + " ";
                _input.caretPosition = _input.text.Length;
            }
            else
            {
                Print("<color=#7B86A0>" + string.Join("   ", matches) + "</color>");
            }

            _input.ActivateInputField();
        }

        private void Close()
        {
            if (_closed) return;
            _closed = true;
            IsOpen = false;
            s_closedFrame = Time.frameCount;
            s_instance = null;

            // 열기 전 값으로 되돌린다. 배속이 걸려 있으면 DebugTimeScaleDriver가
            // 다음 LateUpdate에서 다시 덮어쓰므로 여기서 배속을 신경 쓸 필요가 없다.
            Time.timeScale = _prevTimeScale;

            CodeUI.ClearSelection();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            // 씬 전환 등으로 강제 파괴돼도 플래그가 켜진 채 남지 않게 한다.
            IsOpen = false;
            if (s_instance == this) s_instance = null;
        }
    }
}
#endif
