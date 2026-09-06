// @tags: market, scene, controller, mode, stock, coin, orchestrator
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;
using Stock.UI;
using Coin.UI;

namespace Market
{
    public enum MarketMode { Stock, Coin }

    /// <summary>
    /// 마켓 씬 최상위 오케스트레이터 (설계 §8.2). 주식/코인 계열 전환, 진입/이탈, 상단 골드.
    /// MarketScene은 정착지 위에 Additive로 로드되는 것을 전제로 한다(PlayerStat 참조 유지).
    /// </summary>
    public class MarketSceneController : MonoBehaviour
    {
        [Header("계열 컨테이너")]
        [SerializeField] private GameObject stockModeRoot;
        [SerializeField] private GameObject coinModeRoot;
        [SerializeField] private StockUIController stockUI;
        [SerializeField] private CoinModeUI coinUI;

        [Header("상단바 — 주식/코인 세그먼트 토글")]
        [Tooltip("ToggleGroup에 묶인 주식 탭 토글 (Is On = 활성=노란 알약)")]
        [SerializeField] private Toggle stockTabToggle;
        [Tooltip("ToggleGroup에 묶인 코인 탭 토글")]
        [SerializeField] private Toggle coinTabToggle;
        [SerializeField] private TextMeshProUGUI goldText;
        [SerializeField] private Button closeButton;

        [Header("씬")]
        [SerializeField] private string sceneName = "MarketScene";

        [Header("효과음 (선택 — 비우면 코드 합성음 사용)")]
        [Tooltip("가져온 오디오 에셋으로 마켓 UI 사운드를 교체하는 세트. [Create > Market > SFX Set]으로 만들어 슬롯에 클립을 넣고 여기에 연결.")]
        [SerializeField] private MarketSfxSet sfxSet;

        private PlayerStat _playerStat;
        private MarketMode _mode = MarketMode.Stock;
        private float _visitStartTime;   // 마켓 체류 시간 측정용 (텔레메트리)
        private bool _claimedUIState;    // 우리가 UIState.Market을 세팅했는가(=해제 책임도 우리에게 있다)

        private void Awake()
        {
            DeduplicateOverlaySingletons();

            // 가져온 효과음 에셋 세트를 적용(비우면 코드 합성음). 슬롯을 바꾸면 그 소리로 교체됨.
            MarketUISfx.SetAssetSet(sfxSet);

            // UI 클릭음 자동 훅 — 이 씬의 모든 버튼/토글/입력필드 포섭 (에디터 셋업 불필요)
            if (GetComponent<MarketButtonSfx>() == null) gameObject.AddComponent<MarketButtonSfx>();

            // 물리 마우스 클릭음 — 이 씬에서만 "모니터 안에서 딸깍" 촉감 (씬 언로드 시 함께 사라짐)
            if (GetComponent<MarketMouseClickSfx>() == null) gameObject.AddComponent<MarketMouseClickSfx>();
        }

        /// <summary>
        /// MarketScene이 게임플레이 씬 위에 Additive로 얹히면 씬마다 들고 있는
        /// EventSystem·AudioListener가 중복돼 Unity가 매 프레임 경고/오류를 쏟아낸다.
        /// 게임플레이 씬의 것을 살리고 이 마켓 씬에 있는 중복분만 비활성화한다.
        /// (마켓 씬을 단독 실행할 땐 중복이 없으므로 그대로 둔다.)
        /// </summary>
        private void DeduplicateOverlaySingletons()
        {
            var myScene = gameObject.scene;

            var eventSystems = FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (eventSystems.Length > 1)
                foreach (var es in eventSystems)
                    if (es.gameObject.scene == myScene)
                    {
                        es.enabled = false; // 컴포넌트만 비활성화(같은 오브젝트의 다른 컴포넌트는 보존)
                        var module = es.GetComponent<BaseInputModule>();
                        if (module) module.enabled = false;
                    }

            var listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (listeners.Length > 1)
                foreach (var al in listeners)
                    if (al.gameObject.scene == myScene) al.enabled = false;
        }

        private void Start()
        {
            // 토글이 ON으로 바뀌는 순간에만 모드 전환(OFF 이벤트는 무시 — 그룹이 짝을 꺼주므로).
            if (stockTabToggle) stockTabToggle.onValueChanged.AddListener(on => { if (on) SetMode(MarketMode.Stock); });
            if (coinTabToggle) coinTabToggle.onValueChanged.AddListener(on => { if (on) SetMode(MarketMode.Coin); });
            if (closeButton) closeButton.onClick.AddListener(Exit);

            RefreshCoinLock();
            // 콘솔 치트(node coin)로 씬 안에서 해금해도 바로 탭이 뜨게 한다.
            if (UpgradeManager.Instance != null)
                UpgradeManager.Instance.OnUpgradeStateChanged += RefreshCoinLock;

            HookGold();
            Enter();

            // 단말 기동음 — 마켓 씬 진입
            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SfxKeys.UiTerminalOn);

