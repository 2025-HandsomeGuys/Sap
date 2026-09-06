using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using Stock.Core;
using Market;

namespace Stock.UI
{
    /// <summary>
    /// 주식 UI 최상위 컨트롤러. 패널 열기/닫기, 탭 전환, 상단 골드 표시 + 다음 틱까지 남은 시간 타이머,
    /// 엔진 이벤트 구독을 담당한다. ShopUI와 동일하게 싱글톤으로 외부에서 Open() 호출 가능.
    /// (구 시장심리 표시는 제거되고 그 UI 슬롯을 틱 타이머가 재사용한다.)
    /// </summary>
    public class StockUIController : MonoBehaviour
    {
        public static StockUIController Instance { get; private set; }

        [Header("패널")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;
        [SerializeField] private KeyCode toggleKey = KeyCode.None; // 테스트용 토글 키 (선택)

        [Header("탭 콘텐츠")]
        [SerializeField] private GameObject stocksTab;
        [SerializeField] private GameObject newsTab;
        [SerializeField] private GameObject portfolioTab;

        [Header("탭 버튼")]
        [SerializeField] private Button stocksTabButton;
        [SerializeField] private Button newsTabButton;
        [SerializeField] private Button portfolioTabButton;

        [Header("탭 버튼 라벨 (선택 탭만 색상 강조)")]
        [Tooltip("비우면 각 탭 버튼의 자식 TMP 라벨을 자동으로 찾는다.")]
        [SerializeField] private TextMeshProUGUI stocksTabLabel;
        [SerializeField] private TextMeshProUGUI newsTabLabel;
        [SerializeField] private TextMeshProUGUI portfolioTabLabel;

        [Header("탭 밑줄 바 (선택 탭 아래 노란 바. 비우면 미사용)")]
        [SerializeField] private Image stocksTabUnderline;
        [SerializeField] private Image newsTabUnderline;
        [SerializeField] private Image portfolioTabUnderline;

        [Header("탭 컴포넌트")]
        [SerializeField] private StockListUI stockListUI;
        [SerializeField] private NewsTabUI newsTabUI;
        [SerializeField] private PortfolioUI portfolioUI;

        [Header("상단 정보")]
        [SerializeField] private TextMeshProUGUI goldText;

        [Header("다음 틱 타이머 (구 시장심리 자리)")]
        [Tooltip("다음 틱까지 남은 시간을 텍스트로만 표시(MM:SS). 구 시장심리 텍스트를 그대로 재사용한다.")]
        [FormerlySerializedAs("sentimentText")]
        [SerializeField] private TextMeshProUGUI tickTimerText;

        [Header("탭 배치")]
        [Tooltip("켜면 탭 버튼의 좌→우 순서를 '보유 · 시세 · 뉴스'로 코드에서 강제한다(TabBar의 HorizontalLayoutGroup 기준). 하이어라키에서 직접 정렬해 뒀다면 꺼도 된다.")]
        [SerializeField] private bool autoArrangeTabOrder = true;

        [Tooltip("탭을 왼쪽으로 전환하는 키. None이면 비활성.")]
        [SerializeField] private KeyCode prevTabKey = KeyCode.Q;
        [Tooltip("탭을 오른쪽으로 전환하는 키. None이면 비활성.")]
        [SerializeField] private KeyCode nextTabKey = KeyCode.E;
        [Tooltip("끝에서 한 번 더 누르면 반대쪽 끝으로 순환한다.")]
        [SerializeField] private bool wrapTabCycle = true;

        private enum Tab { Stocks, News, Portfolio }

        /// <summary>화면에 보이는 좌→우 순서. ArrangeTabOrder()의 배치와 반드시 일치시킬 것.</summary>
        private static readonly Tab[] TabOrder = { Tab.Portfolio, Tab.Stocks, Tab.News };
        // 창을 열면 항상 포트폴리오부터 보인다(Open()에서 강제).
        private Tab _currentTab = Tab.Portfolio;
        private PlayerStat _playerStat;
        private bool _engineHooked;
        private bool _externallyOpened; // Open()이 호출됐으면 Start()에서 패널을 끄지 않는다(실행 순서 무관 보장)

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            // 마켓 씬에서 MarketSceneController.Open()이 먼저 켰다면 끄지 않는다.
            // (Start 실행 순서가 보장되지 않아 Open→Start 순서로 꺼지던 버그 방지)
            if (panelRoot && !_externallyOpened) panelRoot.SetActive(false);

            if (closeButton) closeButton.onClick.AddListener(Close);
            if (stocksTabButton) stocksTabButton.onClick.AddListener(() => SelectTab(Tab.Stocks));
            if (newsTabButton) newsTabButton.onClick.AddListener(() => SelectTab(Tab.News));
            if (portfolioTabButton) portfolioTabButton.onClick.AddListener(() => SelectTab(Tab.Portfolio));

            ArrangeTabOrder();

            HookEngine();
        }

