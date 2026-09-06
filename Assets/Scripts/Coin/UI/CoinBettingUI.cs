// @tags: coin, ui, betting, gambling, stake, percent
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UnityEngine.U2D.Animation; // 스프라이트 라이브러리 제어를 위해 추가
using TMPro;
using Coin.Core;
using Coin.Data;
using Market;

namespace Coin.UI
{
    /// <summary>
    /// 캐릭터의 기분 상태 트리거
    /// </summary>
    public enum CoinMood
    {
        Idle,           // 평상시 (수익 0)
        IdleGood,       // 상시 좋음 (수익 양수)
        IdleBad,        // 상시 나쁨 (수익 음수)
        Tension,        // 긴장 (베팅 직후 롤링 중)
        HighTension,    // 초긴장 (떡상/폭락/청산 등 브레이킹 이벤트 직전 멈춤)
        Good,           // 좋음 (일반 적중)
        VeryGood,       // 많이 좋음 (떡상)
        Bad,            // 나쁨 (일반 빗나감)
        VeryBad         // 많이 나쁨 (폭락, 청산, 상장폐지)
    }

    [System.Serializable]
    public struct MoodLibraryMapping
    {
        public CoinMood mood;
        public SpriteLibraryAsset libraryAsset;
    }

    /// <summary>
    /// 베팅 테이블 (설계 §8.7). 차트·스테이크(보유 골드 %)·방향·정산·재미요소 묶음.
    /// 스테이크는 TMP_InputField 없이 **보유 골드의 퍼센트 프리셋 버튼**으로 정한다.
    /// Reveal 중에는 베팅 버튼이 잠긴다(중복 방지).
    /// </summary>
    public class CoinBettingUI : MonoBehaviour
    {
        [SerializeField] private CoinModeUI modeUI;
        [SerializeField] private CoinChartUI chart;
        [SerializeField] private TextMeshProUGUI coinNameText;
        [SerializeField] private TextMeshProUGUI priceText;
        [Tooltip("현재 보유 골드(소지금) 표시. 비우면 숨김")]
        [SerializeField] private TextMeshProUGUI goldText;

        [Header("스테이크 — 보유 골드 퍼센트")]
        [SerializeField] private Button[] stakeButtons;              // percentValues와 같은 순서·길이
        [SerializeField] private int[] percentValues = { 10, 25, 50, 100 }; // 버튼에 대응할 % 후보들
        [SerializeField] private TextMeshProUGUI stakeAmountText;    // "₩X (Y%)"

        [Header("레버리지 (배율)")]
        [SerializeField] private Button[] leverageButtons;           // leverageValues와 같은 순서·길이
        [SerializeField] private int[] leverageValues = { 1, 2, 5, 10, 25 }; // 버튼에 대응할 배율 후보들
        [SerializeField] private TextMeshProUGUI leverageText;       // 메인 라인 "배율 x10" (크게)
        [Tooltip("청산 조건 보조 라인 \"변동 10%↑ 청산\" (작게·회색). 비우면 숨김")]
        [SerializeField] private TextMeshProUGUI leverageSubText;

        [Header("방향 / 흐름")]
        [SerializeField] private Button upButton, downButton, backButton;
        [SerializeField] private TextMeshProUGUI pnlPreviewText;
        [SerializeField] private TextMeshProUGUI roundsLeftText;
        [Tooltip("일일 출금 한도(오늘 시작 골드 × 배수)까지 남은 골드 표시. 비우면 숨김")]
        [FormerlySerializedAs("dailyCapText")]
        [SerializeField] private TextMeshProUGUI withdrawLimitText;
        [SerializeField] private CoinResultPopupUI resultPopup;
        [SerializeField] private StreakHUD streakHud;
        [Tooltip("떡상/떡락/청산 시 잠깐 멈춘 그래프 위에 영향 뉴문을 먼저 띄우고, 뉴스가 그래프 공개를 트리거한다. 비우면 뉴스 없음")]
        [SerializeField] private CoinEventNewsUI eventNews;

        [Header("연출 — 결과 피드백")]
        [Tooltip("결과 시 흔들 UI(보통 차트/패널 RectTransform). 비우면 UI 펀치 없음")]
        [SerializeField] private RectTransform shakeTarget;

