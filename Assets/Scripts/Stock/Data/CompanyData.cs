using System.Collections.Generic;

namespace Stock.Data
{
    public class CompanyData
    {
        public string Id;
        public string NameKey;     // Localization key
        public string DescKey;
        public string[] Tags;   // 종목 분류는 태그로만 표현한다(구 Sector 폐지). [0]=대표 태그
        public int BasePrice;
        public float Volatility;
        public float Reliability;
        public float Reputation;
        public string MarketCap;
        public int ListingOrder;
        public int Tier;           // 해금 티어 (1=시작 개방, 2~4=업그레이드·신규 지역으로 순차 해금)
        
        // Runtime variables
        public int CurrentPrice;
        public List<int> PriceHistory = new List<int>();
        
        public string GetLocalizedName()
        {
            if (LanguageManager.Instance != null)
                return LanguageManager.Instance.L(NameKey);
            return NameKey;
        }

        public string GetLocalizedDesc()
        {
            if (LanguageManager.Instance != null)
                return LanguageManager.Instance.L(DescKey);
            return DescKey;
        }

        /// <summary>태그를 하나라도 가지고 있는지(대소문자 무시).</summary>
        public bool HasTag(string tag)
        {
            if (Tags == null || string.IsNullOrEmpty(tag)) return false;
            foreach (var t in Tags)
                if (string.Equals(t, tag, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