        /// <summary>
        /// 탭 버튼을 좌→우 '보유 · 시세 · 뉴스' 순으로 재배치한다.
        /// TabBar가 HorizontalLayoutGroup으로 배치되므로 sibling index만 바꾸면 화면 순서가 바뀐다.
        /// (타이머 바 등 나머지 자식은 뒤로 밀려 원래 위치를 유지한다)
        /// </summary>
        private void ArrangeTabOrder()
        {
            if (!autoArrangeTabOrder) return;

            for (int i = 0; i < TabOrder.Length; i++)
            {
                var b = ButtonOf(TabOrder[i]);
                if (b) b.transform.SetSiblingIndex(i);
            }
        }

        private Button ButtonOf(Tab tab) => tab switch
        {
            Tab.Stocks => stocksTabButton,
            Tab.News => newsTabButton,
            _ => portfolioTabButton,
        };

        /// <summary>Q/E로 좌우 탭 전환. 수량 입력 등 타이핑 중에는 무시한다.</summary>
        private void HandleTabHotkeys()
        {
            if (!IsOpen) return;
            if (Stock.UI.StockTradeDialog.IsOpen) return; // 수량 팝업이 떠 있으면 탭이 뒤에서 바뀌지 않게
            if (IsTypingInInputField()) return;

            if (prevTabKey != KeyCode.None && Input.GetKeyDown(prevTabKey)) StepTab(-1);
            else if (nextTabKey != KeyCode.None && Input.GetKeyDown(nextTabKey)) StepTab(+1);
        }

        private void StepTab(int delta)
        {
            int cur = System.Array.IndexOf(TabOrder, _currentTab);
            if (cur < 0) cur = 0;

            int next = cur + delta;
            if (wrapTabCycle)
            {
                next = (next % TabOrder.Length + TabOrder.Length) % TabOrder.Length;
            }
            else
            {
                next = Mathf.Clamp(next, 0, TabOrder.Length - 1);
                if (next == cur) return;
            }

            SelectTab(TabOrder[next]);
        }

        /// <summary>TMP 입력필드에 포커스가 있으면 단축키를 먹지 않게 한다(MarketSceneController와 동일 규칙).</summary>
        private static bool IsTypingInInputField()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return false;
            var field = go.GetComponent<TMP_InputField>();
            return field != null && field.isFocused;
        }

