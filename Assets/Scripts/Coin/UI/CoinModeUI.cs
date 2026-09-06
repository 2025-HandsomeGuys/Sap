// @tags: coin, ui, mode, container
using UnityEngine;
using Coin.Core;

namespace Coin.UI
{
    /// <summary>
    /// 코인 계열 컨테이너 컨트롤러 (설계 §8.5). CoinSelectView ↔ BettingView 전환.
    /// </summary>
    public class CoinModeUI : MonoBehaviour
    {
        [SerializeField] private GameObject selectView;
        [SerializeField] private GameObject bettingView;
        [SerializeField] private CoinSelectUI selectUI;
        [SerializeField] private CoinBettingUI bettingUI;

        /// <summary>코인 모드 진입. 항상 선택 화면을 먼저 보여준다(오늘 잠긴 코인은 선택 화면에서 강조·재진입).</summary>
        public void Open() => ShowSelect();

        public void ShowSelect()
        {
            var mgr = CoinGameManager.Instance;
            if (mgr != null) mgr.ClearActiveSlot(); // 미확정 진입 해제(잠금엔 영향 없음)
            if (selectView) selectView.SetActive(true);
            if (bettingView) bettingView.SetActive(false);
            if (selectUI) selectUI.Build();
        }

        /// <summary>
        /// "스테이지 진입" 버튼 처리. 진입 가능한 코인은 아무거나 베팅 화면으로 들어간다(하루 1코인 잠금 없음).
        /// </summary>
        public void EnterSlot(int slotId)
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null) return;
            if (!mgr.BeginSlot(slotId)) return; // 골드 부족 등
            ShowBetting();
        }

        public void BackToSelect() => ShowSelect();

        private void ShowBetting()
        {
            if (selectView) selectView.SetActive(false);
            if (bettingView) bettingView.SetActive(true);
            if (bettingUI) bettingUI.Bind();
        }
    }
}
