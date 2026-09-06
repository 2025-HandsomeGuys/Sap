// @tags: coin, manager, gambling, betting, roster, daily-lock, singleton, save
using System;
using System.Collections.Generic;
using UnityEngine;
using Coin.Data;
using Coin.Systems;

namespace Coin.Core
{
    /// <summary>
    /// 코인 계열 두뇌 (설계 §8.3). 슬롯 로스터 관리, 하루 N판 제한(코인 잠금 없음 — 아무 코인이나 플레이 가능),
    /// 베팅 검증·골드 연동·정산, 상장폐지 교체, 세션/통계/세이브.
    /// 씬 스코프(MarketScene). 영속 데이터는 SaveManager(PlayerData.coinSave)로.
    /// </summary>
    public class CoinGameManager : MonoBehaviour
    {
        public static CoinGameManager Instance { get; private set; }

        [SerializeField] private CoinTableSO coinTable;

        private readonly CoinPriceEngine _engine = new CoinPriceEngine();
        private PlayerStat _playerStat;

        private readonly List<CoinData> _slots = new List<CoinData>();
        private readonly List<CoinSlotState> _roster = new List<CoinSlotState>();
        private List<string> _replacementNames = new List<string>();

        // 엔진 차트가 현재 담고 있는 슬롯. 같은 코인 재진입 시 차트를 리셋하지 않고 이어가기 위한 추적값.
        private int _engineSlotId = -1;

        // 데일리 잠금 / 라운드
        public int LockedSlotId { get; private set; } = -1;
        public int LockedDay { get; private set; } = -1;
        public int RoundsToday { get; private set; }
        public long DayStartGold { get; private set; }   // 오늘 첫 베팅 시점 골드 — 출금 한도 시작점
        public long DayFirstStake { get; private set; }  // 오늘 첫 베팅 금액 — 출금 한도 기준(첫 베팅액 × 배수까지만 이익)

        // 세션(휘발) / 통계(영속)
        public int CurrentStreak { get; private set; }
        public long SessionPnL { get; private set; }
        public long TotalStaked { get; private set; }
        public long TotalProfit { get; private set; }
        public int RoundsPlayed { get; private set; }
        public int BestSingleWin { get; private set; }
        public int LongestStreak { get; private set; }
        public int DelistsWitnessed { get; private set; }

        public event Action<int> OnDailySlotLocked;
        public event Action<CoinRoundResult> OnRoundResolved;
        public event Action<int, string, string> OnCoinDelisted; // slotId, oldName, newName
        public event Action OnSessionChanged;

        public IReadOnlyList<CoinData> Slots => _slots;
        public IReadOnlyList<CoinSlotState> Roster => _roster;
        public IReadOnlyList<float> History => _engine.History;
        public float CurrentPrice => _engine.CurrentPrice;
        public int DailyRoundLimit => coinTable != null ? coinTable.dailyRoundLimit : 5;
        public int RoundsLeft => Mathf.Max(0, DailyRoundLimit - RoundsToday);
        public int PlayerGold => ResolvePlayerStat() != null ? _playerStat.Gold : 0;

        // 일일 출금 한도 — 코인 이익은 오늘 '첫 베팅액 × 배수'까지만. 골드 상한 = 시작 골드 + 첫 베팅액 × 배수.
        public int WithdrawLimitMultiplier => coinTable != null ? Mathf.Max(1, coinTable.withdrawLimitMultiplier) : 5;
        public long WithdrawLimitGold => DayStartGold + DayFirstStake * WithdrawLimitMultiplier;

        /// <summary>오늘 코인 이익으로 더 딸 수 있는 골드(출금 한도까지). 미설정/초과 시 한도 처리.</summary>
        public int WithdrawLimitHeadroom()
        {
            if (LockedDay != Today || DayStartGold <= 0 || DayFirstStake <= 0) return int.MaxValue;
            long room = WithdrawLimitGold - PlayerGold;
            if (room <= 0) return 0;
            return room > int.MaxValue ? int.MaxValue : (int)room;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildRoster();
        }

        private void Start()
        {
            // 세이브에서 로스터·잠금·통계 복원
            if (SaveManager.Instance != null && SaveManager.Instance.playerData != null)
                ApplySaveData(SaveManager.Instance.playerData.coinSave);
            NormalizeForToday(); // 다른 씬에서 날짜가 지났으면 오늘 판수/세션을 리셋
        }

