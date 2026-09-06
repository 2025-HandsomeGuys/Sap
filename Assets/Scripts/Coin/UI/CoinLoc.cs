// @tags: coin, ui, localization, helper
namespace Coin.UI
{
    /// <summary>
    /// 코인 UI용 지역화 헬퍼 (StockLoc 미러, 설계 §10.1). 키가 없으면 fallback을 사용한다.
    /// 덕분에 Coin_Localization.csv에 키를 넣기 전에도 UI가 한국어로 정상 표시된다.
    /// </summary>
    public static class CoinLoc
    {
        public static string L(string key, string fallback)
        {
            if (LanguageManager.Instance == null) return fallback;
            string v = LanguageManager.Instance.L(key);
            return string.IsNullOrEmpty(v) || v == key ? fallback : v;
        }

        public static string LF(string key, string fallback, params object[] args)
        {
            string fmt = L(key, fallback);
            if (string.IsNullOrEmpty(fmt)) return fmt;
            try { return string.Format(fmt, args); }
            catch { return fmt; }
        }
    }
}
