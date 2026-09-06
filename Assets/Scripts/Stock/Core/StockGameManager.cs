using System;
using System.Collections.Generic;
using UnityEngine;
using Stock.Systems;
using Stock.Data;

namespace Stock.Core
{
    public class StockGameManager : MonoBehaviour
    {
        public static StockGameManager Instance { get; private set; }

        [Header("시뮬레이션 설정")]
        [SerializeField] private bool _initializeOnAwake = true;

        // 새 게임 시작 시 미리 굴려둘 틱 수. 시작하자마자 주식창을 열어도
        // 차트가 1틱이 아니라 '이미 진행 중'으로 보이게 한다(세이브 로드 시엔 건너뜀).
        private const int StockWarmupTicks = 20;

        // 위 예열 틱 중 '마지막 N틱'은 뉴스를 실제로 태운다 → 시작하자마자 주식창을 열어도
        // 활성 뉴스·뉴스 이력이 이미 몇 건 쌓여 있게 한다(앞 틱은 여전히 뉴스 없는 순수 워크).
        private const int StockWarmupNewsTicks = 6;

        // 실시간 자동 틱 간격(초). 지상에 머무는 실제 시간 15분마다 시세가 1틱 진행된다.
        // '하루 1틱'(수면)에서 벗어나 실시간 템포로 도는 것이 핵심 변경점.
        private const float RealtimeTickSeconds = 15f * 60f;

        // 침대 수면·지하 복귀처럼 '시간을 건너뛰는' 이벤트에서 한 번에 몰아 진행하는 틱 수.
        // 이 틱들은 뉴스를 적용·진행하지 않고 기본 가격 이동만 한다(ProcessNewsFreeTicks 참고).
        public const int SkipTicks = 4;

        // 건너뛰기 틱은 뉴스를 넘기지 않으므로 항상 이 빈 리스트를 넘긴다(매 틱 새 할당 방지).
        private static readonly List<ActiveNews> s_noNews = new List<ActiveNews>();

        // 지상에서 흐른 실시간 누적치(초). 지하에선 멈추고, ProcessTick이 돌 때마다 0으로 리셋된다
        // → "마지막 틱 이후 지상에서 실제 15분"이 정확히 지나야 다음 자동 틱이 발생.
        private float _realtimeTickTimer;

        public int CurrentTick { get; private set; }
        public bool IsInitialized { get; private set; }

        // ── UI 실시간 틱 타이머 조회용 ─────────────────────────────────
        // 주식창 상단 타이머 바/텍스트가 매 프레임 이 값들을 읽어 "다음 틱까지 남은 시간"을 표시한다.
        // 타이머는 지상에서만 흐르므로(Update 게이트), 지하에선 값이 그대로 얼어붙어 정확히 남은 시간을 보여준다.

        /// <summary>실시간 자동 틱 간격(초). 현재 15분.</summary>
        public float TickIntervalSeconds => RealtimeTickSeconds;

        /// <summary>다음 자동 틱까지 남은 실시간(초). 지하(정지) 상태에선 얼지 않고 그대로 유지된다.</summary>
        public float TimeUntilNextTick => Mathf.Max(0f, RealtimeTickSeconds - _realtimeTickTimer);

        /// <summary>다음 틱까지 진행률 0~1 (0=방금 틱함, 1=곧 틱).</summary>
        public float TickProgress01 =>
            RealtimeTickSeconds <= 0f ? 0f : Mathf.Clamp01(_realtimeTickTimer / RealtimeTickSeconds);

        /// <summary>지금 실시간 타이머가 실제로 흐르는 중인지(초기화 완료 & 지상). 지하에선 false.</summary>
        public bool IsTickTimerRunning => IsInitialized && SurfaceSceneRegistry.IsActiveSceneSurface();

        // 시스템 매니저들
        private CompanyManager _companyManager;
        private NewsManager _newsManager;
        private StockPriceEngine _priceEngine;
        private MarketSentimentTracker _sentimentTracker;
        private PortfolioManager _portfolioManager;
        private TagMatchingSystem _tagMatcher;

