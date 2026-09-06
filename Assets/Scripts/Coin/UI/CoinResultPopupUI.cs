// @tags: coin, ui, result, popup, banner
using System.Collections;
using UnityEngine;
using TMPro;
using Coin.Data;
using Market;

namespace Coin.UI
{
    /// <summary>승/패/극단(상장폐지·떡상) 배너 플래시 (설계 §8.9).</summary>
    public class CoinResultPopupUI : MonoBehaviour
    {
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI deltaText;
        [SerializeField] private TextMeshProUGUI subText;   // 새 코인 상장 안내 등
        [SerializeField] private float showSeconds = 1.2f;
        [SerializeField] private float fadeSeconds = 0.3f;
        [Tooltip("델타 금액 0→목표 카운트업 시간(초)")]
        [SerializeField] private float countUpSeconds = 0.4f;

        private Coroutine _co;
        private int _deltaTarget;
        private Color _deltaColor;

        // 시작 시 디자인용 더미 텍스트("New Text")가 한 프레임 보이지 않게 비우고 숨긴다.
        private void Awake()
        {
            if (titleText) titleText.text = "";
            if (deltaText) deltaText.text = "";
            if (subText) subText.text = "";
            if (group)
            {
                group.alpha = 0f;
                group.gameObject.SetActive(false);
            }
        }

        public void Show(CoinRoundResult r)
        {
            if (group == null) return;

            string title;
            Color color;
            if (r.delisted) { title = CoinLoc.L("ui_coin_delist", "💀 상장폐지"); color = MarketTheme.Down; }
            else if (r.liquidated) { title = CoinLoc.L("ui_coin_liquidated", "🧨 청산!"); color = MarketTheme.Down; }
            else if (r.extreme && r.win) { title = CoinLoc.L("ui_coin_moon", "🚀 떡상!"); color = MarketTheme.Up; }
            else if (r.extreme) { title = CoinLoc.L("ui_coin_crash", "💥 폭락"); color = MarketTheme.Down; }
            else if (r.win) { title = CoinLoc.L("ui_coin_win", "적중!"); color = MarketTheme.Up; }
            else { title = CoinLoc.L("ui_coin_lose", "빗나감"); color = MarketTheme.Down; }

            if (titleText) { titleText.text = title; titleText.color = color; }
            _deltaTarget = r.delta;
            _deltaColor = color;
            if (deltaText) { deltaText.color = color; deltaText.text = FormatDelta(0); }
            if (subText)
            {
                subText.text = (r.delisted && !string.IsNullOrEmpty(r.replacementName))
                    ? CoinLoc.LF("ui_coin_new_listing", "🌱 신규 상장: {0}", r.replacementName)
                    : "";
            }

            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(ShowRoutine());
        }

        private static string FormatDelta(int v)
        {
            string sign = v >= 0 ? "+" : "−";
            return $"{sign}₩{Mathf.Abs(v):N0}";
        }

        private IEnumerator ShowRoutine()
        {
            group.gameObject.SetActive(true);
            group.alpha = 1f;

            // 델타 금액 0→목표로 카운트업
            if (deltaText && countUpSeconds > 0f && _deltaTarget != 0)
            {
                float t = 0f;
                while (t < countUpSeconds)
                {
                    t += Time.unscaledDeltaTime;
                    int shown = Mathf.RoundToInt(Mathf.Lerp(0f, _deltaTarget, t / countUpSeconds));
                    deltaText.text = FormatDelta(shown);
                    deltaText.color = _deltaColor;
                    yield return null;
                }
            }
            if (deltaText) deltaText.text = FormatDelta(_deltaTarget);

            yield return new WaitForSecondsRealtime(showSeconds);

            float ft = 0f;
            while (ft < fadeSeconds)
            {
                ft += Time.unscaledDeltaTime;
                group.alpha = 1f - ft / fadeSeconds;
                yield return null;
            }
            group.alpha = 0f;
            group.gameObject.SetActive(false);
        }
    }
}
