using System;
using System.Collections.Generic;
using UnityEngine;
using Stock.Data;
using Stock.Core;

namespace Stock.Systems
{
    [Serializable]
    public class ActiveChainState
    {
        public string ChainId;
        public int CurrentStep; // chainOrder
        public int LastPublishedTick;
        public int NextScheduledTick;
        public string CurrentActiveNewsId;
        public Dictionary<string, string> ChainParams = new Dictionary<string, string>();
    }

    public class NewsManager
    {
        private List<ActiveNews> _activeNews = new List<ActiveNews>();
        private List<NewsHistoryEntry> _history = new List<NewsHistoryEntry>();
        private Dictionary<string, int> _cooldowns = new Dictionary<string, int>();
        private List<ActiveChainState> _activeChains = new List<ActiveChainState>();

        public event Action<ActiveNews> OnNewsPublished;
        public event Action<ActiveNews> OnNewsExpired;
        public event Action<ActiveNews> OnNewsPhaseChanged;

        public IReadOnlyList<ActiveNews> GetActiveNews() => _activeNews;
        public IReadOnlyList<NewsHistoryEntry> GetHistory() => _history;
        public IReadOnlyList<ActiveChainState> GetActiveChains() => _activeChains;
        public Dictionary<string, int> GetCooldowns() => _cooldowns;

        public void Initialize()
        {
            _activeNews.Clear();
            _history.Clear();
            _cooldowns.Clear();
            _activeChains.Clear();
        }

        // 세이브 복구용 데이터 설정 메서드들
        public void RestoreState(List<ActiveNews> activeNews, List<NewsHistoryEntry> history, List<ActiveChainState> activeChains, Dictionary<string, int> cooldowns)
        {
            if (activeNews != null) _activeNews = new List<ActiveNews>(activeNews);
            if (history != null) _history = new List<NewsHistoryEntry>(history);
            if (activeChains != null) _activeChains = new List<ActiveChainState>(activeChains);
            if (cooldowns != null) _cooldowns = new Dictionary<string, int>(cooldowns);
        }

        public void ProcessTick(int currentTick)
        {
            // 1. 활성 뉴스들의 내부 Tick 업데이트 및 자연적인 상태 전환 (Rumor -> Official -> Fade -> Expired)
            UpdateActiveNewsTicks(currentTick);

            // 2. 진행 중인 이벤트 체인의 후속 단계 처리
            ProcessActiveChains(currentTick);

            // 3. 동시 활성 뉴스 수(최대 3개) 제한 조건 하에 새로운 뉴스/체인 생성
            TrySpawnNewNews(currentTick);
        }

        private void UpdateActiveNewsTicks(int currentTick)
        {
            List<ActiveNews> toRemove = new List<ActiveNews>();

            for (int i = 0; i < _activeNews.Count; i++)
            {
                var news = _activeNews[i];
                news.RemainingTicks--;

                if (news.RemainingTicks <= 0)
                {
                    // 페이즈 전환 로직
                    if (news.CurrentPhase == NewsPhase.Fade)
                    {
                        // 감쇠 페이즈가 끝나면 최종 만료
                        toRemove.Add(news);
                    }
                    else
                    {
                        // Rumor 또는 Official 페이즈가 다했을 때
                        // 후속 뉴스가 정의되어 있는 체인 도중 단계라면 즉시 만료시켜 다음 단계 스폰 유도
                        bool hasNextNews = news.Template.NextNewsIds != null && news.Template.NextNewsIds.Length > 0;
                        if (hasNextNews && !string.IsNullOrEmpty(news.Template.ChainId))
                        {
                            toRemove.Add(news);
                        }
                        else
                        {
                            // 후속이 없는 최종 단계거나 독립 뉴스는 Fade 페이즈로 자연 전이
                            news.CurrentPhase = NewsPhase.Fade;
                            news.RemainingTicks = news.Template.DurationTicks;
                            OnNewsPhaseChanged?.Invoke(news);
                        }
                    }
                }
            }

            // 만료된 뉴스 처리
            foreach (var expired in toRemove)
            {
                _activeNews.Remove(expired);

                // 역사 아카이브 기록 추가
                var historyEntry = new NewsHistoryEntry
                {
                    NewsId = expired.Template.Id,
                    OccurredTick = expired.StartTick,
                    ExpiredTick = currentTick,
                    OutcomeLabel = expired.SelectedOutcome.label,
                    SynthesizedTitle = expired.GetSynthesizedTitle()
                };
                _history.Add(historyEntry);

                // 진행 중인 체인에서 해당 활성 뉴스 ID가 만료됨을 감지
                var chain = _activeChains.Find(c => c.CurrentActiveNewsId == expired.Template.Id);
                if (chain != null)
                {
                    // 후속 뉴스가 없는 마지막 단계인 경우 체인을 즉시 해제
                    if (expired.Template.NextNewsIds == null || expired.Template.NextNewsIds.Length == 0)
                    {
                        _activeChains.Remove(chain);
                    }
                    else
                    {
                        // 다음 후속 단계 스케줄링 진행
                        var chainData = StockDataLoader.Instance.NewsChains[chain.ChainId];
                        int nextGap = UnityEngine.Random.Range(chainData.MinDayGap, chainData.MaxDayGap + 1);
                        chain.NextScheduledTick = currentTick + nextGap;
                        chain.CurrentActiveNewsId = null; // 현재 활성화된 뉴스는 없음 상태
                    }
                }

                OnNewsExpired?.Invoke(expired);
            }
        }