        [Header("연출 — 캐릭터 기분 (스프라이트 라이브러리)")]
        [SerializeField] private SpriteLibrary characterSpriteLibrary;
        [Tooltip("기분 상태별로 인스펙터에서 Sprite Library Asset을 할당해주세요.")]
        [SerializeField] private List<MoodLibraryMapping> moodLibraries;

        private int _percent = 10;
        private int _stake;
        private bool _busy;
        private bool _buttonsOn;          // 마지막 SetButtonsInteractable 상태 — 클릭 중간 갱신용
        private CoinRoundResult _pending;
        private bool _awaitingCoinSwap;   // 상장폐지: 크래시 줌아웃 완료 후 새 코인으로 교체 대기 중

        private Coroutine _shakeCo;
        private Vector2 _shakeOrigin;
        private Coroutine _moodLatchCo;   // 기분 유지용 코루틴 추가

        private Color _priceDefaultColor = Color.white; // 시세 텍스트 원래 색 — 롤링/착지 색에서 복귀용
        private NewsTicker _newsTicker;                 // 하단 흐르는 티커 — 라운드 연출 중 숨김(자동 탐색)
        private bool _newsTickerSearched;

        // WASD 키보드 조작 — 스테이크·배율·방향(업/다운)·뒤로 버튼을 한 판으로 보고 좌표로 이동, Space로 실행.
        // (창고·인벤토리·강화 화면과 같은 공용 내비게이션 시스템)
        private readonly CodeSlotNavigator _nav = new CodeSlotNavigator();
        private readonly List<ICodeNavItem> _navItems = new List<ICodeNavItem>();
        private bool _navBuilt;

        private void Awake()
        {
            if (priceText) _priceDefaultColor = priceText.color;
            if (leverageSubText) leverageSubText.color = MarketTheme.TextMuted; // 청산 라인은 보조 정보 톤
            if (stakeButtons != null)
                for (int i = 0; i < stakeButtons.Length; i++)
                {
                    int pct = percentValues[i];
                    if (stakeButtons[i]) stakeButtons[i].onClick.AddListener(() => SetPercent(pct));
                }
            if (leverageButtons != null)
                for (int i = 0; i < leverageButtons.Length; i++)
                {
                    int lev = leverageValues[i];
                    if (leverageButtons[i]) leverageButtons[i].onClick.AddListener(() => OnLeverage(lev));
                }
            if (upButton) upButton.onClick.AddListener(() => Bet(BetDirection.Up));
            if (downButton) downButton.onClick.AddListener(() => Bet(BetDirection.Down));
            if (backButton) backButton.onClick.AddListener(() => { if (modeUI) modeUI.BackToSelect(); });
        }

        private void OnEnable()
        {
            if (chart)
            {
                chart.OnAnticipatePeak += OnAnticipatePeak;
                chart.OnAnticipateTick += OnChartTick;
                chart.OnRevealValue += OnChartRevealValue;
                chart.OnRevealComplete += OnRevealComplete;
                chart.OnZoomOutComplete += OnZoomOutComplete;
            }

            BuildNav();
            _nav.Begin();
            _nav.ClearFocus();   // 커서 없이 시작 — 첫 W/A/S/D 입력이 커서를 켠다
        }

        private void OnDisable()
        {
            if (chart)
            {
                chart.OnAnticipatePeak -= OnAnticipatePeak;
                chart.OnAnticipateTick -= OnChartTick;
                chart.OnRevealValue -= OnChartRevealValue;
                chart.OnRevealComplete -= OnRevealComplete;
                chart.OnZoomOutComplete -= OnZoomOutComplete;
            }
            _nav.End();
            if (_shakeCo != null) { _shakeCo = null; if (shakeTarget) shakeTarget.anchoredPosition = _shakeOrigin; }
            if (_moodLatchCo != null) _moodLatchCo = null;
        }

        private void Update()
        {
            // Space가 Submit로도 잡혀 마지막 클릭 버튼이 한 번 더 눌리는 걸 막는다.
            CodeUI.ClearSelection();
            // 연출(리빌) 중엔 버튼이 전부 비활성이라 내비게이터는 후보가 없어 조용히 무시된다.
            _nav.Update();
        }

