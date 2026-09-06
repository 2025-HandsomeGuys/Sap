// @tags: market, sound, sfx, ui, button, hook, auto
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Market
{
    /// <summary>
    /// 자기 씬 전체의 Button/Toggle/TMP_InputField에 UI 효과음(MarketUISfx)을 자동으로 붙이는 후커.
    /// MarketSceneController가 Awake에서 자동 부착하므로 에디터 셋업이 필요 없다.
    ///
    /// - 씬 루트들을 통째로 스캔하므로 컨트롤러가 Canvas 밖(Managers)에 있어도 UI 전체를 잡는다.
    /// - 주식 리스트 행·코인 카드처럼 런타임에 생성되는 UI는 rescanInterval 주기 재스캔으로 포섭
    ///   (HashSet 중복 방지 — 이미 훅된 버튼엔 다시 붙지 않는다).
    /// - Additive 로드된 마켓 씬 안의 오브젝트만 대상 — 게임플레이 씬 UI에는 영향 없음.
    /// - 소리 매핑 정책은 Classify()에 모여 있고, 개별 버튼은 MarketSfxOverride로 바꾼다.
    /// </summary>
    public class MarketButtonSfx : MonoBehaviour
    {
        [Tooltip("런타임 생성 버튼(리스트 행·코인 카드 등)을 포섭하는 재스캔 주기(초)")]
        [SerializeField] private float rescanInterval = 0.5f;

        private readonly HashSet<Button> _buttons = new HashSet<Button>();
        private readonly HashSet<Toggle> _toggles = new HashSet<Toggle>();
        private readonly HashSet<TMP_InputField> _inputs = new HashSet<TMP_InputField>();
        private float _nextScan;

        private void OnEnable()
        {
            Rescan();
            _nextScan = Time.unscaledTime + rescanInterval;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + rescanInterval;
            Rescan();
        }

        private void Rescan()
        {
            // 파괴된 UI 정리 (리스트 리빌드 시 행이 Destroy될 수 있음)
            _buttons.RemoveWhere(b => b == null);
            _toggles.RemoveWhere(t => t == null);
            _inputs.RemoveWhere(f => f == null);

            var roots = gameObject.scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                foreach (var b in root.GetComponentsInChildren<Button>(true))
                    if (_buttons.Add(b))
                    {
                        var captured = b;
                        // Kind는 클릭 시점에 판정 — 나중에 붙인 MarketSfxOverride도 즉시 반영
                        b.onClick.AddListener(() => MarketUISfx.Play(Classify(captured)));
                    }

                foreach (var t in root.GetComponentsInChildren<Toggle>(true))
                    if (_toggles.Add(t))
                    {
                        var captured = t;
                        // ON이 되는 순간만 — ToggleGroup이 짝을 끌 때의 이중음 방지
                        t.onValueChanged.AddListener(on => { if (on) MarketUISfx.Play(ClassifyToggle(captured)); });
                    }

                foreach (var f in root.GetComponentsInChildren<TMP_InputField>(true))
                    if (_inputs.Add(f))
                    {
                        f.onSelect.AddListener(_ => MarketUISfx.Play(MarketUISfx.Kind.Select));
                        // 타이핑 틱 — 코드의 SetTextWithoutNotify는 이벤트가 없어 무음
                        f.onValueChanged.AddListener(_ => MarketUISfx.Play(MarketUISfx.Kind.Type));
                    }
            }
        }

        /// <summary>버튼별 소리 결정 — 마켓 UI 사운드 매핑 정책의 단일 지점.</summary>
        private static MarketUISfx.Kind Classify(Button b)
        {
            if (b == null) return MarketUISfx.Kind.None;
            var ov = b.GetComponent<MarketSfxOverride>();
            if (ov != null) return ov.kind;
            if (b.GetComponentInParent<Stock.UI.StockListItemUI>() != null) return MarketUISfx.Kind.Select; // 종목 선택
            if (b.GetComponentInParent<Coin.UI.CoinCardUI>() != null) return MarketUISfx.Kind.Select;       // 코인 카드 선택
            return MarketUISfx.Kind.Click;
        }

        private static MarketUISfx.Kind ClassifyToggle(Toggle t)
        {
            if (t == null) return MarketUISfx.Kind.None;
            var ov = t.GetComponent<MarketSfxOverride>();
            return ov != null ? ov.kind : MarketUISfx.Kind.Tab; // 토글 = 탭/모드 전환
        }
    }
}
