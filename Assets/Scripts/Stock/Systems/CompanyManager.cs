using System;
using System.Collections.Generic;
using UnityEngine;
using Stock.Data;
using Stock.Core;

namespace Stock.Systems
{
    public class CompanyManager
    {
        private Dictionary<string, CompanyData> _companies = new Dictionary<string, CompanyData>();
        private List<CompanyData> _companiesList = new List<CompanyData>();

        // 해금 필터 결과 캐시. 티어가 바뀔 때만 다시 만든다(리스트 UI가 매 재빌드마다 호출하므로).
        private readonly List<CompanyData> _unlockedList = new List<CompanyData>();
        private int _unlockedCacheTier = -1;

        public void Initialize()
        {
            _companies.Clear();
            _companiesList.Clear();
            _unlockedList.Clear();
            _unlockedCacheTier = -1;

            var loadedCompanies = StockDataLoader.Instance.Companies;
            foreach (var kvp in loadedCompanies)
            {
                // 깊은 복사 또는 새 인스턴스로 런타임 데이터 초기화
                var comp = new CompanyData
                {
                    Id = kvp.Value.Id,
                    NameKey = kvp.Value.NameKey,
                    DescKey = kvp.Value.DescKey,
                    Tags = kvp.Value.Tags,
                    BasePrice = kvp.Value.BasePrice,
                    Volatility = kvp.Value.Volatility,
                    Reliability = kvp.Value.Reliability,
                    Reputation = kvp.Value.Reputation,
                    MarketCap = kvp.Value.MarketCap,
                    ListingOrder = kvp.Value.ListingOrder,
                    Tier = kvp.Value.Tier,
                    CurrentPrice = kvp.Value.BasePrice
                };
                
                // 가격 히스토리 초기화 (시작 가격)
                comp.PriceHistory.Clear();
                comp.PriceHistory.Add(comp.CurrentPrice);

                _companies[comp.Id] = comp;
                _companiesList.Add(comp);
            }

            // 정렬 순서대로 리스트 정렬
            _companiesList.Sort((a, b) => a.ListingOrder.CompareTo(b.ListingOrder));
        }

        /// <summary>전 종목(잠긴 것 포함). 시뮬레이션·세이브 전용 —
        /// 잠긴 종목도 계속 시세가 돌아야 해금 시점에 죽은 차트가 아니라 살아있는 차트로 등장한다.
        /// <b>UI에는 쓰지 말 것</b>(→ <see cref="GetUnlockedCompanies"/>).</summary>
        public IReadOnlyList<CompanyData> GetAllCompanies()
        {
            return _companiesList;
        }

        /// <summary>현재 채광 레벨로 해금된 종목만(ListingOrder 순서 유지). 리스트·필터 등 UI용.</summary>
        public IReadOnlyList<CompanyData> GetUnlockedCompanies()
        {
            int tier = StockUnlockGate.UnlockedTier;
            if (tier != _unlockedCacheTier)
            {
                _unlockedList.Clear();
                foreach (var c in _companiesList)
                    if (StockUnlockGate.IsUnlocked(c)) _unlockedList.Add(c);
                _unlockedCacheTier = tier;
            }
            return _unlockedList;
        }

        /// <summary>잠긴 종목 수(해금 안내용).</summary>
        public int LockedCount => _companiesList.Count - GetUnlockedCompanies().Count;

        public CompanyData GetCompany(string id)
        {
            if (_companies.TryGetValue(id, out var company))
                return company;
            return null;
        }

        // 세이브 데이터 복구용
        public void RestoreCompanyPrices(Dictionary<string, List<int>> priceHistories)
        {
            if (priceHistories == null) return;

            foreach (var kvp in priceHistories)
            {
                if (_companies.TryGetValue(kvp.Key, out var comp))
                {
                    comp.PriceHistory = new List<int>(kvp.Value);
                    if (comp.PriceHistory.Count > 0)
                    {
                        comp.CurrentPrice = comp.PriceHistory[comp.PriceHistory.Count - 1];
                    }
                }
            }
        }
    }
}