            // 마켓 가이드 — 처음 단말 앞에 섰을 때 한 번만
            GuideManager.Trigger("market_basic");
        }

        private void OnDestroy()
        {
            if (UpgradeManager.Instance != null)
                UpgradeManager.Instance.OnUpgradeStateChanged -= RefreshCoinLock;

            var stat = ResolvePlayerStat();
            if (stat != null) stat.OnGoldChanged -= OnGoldChanged;

            ReleaseUIState();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { CodeUI.PlayBack(); Exit(); }

            // Tab = 주식 ↔ 코인 계열 전환 (게임 쪽 인벤토리 토글은 UIStateManager가 Market 상태에서 차단).
            // 수량 입력 등 입력필드 타이핑 중엔 무시한다.
            if (Input.GetKeyDown(KeyCode.Tab) && !IsTypingInInputField())
                SetMode(_mode == MarketMode.Stock ? MarketMode.Coin : MarketMode.Stock);
        }

        private static bool IsTypingInInputField()
        {
            var es = EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return false;
            var field = go.GetComponent<TMP_InputField>();
            return field != null && field.isFocused;
        }

        public void Enter()
        {
            ClaimUIState();
            SetMode(MarketMode.Stock);
            RefreshGold();

            _visitStartTime = Time.unscaledTime;
            Telemetry.Log(TelemetryEvents.MarketVisit, TelemetryPayload.New().Add("phase", "enter"));
        }

        /// <summary>
        /// 코인 계열을 여는 업그레이드 노드. 이 노드를 사기 전에는 탭이 잠긴다.
        /// (2026-08-24 결정 — 단말기 개통은 주식까지, 코인은 2지층 통행권이다.)
        /// 업그레이드 시스템이 없는 테스트 씬에서는 잠그지 않는다.
        /// </summary>
        public const string CoinUnlockNodeId = "Facility_Coin_T1";

        private static bool IsCoinUnlocked()
        {
            var mgr = UpgradeManager.Instance;
            return mgr == null || mgr.IsNodeUnlocked(CoinUnlockNodeId);
        }

        /// <summary>
        /// 코인 탭을 해금 상태에 맞춰 켜고 끈다. 잠겼으면 탭 자체를 숨긴다(존재를 노출하지 않는다).
        ///
        /// 숨기기만 하면 상단 세그먼트(ModeToggle)의 HorizontalLayoutGroup이
        /// childForceExpandWidth로 남은 주식 탭 하나를 알약 전체 폭까지 늘려버려
        /// 글자가 가운데로 밀린다. 그래서 잠긴 동안에는 주식 탭에 LayoutElement를 얹어
        /// "원래의 반칸" 폭으로 고정한다 → 두 칸일 때와 같은 자리·같은 크기로 보인다.
        /// (씬 편집 없이 런타임에서만 처리 — 프리팹/씬 값은 그대로 둔다.)
        /// </summary>
        private void RefreshCoinLock()
        {
            bool open = IsCoinUnlocked();

            if (coinTabToggle) coinTabToggle.gameObject.SetActive(open);
            if (!open && coinModeRoot) coinModeRoot.SetActive(false);
            if (!open && _mode == MarketMode.Coin) SetMode(MarketMode.Stock);

            ApplySoloTabWidth(!open);
        }

        /// <summary>
        /// 코인 탭이 빠졌을 때 주식 탭이 알약 전체로 늘어나지 않게 폭을 반칸으로 묶는다.
        /// 해금되면 묶음을 풀어 레이아웃 그룹이 원래대로 두 칸을 나눠 갖게 한다.
        /// </summary>
        private void ApplySoloTabWidth(bool solo)
        {
            if (stockTabToggle == null) return;

            var le = stockTabToggle.GetComponent<LayoutElement>();
            if (le == null)
            {
                if (!solo) return;               // 풀 때는 없으면 할 일도 없다
                le = stockTabToggle.gameObject.AddComponent<LayoutElement>();
            }

            if (!solo)
            {
                le.ignoreLayout = false;
                le.preferredWidth = -1f;
                le.flexibleWidth = -1f;
                return;
            }

            // 두 칸일 때의 한 칸 폭 = (부모 폭 − 좌우 패딩 − 간격) / 2
            float slot = 0f;
            var parent = stockTabToggle.transform.parent as RectTransform;
            if (parent != null)
            {
                float inner = parent.rect.width;
                var hl = parent.GetComponent<HorizontalLayoutGroup>();
                if (hl != null) inner -= hl.padding.left + hl.padding.right + hl.spacing;
                slot = inner * 0.5f;
            }

            le.preferredWidth = slot > 1f ? slot : -1f;
            le.flexibleWidth = 0f;              // 남는 공간을 먹지 않게 — 이게 가운데로 밀리는 원인
        }