        // ===================================================
        // 캐릭터 기분 변경
        // ===================================================
        private void SetMood(CoinMood mood, bool isBaseMood = false)
        {
            // 수동으로 긴장 상태 등을 세팅할 땐 기존 래치 타이머를 취소합니다.
            if (!isBaseMood && _moodLatchCo != null)
            {
                StopCoroutine(_moodLatchCo);
                _moodLatchCo = null;
            }

            if (characterSpriteLibrary == null || moodLibraries == null) return;

            foreach (var mapping in moodLibraries)
            {
                if (mapping.mood == mood && mapping.libraryAsset != null)
                {
                    characterSpriteLibrary.spriteLibraryAsset = mapping.libraryAsset;
                    break;
                }
            }
        }

        private void UpdateBaseMood()
        {
            long todayProfit = DayEarningsLedger.CoinToday;

            if (todayProfit > 0)
                SetMood(CoinMood.IdleGood, true);
            else if (todayProfit < 0)
                SetMood(CoinMood.IdleBad, true);
            else
                SetMood(CoinMood.Idle, true);
        }

        private void LatchResultMood(float duration)
        {
            if (_moodLatchCo != null) StopCoroutine(_moodLatchCo);
            _moodLatchCo = StartCoroutine(MoodLatchRoutine(duration));
        }

        private IEnumerator MoodLatchRoutine(float duration)
        {
            yield return new WaitForSeconds(duration);
            UpdateBaseMood(); // 지정된 시간 이후 상시 기분으로 자동 복귀
            _moodLatchCo = null;
        }

        // ===================================================
        // WASD 키보드 내비게이션
        // ===================================================
        /// <summary>스테이크·배율·업/다운·뒤로 버튼을 이동 대상으로 등록(1회). 꺼진·비활성 버튼은 collect가 실시간으로 거른다.</summary>
        private void BuildNav()
        {
            if (_navBuilt) return;
            _navBuilt = true;

            AttachNavRange(stakeButtons);
            AttachNavRange(leverageButtons);
            AttachNavOne(upButton);
            AttachNavOne(downButton);
            AttachNavOne(backButton);

            _nav.collect = list =>
            {
                list.Clear();
                foreach (var n in _navItems)
                    if (n != null && n.NavUsable) list.Add(n);
            };
        }

        private void AttachNavRange(Button[] btns)
        {
            if (btns == null) return;
            foreach (var b in btns) AttachNavOne(b);
        }

        private void AttachNavOne(Button b)
        {
            if (b == null) return;
            var nav = CodeNavButton.Attach(b);   // 코드 생성 포커스 링(기본 스킨)
            if (nav != null) _navItems.Add(nav);
        }

        /// <summary>베팅 화면 진입 시 호출.</summary>
        public void Bind()
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null) return;
            mgr.ResumeLockedSlot();
            if (chart) chart.SetHistory(mgr.History);
            _busy = false;
            _awaitingCoinSwap = false;

            if (_percent <= 0) _percent = 10;
            SetNewsTickerVisible(true); // 연출 중이 아니므로 티커 보임 보장(중단 복귀 안전망)
            RecomputeStake();
            SetButtonsInteractable(mgr.CanBetToday);
            RefreshLeverageDisplay();
            RefreshHeader();
            RefreshRounds();
            RefreshWithdrawLimit();
            if (streakHud) streakHud.Refresh();

