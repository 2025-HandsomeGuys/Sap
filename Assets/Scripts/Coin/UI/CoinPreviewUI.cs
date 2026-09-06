// @tags: coin, ui, preview, select, chart, live
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Coin.Core;
using Coin.Data;
using Coin.Systems;
using Market;

namespace Coin.UI
{
    /// <summary>
    /// 난이도 선택 화면 우측 "스테이지 미리보기" 패널.
    /// 좌측 목록에서 코인을 선택하면 그 코인의 실시간 시세(미리보기 차트)·정보를 보여주고,
    /// "스테이지 진입" 버튼으로 실제 잠금+베팅 진입을 트리거한다.
    ///
    /// 미리보기 차트는 잠금과 무관한 별도 CoinPriceEngine으로 돌아간다(코스메틱, 골드 영향 없음).
    /// </summary>
    public class CoinPreviewUI : MonoBehaviour
    {
        [SerializeField] private CoinModeUI modeUI;
        [SerializeField] private CoinChartUI chart;

        [Header("헤더")]
        [SerializeField] private TextMeshProUGUI coinNameText;
        [SerializeField] private TextMeshProUGUI riskBadgeText;  // "고위험 스테이지"
        [SerializeField] private Image riskBadgeBg;              // 배지 배경(색만 코드가 지정)
        [SerializeField] private TextMeshProUGUI priceText;      // "₩2,860"
        [SerializeField] private TextMeshProUGUI changeText;     // "▲ 30.9%"

        [Header("정보 3열")]
        [SerializeField] private TextMeshProUGUI swingText;      // 변동폭 "14% ~ 30%"
        [SerializeField] private TextMeshProUGUI extremeText;    // 극단 확률 "15%"
        [SerializeField] private TextMeshProUGUI betRangeText;   // 베팅 한도 "최대 2,000 G"

        [Header("하단")]
        [SerializeField] private TextMeshProUGUI warningText;    // ⚠ 플레이버/경고 한 줄
        [SerializeField] private Button enterButton;            // 스테이지 진입
        [SerializeField] private TextMeshProUGUI enterButtonText;

        [Header("라이브")]
        [Tooltip("미리보기 차트가 새 점을 추가하는 간격(초)")]
        [SerializeField] private float tickInterval = 0.7f;

        // 슬롯별 미리보기 엔진을 캐시 — 다른 코인 갔다 와도 처음부터 다시 안 그리고 "이어서" 흐른다.
        // 코인 인스턴스가 갈리면(currentName 변경) 해당 슬롯만 새로 시작한다.
        private readonly Dictionary<int, CoinPriceEngine> _engines = new Dictionary<int, CoinPriceEngine>();
        private readonly Dictionary<int, string> _engineNames = new Dictionary<int, string>();
        private CoinPriceEngine _engine;
        private int _slotId = -1;
        private bool _live;
        private float _tick;

        // 좌측 목록에서 D(오른쪽)로 넘어왔을 때 '스테이지 진입' 버튼에 씌우는 키보드 포커스 링.
        private CodeNavButton _enterNav;

        private void Awake()
        {
            if (enterButton)
            {
                enterButton.onClick.AddListener(OnEnter);
                _enterNav = CodeNavButton.Attach(enterButton);   // 코드 생성 포커스 링(기본 스킨)
                if (_enterNav != null) _enterNav.SetNavFocus(false);
            }
        }

        /// <summary>지금 스테이지 진입이 가능한지(버튼이 살아 있고 활성).</summary>
        public bool CanEnter => enterButton != null && enterButton.interactable
                                && enterButton.gameObject.activeInHierarchy;

        /// <summary>'스테이지 진입' 버튼의 키보드 포커스 링 on/off (좌측 목록이 제어).</summary>
        public void SetEnterFocus(bool on)
        {
            if (_enterNav != null) _enterNav.SetNavFocus(on && CanEnter);
        }

        /// <summary>스페이스바로 스테이지 진입 실행(가능할 때만).</summary>
        public void ActivateEnter()
        {
            if (CanEnter) OnEnter();
        }

        private void OnDisable() => _live = false;

        /// <summary>슬롯을 미리보기로 띄운다(잠금 없음). 좌측 카드 선택 시 호출.</summary>
        public void Show(int slotId)
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null) return;
            var tier = mgr.SlotTier(slotId);
            var state = mgr.Slot(slotId);
            if (tier == null || state == null) return;

            _slotId = slotId;