        private void OnEnable()
        {
            if (DayCycleManager.Instance != null)
                DayCycleManager.Instance.OnDateTimeChanged += OnDateChanged;
        }

        private void OnDisable()
        {
            if (DayCycleManager.Instance != null)
                DayCycleManager.Instance.OnDateTimeChanged -= OnDateChanged;
        }

        private void OnDateChanged(int day, TimeOfDay time) => NormalizeForToday();

        // 날짜가 앵커일(LockedDay)과 다르면 새 날: 오늘 판수·세션 휘발값을 리셋한다.
        // (출금 한도 앵커도 다음 첫 베팅에서 LockedDay!=Today 분기로 다시 잡힌다)
        private void NormalizeForToday()
        {
            if (LockedDay == Today) return;
            RoundsToday = 0;
            CurrentStreak = 0;
            SessionPnL = 0;
            OnSessionChanged?.Invoke();
        }

        // ===================================================
        // 로스터 구성
        // ===================================================
        private void BuildRoster()
        {
            _slots.Clear();
            _roster.Clear();

            var slots = (coinTable != null && coinTable.slots != null && coinTable.slots.Count > 0)
                ? coinTable.slots : CoinTableSO.DefaultSlots();
            _replacementNames = (coinTable != null && coinTable.replacementNames != null && coinTable.replacementNames.Count > 0)
                ? new List<string>(coinTable.replacementNames) : CoinTableSO.DefaultReplacementNames();

            foreach (var s in slots)
            {
                _slots.Add(s);
                _roster.Add(new CoinSlotState
                {
                    slotId = s.id,
                    currentName = s.defaultName,
                    currentBasePrice = s.basePrice,
                    delistCount = 0
                });
            }
        }

        public CoinData SlotTier(int slotId) => _slots.Find(s => s.id == slotId);
        public CoinSlotState Slot(int slotId) => _roster.Find(r => r.slotId == slotId);
        public bool CanAfford(int slotId) { var t = SlotTier(slotId); return t != null && PlayerGold >= t.minBet; }

        private int Today => DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 0;

        /// <summary>오늘 날짜(세이브 앵커 비교용). UI가 하루 단위 상태를 판정할 때 사용.</summary>
        public int CurrentDay => Today;

        // ===================================================
        // 데일리 잠금 / 선택
        // ===================================================
        // 하루 1코인 잠금 제거 — 진입 가능한 코인은 아무거나 플레이할 수 있다.
        // (판수·출금 한도만 하루 단위로 유지) 항상 선택 가능.
        public bool CanSelectSlotToday => true;

        // 베팅 가능 조건: 베팅 화면에 들어와 있고(ActiveSlot), 오늘 판 여유가 남았을 때.
        // 코인 잠금은 없다 — 남은 판수는 코인 전체 합산으로 소진된다.
        public bool CanBetToday => ActiveSlotId >= 0 && RoundsToday < DailyRoundLimit;

        /// <summary>현재 플레이(또는 미리보기 진입) 중인 슬롯. 미확정 진입도 포함.</summary>
        public int ActiveSlotId { get; private set; } = -1;

        /// <summary>스테이크·헤더 계산용 현재 슬롯(미확정 진입 우선, 없으면 잠긴 슬롯).</summary>
        public int CurrentSlotId => ActiveSlotId >= 0 ? ActiveSlotId : LockedSlotId;

        /// <summary>현재 선택 배율(레버리지). 1=일반.</summary>
        public int CurrentLeverage { get; private set; } = 1;

        /// <summary>현재 슬롯이 허용하는 최대 배율.</summary>
        public int MaxLeverage { get { var t = SlotTier(CurrentSlotId); return t != null ? Mathf.Max(1, t.maxLeverage) : 1; } }

        /// <summary>배율 설정(슬롯 최대치로 클램프). 베팅 UI에서 호출.</summary>
        public void SetLeverage(int lev) => CurrentLeverage = Mathf.Clamp(lev, 1, MaxLeverage);

