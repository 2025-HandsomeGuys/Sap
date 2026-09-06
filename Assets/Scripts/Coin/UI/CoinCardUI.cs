// @tags: coin, ui, card, slot
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Coin.Core;
using Coin.Data;
using Market;

namespace Coin.UI
{
    /// <summary>
    /// 단일 슬롯 카드 (난이도 선택 좌측 목록의 한 행).
    /// 디자인: 코인 영문명 + 가격(상단 한 줄) / "위험" 라벨 + 위험 게이지 막대 + 위험 점(dots).
    /// 카드 클릭 = 선택(우측 미리보기 갱신)이며, 실제 진입은 미리보기의 "스테이지 진입" 버튼이 담당한다.
    /// 따라서 골드 부족이어도 카드는 클릭(미리보기)할 수 있고, lockedOverlay는 시각 힌트로만 쓴다.
    /// </summary>
    public class CoinCardUI : MonoBehaviour
    {
        [Header("텍스트")]
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI priceText;

        [Header("위험 표시")]
        [SerializeField] private Slider riskGaugeSlider; // 게이지를 Slider(Handle 제거, ProgressBar)로 쓸 때
        [SerializeField] private Image riskGaugeFill;    // 게이지 Fill 이미지(색만 코드가 지정). Slider면 Fill을 연결
        [SerializeField] private Image[] riskDots;        // 위험 티어 점(예 5개). 켜진 개수 = riskTier

        [Header("상호작용")]
        [SerializeField] private Button button;
        [SerializeField] private GameObject selectionHighlight; // 선택 시 골드 테두리/배경 (기본 비활성)
        [SerializeField] private GameObject lockedOverlay;      // 골드 부족 시각 힌트 (Raycast Off 권장)
        [SerializeField] private CanvasGroup dimGroup;          // 오늘 잠겨 선택 불가일 때 흐리게 (옵션)

        [Header("선택(옵션, 미연결 시 무시)")]
        [SerializeField] private TextMeshProUGUI swingText;
        [SerializeField] private TextMeshProUGUI betRangeText;
        [SerializeField] private TextMeshProUGUI flavorText;

        private int _slotId;
        private Action<int> _onClick;
        private bool _lockedOut;     // 소지금이 minBet에 못 미쳐 선택 불가(자연 해금 게이트)
        private bool _isTodaysCoin;  // (미사용) 과거 '오늘의 코인' 잠금 잔재

        // 프리팹의 기본 텍스트 색. 잠금 → 복귀 시 원래 색으로 되돌리기 위해 한 번 캡처한다.
        private Color _nameBaseColor = MarketTheme.TextPrimary;
        private Color _priceBaseColor = MarketTheme.TextMuted;
        private bool _baseColorsCaptured;

        public int SlotId => _slotId;

