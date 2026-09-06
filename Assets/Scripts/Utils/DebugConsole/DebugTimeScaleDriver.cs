// @tags: debug, console, qa, timescale, hud, indicator
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ENABLE_DEBUG_CONSOLE
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DebugTools
{
    /// <summary>
    /// 배속을 매 프레임 재적용하고(<see cref="DebugTimeScale"/> 참고), 1배가 아닐 때
    /// 화면 우상단에 표시기를 띄운다. DebugTimeScale.Bootstrap이 자동 생성한다.
    ///
    /// 표시기는 장식이 아니다 — 배속을 켜둔 걸 잊고 측정한 수치는 전부 쓰레기가 되므로,
    /// 켜져 있다는 사실이 항상 눈에 보여야 한다.
    ///
    /// LateUpdate인 이유: 오버레이들이 Update/열기 시점에 timeScale을 건드리므로
    /// 마지막 발언권을 가져야 한다.
    /// </summary>
    internal class DebugTimeScaleDriver : MonoBehaviour
    {
        private GameObject _hud;
        private TextMeshProUGUI _label;

        private void LateUpdate()
        {
            DebugTimeScale.Apply();
            UpdateHud();
        }

        private void UpdateHud()
        {
            if (!DebugTimeScale.IsActive)
            {
                if (_hud != null) Destroy(_hud);
                _hud = null;
                _label = null;
                return;
            }

            if (_hud == null) BuildHud();
            if (_label != null) _label.text = $"■ TIME x{DebugTimeScale.Multiplier:0.##}";
        }

        private void BuildHud()
        {
            _hud = new GameObject("DebugTimeScaleHud", typeof(Canvas), typeof(CanvasScaler));
            DontDestroyOnLoad(_hud);

            var canvas = _hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31900;   // 콘솔(32000) 바로 아래, 게임 UI 위

            var scaler = _hud.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // GraphicRaycaster를 붙이지 않는다 — 표시기가 클릭을 먹으면 안 된다.
            _label = CodeUI.CreateText(_hud.transform, "Label", 26f, FontStyles.Bold,
                CodeUI.WarnColor, TextAlignmentOptions.Right);

            var rt = _label.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-24f, -18f);
            rt.sizeDelta = new Vector2(280f, 36f);
        }

        private void OnDestroy()
        {
            if (_hud != null) Destroy(_hud);
        }
    }
}
#endif