        public void SetMode(MarketMode mode)
        {
            // 잠긴 코인으로는 넘어가지 않는다 (Tab 키·직접 호출 모두 여기서 막힌다).
            if (mode == MarketMode.Coin && !IsCoinUnlocked()) return;

            _mode = mode;

            // 토글 시각 상태 동기화. 활성 토글만 isOn=true로 두면 ToggleGroup이 나머지를 자동으로 끈다.
            // 이미 켜져 있으면(=사용자가 직접 누른 경우) 다시 세팅하지 않아 콜백이 재귀하지 않는다.
            if (mode == MarketMode.Stock && stockTabToggle && !stockTabToggle.isOn) stockTabToggle.isOn = true;
            if (mode == MarketMode.Coin && coinTabToggle && !coinTabToggle.isOn) coinTabToggle.isOn = true;

            if (stockModeRoot) stockModeRoot.SetActive(mode == MarketMode.Stock);
            if (coinModeRoot) coinModeRoot.SetActive(mode == MarketMode.Coin);

            if (mode == MarketMode.Stock)
            {
                if (stockUI) stockUI.Open();
            }
            else
            {
                if (stockUI) stockUI.Close();
                if (coinUI) coinUI.Open();
            }
        }

        /// <summary>
        /// 마켓 씬이 떠 있는 동안 뒤쪽 월드 입력(이동·채굴·E 상호작용·핫키)을 확실히 막는다.
        /// 보통은 진입 지점(MarketTerminalBehaviour)이 이미 UIState.Market을 세팅해 두는데,
        /// 다른 경로(디버그 로드·씬 단독 실행 등)로 열렸을 때 뒤쪽 월드가 살아 있는 구멍을 여기서 메운다.
        /// 우리가 세팅한 경우에만 OnDestroy에서 되돌린다(진입 지점의 복구와 이중으로 꼬이지 않게).
        /// </summary>
        private void ClaimUIState()
        {
            var ui = UIStateManager.Instance;
            if (ui == null || ui.CurrentState == UIState.Market) return;

            ui.SetState(UIState.Market);
            _claimedUIState = true;
        }

        private void ReleaseUIState()
        {
            if (!_claimedUIState) return;
            _claimedUIState = false;

            var ui = UIStateManager.Instance;
            if (ui != null && ui.CurrentState == UIState.Market) ui.SetState(UIState.None);
        }

        public void Exit()
        {
            // 체류 시간이 바닥이면 '공들인 기능을 아무도 안 쓴다'도 하나의 결론이다(설계 §3.4)
            Telemetry.Log(TelemetryEvents.MarketVisit, TelemetryPayload.New()
                .Add("phase", "exit")
                .Add("seconds", _visitStartTime > 0f ? Time.unscaledTime - _visitStartTime : 0f)
                .Add("mode", _mode.ToString()));

            // 코인 로스터·통계 등을 파일에 확정 (CoinGameManager가 살아있는 동안 캡처)
            if (SaveManager.Instance != null) SaveManager.Instance.Save();

            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded && SceneManager.sceneCount > 1)
                SceneManager.UnloadSceneAsync(sceneName);
        }

        // ===================================================
        // 골드
        // ===================================================
        private void HookGold()
        {
            var stat = ResolvePlayerStat();
            if (stat != null)
            {
                stat.OnGoldChanged -= OnGoldChanged;
                stat.OnGoldChanged += OnGoldChanged;
            }
        }

        private void OnGoldChanged(int g) => RefreshGold();

        public void RefreshGold()
        {
            var stat = ResolvePlayerStat();
            if (goldText) goldText.text = stat != null ? StockLoc.LF("ui_stock_gold", "{0:N0} G", stat.Gold) : "- G";
        }

        private PlayerStat ResolvePlayerStat()
        {
            if (_playerStat == null)
                _playerStat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
            return _playerStat;
        }
    }
}
