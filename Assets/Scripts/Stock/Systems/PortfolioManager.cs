using System;
using System.Collections.Generic;
using UnityEngine;
using Stock.Data;
using Stock.Core;

namespace Stock.Systems
{
    /// <summary>
    /// 플레이어 포트폴리오(보유 주식) 관리.
    /// 매수/매도는 기존 골드 시스템(PlayerStat)을 그대로 사용하며, 시세는 CompanyManager에서 읽는다.
    ///
    /// 설계 규칙:
    ///  - 당일(같은 틱) 매수한 종목은 당일 매도 불가 (다음 틱부터 가능)
    ///  - 보유 기간 제한(인내심) 없음 — 원하는 만큼 장기 보유 가능
    /// </summary>
    public class PortfolioManager
    {
        private CompanyManager _companyManager;
        private PlayerStat _playerStat;
        private readonly List<OwnedStock> _holdings = new List<OwnedStock>();

        /// <summary>포트폴리오 구성이 바뀔 때(매수/매도) 발생. UI 갱신용.</summary>
        public event Action OnPortfolioChanged;

        public void Initialize(CompanyManager companyManager)
        {
            _companyManager = companyManager;
            _holdings.Clear();
        }

        // ===================================================
        // 조회
        // ===================================================
        public IReadOnlyList<OwnedStock> GetHoldings() => _holdings;

        /// <summary>현재 플레이어 보유 골드(소지금). PlayerStat을 찾지 못하면 0. 매수 비율(%) 계산용.</summary>
        public long CurrentGold
        {
            get { var stat = ResolvePlayerStat(); return stat != null ? stat.Gold : 0L; }
        }

        public OwnedStock GetHolding(string companyId)
            => _holdings.Find(h => h.CompanyId == companyId);

        /// <summary>당일 매수분도 매도 가능(당일 매수·당일 매도 허용). 보유분이 있으면 언제든 매도 가능.</summary>
        public bool CanSell(OwnedStock stock, int currentTick)
            => stock != null && stock.Shares > 0;

        /// <summary>현재 보유분 전체의 매입 원가 총합.</summary>
        public long GetTotalCost()
        {
            long sum = 0;
            foreach (var h in _holdings) sum += h.GetCostBasis();
            return sum;
        }

        /// <summary>현재 시세 기준 평가 금액 총합.</summary>
        public long GetTotalValue()
        {
            long sum = 0;
            foreach (var h in _holdings)
            {
                var c = _companyManager?.GetCompany(h.CompanyId);
                if (c != null) sum += (long)c.CurrentPrice * h.Shares;
            }
            return sum;
        }

        /// <summary>전체 평가 수익률(0.05 = +5%).</summary>
        public float GetProfitRate()
        {
            long cost = GetTotalCost();
            if (cost <= 0) return 0f;
            return (float)(GetTotalValue() - cost) / cost;
        }

        /// <summary>전체 평가 손익 금액(평가금액 - 매입원가).</summary>
        public long GetProfitAmount() => GetTotalValue() - GetTotalCost();

        /// <summary>현재 보유 중인 종목 수.</summary>
        public int GetHoldingCount() => _holdings.Count;

