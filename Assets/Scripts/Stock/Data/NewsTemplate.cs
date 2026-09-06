using System.Collections.Generic;

namespace Stock.Data
{
    public class NewsTemplate
    {
        public string Id;
        public string ChainId;
        public int ChainOrder;
        public NewsPhase Phase;
        public string TitleKey;
        public string SummaryKey;
        public Dictionary<string, string[]> TemplateParams = new Dictionary<string, string[]>();
        public string[] Tags;
        public NewsSentiment Sentiment;
        public float ImpactStrength;
        public float VolatilityBoost;
        public int DurationTicks;
        public OutcomeEntry[] Outcomes;
        public string[] NextNewsIds;
        public int[] NextWeights;
        public float PricingInFactor;
        public int CooldownTicks;

        /// <summary>
        /// 급등주(테마주) 플래그. 0=일반 뉴스(1틱 변동 상한에 갇힘),
        /// >0=이 뉴스의 주가 영향이 일반 상한을 우회한다(급등/폭락이 여러 틱 누적돼 몇 배까지 감).
        /// 최종 가격은 여전히 basePrice×0.1~10 클램프로만 제한된다. StockPriceEngine 참고.
        /// </summary>
        public float HypeBypass;
    }
}
