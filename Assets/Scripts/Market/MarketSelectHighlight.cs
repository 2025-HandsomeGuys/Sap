// @tags: market, ui, button, selected, highlight, preset, percent, leverage
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Market
{
    /// <summary>
    /// 프리셋 버튼(스테이크 %·레버리지 배율·소지금 % 등)의 '현재 선택됨' 상태를
    /// 눈에 띄게 표시하는 헬퍼. 프리팹 세팅 없이 코드가 자동 부착한다.
    /// 선택 시 배경 = 액센트 시안, 라벨 = 어두운 색. 해제 시 원래 색으로 복원한다.
    /// 주식(CompanyDetailUI)·코인(CoinBettingUI)이 공유한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class MarketSelectHighlight : MonoBehaviour
    {
        private Graphic _bg;
        private TMP_Text _label;
        private Color _bgBase, _labelBase;
        private bool _captured;

        /// <summary>버튼의 선택 표시를 갱신한다. 처음 선택될 때 자동 부착·원본 색 캡처.</summary>
        public static void Set(Button button, bool selected)
        {
            if (button == null) return;
            var h = button.GetComponent<MarketSelectHighlight>();
            if (h == null)
            {
                if (!selected) return; // 선택된 적 없는 버튼은 손대지 않는다(복원할 것도 없음)
                h = button.gameObject.AddComponent<MarketSelectHighlight>();
            }
            h.Apply(button, selected);
        }

        private void Apply(Button button, bool selected)
        {
            if (!_captured)
            {
                if (!selected) return;
                _bg = button.targetGraphic != null ? button.targetGraphic : button.GetComponent<Graphic>();
                _label = button.GetComponentInChildren<TMP_Text>(true);
                if (_bg != null) _bgBase = _bg.color;
                if (_label != null) _labelBase = _label.color;
                _captured = true;
            }
            if (_bg != null) _bg.color = selected ? MarketTheme.AccentCyan : _bgBase;
            if (_label != null) _label.color = selected ? MarketTheme.ScreenBg : _labelBase;
        }
    }
}