        // ===================================================
        // 거래
        // ===================================================
        /// <summary>지정 종목을 현재가로 매수. 성공 시 true.</summary>
        public bool Buy(string companyId, int shares)
        {
            if (shares <= 0) return false;

            var company = _companyManager?.GetCompany(companyId);
            if (company == null)
            {
                Debug.LogWarning($"[PortfolioManager] Buy 실패: 알 수 없는 종목 {companyId}");
                return false;
            }

            var stat = ResolvePlayerStat();
            if (stat == null)
            {
                Debug.LogWarning("[PortfolioManager] Buy 실패: PlayerStat을 찾을 수 없습니다.");
                return false;
            }

            long totalCost = (long)company.CurrentPrice * shares;
            if (stat.Gold < totalCost)
            {
                Debug.Log($"[PortfolioManager] 골드 부족: 필요 {totalCost}, 보유 {stat.Gold}");
                return false;
            }

            if (!stat.SpendGold((int)totalCost)) return false;
            // 매수는 '손익'이 아니라 자산 교환이므로 일일 장부에 기록하지 않는다(실현 손익만 집계).
            // 매수로 빠져나간 골드는 최종 손익 총액(현재 골드 기준)엔 그대로 반영되고 미매도분은 '기타'로 흡수된다.

            int currentTick = CurrentTick();
            var holding = GetHolding(companyId);
            if (holding == null)
            {
                _holdings.Add(new OwnedStock(companyId, shares, company.CurrentPrice, currentTick));
            }
            else
            {
                // 평균 단가 재계산 후 보유 수량 갱신.
                long totalShares = (long)holding.Shares + shares;
                long weighted = (long)holding.AvgBuyPrice * holding.Shares + (long)company.CurrentPrice * shares;
                holding.AvgBuyPrice = (int)(weighted / totalShares);
                holding.Shares = (int)totalShares;
                holding.PurchasedTick = currentTick;
            }

            Debug.Log($"[PortfolioManager] 매수: {companyId} x{shares} @ ₩{company.CurrentPrice:N0} (총 ₩{totalCost:N0})");
            OnPortfolioChanged?.Invoke();
            return true;
        }

        /// <summary>지정 종목을 현재가로 매도. 당일 매수분은 매도 불가. 성공 시 true.</summary>
        public bool Sell(string companyId, int shares)
        {
            if (shares <= 0) return false;

            var holding = GetHolding(companyId);
            if (holding == null || holding.Shares < shares)
            {
                Debug.Log($"[PortfolioManager] 매도 실패: 보유 수량 부족 ({companyId})");
                return false;
            }

            int currentTick = CurrentTick();
            if (!CanSell(holding, currentTick))
            {
                Debug.Log($"[PortfolioManager] 매도 실패: 당일 매수 종목은 매도할 수 없습니다 ({companyId})");
                return false;
            }

            return SellInternal(holding, shares);
        }

        private bool SellInternal(OwnedStock holding, int shares)
        {
            var company = _companyManager?.GetCompany(holding.CompanyId);
            if (company == null) return false;

            var stat = ResolvePlayerStat();
            long proceeds = (long)company.CurrentPrice * shares;
            long costBasis = (long)holding.AvgBuyPrice * shares; // 매도분의 평균 매입 원가(Shares 차감 전에 계산)
            if (stat != null)
            {
                stat.AddGold((int)proceeds);
                // 실현 손익(매도금액 − 매입원가)만 일일 장부에 기록한다 — 매수는 미기록이므로 여기서 순손익이 잡힌다.
                DayEarningsLedger.Report(DayEarningsCategory.Stock, (int)(proceeds - costBasis));
            }

            holding.Shares -= shares;
            if (holding.Shares <= 0) _holdings.Remove(holding);

            Debug.Log($"[PortfolioManager] 매도: {holding.CompanyId} x{shares} @ ₩{company.CurrentPrice:N0} (수령 ₩{proceeds:N0})");

            OnPortfolioChanged?.Invoke();
            return true;
        }

        // ===================================================
        // 틱 처리 — 보유 기간 제한(인내심) 삭제로 현재는 처리할 일이 없다.
        // 시그니처는 StockGameManager.ProcessTick 호출부 호환을 위해 유지한다.
        // ===================================================
        public void ProcessTick(int currentTick) { }

        // ===================================================
        // 세이브 연동
        // ===================================================
        public List<OwnedStock> ExportHoldings() => new List<OwnedStock>(_holdings);

        public void RestoreHoldings(List<OwnedStock> list)
        {
            _holdings.Clear();
            if (list != null) _holdings.AddRange(list);
            OnPortfolioChanged?.Invoke();
        }

        // ===================================================
        // 내부 헬퍼
        // ===================================================
        private int CurrentTick()
            => StockGameManager.Instance != null ? StockGameManager.Instance.CurrentTick : 0;

        private PlayerStat ResolvePlayerStat()
        {
            // 씬 전환으로 참조가 끊길 수 있으므로 null일 때마다 재탐색한다. (SaveManager와 동일 패턴)
            if (_playerStat == null)
                _playerStat = UnityEngine.Object.FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
            return _playerStat;
        }
    }
}