        private void ProcessActiveChains(int currentTick)
        {
            List<ActiveChainState> completedChains = new List<ActiveChainState>();

            foreach (var chain in _activeChains)
            {
                // 현재 해당 체인에서 활성화된 뉴스가 없고, 예약된 틱에 도달했으면 다음 뉴스 실행
                if (chain.CurrentActiveNewsId == null && currentTick >= chain.NextScheduledTick)
                {
                    // 이전까지 활성화되었던 템플릿 탐색
                    var historyLast = _history.FindLast(h => {
                        var temp = StockDataLoader.Instance.NewsTemplates[h.NewsId];
                        return temp.ChainId == chain.ChainId && temp.ChainOrder == chain.CurrentStep;
                    });

                    NewsTemplate currentTemplate = null;
                    if (historyLast != null)
                    {
                        currentTemplate = StockDataLoader.Instance.NewsTemplates[historyLast.NewsId];
                    }
                    else
                    {
                        // 역사에 없다면 예외 대비책으로 현재 스펙에서 직접 선택 시도
                        foreach (var kvp in StockDataLoader.Instance.NewsTemplates)
                        {
                            if (kvp.Value.ChainId == chain.ChainId && kvp.Value.ChainOrder == chain.CurrentStep)
                            {
                                currentTemplate = kvp.Value;
                                break;
                            }
                        }
                    }

                    if (currentTemplate != null)
                    {
                        string nextNewsId = SelectNextNews(currentTemplate);
                        if (!string.IsNullOrEmpty(nextNewsId) && StockDataLoader.Instance.NewsTemplates.TryGetValue(nextNewsId, out var nextTemplate))
                        {
                            chain.CurrentStep = nextTemplate.ChainOrder;
                            chain.CurrentActiveNewsId = nextTemplate.Id;
                            chain.LastPublishedTick = currentTick;
                            chain.NextScheduledTick = 0;

                            PublishNews(nextTemplate, currentTick);
                        }
                        else
                        {
                            // 후속 뉴스가 없는 경우 체인 종료
                            completedChains.Add(chain);
                        }
                    }
                    else
                    {
                        completedChains.Add(chain);
                    }
                }
            }

            foreach (var completed in completedChains)
            {
                _activeChains.Remove(completed);
            }
        }

        // 동시 활성 뉴스 상한. 뉴스 1건이 태그매칭으로 여러 종목에 영향을 주고,
        // 시장심리는 모든 종목에 작용한다. 하루 평균 신규 2~3건 × 짧아진 체류(2~3일)로
        // 정상상태 활성이 6건 안팎이 되므로, 상한이 스폰을 막지 않도록 8로 둔다.
        private const int MaxActiveNews = 8;

        private void TrySpawnNewNews(int currentTick)
        {
            if (_activeNews.Count >= MaxActiveNews) return;

            // 한 틱(=하루)에 시도할 신규 뉴스 발생 횟수 (2 ~ 6회 시도).
            // 각 시도 60% 통과 → 하루 평균 실현 신규 뉴스 ≈ 2.4건(2~3건 목표).
            int spawnAttempts = UnityEngine.Random.Range(2, 7);
            for (int attempt = 0; attempt < spawnAttempts; attempt++)
            {
                if (_activeNews.Count >= MaxActiveNews) break;

                // 각 개별 스폰 시도마다 60% 확률 통과
                if (UnityEngine.Random.value > 0.6f) continue;

                // 발생 후보 템플릿 및 체인 선정
                List<NewsTemplate> candidates = new List<NewsTemplate>();
                foreach (var kvp in StockDataLoader.Instance.NewsTemplates)
                {
                    var template = kvp.Value;

                    // 쿨다운 확인
                    if (_cooldowns.TryGetValue(template.Id, out int cdTick) && currentTick < cdTick)
                        continue;

                    // 이미 현재 활성화되어 떠 있는 뉴스는 중복 스폰 제외
                    if (_activeNews.Exists(n => n.Template.Id == template.Id))
                        continue;

                    if (!string.IsNullOrEmpty(template.ChainId))
                    {
                        // 체인 뉴스인 경우 첫 단계(ChainOrder == 0)만 신규 발생 후보가 됨
                        if (template.ChainOrder != 0) continue;

                        // 해당 체인이 이미 활성화되어 진행 중이면 건너뜀
                        if (_activeChains.Exists(c => c.ChainId == template.ChainId))
                            continue;

                        // 체인 쿨다운 확인
                        if (_cooldowns.TryGetValue(template.ChainId, out int chainCdTick) && currentTick < chainCdTick)
                            continue;
                    }

                    candidates.Add(template);
                }

                if (candidates.Count == 0) continue;

                // 가중치 합산 계산
                float totalWeight = 0f;
                List<float> weights = new List<float>();
                foreach (var cand in candidates)
                {
                    float weight = 5f; // 독립 뉴스의 기본 가중치
                    if (!string.IsNullOrEmpty(cand.ChainId))
                    {
                        if (StockDataLoader.Instance.NewsChains.TryGetValue(cand.ChainId, out var chainData))
                        {
                            weight = chainData.Weight;
                        }
                    }
                    weights.Add(weight);
                    totalWeight += weight;
                }

                if (totalWeight <= 0f) continue;

                // 가중치 기반 랜덤 선택
                float roll = UnityEngine.Random.Range(0f, totalWeight);
                float cumulative = 0f;
                NewsTemplate selected = candidates[candidates.Count - 1];
                for (int i = 0; i < candidates.Count; i++)
                {
                    cumulative += weights[i];
                    if (roll <= cumulative)
                    {
                        selected = candidates[i];
                        break;
                    }
                }

                // 선택된 뉴스 발행
                if (!string.IsNullOrEmpty(selected.ChainId))
                {
                    // 체인 뉴스인 경우 체인 상태 추가 등록
                    var newChainState = new ActiveChainState
                    {
                        ChainId = selected.ChainId,
                        CurrentStep = 0,
                        LastPublishedTick = currentTick,
                        NextScheduledTick = 0,
                        CurrentActiveNewsId = selected.Id,
                        ChainParams = new Dictionary<string, string>()
                    };
                    _activeChains.Add(newChainState);
                }

                PublishNews(selected, currentTick);
            }
        }

