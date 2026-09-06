using UnityEngine;
using Stock.Core;
using Stock.Data;
using System.Text;

namespace Stock.Testing
{
    /// <summary>
    /// 주식 시뮬레이션 시스템 Phase 2 통합 테스트 및 콘솔 덤프 디버그 도구
    /// </summary>
    public class StockTestRunner : MonoBehaviour
    {
        [Header("시뮬레이션 조작 설정")]
        [Tooltip("체크 시 키보드 Space바를 누르면 하루(1틱)가 경과합니다.")]
        [SerializeField] private bool _enableKeyboardInput = true;

        [Header("대량 틱 시뮬레이션")]
        [Range(1, 100)]
        [SerializeField] private int _bulkTicks = 10;

        [Header("포트폴리오 매매 테스트")]
        [Tooltip("매수/매도 테스트에 사용할 종목 ID")]
        [SerializeField] private string _testCompanyId = "comp_medigen";
        [SerializeField] private int _testShares = 10;

        private void Start()
        {
            // 주식 시뮬레이션 시스템의 이벤트 구독 연동
            if (StockGameManager.Instance != null)
            {
                StockGameManager.Instance.OnSystemInitialized += OnSystemInitialized;
                StockGameManager.Instance.OnTickProcessed += OnTickProcessed;
            }
            else
            {
                Debug.LogError("[StockTestRunner] 씬에 StockGameManager 인스턴스가 존재하지 않습니다!");
            }
        }

        private void Update()
        {
            if (_enableKeyboardInput && Input.GetKeyDown(KeyCode.Space))
            {
                TriggerSingleTick();
            }
        }

        private void OnDestroy()
        {
            if (StockGameManager.Instance != null)
            {
                StockGameManager.Instance.OnSystemInitialized -= OnSystemInitialized;
                StockGameManager.Instance.OnTickProcessed -= OnTickProcessed;
            }
        }

        private void OnSystemInitialized()
        {
            // 자동 콘솔 덤프는 제거됨. 필요 시 인스펙터 컨텍스트 메뉴로 PrintMarketStatus 수동 호출.
        }

        private void OnTickProcessed(int tick)
        {
            // 자동 콘솔 덤프는 제거됨. 필요 시 인스펙터 컨텍스트 메뉴로 PrintMarketStatus 수동 호출.
        }

        /// <summary>
        /// 1틱 수동 전진 (Space 입력 또는 인스펙터 우클릭 메뉴에서 실행 가능)
        /// </summary>
        [ContextMenu("1틱 실행 (Space)")]
        public void TriggerSingleTick()
        {
            if (StockGameManager.Instance == null) return;
            StockGameManager.Instance.ProcessTick();
        }

        /// <summary>
        /// 인스펙터 우클릭 컨텍스트 메뉴를 통해 대량 틱 자동 진행 실행
        /// </summary>
        [ContextMenu("선택한 대량 틱 실행")]
        public void TriggerBulkTicks()
        {
            if (StockGameManager.Instance == null) return;
            
            Debug.Log($"<color=orange><b>[StockTestRunner] {_bulkTicks}틱 고속 연속 시뮬레이션을 시작합니다.</b></color>");
            for (int i = 0; i < _bulkTicks; i++)
            {
                StockGameManager.Instance.ProcessTick();
            }
            Debug.Log($"<color=orange><b>[StockTestRunner] {_bulkTicks}틱 정산 완료. 최종 상태를 확인하십시오.</b></color>");
        }

        [ContextMenu("테스트: 매수")]
        public void TestBuy()
        {
            var pm = StockGameManager.Instance?.PortfolioManager;
            if (pm == null) { Debug.LogError("[StockTestRunner] PortfolioManager 없음"); return; }
            bool ok = pm.Buy(_testCompanyId, _testShares);
            Debug.Log($"[StockTestRunner] 매수 시도 {_testCompanyId} x{_testShares} → {(ok ? "성공" : "실패")}");
            PrintPortfolio();
        }

