// @tags: market, ui, toggle, segmented, label, color, tab
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Market
{
    /// <summary>
    /// 세그먼트 토글(주식/코인 탭)의 라벨·배경 색을 on/off 상태에 따라 자동 전환한다.
    /// Unity Toggle은 "Graphic"의 표시/숨김만 처리하고 라벨 색은 안 바꿔주므로 이 보조 컴포넌트로 채운다.
    /// 각 Toggle과 같은 게임오브젝트에 부착하고 라벨(TMP)을 연결한다.
    /// 배경(Graphic)을 연결하면 알약 색도 같이 바뀐다(선택).
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class SegmentedToggleLabel : MonoBehaviour
    {
        [Header("라벨")]
        [SerializeField] private TextMeshProUGUI label;
        [Tooltip("선택(켜짐) 라벨 색 — 노란 알약 위 어두운 글자")]
        [SerializeField] private Color onTextColor = new Color(0.051f, 0.067f, 0.090f, 1f);   // #0D1117 계열
        [Tooltip("비선택(꺼짐) 라벨 색 — 흐린 글자")]
        [SerializeField] private Color offTextColor = new Color(0.545f, 0.596f, 0.663f, 1f);  // textMuted #8B98A9

        [Header("(선택) 배경/알약도 색 전환")]
        [SerializeField] private Graphic background;
        [Tooltip("선택 시 배경색 — 골드 알약")]
        [SerializeField] private Color onBgColor = new Color(0.961f, 0.773f, 0.259f, 1f);     // gold #F5C542
        [Tooltip("비선택 시 배경색 — 투명")]
        [SerializeField] private Color offBgColor = new Color(0f, 0f, 0f, 0f);

        private Toggle _toggle;

        private void Awake()
        {
            _toggle = GetComponent<Toggle>();
            _toggle.onValueChanged.AddListener(Apply);
        }

        private void OnEnable()
        {
            if (_toggle == null) _toggle = GetComponent<Toggle>();
            Apply(_toggle.isOn);
        }

        private void OnDestroy()
        {
            if (_toggle != null) _toggle.onValueChanged.RemoveListener(Apply);
        }

        private void Apply(bool on)
        {
            if (label) label.color = on ? onTextColor : offTextColor;
            if (background) background.color = on ? onBgColor : offBgColor;
        }
    }
}
