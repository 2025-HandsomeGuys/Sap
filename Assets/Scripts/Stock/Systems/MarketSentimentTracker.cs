using System;
using System.Collections.Generic;
using UnityEngine;
using Stock.Data;

namespace Stock.Systems
{
    public class MarketSentimentTracker
    {
        public float Sentiment { get; private set; } = 0.5f; // 0.0 (Extreme Fear) ~ 1.0 (Extreme Greed)

        public event Action<float> OnSentimentChanged;

        public void Initialize(float initialSentiment)
        {
            Sentiment = Mathf.Clamp01(initialSentiment);
            OnSentimentChanged?.Invoke(Sentiment);
        }

        public void ProcessTick(List<ActiveNews> activeNews)
        {
            float targetSentiment = 0.5f;
            float gravityStrength = 0.30f; // 30% decay back to 0.5f per tick — 평형점을 [0,1] 안쪽으로 당겨 양 끝 클립 방지

            // 활성 뉴스의 감정 방향성 × 유효 영향 강도를 '평균'으로 집계.
            // 합산 방식은 동시 뉴스(최대 5개)가 한쪽으로 쏠릴 때 shift가 폭주해
            // Clamp01 경계(0/1)에 영구히 눌러붙었다. 개수로 나눠 규모를 억제한다.
            float newsSentimentShift = 0f;
            int contributing = 0;
            foreach (var news in activeNews)
            {
                float direction = news.Template.Sentiment switch
                {
                    NewsSentiment.Positive => 1f,
                    NewsSentiment.Negative => -1f,
                    _ => 0f
                };
                if (direction == 0f) continue; // 중립 뉴스는 심리에 기여하지 않음(평균 분모에서도 제외)

                float impact = Mathf.Abs(news.GetEffectiveImpact());
                newsSentimentShift += direction * impact;
                contributing++;
            }
            if (contributing > 0)
                newsSentimentShift /= contributing;

            float rawNext = Sentiment + newsSentimentShift;
            float oldSentiment = Sentiment;

            // 뉴스의 영향력을 더한 뒤 중립(0.5f)으로 서서히 감쇄
            Sentiment = Mathf.Lerp(rawNext, targetSentiment, gravityStrength);
            Sentiment = Mathf.Clamp01(Sentiment);

            if (!Mathf.Approximately(oldSentiment, Sentiment))
            {
                OnSentimentChanged?.Invoke(Sentiment);
            }
        }
    }
}