        [ContextMenu("테스트: 매도")]
        public void TestSell()
        {
            var pm = StockGameManager.Instance?.PortfolioManager;
            if (pm == null) { Debug.LogError("[StockTestRunner] PortfolioManager 없음"); return; }
            bool ok = pm.Sell(_testCompanyId, _testShares);
            Debug.Log($"[StockTestRunner] 매도 시도 {_testCompanyId} x{_testShares} → {(ok ? "성공" : "실패")}");
            PrintPortfolio();
        }

        [ContextMenu("포트폴리오 출력")]
        public void PrintPortfolio()
        {
            var pm = StockGameManager.Instance?.PortfolioManager;
            if (pm == null) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"<color=yellow><b>========== [포트폴리오] ==========</b></color>");
            sb.AppendLine($"평가금액: ₩{pm.GetTotalValue():N0} / 원가: ₩{pm.GetTotalCost():N0} / 수익률: {pm.GetProfitRate() * 100f:F1}%");
            var holdings = pm.GetHoldings();
            if (holdings.Count == 0)
            {
                sb.AppendLine("  (보유 종목 없음)");
            }
            else
            {
                foreach (var h in holdings)
                {
                    var comp = StockGameManager.Instance.CompanyManager.GetCompany(h.CompanyId);
                    int cur = comp != null ? comp.CurrentPrice : 0;
                    sb.AppendLine($"  * {h.CompanyId}: {h.Shares}주, 평단 ₩{h.AvgBuyPrice:N0}, 현재 ₩{cur:N0} (매수틱 {h.PurchasedTick})");
                }
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// 시장 및 개별 주가의 실시간 변동 지표 콘솔 덤프 리포트
        /// </summary>
        [ContextMenu("시장 통계 리포트 출력")]
        private void PrintMarketStatus()
        {
            var manager = StockGameManager.Instance;
            if (manager == null) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"========== [주식 시장 통계 리포트] Tick: {manager.CurrentTick} ==========");
            sb.AppendLine($"시장 전체 공포/탐욕 심리 지수 (Sentiment): {manager.SentimentTracker.Sentiment:F3}");

            // 1. 현재 실시간으로 시장에 영향을 끼치고 있는 뉴스 출력
            var activeNews = manager.NewsManager.GetActiveNews();
            sb.AppendLine($"▶ 활성 뉴스 목록 ({activeNews.Count}개):");
            foreach (var news in activeNews)
            {
                sb.AppendLine($"  - [{news.CurrentPhase}] {news.GetSynthesizedTitle()} (효과: {news.GetEffectiveImpact() * 100f:F1}%, 남은 기간: {news.RemainingTicks}틱)");
            }

            // 2. 회사들의 현재 주가 및 전일 대비 변동률 정밀 계산
            var companies = manager.CompanyManager.GetAllCompanies();
            sb.AppendLine("▶ 상장 기업 주가 동향:");
            foreach (var comp in companies)
            {
                int prevPrice = comp.PriceHistory.Count > 1 ? comp.PriceHistory[comp.PriceHistory.Count - 2] : comp.BasePrice;
                float changeRatio = prevPrice > 0 ? (float)(comp.CurrentPrice - prevPrice) / prevPrice * 100f : 0f;
                string changeSign = changeRatio > 0 ? "+" : "";
                string colorTag = changeRatio > 0 ? "red" : (changeRatio < 0 ? "blue" : "black");
                
                sb.AppendLine($"  * <color={colorTag}>{comp.Id}</color> ({(comp.Tags != null ? string.Join("|", comp.Tags) : "-")}) : ₩{comp.CurrentPrice:N0} (<color={colorTag}>{changeSign}{changeRatio:F1}%</color>) [시작가: ₩{comp.BasePrice:N0}]");
            }
            
            // 3. 현재 배후에서 진행 중인 이벤트 스릴러 스토리 체인
            var chains = manager.NewsManager.GetActiveChains();
            sb.AppendLine($"▶ 진행 중인 이벤트 시나리오 체인 ({chains.Count}개):");
            foreach (var ch in chains)
            {
                sb.AppendLine($"  - 시나리오 ID: {ch.ChainId} (현재 단계: {ch.CurrentStep}, 다음 뉴스 스폰 틱: {ch.NextScheduledTick})");
            }

            Debug.Log(sb.ToString());
        }
    }
}