        public CompanyManager CompanyManager => _companyManager;
        public NewsManager NewsManager => _newsManager;
        public StockPriceEngine PriceEngine => _priceEngine;
        public MarketSentimentTracker SentimentTracker => _sentimentTracker;
        public PortfolioManager PortfolioManager => _portfolioManager;
        public TagMatchingSystem TagMatcher => _tagMatcher;

        public event Action<int> OnTickProcessed; // tick 번호가 인자로 전달됨
        public event Action OnSystemInitialized;

        // 엔진 초기화 완료 전에 세이브 로드가 먼저 도착한 경우 보류해 두는 데이터
        private StockSaveData _pendingSaveData;

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

            if (_initializeOnAwake)
            {
                // 게임 데이터 로더가 준비되었는지 확인 후 초기화 시도
                StartCoroutine(WaitAndInitialize());
            }
        }

        private System.Collections.IEnumerator WaitAndInitialize()
        {
            // DataSheetCache와 StockDataLoader가 로드될 때까지 1프레임 대기
            yield return null;
            InitializeSystem();
        }

        private void Update()
        {
            if (!IsInitialized) return;

            // 실시간 자동 틱은 '지상'에 있을 때만 시간을 쌓는다(지하에선 정지).
            // 지하 잠수분은 지상 복귀 시점의 별도 1틱(ExploreExitController)으로 반영된다.
            if (!SurfaceSceneRegistry.IsActiveSceneSurface()) return;

            // unscaled: 상점·메뉴에서 timeScale=0이 되거나 디버그 배속을 걸어도 '실제 시간'을 센다.
            _realtimeTickTimer += Time.unscaledDeltaTime;
            if (_realtimeTickTimer >= RealtimeTickSeconds)
                ProcessTick(); // ProcessTick 안에서 타이머가 0으로 리셋된다
        }

        public void InitializeSystem()
        {
            // 1. 데이터 로더 작동
            if (StockDataLoader.Instance != null)
            {
                StockDataLoader.Instance.LoadAllData();
            }
            else
            {
                Debug.LogError("[StockGameManager] StockDataLoader 인스턴스가 존재하지 않습니다!");
                return;
            }

            // 2. 엔진 컴포넌트 객체들 생성 및 초기화
            _tagMatcher = new TagMatchingSystem();

            _companyManager = new CompanyManager();
            _companyManager.Initialize();

            _newsManager = new NewsManager();
            _newsManager.Initialize();

            _sentimentTracker = new MarketSentimentTracker();
            _sentimentTracker.Initialize(0.5f); // 기본 0.5 중립 심리로 시작

            _priceEngine = new StockPriceEngine();
            _priceEngine.Initialize(_companyManager, _tagMatcher);

            _portfolioManager = new PortfolioManager();
            _portfolioManager.Initialize(_companyManager);

            CurrentTick = 0;
            IsInitialized = true;

            // 새 게임(보류 중인 세이브 없음)일 때만: 주가를 미리 예열해
            // 시작 시점의 차트를 '이미 진행 중'으로 만든다. 리스너에게 알리기 전에 실행해
            // 예열 중간 틱이 UI로 새어나가지 않게 한다. 세이브가 있으면 어차피
            // 아래 ApplySaveData(또는 이후 RestoreCompanyPrices)가 히스토리를 덮으므로 스킵.
            if (_pendingSaveData == null)
                WarmUpMarket();

            OnSystemInitialized?.Invoke();

            // 엔진 초기화 전에 세이브 로드가 먼저 도착했다면, 지금 적용한다.
            if (_pendingSaveData != null)
            {
                ApplySaveData(_pendingSaveData);
                _pendingSaveData = null;
            }
        }

