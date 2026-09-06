using System;

namespace Stock.Data
{
    public enum NewsPhase
    {
        Rumor,
        Official,
        Fade
    }

    public enum NewsSentiment
    {
        Positive,
        Negative,
        Neutral
    }

    [Serializable]
    public struct OutcomeEntry
    {
        public float probability;
        public float priceEffect;
        public string label;
    }

    // JSON Parsing Wrapper for Outcomes
    [Serializable]
    public class OutcomeWrapper
    {
        public OutcomeEntry[] outcomes;
    }
}