        public void Bind(int slotId, Action<int> onClick)
        {
            _slotId = slotId;
            _onClick = onClick;
            if (!_baseColorsCaptured)
            {
                if (nameText) _nameBaseColor = nameText.color;
                if (priceText) _priceBaseColor = priceText.color;
                _baseColorsCaptured = true;
            }
            if (button)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => _onClick?.Invoke(_slotId));
            }
            Refresh();
        }

        /// <summary>선택 강조 토글(골드 테두리). CoinSelectUI가 단일 선택을 관리한다.</summary>
        public void SetSelected(bool on)
        {
            if (selectionHighlight) selectionHighlight.SetActive(on);
        }

        /// <summary>오늘 잠금 상태 적용. on=true는 '오늘의 코인'(선택 가능), false는 잠겨 선택 불가.</summary>
        public void SetInteractable(bool on)
        {
            _isTodaysCoin = on;
            _lockedOut = !on;
            if (button) button.interactable = on;
            ApplyLockVisual();
        }

        // 소지금이 모자란 코인을 또렷이 '선택 불가'로: 흐림 + 자물쇠 + 회색 이름 + 오버레이.
        private void ApplyLockVisual()
        {
            if (dimGroup) dimGroup.alpha = _lockedOut ? 0.3f : 1f;
            var state = CoinGameManager.Instance != null ? CoinGameManager.Instance.Slot(_slotId) : null;
            string baseName = state != null ? state.currentName : null;
            if (nameText && baseName != null)
            {
                nameText.text = _lockedOut ? CoinLoc.LF("ui_coin_locked_name", "🔒 {0}", baseName) : baseName;
                nameText.color = _lockedOut ? MarketTheme.TextMuted : _nameBaseColor;
            }
            if (lockedOverlay) lockedOverlay.SetActive(_lockedOut);
        }

        public void Refresh()
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null) return;
            var tier = mgr.SlotTier(_slotId);
            var state = mgr.Slot(_slotId);
            if (tier == null || state == null) return;

            if (nameText) nameText.text = state.currentName;

            // 카드에는 코인 표시가가 아니라 '입장에 드는 돈'(minBet = 자연 해금 게이트)을 보여준다.
            // 소지금이 충분하면 입장액, 모자라면 필요액(흐린 색)으로 구분한다.
            bool affordable = mgr.CanAfford(_slotId);
            if (priceText)
            {
                priceText.text = affordable
                    ? CoinLoc.LF("ui_coin_entry", "입장 ₩{0:N0}", tier.minBet)
                    : CoinLoc.LF("ui_coin_need",  "필요 ₩{0:N0}", tier.minBet);
                priceText.color = affordable ? _priceBaseColor : MarketTheme.TextDim;
            }

            float risk01 = Mathf.Clamp01(tier.riskTier / 5f);
            Color riskColor = MarketTheme.SentimentColor(1f - risk01); // 낮음=초록, 높음=빨강

            // 게이지 값: Slider(ProgressBar)면 normalizedValue로, Image(Filled) 단독이면 fillAmount로.
            if (riskGaugeSlider)
            {
                riskGaugeSlider.interactable = false;     // 표시 전용(드래그 방지)
                riskGaugeSlider.normalizedValue = risk01;
            }
            if (riskGaugeFill)
            {
                riskGaugeFill.color = riskColor;          // Fill 색 = 위험도 색(자동)
                if (!riskGaugeSlider) riskGaugeFill.fillAmount = risk01;
            }
            if (riskDots != null)
            {
                for (int i = 0; i < riskDots.Length; i++)
                {
                    if (!riskDots[i]) continue;
                    bool lit = i < tier.riskTier;
                    riskDots[i].color = lit ? riskColor : new Color(riskColor.r, riskColor.g, riskColor.b, 0.2f);
                }
            }

            // 옵션 텍스트(연결되어 있으면 채움)
            if (swingText)
                swingText.text = CoinLoc.LF("ui_coin_swing", "변동 {0}%~{1}%",
                    Mathf.RoundToInt(tier.minSwing * 100f), Mathf.RoundToInt(tier.maxSwing * 100f));
            if (betRangeText)
            {
                string maxLabel = tier.unlimitedMax ? "∞" : CoinLoc.LF("ui_coin_price", "₩{0:N0}", tier.maxBet);
                betRangeText.text = CoinLoc.LF("ui_coin_bet_range", "베팅 ₩{0:N0}~{1}", tier.minBet, maxLabel);
            }
            // 플레이버는 현재 상장된 코인 기준(순환 코인마다 다름). 키가 없으면 슬롯 기본 코인 플레이버로 폴백.
            if (flavorText)
                flavorText.text = CoinLoc.L(CoinData.FlavorKeyFor(state.currentName), CoinLoc.L(tier.flavorKey, ""));

            // minBet > 보유골드면 자연 해금 게이트에 걸린 잠금 코인 — 카드 선택 불가(설계 §357/§506).
            // 돈이 불어나면 그 자리에서 자연히 해금된다(별도 해금 플래그 없음).
            _lockedOut = !affordable;
            if (button) button.interactable = affordable;

            ApplyLockVisual(); // 흐림/자물쇠/이름색/오버레이를 잠금 상태에 맞춰 적용
        }
    }
}