            UpdateBaseMood(); // 진입 시 오늘 수익에 맞춘 상시 기분으로 초기화
        }

        // ===================================================
        // 스테이크 (보유 골드 퍼센트)
        // ===================================================
        private void SetPercent(int pct)
        {
            _percent = Mathf.Clamp(pct, 1, 100);
            RecomputeStake();
            RefreshStakeButtons(_buttonsOn); // 선택 하이라이트 이동
        }

        private void RecomputeStake()
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null) return;
            EnsureValidPercent();                 // 최소 미달 %면 유효한 가장 낮은 %로 이동
            _stake = mgr.StakeFromPercent(_percent);
            RefreshStakeDisplay();
            RefreshPreview();
        }

        private void EnsureValidPercent()
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null || percentValues == null) return;
            if (IsPercentAvailable(_percent)) return;

            int below = -1, above = int.MaxValue;
            for (int i = 0; i < percentValues.Length; i++)
            {
                int pct = percentValues[i];
                if (!IsPercentAvailable(pct)) continue;
                if (pct <= _percent) below = Mathf.Max(below, pct);
                else above = Mathf.Min(above, pct);
            }
            if (below > 0) _percent = below;
            else if (above < int.MaxValue) _percent = above;
            else _percent = 100; // 폴백 — 입장 가능했으면 100%(전액)는 항상 최소 이상
        }

        private bool IsPercentAvailable(int pct)
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null) return true;
            if (!mgr.IsStakePercentValid(pct)) return false;
            var (_, max) = mgr.StakeRange();
            if (RawStake(mgr, pct) <= max) return true;
            if (percentValues != null)
                for (int i = 0; i < percentValues.Length; i++)
                {
                    int other = percentValues[i];
                    if (other >= pct) continue;
                    if (mgr.IsStakePercentValid(other) && RawStake(mgr, other) > max)
                        return false; // 더 낮은 초과 %가 한도 베팅 대표
                }
            return true;
        }

        private static int RawStake(CoinGameManager mgr, int pct)
            => Mathf.RoundToInt(mgr.PlayerGold * (Mathf.Clamp(pct, 0, 100) / 100f));

        private void RefreshStakeDisplay()
        {
            if (stakeAmountText)
                stakeAmountText.text = CoinLoc.LF("ui_coin_stake_pct", "₩{0:N0} ({1}%)", _stake, _percent);
        }

        // ===================================================
        // 레버리지 (배율)
        // ===================================================
        private void OnLeverage(int lev)
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null) return;
            mgr.SetLeverage(lev);
            RefreshLeverageDisplay();
            RefreshLeverageButtons(_buttonsOn); // 선택 하이라이트 이동
            RefreshPreview(); // 손익 미리보기에 배율 반영
        }

        private void RefreshLeverageDisplay()
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null) return;
            int liqPct = Mathf.Clamp(Mathf.RoundToInt(100f / Mathf.Max(1, mgr.CurrentLeverage)), 1, 100);
            if (leverageText)
                leverageText.text = CoinLoc.LF("ui_coin_leverage", "배율 x{0}", mgr.CurrentLeverage);
            if (leverageSubText)
                leverageSubText.text = CoinLoc.LF("ui_coin_leverage_liq", "변동 {0}%↑ 청산", liqPct);
        }

        private void RefreshLeverageButtons(bool canBet)
        {
            if (leverageButtons == null || leverageValues == null) return;
            var mgr = CoinGameManager.Instance;
            int maxLev = mgr != null ? mgr.MaxLeverage : 1;
            int cur = mgr != null ? mgr.CurrentLeverage : 1;

            int snapped = SnapLeverage(cur, maxLev);
            if (mgr != null && snapped != cur)
            {
                mgr.SetLeverage(snapped);
                cur = mgr.CurrentLeverage;
                RefreshLeverageDisplay();
                RefreshPreview();
            }

            for (int i = 0; i < leverageButtons.Length; i++)
            {
                if (leverageButtons[i] == null) continue;
                int lev = leverageValues[i];
                bool valid = lev <= maxLev;
                leverageButtons[i].gameObject.SetActive(valid);
                leverageButtons[i].interactable = canBet && valid;
                MarketSelectHighlight.Set(leverageButtons[i], valid && lev == cur);
            }
        }

        private int SnapLeverage(int cur, int maxLev)
        {
            int below = -1, lowest = int.MaxValue;
            for (int i = 0; i < leverageValues.Length; i++)
            {
                int lev = leverageValues[i];
                if (lev > maxLev) continue;
                if (lev == cur) return cur;
                if (lev < cur) below = Mathf.Max(below, lev);
                lowest = Mathf.Min(lowest, lev);
            }
            if (below > 0) return below;
            return lowest == int.MaxValue ? 1 : lowest;
        }

        // ===================================================
        // 베팅
        // ===================================================
        private void Bet(BetDirection dir)
        {
            if (_busy) return;
            var mgr = CoinGameManager.Instance;
            if (mgr == null || !mgr.CanBetToday) return;

            float oldPrice = mgr.CurrentPrice;
            if (!mgr.TryBet(dir, _stake, out var result)) return;

            _pending = result;
            _busy = true;
            CoinSfx.Play(CoinSfx.Bet);
            SetButtonsInteractable(false);
            if (eventNews) eventNews.HideImmediate();
            SetNewsTickerVisible(false);

            SetMood(CoinMood.Tension); // 베팅 시작 시 긴장 상태 돌입

            bool newsBeforeReveal = IsBreaking(result) && eventNews != null;
            if (chart) chart.PlayReveal(oldPrice, result.newPrice, result.win, result.extreme, result.swing, newsBeforeReveal);
            else OnRevealComplete();
        }

        private static bool IsBreaking(CoinRoundResult r) => r.extreme || r.delisted || r.liquidated;

        private void SetNewsTickerVisible(bool visible)
        {
            if (_newsTicker == null && !_newsTickerSearched)
            {
                _newsTicker = FindFirstObjectByType<NewsTicker>(FindObjectsInactive.Include);
                _newsTickerSearched = true;
            }
            if (_newsTicker != null) _newsTicker.SetVisible(visible);
        }

        private void OnChartTick(bool up, float progress)
        {
            if (priceText == null) return;
            float jitter = Mathf.Lerp(0.006f, 0.07f, progress) * Random.Range(0.55f, 1f);
            float shown = Mathf.Max(0f, _pending.oldPrice * (1f + (up ? jitter : -jitter)));
            priceText.text = CoinLoc.LF("ui_coin_price", "₩{0:N0}", shown);
            priceText.color = up ? MarketTheme.Up : MarketTheme.Down;
        }

        private void OnChartRevealValue(float value)
        {
            if (priceText == null) return;
            priceText.text = CoinLoc.LF("ui_coin_price", "₩{0:N0}", Mathf.Max(0f, value));
            priceText.color = _pending.newPrice >= _pending.oldPrice ? MarketTheme.Up : MarketTheme.Down;
        }

        private void OnAnticipatePeak()
        {
            bool bullish = _pending.newPrice >= _pending.oldPrice;

            // 극단적인 결과(상폐, 청산, 떡상) 직전 멈춤 시엔 초긴장 상태로 변경
            if (IsBreaking(_pending)) SetMood(CoinMood.HighTension);

            if (eventNews != null && chart != null)
                eventNews.Show(bullish, chart.TriggerReveal);
            else if (chart != null)
                chart.TriggerReveal();
        }

        private void OnRevealComplete()
        {
            PlayResultFeedback(_pending);
            if (resultPopup) resultPopup.Show(_pending);
            if (streakHud) streakHud.Refresh();

            if (chart != null && _pending.delisted) _awaitingCoinSwap = true;
            else FinishRound();

            if (priceText)
            {
                priceText.text = CoinLoc.LF("ui_coin_price", "₩{0:N0}", _pending.newPrice);
                priceText.color = _pending.newPrice >= _pending.oldPrice ? MarketTheme.Up : MarketTheme.Down;
            }
        }

        private void OnZoomOutComplete()
        {
            if (!_awaitingCoinSwap) return;
            _awaitingCoinSwap = false;
            var mgr = CoinGameManager.Instance;
            if (chart != null && mgr != null) chart.SetHistory(mgr.History);
            FinishRound();
        }

        private void FinishRound()
        {
            var mgr = CoinGameManager.Instance;
            _busy = false;

            SetNewsTickerVisible(true);
            RefreshHeader();
            RefreshRounds();
            RefreshWithdrawLimit();
            RecomputeStake();
            SetButtonsInteractable(mgr != null && mgr.CanBetToday);
        }

        // ===================================================
        // 갱신
        // ===================================================
        private void RefreshHeader()
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null) return;
            var state = mgr.Slot(mgr.CurrentSlotId);
            if (coinNameText && state != null) coinNameText.text = state.currentName;
            if (priceText)
            {
                priceText.text = CoinLoc.LF("ui_coin_price", "₩{0:N0}", mgr.CurrentPrice);
                priceText.color = _priceDefaultColor;
            }
            RefreshGold();
        }

        private void RefreshGold()
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null || goldText == null) return;
            goldText.text = CoinLoc.LF("ui_coin_hold", "소지금 ₩{0:N0}", mgr.PlayerGold);
        }

        private void RefreshPreview()
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null || pnlPreviewText == null) return;
            var (worst, best) = mgr.PreviewPnL(_stake);
            int loss = -worst;
            string text = CoinLoc.LF("ui_coin_pnl_preview", "적중 +₩{0:N0} / 빗나감 −₩{1:N0}", best, loss);
            if (_stake > 0 && loss >= _stake)
                text += "  " + CoinLoc.L("ui_coin_liq_tag", "⚠ 청산");
            pnlPreviewText.text = text;
        }

        private void RefreshRounds()
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null || roundsLeftText == null) return;
            roundsLeftText.text = CoinLoc.LF("ui_coin_rounds_left", "오늘 남은 판 {0}/{1}", mgr.RoundsLeft, mgr.DailyRoundLimit);
        }

        private void RefreshWithdrawLimit()
        {
            var mgr = CoinGameManager.Instance;
            if (mgr == null || withdrawLimitText == null) return;
            int headroom = mgr.WithdrawLimitHeadroom();
            if (headroom == int.MaxValue)
                withdrawLimitText.text = CoinLoc.LF("ui_coin_withdraw_limit_idle", "오늘 출금 한도 = 첫 베팅액 x{0} (첫 베팅에 확정)", mgr.WithdrawLimitMultiplier);
            else if (headroom <= 0)
                withdrawLimitText.text = CoinLoc.L("ui_coin_withdraw_limit_done", "🎉 출금 한도 도달 — 적중해도 +₩0 (잃은 만큼만 회복 가능)");
            else
                withdrawLimitText.text = CoinLoc.LF("ui_coin_withdraw_limit", "출금 한도까지 +₩{0:N0} (한도 ₩{1:N0})", headroom, mgr.WithdrawLimitGold);
        }

        private void SetButtonsInteractable(bool on)
        {
            _buttonsOn = on;
            if (upButton) upButton.interactable = on;
            if (downButton) downButton.interactable = on;
            RefreshStakeButtons(on);
            RefreshLeverageButtons(on);
        }

        private void RefreshStakeButtons(bool canBet)
        {
            if (stakeButtons == null || percentValues == null) return;

            for (int i = 0; i < stakeButtons.Length; i++)
            {
                if (stakeButtons[i] == null) continue;
                int pct = percentValues[i];
                bool avail = IsPercentAvailable(pct);
                stakeButtons[i].gameObject.SetActive(avail);
                stakeButtons[i].interactable = canBet && avail;
                MarketSelectHighlight.Set(stakeButtons[i], avail && pct == _percent);
            }
        }

        // ===================================================
        // 결과 피드백 (사운드 + 카메라 셰이크 + UI 펀치 + 기분 연출)
        // ===================================================
        private void PlayResultFeedback(CoinRoundResult r)
        {
            string sfx;

            // 결과에 따른 기분 설정
            if (r.delisted || r.liquidated)
            {
                sfx = r.delisted ? CoinSfx.Delist : CoinSfx.Crash;
                SetMood(CoinMood.VeryBad);
            }
            else if (r.extreme && r.win)
            {
                sfx = CoinSfx.Moon;
                SetMood(CoinMood.VeryGood);
            }
            else if (r.extreme)
            {
                sfx = CoinSfx.Crash;
                SetMood(CoinMood.VeryBad);
            }
            else if (r.win)
            {
                sfx = CoinSfx.Win;
                SetMood(CoinMood.Good);
            }
            else
            {
                sfx = CoinSfx.Lose;
                SetMood(CoinMood.Bad);
            }

            float intensity = Mathf.Clamp01(r.swing * Mathf.Max(1, r.leverage));
            if (r.extreme || r.delisted || r.liquidated) intensity = Mathf.Max(intensity, 0.85f);
            float mag = Mathf.Lerp(0.04f, 0.36f, intensity);
            float dur = Mathf.Lerp(0.12f, 0.50f, intensity);

            CoinSfx.Play(sfx);
            if (CameraShakeManager.Instance != null) CameraShakeManager.Instance.Shake(dur, mag);
            UiPunch(mag);

            // 결과에 맞는 표정을 1.5초 유지 후 상시 상태로 돌아갑니다.
            LatchResultMood(1.5f);
        }

        private void UiPunch(float magUnits)
        {
            if (shakeTarget == null) return;
            if (_shakeCo != null) { StopCoroutine(_shakeCo); shakeTarget.anchoredPosition = _shakeOrigin; }
            _shakeCo = StartCoroutine(UiShakeRoutine(magUnits));
        }

        private IEnumerator UiShakeRoutine(float magUnits)
        {
            _shakeOrigin = shakeTarget.anchoredPosition;
            float amp = Mathf.Clamp(magUnits * 60f, 4f, 24f);
            float dur = 0.25f, t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float falloff = 1f - t / dur;
                shakeTarget.anchoredPosition = _shakeOrigin
                    + new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * amp * falloff;
                yield return null;
            }
            shakeTarget.anchoredPosition = _shakeOrigin;
            _shakeCo = null;
        }
    }
}