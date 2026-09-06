// @tags: coin, ui, streak, combo, session, hud
using UnityEngine;
using TMPro;
using Coin.Core;
using Market;

namespace Coin.UI
{
    /// <summary>연승 콤보 + 세션 손익 표시 (설계 §7.6 재미요소).</summary>
    public class StreakHUD : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI streakText;
        [SerializeField] private TextMeshProUGUI sessionPnLText;

        public void Refresh()
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null) return;

            if (streakText)
                streakText.text = mgr.CurrentStreak >= 2
                    ? CoinLoc.LF("ui_coin_streak", "🔥 {0}연승", mgr.CurrentStreak)
                    : "";

            if (sessionPnLText)
            {
                long p = mgr.SessionPnL;
                string sign = p >= 0 ? "+" : "−";
                sessionPnLText.text = CoinLoc.LF("ui_coin_session", "세션 {0}₩{1:N0}", sign, System.Math.Abs(p));
                sessionPnLText.color = p >= 0 ? MarketTheme.Up : MarketTheme.Down;
            }
        }
    }
}
