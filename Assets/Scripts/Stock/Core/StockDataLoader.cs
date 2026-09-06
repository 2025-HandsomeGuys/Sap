using System;
using System.Collections.Generic;
using UnityEngine;
using Stock.Data;

namespace Stock.Core
{
    public class StockDataLoader : MonoBehaviour
    {
        public static StockDataLoader Instance { get; private set; }

        public Dictionary<string, CompanyData> Companies { get; private set; } = new Dictionary<string, CompanyData>();
        public Dictionary<string, NewsTemplate> NewsTemplates { get; private set; } = new Dictionary<string, NewsTemplate>();
        public Dictionary<string, NewsChainData> NewsChains { get; private set; } = new Dictionary<string, NewsChainData>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        public void LoadAllData()
        {
            LoadCompanies();
            LoadNewsChains();
            LoadNewsTemplates();
        }

        private void LoadCompanies()
        {
            Companies.Clear();
            var sheet = DataSheetCache.Instance.GetSheet("Companies.csv");
            foreach (var row in sheet)
            {
                if (!row.TryGetValue("id", out string id) || string.IsNullOrEmpty(id)) continue;

                var data = new CompanyData
                {
                    Id = id,
                    NameKey = GetString(row, "nameKey"),
                    DescKey = GetString(row, "descKey"),
                    Tags = GetStringArray(row, "tags", '|'),
                    BasePrice = GetInt(row, "basePrice"),
                    Volatility = GetFloat(row, "volatility"),
                    Reliability = GetFloat(row, "reliability"),
                    Reputation = GetFloat(row, "reputation"),
                    MarketCap = GetString(row, "marketCap"),
                    ListingOrder = GetInt(row, "listingOrder"),
                    Tier = GetInt(row, "tier"),
                    CurrentPrice = GetInt(row, "basePrice")
                };
                Companies[id] = data;
            }
        }

        private void LoadNewsChains()
        {
            NewsChains.Clear();
            var sheet = DataSheetCache.Instance.GetSheet("NewsChains.csv");
            foreach (var row in sheet)
            {
                if (!row.TryGetValue("chainId", out string id) || string.IsNullOrEmpty(id)) continue;

                var data = new NewsChainData
                {
                    ChainId = id,
                    NameKey = GetString(row, "nameKey"),
                    TotalPhases = GetInt(row, "totalPhases"),
                    Weight = GetInt(row, "weight"),
                    MinDayGap = GetInt(row, "minDayGap"),
                    MaxDayGap = GetInt(row, "maxDayGap")
                };
                NewsChains[id] = data;
            }
        }

        private void LoadNewsTemplates()
        {
            NewsTemplates.Clear();
            var sheet = DataSheetCache.Instance.GetSheet("NewsTemplates.csv");
            foreach (var row in sheet)
            {
                if (!row.TryGetValue("id", out string id) || string.IsNullOrEmpty(id)) continue;

                var template = new NewsTemplate
                {
                    Id = id,
                    ChainId = GetString(row, "chainId"),
                    ChainOrder = GetInt(row, "chainOrder"),
                    Phase = ParseEnum<NewsPhase>(GetString(row, "phase")),
                    TitleKey = GetString(row, "titleKey"),
                    SummaryKey = GetString(row, "summaryKey"),
                    Tags = GetStringArray(row, "tags", '|'),
                    Sentiment = ParseEnum<NewsSentiment>(GetString(row, "sentiment")),
                    ImpactStrength = GetFloat(row, "impactStrength"),
                    VolatilityBoost = GetFloat(row, "volatilityBoost"),
                    DurationTicks = GetInt(row, "durationTicks"),
                    NextNewsIds = GetStringArray(row, "nextNewsIds", '|'),
                    NextWeights = GetIntArray(row, "nextWeights", '|'),
                    PricingInFactor = GetFloat(row, "pricingInFactor"),
                    CooldownTicks = GetInt(row, "cooldownTicks"),
                    HypeBypass = GetFloat(row, "hypeBypass") // 컬럼 없으면 0 = 일반 뉴스
                };

                // Parse TemplateParams JSON
                string templateParamsJson = GetString(row, "templateParams");
                if (!string.IsNullOrEmpty(templateParamsJson))
                {
                    template.TemplateParams = ParseSimpleDictionary(templateParamsJson);
                }

                // Parse Outcomes JSON
                string outcomesJson = GetString(row, "outcomes");
                if (!string.IsNullOrEmpty(outcomesJson))
                {
                    string wrappedJson = "{\"outcomes\":" + outcomesJson + "}";
                    var wrapper = JsonUtility.FromJson<OutcomeWrapper>(wrappedJson);
                    if (wrapper != null && wrapper.outcomes != null)
                    {
                        template.Outcomes = wrapper.outcomes;
                    }
                }

                NewsTemplates[id] = template;
            }
        }
        
        private Dictionary<string, string[]> ParseSimpleDictionary(string json)
        {
            var dict = new Dictionary<string, string[]>();
            if (string.IsNullOrEmpty(json)) return dict;

            // "key" : "value" 쌍을 안전하게 추출하는 정규식 패턴 정의
            var matches = System.Text.RegularExpressions.Regex.Matches(json, @"""([^""]+)""\s*:\s*""([^""]+)""");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (match.Groups.Count >= 3)
                {
                    string key = match.Groups[1].Value;
                    string value = match.Groups[2].Value;
                    string[] arr = value.Split('/');
                    dict[key] = arr;
                }
            }
            return dict;
        }

        private string GetString(Dictionary<string, string> row, string column)
        {
            return row.TryGetValue(column, out string val) ? val : string.Empty;
        }

        private int GetInt(Dictionary<string, string> row, string column)
        {
            if (row.TryGetValue(column, out string val) && int.TryParse(val, out int result))
                return result;
            return 0;
        }

        private float GetFloat(Dictionary<string, string> row, string column)
        {
            if (row.TryGetValue(column, out string val) && float.TryParse(val, out float result))
                return result;
            return 0f;
        }

        private string[] GetStringArray(Dictionary<string, string> row, string column, char separator)
        {
            if (row.TryGetValue(column, out string val) && !string.IsNullOrEmpty(val))
            {
                return val.Split(separator, StringSplitOptions.RemoveEmptyEntries);
            }
            return new string[0];
        }

        private int[] GetIntArray(Dictionary<string, string> row, string column, char separator)
        {
            string[] strArray = GetStringArray(row, column, separator);
            int[] result = new int[strArray.Length];
            for (int i = 0; i < strArray.Length; i++)
            {
                if (int.TryParse(strArray[i], out int val))
                    result[i] = val;
            }
            return result;
        }

        private T ParseEnum<T>(string value) where T : struct
        {
            if (Enum.TryParse(value, true, out T result))
                return result;
            return default;
        }
    }
}
