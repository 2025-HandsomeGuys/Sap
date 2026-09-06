using System;
using System.Collections.Generic;

namespace Stock.Data
{
    /// <summary>
    /// 주식 시뮬레이션 전체 상태의 세이브 직렬화 구조.
    /// PlayerData에 포함되어 JsonUtility로 저장된다.
    ///
    /// 주의: JsonUtility는 Dictionary를 직렬화하지 못하므로,
    /// 런타임의 모든 Dictionary는 직렬화 가능한 List 형태로 변환해 저장한다.
    /// </summary>
    [Serializable]
    public class StockSaveData
    {
        public bool hasData;             // 엔진이 한 번이라도 가동되어 복원할 상태가 있는지
        public int currentTick;
        public float marketSentiment = 0.5f;

        public List<OwnedStock> portfolio = new List<OwnedStock>();
        public List<CompanyPriceHistory> priceHistories = new List<CompanyPriceHistory>();
        public List<ActiveNewsSave> activeNews = new List<ActiveNewsSave>();
        public List<NewsHistoryEntry> newsHistory = new List<NewsHistoryEntry>();
        public List<ActiveChainSave> activeChains = new List<ActiveChainSave>();
        public List<StockIntEntry> cooldowns = new List<StockIntEntry>();
    }

    [Serializable]
    public class CompanyPriceHistory
    {
        public string companyId;
        public List<int> prices = new List<int>();
    }

    [Serializable]
    public class StockIntEntry
    {
        public string key;
        public int value;
    }

    [Serializable]
    public class StockStrEntry
    {
        public string key;
        public string value;
    }

    /// <summary>활성 뉴스 1건의 세이브 형태. Template은 ID로만 저장하고 로드 시 재연결한다.</summary>
    [Serializable]
    public class ActiveNewsSave
    {
        public string templateId;
        public int startTick;
        public int remainingTicks;
        public int currentPhase;   // (int)NewsPhase
        public bool wasPricedIn;
        public OutcomeEntry selectedOutcome;
        public List<StockStrEntry> selectedParams = new List<StockStrEntry>();
    }

    /// <summary>진행 중 이벤트 체인 1건의 세이브 형태.</summary>
    [Serializable]
    public class ActiveChainSave
    {
        public string chainId;
        public int currentStep;
        public int lastPublishedTick;
        public int nextScheduledTick;
        public string currentActiveNewsId;
        public List<StockStrEntry> chainParams = new List<StockStrEntry>();
    }
}
