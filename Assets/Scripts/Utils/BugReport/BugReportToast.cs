// @tags: bug-report, qa, ui, toast, code-generated
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_BUG_REPORT
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BugReport
{
    /// <summary>
    /// 저장 결과를 화면 우하단에 잠깐 띄운다. 코드 생성 — 씬 세팅 불필요.
    ///
    /// 레벨디자이너가 콘솔을 안 보기 때문에 필요하다.
    /// '폴더 열기'는 리포트를 압축해 공유하러 갈 때 쓴다.
    /// </summary>
    public class BugReportToast : MonoBehaviour
    {
        private const float LifeSeconds = 6f;

        private float _born;

        public static void Show(string message, string folderPath)
        {
            CodeUI.EnsureEventSystem();

            var root = new GameObject("BugReportToast",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32001;   // 오버레이보다도 위

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var panel = CodeUI.CreateImage(root.transform, "Panel", CodeUI.CardBg);
            var prt = panel.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(1f, 0f);
            prt.pivot = new Vector2(1f, 0f);
            prt.anchoredPosition = new Vector2(-30f, 30f);
            prt.sizeDelta = new Vector2(760f, string.IsNullOrEmpty(folderPath) ? 110f : 160f);

            var label = CodeUI.CreateText(panel.transform, "Message", 22f, FontStyles.Normal,
                CodeUI.LabelColor, TextAlignmentOptions.TopLeft);
            label.text = message;
            label.textWrappingMode = TextWrappingModes.Normal;
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.offsetMin = new Vector2(20f, -95f);
            lrt.offsetMax = new Vector2(-20f, -14f);

            if (!string.IsNullOrEmpty(folderPath))
            {
                CodeUI.CreateTextButton(panel.transform, "OpenFolder",
                    CodeUI.NeutralBg, Color.white, 20f,
                    () => OpenFolder(folderPath), out var btnLabel);
                btnLabel.text = "폴더 열기";
                var brt = btnLabel.transform.parent.GetComponent<RectTransform>();
                brt.anchorMin = brt.anchorMax = new Vector2(1f, 0f);
                brt.pivot = new Vector2(1f, 0f);
                brt.anchoredPosition = new Vector2(-20f, 16f);
                brt.sizeDelta = new Vector2(150f, 44f);
            }

            var toast = root.AddComponent<BugReportToast>();
            toast._born = Time.unscaledTime;
        }

        private static void OpenFolder(string path)
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(path);
#else
            Application.OpenURL("file://" + path);
#endif
        }

        // 일시정지 직후에도 사라져야 하므로 unscaled 시간을 쓴다.
        private void Update()
        {
            if (Time.unscaledTime - _born > LifeSeconds) Destroy(gameObject);
        }
    }
}
#endif