        /// <summary>
        /// 베팅 화면 진입. 진입 가능한 코인은 아무거나 들어갈 수 있다(하루 1코인 잠금 없음).
        /// 엔진이 이미 이 코인의 차트를 들고 있으면 세션을 이어가고, 다른 코인이면 새 차트로 시작한다.
        /// 새로 시작하는 경우 골드가 최소 베팅액에 못 미치면 false.
        /// </summary>
        public bool BeginSlot(int slotId)
        {
            var tier = SlotTier(slotId);
            var state = Slot(slotId);
            if (tier == null || state == null) return false;

            bool resumeSame = _engine.HasData && _engineSlotId == slotId; // 진행 중인 그 코인으로 재진입
            if (!resumeSame && PlayerGold < tier.minBet) return false;    // 새 코인 진입 골드 게이트

            ActiveSlotId = slotId;
            if (!resumeSame)
            {
                // 새 코인 탐색: 세션 초기화 + 엔진 새 차트로 시작.
                CurrentStreak = 0;
                SessionPnL = 0;
                CurrentLeverage = 1; // 새 코인은 기본 1배부터
                _engine.StartCoin(tier, state.currentBasePrice);
                _engineSlotId = slotId;
            }
            CurrentLeverage = Mathf.Clamp(CurrentLeverage, 1, MaxLeverage); // 티어 최대치로 클램프
            OnSessionChanged?.Invoke();
            return true;
        }

        /// <summary>베팅 화면을 떠나 선택 화면으로 돌아갈 때 미확정 진입을 해제한다(잠금엔 영향 없음).</summary>
        public void ClearActiveSlot() => ActiveSlotId = -1;

        /// <summary>베팅 화면 표시 시 현재 활성 슬롯의 엔진(비영속 차트)이 없으면 다시 띄운다.</summary>
        public void ResumeLockedSlot()
        {
            if (_engine.HasData) return; // 이미 진행 중
            int slot = ActiveSlotId >= 0 ? ActiveSlotId : LockedSlotId;
            if (slot < 0) return;
            var tier = SlotTier(slot);
            var state = Slot(slot);
            if (tier != null && state != null)
            {
                _engine.StartCoin(tier, state.currentBasePrice);
                _engineSlotId = slot;
            }
        }

        // ===================================================
        // 베팅
        // ===================================================
        public (int min, int max) StakeRange()
        {
            var tier = SlotTier(CurrentSlotId);
            if (tier == null) return (0, 0);
            int gold = PlayerGold;
            int max = tier.unlimitedMax ? gold : Mathf.Min(tier.maxBet, gold);
            int min = Mathf.Min(tier.minBet, Mathf.Max(0, max));
            return (min, max);
        }

        public int ClampStake(int requested)
        {
            var (min, max) = StakeRange();
            if (max <= 0) return 0;
            return Mathf.Clamp(requested, min, max);
        }

        /// <summary>보유 골드의 pct%(0~100)를 스테이크로 환산(한도 클램프). 코인 베팅 UI용.</summary>
        public int StakeFromPercent(int pct)
        {
            pct = Mathf.Clamp(pct, 0, 100);
            return ClampStake(Mathf.RoundToInt(PlayerGold * (pct / 100f)));
        }

        /// <summary>해당 %의 원금액이 최소 베팅금액 이상이라 유효한지(클램프로 끌어올려지지 않는지).</summary>
        public bool IsStakePercentValid(int pct)
        {
            var (min, max) = StakeRange();
            if (max <= 0) return false;
            int raw = Mathf.RoundToInt(PlayerGold * (Mathf.Clamp(pct, 0, 100) / 100f));
            return raw >= min;
        }

        /// <summary>예상 손익 범위(레버리지 반영). 빗나감은 마진까지만(청산), 적중은 N배·엣지 차감.</summary>
        public (int worst, int best) PreviewPnL(int stake)
        {
            var tier = SlotTier(CurrentSlotId);
            if (tier == null) return (0, 0);
            // 평균 변동폭 × 레버리지 기준(대표값). 레버리지가 클수록 적중·청산 상한이 함께 커진다.
            // 적중은 effWinCap·엣지·연승감쇠·베팅세 + 일일 출금 한도 헤드룸, 빗나감은 effLossCap·베팅세 후 지갑 클램프.
            int lev = Mathf.Max(1, CurrentLeverage);
            float avgSwing = (tier.minSwing + tier.maxSwing) * 0.5f;
            float move = avgSwing * lev;
            float t = tier.maxLeverage <= 1 ? 0f : Mathf.Clamp01((lev - 1f) / (tier.maxLeverage - 1f));
            float effWinCap = Mathf.Lerp(tier.winCap * 0.5f, tier.winCap, t);
            float effLossCap = Mathf.Lerp(1f, tier.lossCap, t);
            int feeAmount = coinTable != null && coinTable.fee > 0f ? Mathf.RoundToInt(stake * coinTable.fee) : 0;

            float effWin = Mathf.Min(move, effWinCap);
            float edge = Mathf.Clamp01(tier.houseEdge + Mathf.Max(0, CurrentStreak) * tier.streakPayoutDecay);
            int best = Mathf.RoundToInt(stake * effWin * (1f - edge)) - feeAmount;
            int headroom = WithdrawLimitHeadroom();                            // 일일 출금 한도까지만
            if (best > headroom) best = headroom;
            if (best < 0) best = 0;

            float effLoss = Mathf.Min(move, effLossCap);
            int worst = Mathf.Min(Mathf.RoundToInt(stake * effLoss) + feeAmount, PlayerGold);
            return (-worst, best);
        }

