namespace Stock.UI
{
    /// <summary>
    /// 주식 UI용 지역화 헬퍼. LanguageManager에 키가 없으면(L()이 키를 그대로 반환) fallback을 사용한다.
    /// 덕분에 Stock_Localization.csv에 키를 넣기 전에도 UI가 한국어로 정상 표시된다.
    /// </summary>
    public static class StockLoc
    {
        public static string L(string key, string fallback)
        {
            if (LanguageManager.Instance == null) return fallback;
            string v = LanguageManager.Instance.L(key);
            return string.IsNullOrEmpty(v) || v == key ? fallback : v;
        }

        /// <summary>지역화 포맷 문자열을 가져와 args로 포맷한다. 키가 없으면 fallback 포맷을 사용한다.</summary>
        public static string LF(string key, string fallback, params object[] args)
        {
            string fmt = L(key, fallback);
            if (string.IsNullOrEmpty(fmt)) return fmt;
            try { return string.Format(fmt, args); }
            catch { return fmt; }
        }

        /// <summary>raw 태그 id(chip 등) → STOCK_TAG_{대문자} 키로 지역화. 키 없으면 raw 폴백.
        /// 태그를 화면에 '표시'할 때만 쓴다 — 매칭·저장 로직은 raw 문자열 그대로 사용할 것.</summary>
        public static string Tag(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            return L("STOCK_TAG_" + raw.ToUpperInvariant(), raw);
        }
    }
}