            // 캐시된 엔진이 있고 같은 코인 인스턴스면 이어서, 없거나 코인이 갈렸으면 새로 시작.
            bool sameCoin = _engineNames.TryGetValue(slotId, out var cachedName) && cachedName == state.currentName;
            if (!_engines.TryGetValue(slotId, out _engine) || _engine == null || !sameCoin)
            {
                _engine = new CoinPriceEngine();
                _engine.StartCoin(tier, state.currentBasePrice);
                _engines[slotId] = _engine;
                _engineNames[slotId] = state.currentName;
            }

            if (chart) chart.SetHistory(_engine.History);

            RefreshStatic(tier, state);
            RefreshLive();

            _live = true;
            _tick = 0f;
        }

        private void Update()
        {
            if (!_live || _engine == null) return;
            _tick += Time.unscaledDeltaTime;
            if (_tick < tickInterval) return;
            _tick = 0f;

            _engine.StepCosmetic();
            if (chart) chart.SetHistory(_engine.History);
            RefreshLive();
        }

        private void RefreshStatic(CoinData tier, CoinSlotState state)
        {
            if (coinNameText) coinNameText.text = state.currentName;

            // 위험 배지: 라벨은 티어로 구간 결정, 색은 게이지와 동일한 위험도 색(연속) 사용.
            float risk01 = Mathf.Clamp01(tier.riskTier / 5f);
            Color col = MarketTheme.SentimentColor(1f - risk01); // 낮음=초록 → 높음=빨강
            string badge = tier.riskTier >= 4 ? CoinLoc.L("ui_coin_risk_high", "고위험 스테이지")
                         : tier.riskTier == 3 ? CoinLoc.L("ui_coin_risk_mid",  "변동성 스테이지")
                                              : CoinLoc.L("ui_coin_risk_low",  "안전 스테이지");
            if (riskBadgeText) { riskBadgeText.text = badge; riskBadgeText.color = col; }
            if (riskBadgeBg)   riskBadgeBg.color = new Color(col.r, col.g, col.b, 0.22f);

            if (swingText)
                swingText.text = CoinLoc.LF("ui_coin_swing_short", "{0}% ~ {1}%",
                    Mathf.RoundToInt(tier.minSwing * 100f), Mathf.RoundToInt(tier.maxSwing * 100f));

            if (extremeText)
            {
                if (tier.extremeChance > 0f)
                {
                    extremeText.text = Mathf.RoundToInt(tier.extremeChance * 100f) + "%";
                    extremeText.color = MarketTheme.Down;
                }
                else
                {
                    extremeText.text = CoinLoc.L("ui_coin_extreme_none", "없음");
                    extremeText.color = MarketTheme.TextMuted;
                }
            }

            if (betRangeText)
            {
                string maxLabel = tier.unlimitedMax ? "∞" : CoinLoc.LF("ui_coin_gold", "{0:N0} G", tier.maxBet);
                betRangeText.text = CoinLoc.LF("ui_coin_bet_max", "최대 {0}", maxLabel);
            }

            // 플레이버는 현재 상장된 코인 기준(순환 코인마다 다름). 키가 없으면 슬롯 기본 코인 플레이버로 폴백.
            if (warningText)
                warningText.text = CoinLoc.L(CoinData.FlavorKeyFor(state.currentName), CoinLoc.L(tier.flavorKey, ""));

            if (enterButtonText) enterButtonText.text = CoinLoc.L("ui_coin_enter", "스테이지 진입");
        }

        private void RefreshLive()
        {
            var hist = _engine.History;
            float price = _engine.CurrentPrice;

            if (priceText) priceText.text = CoinLoc.LF("ui_coin_price", "₩{0:N0}", price);

            if (changeText && hist.Count > 0)
            {
                float baseP = hist[0];
                float pct = baseP > 0f ? (price / baseP - 1f) * 100f : 0f;
                bool up = pct >= 0f;
                changeText.text = (up ? "▲ " : "▼ ") + Mathf.Abs(pct).ToString("0.0") + "%";
                changeText.color = up ? MarketTheme.Up : MarketTheme.Down;
            }

            // 진입 게이트: 진입 가능한 코인은 아무거나(하루 1코인 잠금 없음) — 골드가 최소 베팅액 이상이면 진입.
            var mgr = CoinGameManager.Instance;
            if (enterButton && mgr != null)
                enterButton.interactable = mgr.CanAfford(_slotId);
        }

        private void OnEnter()
        {
            if (_slotId < 0 || modeUI == null) return;
            _live = false;
            modeUI.EnterSlot(_slotId);
        }
    }
}
