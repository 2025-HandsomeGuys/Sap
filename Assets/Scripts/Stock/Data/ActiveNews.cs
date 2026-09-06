using System;
using System.Collections.Generic;

namespace Stock.Data
{
    [Serializable]
    public class ActiveNews
    {
        public NewsTemplate Template;
        public int StartTick;
        public int RemainingTicks;
        public OutcomeEntry SelectedOutcome;
        public NewsPhase CurrentPhase;
        public bool WasPricedIn; // 선반영 여부
        
        // 런타임 랜덤 선택된 템플릿 매개변수 (예: "region" -> "A")
        public Dictionary<string, string> SelectedParams = new Dictionary<string, string>();

        public float GetEffectiveImpact()
        {
            if (string.IsNullOrEmpty(SelectedOutcome.label))
                return 0f;

            float base_ = SelectedOutcome.priceEffect * Template.ImpactStrength;
            return CurrentPhase switch
            {
                NewsPhase.Rumor => base_ * Template.PricingInFactor,
                NewsPhase.Official => WasPricedIn 
                    ? base_ * (1f - Template.PricingInFactor) 
                    : base_,
                NewsPhase.Fade => base_ * ((float)RemainingTicks / Template.DurationTicks) * 0.5f,
                _ => 0f
            };
        }

        public string GetSynthesizedTitle()
        {
            string title = (LanguageManager.Instance != null)
                ? LanguageManager.Instance.L(Template.TitleKey)
                : Template.TitleKey;

            foreach (var kvp in SelectedParams)
            {
                title = title.Replace("{" + kvp.Key + "}", LocalizeParam(kvp.Value));
            }
            return title;
        }

        public string GetSynthesizedSummary()
        {
            string summary = (LanguageManager.Instance != null)
                ? LanguageManager.Instance.L(Template.SummaryKey)
                : Template.SummaryKey;

            foreach (var kvp in SelectedParams)
            {
                summary = summary.Replace("{" + kvp.Key + "}", LocalizeParam(kvp.Value));
            }
            return summary;
        }

        /// <summary>
        /// 템플릿 파라미터 값을 지역화한다. 값이 로컬라이즈 키(예: PARAM_REGION_NATION)면 번역하고,
        /// 일반 리터럴이면 그대로 통과시킨다(폴백). 저장에는 키가 그대로 남아 언어 전환에 안전하다.
        /// </summary>
        private static string LocalizeParam(string value)
        {
            if (string.IsNullOrEmpty(value) || LanguageManager.Instance == null) return value;
            string v = LanguageManager.Instance.L(value);
            return string.IsNullOrEmpty(v) || v == value ? value : v;
        }
    }

    [Serializable]
    public class NewsHistoryEntry
    {
        public string NewsId;
        public int OccurredTick;
        public int ExpiredTick; // 뉴스가 종료 페이즈까지 마치고 히스토리로 넘어간 틱 — UI 자동 소멸 판정에 사용
        public string OutcomeLabel;
        public string SynthesizedTitle;
    }
}