        private void PublishNews(NewsTemplate template, int currentTick)
        {
            var activeNews = new ActiveNews
            {
                Template = template,
                StartTick = currentTick,
                RemainingTicks = template.DurationTicks,
                CurrentPhase = template.Phase,
                WasPricedIn = template.PricingInFactor > 0f
            };

            // Outcomes 확률 롤링
            activeNews.SelectedOutcome = RollOutcome(template);

            // 체인 파라미터 계승 처리
            ActiveChainState associatedChain = null;
            if (!string.IsNullOrEmpty(template.ChainId))
            {
                associatedChain = _activeChains.Find(c => c.ChainId == template.ChainId);
            }

            if (associatedChain != null)
            {
                // 이전 단계에서 결정되어 저장된 체인 파라미터가 있다면 물려받음
                foreach (var kvp in associatedChain.ChainParams)
                {
                    activeNews.SelectedParams[kvp.Key] = kvp.Value;
                }
            }

            // 템플릿 매개변수 중 아직 결정되지 않은 것들 랜덤 선택
            foreach (var kvp in template.TemplateParams)
            {
                if (!activeNews.SelectedParams.ContainsKey(kvp.Key) && kvp.Value != null && kvp.Value.Length > 0)
                {
                    string chosen = kvp.Value[UnityEngine.Random.Range(0, kvp.Value.Length)];
                    activeNews.SelectedParams[kvp.Key] = chosen;

                    // 만약 체인이 진행 중이라면 첫 결정된 파라미터를 캐시에 등록하여 후속 단계에 전파
                    if (associatedChain != null)
                    {
                        associatedChain.ChainParams[kvp.Key] = chosen;
                    }
                }
            }

            _activeNews.Add(activeNews);

            // 쿨다운 등록 (뉴스 템플릿 자체 및 체인 단위 쿨다운 적용)
            _cooldowns[template.Id] = currentTick + template.CooldownTicks;
            if (!string.IsNullOrEmpty(template.ChainId))
            {
                _cooldowns[template.ChainId] = currentTick + template.CooldownTicks;
            }

            OnNewsPublished?.Invoke(activeNews);
        }

        private OutcomeEntry RollOutcome(NewsTemplate template)
        {
            if (template.Outcomes == null || template.Outcomes.Length == 0)
            {
                return new OutcomeEntry { probability = 1f, priceEffect = 0f, label = "neutral" };
            }

            float roll = UnityEngine.Random.value;
            float cumulative = 0f;
            foreach (var entry in template.Outcomes)
            {
                cumulative += entry.probability;
                if (roll <= cumulative)
                {
                    return entry;
                }
            }
            return template.Outcomes[template.Outcomes.Length - 1];
        }

        private string SelectNextNews(NewsTemplate current)
        {
            string[] candidates = current.NextNewsIds;
            int[] weights = current.NextWeights;
            
            if (candidates == null || candidates.Length == 0) return null;
            if (weights == null || weights.Length == 0 || weights.Length != candidates.Length)
                return candidates[0];

            int totalWeight = 0;
            foreach (var w in weights) totalWeight += w;

            int roll = UnityEngine.Random.Range(0, totalWeight);
            int cumulative = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                cumulative += weights[i];
                if (roll < cumulative) return candidates[i];
            }
            return candidates[candidates.Length - 1];
        }
    }
}