        private void Update()
        {
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey)) Toggle();
            HandleTabHotkeys();
            UpdateTickTimer();
        }

        private void OnDestroy()
        {
            var sgm = StockGameManager.Instance;
            if (sgm != null)
            {
                sgm.OnSystemInitialized -= OnEngineInitialized;
                sgm.OnTickProcessed -= OnTick;
            }
            UnhookManagers();
        }

        // ===================================================
        // 열기 / 닫기 / 탭
        // ===================================================
        public void Open()
        {
            _externallyOpened = true;
            HookEngine();
            if (panelRoot) panelRoot.SetActive(true);

            if (stockListUI)
            {
                stockListUI.Build();
                // 들어올 때마다 기본 상태로: 전체 태그 · 보유중 꺼짐 · 검색어 없음.
                // (보유탭에서 종목을 눌러 넘어가는 ShowInStockTab이 그때 보유중을 다시 켠다)
                stockListUI.ResetFilters();
            }
            // 열 때마다 포트폴리오부터 보여준다(이전에 보던 탭을 기억하지 않는다).
            SelectTab(Tab.Portfolio);
            RefreshTopBar();
        }

        public void Close()
        {
            if (panelRoot) panelRoot.SetActive(false);
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        /// <summary>보유 탭에서 종목을 클릭했을 때 — 시세 탭으로 넘어가 그 종목을 펼친다.
        /// 이때 '보유중' 필터를 켜서 목록이 보유 종목만 남게 하고, 남아 있던 검색어는 비운다
        /// (검색어가 걸려 있으면 방금 고른 종목이 목록에서 사라져 보일 수 있다).</summary>
        public void ShowInStockTab(string companyId)
        {
            if (string.IsNullOrEmpty(companyId)) return;

            SelectTab(Tab.Stocks);
            if (stockListUI == null) return;

            stockListUI.ClearSearch();
            stockListUI.SetHoldingsFilter(true);
            stockListUI.SelectCompany(companyId);
        }

        private void SelectTab(Tab tab)
        {
            _currentTab = tab;
            if (stocksTab) stocksTab.SetActive(tab == Tab.Stocks);
            if (newsTab) newsTab.SetActive(tab == Tab.News);
            if (portfolioTab) portfolioTab.SetActive(tab == Tab.Portfolio);
            UpdateTabLabelStyles();
            RefreshCurrentTab();
        }

        /// <summary>선택된 탭은 흰색 + 아래 노란 바, 나머지는 뮤트 회색으로 전환.</summary>
        private void UpdateTabLabelStyles()
        {
            ApplyTabStyle(ResolveTabLabel(ref stocksTabLabel, stocksTabButton), stocksTabUnderline, _currentTab == Tab.Stocks);
            ApplyTabStyle(ResolveTabLabel(ref newsTabLabel, newsTabButton), newsTabUnderline, _currentTab == Tab.News);
            ApplyTabStyle(ResolveTabLabel(ref portfolioTabLabel, portfolioTabButton), portfolioTabUnderline, _currentTab == Tab.Portfolio);
        }

        /// <summary>선택 탭: 흰색(TextPrimary) + 노란(Gold) 밑줄 바 표시. 비선택 탭: 회색(TextMuted) + 밑줄 숨김.</summary>
        private static void ApplyTabStyle(TextMeshProUGUI label, Image underline, bool selected)
        {
            if (label)
            {
                label.color = selected ? MarketTheme.TextPrimary : MarketTheme.TextMuted;
            }
            if (underline)
            {
                underline.color = MarketTheme.Gold;
                underline.enabled = selected;
            }
        }

        /// <summary>인스펙터에 라벨이 비어 있으면 버튼 자식에서 TMP 라벨을 찾아 캐시한다.</summary>
        private static TextMeshProUGUI ResolveTabLabel(ref TextMeshProUGUI cached, Button button)
        {
            if (cached) return cached;
            if (button) cached = button.GetComponentInChildren<TextMeshProUGUI>(true);
            return cached;
        }

        // ===================================================
        // 갱신
        // ===================================================
        private void RefreshCurrentTab()
        {
            switch (_currentTab)
            {
                case Tab.Stocks: if (stockListUI) stockListUI.Refresh(); break;
                case Tab.News: if (newsTabUI) newsTabUI.Refresh(); break;
                case Tab.Portfolio: if (portfolioUI) portfolioUI.Refresh(); break;
            }
        }

        private void RefreshTopBar()
        {
            if (goldText)
            {
                var stat = ResolvePlayerStat();
                goldText.text = stat != null ? StockLoc.LF("ui_stock_gold", "{0:N0} G", stat.Gold) : "- G";
            }
            UpdateTickTimer();
        }

        /// <summary>
        /// 다음 틱까지 남은 시간을 텍스트에 매 프레임 반영한다(구 시장심리 표시 대체).
        /// 엔진 타이머가 지상에서만 흐르므로, 지하에선 값이 얼어붙고 '멈춤' 문구를 띄운다.
        /// </summary>
        private void UpdateTickTimer()
        {
            if (!IsOpen) return;

            var sgm = StockGameManager.Instance;
            bool running = sgm != null && sgm.IsTickTimerRunning;
            float remaining = sgm != null ? sgm.TimeUntilNextTick : 0f;

            if (tickTimerText)
            {
                if (running)
                {
                    tickTimerText.text = StockLoc.LF("ui_stock_next_tick", "다음 갱신까지 {0}", FormatTime(remaining));
                    tickTimerText.color = MarketTheme.TextPrimary;
                }
                else
                {
                    tickTimerText.text = StockLoc.L("ui_stock_tick_paused", "지하에서는 멈춤");
                    tickTimerText.color = MarketTheme.TextMuted;
                }
            }
        }

        /// <summary>남은 초를 MM:SS로 포맷. 올림해 15:00에서 시작해 00:00 직전에 틱이 돈다.</summary>
        private static string FormatTime(float seconds)
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }

        // ===================================================
        // 엔진 이벤트 연결
        // ===================================================
        private void HookEngine()
        {
            var sgm = StockGameManager.Instance;
            if (sgm == null) return;

            sgm.OnSystemInitialized -= OnEngineInitialized;
            sgm.OnSystemInitialized += OnEngineInitialized;
            sgm.OnTickProcessed -= OnTick;
            sgm.OnTickProcessed += OnTick;

            if (sgm.IsInitialized) OnEngineInitialized();
        }

        private void OnEngineInitialized()
        {
            if (_engineHooked) return;
            _engineHooked = true;

            var pm = StockGameManager.Instance?.PortfolioManager;
            if (pm != null) pm.OnPortfolioChanged += OnPortfolioChanged;
        }

        private void UnhookManagers()
        {
            var pm = StockGameManager.Instance?.PortfolioManager;
            if (pm != null) pm.OnPortfolioChanged -= OnPortfolioChanged;
            _engineHooked = false;
        }

        private void OnTick(int tick)
        {
            if (!IsOpen) return;
            RefreshCurrentTab();
            RefreshTopBar();
        }

        private void OnPortfolioChanged()
        {
            if (!IsOpen) return;
            RefreshCurrentTab();
            RefreshTopBar();
        }

        private PlayerStat ResolvePlayerStat()
        {
            if (_playerStat == null)
                _playerStat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
            return _playerStat;
        }
    }
}