        /// <summary>
        /// 시뮬레이션 핵심 틱 루프. 지상 실시간 15분 경과(Update)에서 호출되며,
        /// 뉴스 갱신·심리·가격을 모두 반영하는 '완전한' 틱이다.
        /// 침대 수면·지하 복귀는 대신 <see cref="ProcessNewsFreeTicks"/>로 뉴스 없는
        /// 여러 틱을 몰아 진행한다. 어느 경로로 돌든 실시간 누적 타이머는 0으로 리셋된다.
        /// </summary>
        public void ProcessTick()
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("[StockGameManager] 엔진이 아직 초기화되지 않아 틱을 건너뜁니다.");
                return;
            }

            // 마지막 틱 기준으로 실시간 15분을 다시 세도록 리셋(복귀·수면 직후 즉시 재틱 방지).
            _realtimeTickTimer = 0f;

            RunSingleNewsTick();

            // Debug.Log($"[StockGameManager] Tick {CurrentTick} 처리가 성공적으로 완료되었습니다. 시장 심리: {_sentimentTracker.Sentiment:F2}");
            OnTickProcessed?.Invoke(CurrentTick);
        }

        /// <summary>
        /// 뉴스·심리·가격을 모두 반영하는 '완전한' 1틱. CurrentTick을 1 올리고 각 시스템을 순서대로 돌린다.
        /// 타이머 리셋·OnTickProcessed 통지는 호출부가 담당한다(단발 ProcessTick, 배치 ProcessNewsTicks,
        /// 뉴스 예열 루프가 각자 다른 타이밍에 알리므로).
        /// </summary>
        private void RunSingleNewsTick()
        {
            CurrentTick++;

            // 1. 뉴스 갱신 및 페이즈 상태전환 진행
            _newsManager.ProcessTick(CurrentTick);

            // 2. 현재 활성 뉴스를 기준으로 전체 시장의 공포/탐욕 심리 추적
            var activeNews = _newsManager.GetActiveNews();
            _sentimentTracker.ProcessTick(new System.Collections.Generic.List<ActiveNews>(activeNews));

            // 3. 뉴스 영향, 자연 노이즈, 시장 심리, 추세 관성을 반영하여 새로운 주가 계산
            _priceEngine.ProcessTick(CurrentTick, new System.Collections.Generic.List<ActiveNews>(activeNews), _sentimentTracker.Sentiment);

            // 4. 포트폴리오 틱 훅(현재는 no-op — 인내심/강제매각 삭제됨). 향후 확장 대비 유지.
            _portfolioManager.ProcessTick(CurrentTick);
        }

        /// <summary>
        /// 침대 수면 전용 틱 진행: 앞 (<paramref name="count"/>−1)틱은 뉴스 없는 순수 가격 워크로
        /// 조용히 지나가고, '마지막 1틱만' 뉴스를 태운다. → 밤새 뉴스가 우르르 쌓이는 게 아니라,
        /// 아침에 일어나는 순간 갓 나온 뉴스 1건과 그 여파가 시세에 반영된 상태가 된다.
        /// OnTickProcessed는 끝에 한 번만 쏜다. count가 1 이하면 뉴스 1틱만 진행.
        /// </summary>
        public void ProcessSleepTicks(int count)
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("[StockGameManager] 엔진이 아직 초기화되지 않아 틱을 건너뜁니다.");
                return;
            }
            if (count <= 0) return;

            // 마지막 틱 기준으로 실시간 15분을 다시 세도록 리셋(수면 직후 즉시 재틱 방지).
            _realtimeTickTimer = 0f;

            // 앞 (count−1)틱: 뉴스 정지, 순수 가격 이동만(자는 사이 조용히). 심리는 현재 값 고정.
            float sentiment = _sentimentTracker != null ? _sentimentTracker.Sentiment : 0.5f;
            int silent = Mathf.Max(0, count - 1);
            for (int i = 0; i < silent; i++)
            {
                CurrentTick++;
                _priceEngine.ProcessTick(CurrentTick, s_noNews, sentiment);
                _portfolioManager.ProcessTick(CurrentTick);
            }

            // 마지막 1틱만 뉴스 포함(뉴스 갱신+심리+가격).
            RunSingleNewsTick();

            OnTickProcessed?.Invoke(CurrentTick);
        }

        /// <summary>
        /// 새 게임 예열: 앞 (StockWarmupTicks − StockWarmupNewsTicks)틱은 뉴스 없는 순수 가격 워크,
        /// 마지막 StockWarmupNewsTicks틱은 뉴스를 실제로 태운다. 후자 덕에 게임 시작 시점에
        /// 활성 뉴스·뉴스 이력이 이미 몇 건 존재한다. 잭팟 점프는 예열 전 구간 억제(평범한 시작 시세).
        /// CurrentTick은 뉴스 예열 틱만큼만 올라간다(앞 순수 워크는 WarmUp이 CurrentTick을 안 건드림).
        /// </summary>
        private void WarmUpMarket()
        {
            int newsTicks = Mathf.Clamp(StockWarmupNewsTicks, 0, StockWarmupTicks);
            int silentTicks = StockWarmupTicks - newsTicks;

            // 1) 앞부분: 뉴스 없는 순수 가격 예열(기존 동작). 내부에서 점프 억제 on/off.
            if (silentTicks > 0)
                _priceEngine.WarmUp(silentTicks);

            // 2) 마지막 몇 틱: 뉴스를 실제로 태워 시작 시점에 활성 뉴스·이력이 존재하게 한다.
            //    잭팟 점프는 여전히 억제(플레이어가 아무것도 안 했는데 시작 보드에 잭팟 뜨는 것 방지).
            if (newsTicks > 0)
            {
                bool prev = _priceEngine.SuppressJumps;
                _priceEngine.SuppressJumps = true;
                for (int i = 0; i < newsTicks; i++)
                    RunSingleNewsTick();
                _priceEngine.SuppressJumps = prev;
            }
        }

        /// <summary>
        /// 뉴스를 전혀 적용·진행하지 않고 기본 가격 이동(노이즈+심리+관성+회귀)만
        /// <paramref name="count"/>틱 진행한다. 지하 복귀처럼 '짧게 자리를 비운' 시간 건너뛰기에 쓴다.
        /// (침대 수면은 밤새 뉴스가 흐르도록 <see cref="ProcessNewsTicks"/>로 바뀌었다.)
        /// NewsManager·SentimentTracker는 아예 돌리지 않으므로
        /// 활성 뉴스의 수명·만료·새 뉴스 발생이 모두 정지되고, 시장 심리도 현재 값을 유지한다
        /// (WarmUp과 동일 취지 — 플레이어가 지켜보지 않는 사이 뉴스가 터지지 않게).
        /// 실시간 누적 타이머는 여기서 0으로 리셋된다.
        /// </summary>
        public void ProcessNewsFreeTicks(int count)
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("[StockGameManager] 엔진이 아직 초기화되지 않아 틱을 건너뜁니다.");
                return;
            }
            if (count <= 0) return;

            // 마지막 틱 기준으로 실시간 15분을 다시 세도록 리셋(복귀·수면 직후 즉시 재틱 방지).
            _realtimeTickTimer = 0f;

            // 뉴스가 정지된 동안 시장 심리는 현재 값으로 고정한다(뉴스에서 파생되는 값이므로).
            float sentiment = _sentimentTracker != null ? _sentimentTracker.Sentiment : 0.5f;

            for (int i = 0; i < count; i++)
            {
                CurrentTick++;
                // 뉴스 없는 순수 가격 워크만. NewsManager·SentimentTracker는 건드리지 않는다.
                _priceEngine.ProcessTick(CurrentTick, s_noNews, sentiment);
                _portfolioManager.ProcessTick(CurrentTick);
            }

            OnTickProcessed?.Invoke(CurrentTick);
        }

        // 세이브 데이터 로드 시 시스템 상태 복구 메서드
        public void RestoreSavedSystemState(int tick, float sentiment, List<ActiveNews> activeNews, List<NewsHistoryEntry> history, List<ActiveChainState> activeChains, Dictionary<string, int> cooldowns, Dictionary<string, List<int>> priceHistories)
        {
            CurrentTick = tick;
            
            if (_sentimentTracker != null)
                _sentimentTracker.Initialize(sentiment);

            if (_newsManager != null)
                _newsManager.RestoreState(activeNews, history, activeChains, cooldowns);

            if (_companyManager != null)
                _companyManager.RestoreCompanyPrices(priceHistories);

            OnTickProcessed?.Invoke(CurrentTick);
        }

        // ===================================================
        // 세이브 직렬화 (SaveManager 연동)
        // ===================================================

        /// <summary>현재 시뮬레이션 상태를 직렬화 가능한 StockSaveData로 추출한다.</summary>
        public StockSaveData CaptureSaveData()
        {
            var data = new StockSaveData { hasData = true, currentTick = CurrentTick };

            if (_sentimentTracker != null)
                data.marketSentiment = _sentimentTracker.Sentiment;

            if (_portfolioManager != null)
                data.portfolio = _portfolioManager.ExportHoldings();

            if (_companyManager != null)
            {
                foreach (var c in _companyManager.GetAllCompanies())
                {
                    data.priceHistories.Add(new CompanyPriceHistory
                    {
                        companyId = c.Id,
                        prices = new List<int>(c.PriceHistory)
                    });
                }
            }

            if (_newsManager != null)
            {
                foreach (var n in _newsManager.GetActiveNews())
                {
                    var save = new ActiveNewsSave
                    {
                        templateId = n.Template?.Id,
                        startTick = n.StartTick,
                        remainingTicks = n.RemainingTicks,
                        currentPhase = (int)n.CurrentPhase,
                        wasPricedIn = n.WasPricedIn,
                        selectedOutcome = n.SelectedOutcome
                    };
                    foreach (var kv in n.SelectedParams)
                        save.selectedParams.Add(new StockStrEntry { key = kv.Key, value = kv.Value });
                    data.activeNews.Add(save);
                }

                foreach (var h in _newsManager.GetHistory())
                    data.newsHistory.Add(h);

                foreach (var ch in _newsManager.GetActiveChains())
                {
                    var save = new ActiveChainSave
                    {
                        chainId = ch.ChainId,
                        currentStep = ch.CurrentStep,
                        lastPublishedTick = ch.LastPublishedTick,
                        nextScheduledTick = ch.NextScheduledTick,
                        currentActiveNewsId = ch.CurrentActiveNewsId
                    };
                    foreach (var kv in ch.ChainParams)
                        save.chainParams.Add(new StockStrEntry { key = kv.Key, value = kv.Value });
                    data.activeChains.Add(save);
                }

                foreach (var kv in _newsManager.GetCooldowns())
                    data.cooldowns.Add(new StockIntEntry { key = kv.Key, value = kv.Value });
            }

            return data;
        }

        /// <summary>세이브 데이터로 시뮬레이션 상태를 복원한다. 엔진 초기화 전이면 보류 후 초기화 시 적용한다.</summary>
        public void ApplySaveData(StockSaveData data)
        {
            if (data == null || !data.hasData) return;

            if (!IsInitialized)
            {
                _pendingSaveData = data;
                return;
            }

            var loader = StockDataLoader.Instance;

            // 활성 뉴스 복원 (templateId → Template 재연결)
            var activeNews = new List<ActiveNews>();
            foreach (var s in data.activeNews)
            {
                if (loader == null || string.IsNullOrEmpty(s.templateId) ||
                    !loader.NewsTemplates.TryGetValue(s.templateId, out var tmpl))
                    continue;

                var n = new ActiveNews
                {
                    Template = tmpl,
                    StartTick = s.startTick,
                    RemainingTicks = s.remainingTicks,
                    CurrentPhase = (NewsPhase)s.currentPhase,
                    WasPricedIn = s.wasPricedIn,
                    SelectedOutcome = s.selectedOutcome
                };
                foreach (var p in s.selectedParams)
                    n.SelectedParams[p.key] = p.value;
                activeNews.Add(n);
            }

            var history = new List<NewsHistoryEntry>(data.newsHistory);

            var chains = new List<ActiveChainState>();
            foreach (var s in data.activeChains)
            {
                var ch = new ActiveChainState
                {
                    ChainId = s.chainId,
                    CurrentStep = s.currentStep,
                    LastPublishedTick = s.lastPublishedTick,
                    NextScheduledTick = s.nextScheduledTick,
                    CurrentActiveNewsId = s.currentActiveNewsId
                };
                foreach (var p in s.chainParams)
                    ch.ChainParams[p.key] = p.value;
                chains.Add(ch);
            }

            var cooldowns = new Dictionary<string, int>();
            foreach (var c in data.cooldowns)
                cooldowns[c.key] = c.value;

            var priceHistories = new Dictionary<string, List<int>>();
            foreach (var ph in data.priceHistories)
                priceHistories[ph.companyId] = new List<int>(ph.prices);

            RestoreSavedSystemState(data.currentTick, data.marketSentiment, activeNews, history, chains, cooldowns, priceHistories);

            if (_portfolioManager != null)
                _portfolioManager.RestoreHoldings(data.portfolio);
        }
    }
}
