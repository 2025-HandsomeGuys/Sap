// @tags: bug-report, qa, ui, overlay, code-generated
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BugReport
{
    /// <summary>
    /// 메모 입력 오버레이. CodeUI 키트로 전부 코드 생성 — 씬 세팅 불필요.
    ///
    /// 스크린샷과 상태는 이 오버레이가 뜨기 전에 이미 확정돼 있다.
    /// 여기서는 메모 문자열만 채워 넣는다.
    ///
    /// IMGUI(OnGUI)를 쓰지 않는 이유: 빌드에서 한글 IME 입력이 깨진다.
    /// </summary>
    public class BugReportOverlayUI : MonoBehaviour
    {
        /// <summary>UIStateManager가 전역 단축키를 차단하는 데 쓴다.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>
        /// 닫힌 그 프레임의 ESC가 일시정지 토글로 새어나가는 것을 막는다.
        /// 프로젝트의 다른 코드 생성 오버레이와 같은 관례.
        /// </summary>
        public static bool ClosedThisFrame => Time.frameCount == s_closedFrame;
        private static int s_closedFrame = -1;

        private TMP_InputField _input;
        private Action<string> _onSubmit;
        private Action _onCancel;
        private bool _closed;
        private bool _focusRequested;

        public static BugReportOverlayUI Show(
            Texture2D screenshot, Action<string> onSubmit, Action onCancel)
        {
            CodeUI.EnsureEventSystem();

            var root = new GameObject("BugReportOverlay",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;   // 다른 모든 UI 위

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var ui = root.AddComponent<BugReportOverlayUI>();
            ui._onSubmit = onSubmit;
            ui._onCancel = onCancel;
            ui.Build(root.transform, screenshot);

            IsOpen = true;
            return ui;
        }

        private void Build(Transform parent, Texture2D screenshot)
        {
            // 암막 — 뒤쪽 클릭을 전부 먹는다.
            var dim = CodeUI.CreateImage(parent, "Dim", new Color(0f, 0f, 0f, 0.75f), null, null, false);
            CodeUI.StretchFull(dim.rectTransform);

            var panel = CodeUI.CreateImage(parent, "Panel", CodeUI.PanelBg);
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(900f, 640f);

            var title = CodeUI.CreateText(panel.transform, "Title", 34f, FontStyles.Bold,
                CodeUI.LabelColor, TextAlignmentOptions.Left);
            title.text = "버그 리포트";
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(30f, -80f);
            trt.offsetMax = new Vector2(-30f, -22f);

            BuildPreview(panel.transform, screenshot);
            BuildInput(panel.transform);
            BuildButtons(panel.transform);
        }

        /// <summary>
        /// 스크린샷 미리보기. 무엇이 찍혔는지 보고 메모를 쓴다.
        /// Sprite가 아니라 RawImage로 텍스처를 직접 물린다 — 런타임 Sprite는 수동 해제 대상이라 번거롭다.
        /// </summary>
        private void BuildPreview(Transform panel, Texture2D screenshot)
        {
            var holder = CodeUI.CreateRect(panel, "PreviewArea");
            holder.anchorMin = new Vector2(0f, 1f);
            holder.anchorMax = new Vector2(1f, 1f);
            holder.pivot = new Vector2(0.5f, 1f);
            holder.offsetMin = new Vector2(30f, -430f);
            holder.offsetMax = new Vector2(-30f, -92f);

            if (screenshot == null)
            {
                var missing = CodeUI.CreateText(holder, "Missing", 24f, FontStyles.Italic,
                    CodeUI.MutedColor, TextAlignmentOptions.Center);
                missing.text = "(스크린샷 캡처 실패 — 나머지 정보는 정상 저장됩니다)";
                CodeUI.StretchFull(missing.rectTransform);
                return;
            }

            var imgObj = new GameObject("Preview", typeof(RectTransform));
            imgObj.transform.SetParent(holder, false);
            var raw = imgObj.AddComponent<RawImage>();
            raw.texture = screenshot;
            raw.raycastTarget = false;

            var rrt = (RectTransform)imgObj.transform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.pivot = new Vector2(0.5f, 0.5f);

            var fitter = imgObj.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = (float)screenshot.width / Mathf.Max(1, screenshot.height);
        }

        /// <summary>
        /// TMP_InputField를 코드로 조립. 뷰포트·텍스트 컴포넌트를 손으로 연결해야 동작한다.
        /// </summary>
        private void BuildInput(Transform panel)
        {
            var box = CodeUI.CreateImage(panel, "InputBox", CodeUI.BoxBg);
            var brt = box.rectTransform;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.offsetMin = new Vector2(30f, 105f);
            brt.offsetMax = new Vector2(-30f, 195f);

            var viewport = CodeUI.CreateRect(box.transform, "TextArea");
            CodeUI.StretchFull(viewport);
            viewport.offsetMin = new Vector2(14f, 8f);
            viewport.offsetMax = new Vector2(-14f, -8f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var text = CodeUI.CreateText(viewport, "Text", 26f, FontStyles.Normal,
                Color.white, TextAlignmentOptions.TopLeft);
            text.textWrappingMode = TextWrappingModes.Normal;   // CodeUI 기본값은 NoWrap
            CodeUI.StretchFull(text.rectTransform);

            var placeholder = CodeUI.CreateText(viewport, "Placeholder", 26f, FontStyles.Italic,
                CodeUI.MutedColor, TextAlignmentOptions.TopLeft);
            placeholder.text = "무슨 일이 있었나요?  (예: 여기 벽이 안 막힘)";
            placeholder.textWrappingMode = TextWrappingModes.Normal;
            CodeUI.StretchFull(placeholder.rectTransform);

            _input = box.gameObject.AddComponent<TMP_InputField>();
            _input.textViewport = viewport;
            _input.textComponent = text;
            _input.placeholder = placeholder;
            // MultiLineSubmit: Enter가 줄바꿈을 넣지 않고 onSubmit을 쏜다.
            _input.lineType = TMP_InputField.LineType.MultiLineSubmit;
            _input.characterLimit = 500;
            _input.onSubmit.AddListener(_ => Close(true));
        }

        private void BuildButtons(Transform panel)
        {
            CodeUI.CreateTextButton(panel, "Save", CodeUI.PositiveColor, Color.white, 26f,
                () => Close(true), out var saveLabel);
            saveLabel.text = "저장 (Enter)";
            var srt = saveLabel.transform.parent.GetComponent<RectTransform>();
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 0f);
            srt.pivot = new Vector2(1f, 0f);
            srt.anchoredPosition = new Vector2(-30f, 25f);
            srt.sizeDelta = new Vector2(190f, 62f);

            CodeUI.CreateTextButton(panel, "Cancel", CodeUI.NeutralBg, Color.white, 26f,
                () => Close(false), out var cancelLabel);
            cancelLabel.text = "취소 (ESC)";
            var crt = cancelLabel.transform.parent.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 0f);
            crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-235f, 25f);
            crt.sizeDelta = new Vector2(190f, 62f);

            var hint = CodeUI.CreateText(panel, "Hint", 20f, FontStyles.Normal,
                CodeUI.MutedColor, TextAlignmentOptions.Left);
            hint.text = "스크린샷·좌표·로그·세이브가 함께 저장됩니다.";
            var hrt = hint.rectTransform;
            hrt.anchorMin = new Vector2(0f, 0f);
            hrt.anchorMax = new Vector2(0f, 0f);
            hrt.pivot = new Vector2(0f, 0f);
            hrt.anchoredPosition = new Vector2(30f, 42f);
            hrt.sizeDelta = new Vector2(460f, 30f);
        }

        private void Update()
        {
            if (_closed) return;

            // 생성 직후 한 프레임 뒤에 포커스를 준다 — 같은 프레임에 주면 놓치는 경우가 있다.
            if (!_focusRequested && _input != null)
            {
                _focusRequested = true;
                _input.ActivateInputField();
            }

            // Enter 저장은 TMP의 onSubmit이 처리한다(MultiLineSubmit).
            // 프로젝트의 UI 확인 키는 Space지만 텍스트 입력 중에는 Space가 공백 문자라 여기만 예외.
            if (Input.GetKeyDown(KeyCode.Escape)) Close(false);
        }

        private void Close(bool submit)
        {
            if (_closed) return;
            _closed = true;
            IsOpen = false;
            s_closedFrame = Time.frameCount;

            string memo = _input != null ? _input.text : "";
            var onSubmit = _onSubmit;
            var onCancel = _onCancel;

            Destroy(gameObject);

            if (submit) onSubmit?.Invoke(memo);
            else onCancel?.Invoke();
        }

        private void OnDestroy()
        {
            // 씬 전환 등으로 강제 파괴돼도 플래그가 켜진 채 남지 않게 한다.
            IsOpen = false;
        }
    }
}
#endif