        /// <summary>베팅 1회. 검증 통과 시 정산하고 결과를 반환. 상장폐지면 슬롯 코인 교체.</summary>
        public bool TryBet(BetDirection dir, int stake, out CoinRoundResult result)
        {
            result = default;
            if (ActiveSlotId < 0) return false;

            // 하루 첫 베팅에서 날짜 앵커를 확정한다(코인 잠금은 없음 — 출금 한도·판수만 하루 단위).
            if (LockedDay != Today)
            {
                LockedDay = Today;
                RoundsToday = 0;
                DayStartGold = PlayerGold;           // 오늘 시작 골드 기록(출금 한도 시작점)
                DayFirstStake = Mathf.Max(1, stake); // 오늘 첫 베팅액 기록(출금 한도 = 첫 베팅액 × 배수)
                OnDailySlotLocked?.Invoke(ActiveSlotId);
            }
            LockedSlotId = ActiveSlotId; // 이번 판 대상 코인(통계·상장폐지·텔레메트리용)

            if (!CanBetToday) return false;

            var tier = SlotTier(LockedSlotId);
            var state = Slot(LockedSlotId);
            if (tier == null || state == null) return false;

            var (min, max) = StakeRange();
            if (stake <= 0 || stake < min || stake > max) return false;

            result = _engine.Resolve(dir, stake, CurrentStreak, CurrentLeverage); // 연승 욕심 + 레버리지 반영

            // 양방향 베팅세 — 승패 무관 stake에서 레이크(money sink). 기본 0이면 영향 없음.
            float fee = coinTable != null ? coinTable.fee : 0f;
            if (fee > 0f)
            {
                result.fee = Mathf.RoundToInt(stake * fee);
                result.delta -= result.fee;
            }

            // 일일 출금 한도 — 코인 이익으로 골드가 (시작 골드 × 배수)를 넘지 못한다(베팅·손실은 무제한).
            if (result.delta > 0)
            {
                int headroom = WithdrawLimitHeadroom();
                if (result.delta > headroom) result.delta = headroom;
            }

            // 소프트 초과청산(L1) — 손실이 보유 골드를 넘으면 지갑까지만(음수 골드/빚 없음).
            int gold = PlayerGold;
            if (result.delta < -gold)
                result.delta = -gold;

            ApplyDelta(result.delta);

            // 텔레메트리 — 설계대로 EV가 마이너스인지 실측 검증하는 유일한 수단(설계 §3.4).
            // CurrentStreak/CurrentLeverage는 아직 이번 판을 반영하기 전 값 = Resolve에 실제로 들어간 값이다.
            Telemetry.Log(TelemetryEvents.CoinBet, TelemetryPayload.New()
                .Add("slot", LockedSlotId)
                .Add("coin", state.currentName)
                .Add("dir", dir.ToString())
                .Add("stake", stake)
                .Add("delta", result.delta)
                .Add("fee", result.fee)
                .Add("win", result.win)
                .Add("liquidated", result.liquidated)
                .Add("delisted", result.delisted)
                .Add("leverage", CurrentLeverage)
                .Add("streak_before", CurrentStreak)
                .Add("round_of_day", RoundsToday + 1));

            // 통계 / 세션
            RoundsToday++;
            RoundsPlayed++;
            TotalStaked += stake;
            TotalProfit += result.delta;
            SessionPnL += result.delta;
            if (result.win)
            {
                CurrentStreak++;
                if (CurrentStreak > LongestStreak) LongestStreak = CurrentStreak;
                if (result.delta > BestSingleWin) BestSingleWin = result.delta;
            }
            else CurrentStreak = 0;

            // 상장폐지 → 슬롯 코인 교체
            if (result.delisted)
            {
                string oldName = state.currentName;
                string newName = DelistAndReplace(LockedSlotId);
                result.replacementName = newName;
                DelistsWitnessed++;
                OnCoinDelisted?.Invoke(LockedSlotId, oldName, newName);
            }

            MirrorToSave();
            OnRoundResolved?.Invoke(result);
            OnSessionChanged?.Invoke();
            return true;
        }

        // ===================================================
        // 상장폐지 → 교체 (동적 로스터 §7.7)
        // ===================================================
        private string DelistAndReplace(int slotId)
        {
            var tier = SlotTier(slotId);
            var state = Slot(slotId);
            if (tier == null || state == null) return null;

            // 슬롯(스테이지)별 순환 목록이 있으면 A→B→C→A 로 돌려 쓴다. 없으면 전역 예비 풀에서 랜덤.
            string newName = tier.RotationNameAt(state.delistCount + 1) ?? PickReplacementName();
            state.currentName = newName;
            state.currentBasePrice = tier.basePrice * UnityEngine.Random.Range(0.6f, 1.8f);
            state.delistCount++;
            _engine.StartCoin(tier, state.currentBasePrice);
            _engineSlotId = slotId;
            return newName;
        }

        private string PickReplacementName()
        {
            var inUse = new HashSet<string>();
            foreach (var r in _roster) inUse.Add(r.currentName);

            var avail = new List<string>();
            foreach (var n in _replacementNames)
                if (!inUse.Contains(n)) avail.Add(n);

            if (avail.Count > 0)
                return avail[UnityEngine.Random.Range(0, avail.Count)];

            // 풀 소진: 접미 번호 부여
            string baseName = _replacementNames.Count > 0
                ? _replacementNames[UnityEngine.Random.Range(0, _replacementNames.Count)]
                : "COIN";
            return baseName + "-" + UnityEngine.Random.Range(2, 99);
        }

        // ===================================================
        // 골드 연동
        // ===================================================
        private void ApplyDelta(int delta)
        {
            var stat = ResolvePlayerStat();
            if (stat == null) return;
            if (delta >= 0) stat.AddGold(delta);
            else stat.SpendGold(-delta);
            DayEarningsLedger.Report(DayEarningsCategory.Coin, delta);
        }

        private PlayerStat ResolvePlayerStat()
        {
            if (_playerStat == null)
                _playerStat = UnityEngine.Object.FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
            return _playerStat;
        }

        // ===================================================
        // 세이브 (PlayerData.coinSave ↔ SaveManager)
        // ===================================================
        public CoinSaveData CaptureSaveData()
        {
            var data = new CoinSaveData
            {
                hasData = true,
                lockedDay = LockedDay,
                lockedSlotId = LockedSlotId,
                roundsToday = RoundsToday,
                dayStartGold = DayStartGold,
                dayFirstStake = DayFirstStake,
                totalStaked = TotalStaked,
                totalProfit = TotalProfit,
                roundsPlayed = RoundsPlayed,
                bestSingleWin = BestSingleWin,
                longestStreak = LongestStreak,
                delistsWitnessed = DelistsWitnessed
            };
            foreach (var r in _roster)
            {
                data.roster.Add(new CoinSlotState
                {
                    slotId = r.slotId,
                    currentName = r.currentName,
                    currentBasePrice = r.currentBasePrice,
                    delistCount = r.delistCount
                });
            }
            return data;
        }

        public void ApplySaveData(CoinSaveData data)
        {
            if (data == null || !data.hasData) return;

            LockedDay = data.lockedDay;
            LockedSlotId = data.lockedSlotId;
            RoundsToday = data.roundsToday;
            DayStartGold = data.dayStartGold;
            DayFirstStake = data.dayFirstStake;
            TotalStaked = data.totalStaked;
            TotalProfit = data.totalProfit;
            RoundsPlayed = data.roundsPlayed;
            BestSingleWin = data.bestSingleWin;
            LongestStreak = data.longestStreak;
            DelistsWitnessed = data.delistsWitnessed;

            if (data.roster != null)
            {
                foreach (var saved in data.roster)
                {
                    var state = Slot(saved.slotId);
                    if (state == null) continue;
                    state.currentName = saved.currentName;
                    state.currentBasePrice = saved.currentBasePrice;
                    state.delistCount = saved.delistCount;
                }
            }
            OnSessionChanged?.Invoke();
        }

        /// <summary>인메모리 세이브 미러 갱신. 다음 SaveManager.Save() 때 파일에 반영된다.</summary>
        private void MirrorToSave()
        {
            if (SaveManager.Instance != null && SaveManager.Instance.playerData != null)
                SaveManager.Instance.playerData.coinSave = CaptureSaveData();
        }
    }
}
