// @tags: environment, interaction, sleep, input, ui, fade, daysummary
using System.Collections;
using UnityEngine;
using Sap.UI.Notification;
using Stock.Core;

public class BedInteractable : MonoBehaviour, IInteractionAvailability, IInteractionPrompt
{
    private const float FadeDuration = 0.6f;

    private bool isPlayerInRange = false;
    private bool _isSleeping = false; // 수면 시퀀스(페이드·정산 연출) 진행 중 재진입 방지

    /// <summary>지금 수면(하루 마무리)이 가능한가 — 밤(Afternoon)에만.</summary>
    private static bool CanSleepNow =>
        DayCycleManager.Instance != null &&
        DayCycleManager.Instance.CurrentTime == TimeOfDay.Afternoon;

    /// <summary>지금 낮잠(주식 1틱만)이 가능한가 — 남은 낮잠 횟수가 있으면 낮/밤 무관.</summary>
    private static bool CanNapNow =>
        NapManager.Instance != null && NapManager.Instance.CanNap;

    /// <summary>다른 침대/시퀀스가 이미 재생 중인가.</summary>
    private static bool AnyRestPlaying =>
        SleepSequence.IsPlaying || NapSequence.IsPlaying;

    /// <summary>
    /// 낮잠(횟수 있으면 언제나) 또는 수면(밤에만) 중 하나라도 가능하면 상호작용 가능.
    /// 둘 다 불가한 낮에는 InteractionIndicator 느낌표도 뜨지 않는다.
    /// </summary>
    public bool IsInteractionAvailable =>
        !_isSleeping && !AnyRestPlaying && (CanSleepNow || CanNapNow);

    /// <summary>
    /// 근접 안내 문구 (InteractionPromptLabel이 표시).
    /// 낮잠·수면 둘 다 가능 = 선택 안내 / 밤만 = 하루 마무리 / 낮+낮잠 = 낮잠 / 아무것도 못하면 붉은 안내.
    /// </summary>
    public InteractionPromptInfo GetInteractionPrompt()
    {
        if (_isSleeping) return InteractionPromptInfo.None; // 정산 연출 중에는 문구를 감춘다

        bool canSleep = CanSleepNow;
        bool canNap = CanNapNow;

        if (canSleep && canNap)
            return InteractionPromptInfo.Ok("interact_bed_choose", "수면 / 낮잠");
        if (canSleep)
            return InteractionPromptInfo.Ok("interact_bed_sleep", "하루를 마무리 하기");
        if (canNap)
            return InteractionPromptInfo.Ok("interact_bed_nap", "낮잠 자기");

        // 낮 + 낮잠 불가 = 아무 문구도 띄우지 않는다(BedBehaviour와 동일)
        return InteractionPromptInfo.None;
    }

    private void Update()
    {
        // 설정 오버레이가 열려 있는 동안은 월드 상호작용으로 키가 새지 않게 차단.
        if (SettingsOverlayUI.IsOpen) return;

        // 선택 팝업이 떠 있거나 다른 UI가 열려 있으면 상호작용 키가 침대로 새지 않게 막는다.
        if (BedRestChoiceOverlayUI.IsOpen) return;
        if (UIStateManager.Instance != null && UIStateManager.Instance.CurrentState != UIState.None) return;

        if (isPlayerInRange && InteractionKeys.InteractPressed)
        {
            Interact();
        }
    }

    private void Interact()
    {
        if (_isSleeping || AnyRestPlaying) return;

        if (DayCycleManager.Instance == null)
        {
            Debug.LogError("[Bed] DayCycleManager.Instance가 존재하지 않습니다. 씬에 DayCycleManager가 컴포넌트로 포함되어 있는지 확인하세요.");
            if (NotificationUI.Instance != null)
                NotificationUI.Instance.ShowNotification("시스템 오류: 시간을 전환할 수 없습니다.");
            return;
        }

        bool canSleep = CanSleepNow;
        bool canNap = CanNapNow;

        // 낮잠·수면 둘 다 가능하면 NPC식 선택 팝업을 띄운다.
        if (canSleep && canNap)
        {
            int remaining = NapManager.Instance != null ? NapManager.Instance.RemainingNaps : 0;
            BedRestChoiceOverlayUI.Open(
                remaining,
                onNap:   () => StartCoroutine(NapSequence.Run()),
                onSleep: () => StartCoroutine(SleepRoutine()));
            return;
        }

        // 밤 = 수면(하루 마무리)
        if (canSleep)
        {
            StartCoroutine(SleepRoutine());
            return;
        }

        // 낮 + 낮잠 횟수 남음 = 낮잠(주식 1틱만)
        if (canNap)
        {
            StartCoroutine(NapSequence.Run());
            return;
        }

        // 아무것도 못함 (낮인데 낮잠 횟수도 없음)
        if (NotificationUI.Instance != null)
            NotificationUI.Instance.ShowNotification(CodeUI.L("interact_bed_daytime", "낮에는 잘 수 없습니다."));
    }

    /// <summary>
    /// 수면 시퀀스: 페이드아웃 → 날짜 전환·주식 틱 → 하루 정산 연출(DaySummaryUI) → 페이드인.
    /// 정산 연출은 검은 화면 위에서 재생되어 밤→아침이 자연스럽게 이어진다.
    /// </summary>
    private IEnumerator SleepRoutine()
    {
        _isSleeping = true;
        int endedDay = DayCycleManager.Instance.CurrentDay; // 방금 끝나는 날 (정산 제목용)

        // 1) 페이드아웃 — 화면을 검게 덮고 시작
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeOut(FadeDuration);

        DayCycleManager.Instance.AdvanceToNextDay();

        // 하루 경과 = 주식 시장 SkipTicks(4)틱 진행. 앞 틱은 뉴스 없이 조용히, '마지막 1틱만' 뉴스를
        // 태운다 → 아침에 갓 나온 뉴스 1건과 그 여파만 시세에 반영. 저장보다 먼저 처리하여
        // 변동된 시세·골드가 세이브에 반영되도록 한다.
        if (StockGameManager.Instance != null && StockGameManager.Instance.IsInitialized)
        {
            StockGameManager.Instance.ProcessSleepTicks(StockGameManager.SkipTicks);
            // Debug.Log("[Bed] 주식 시장 틱 진행 완료.");
        }

        // 2) 하루 장부 스냅샷 → 새 하루로 리셋 (리셋된 장부가 세이브에 반영되도록 저장보다 먼저)
        var stat = FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
        int goldNow = stat != null ? stat.Gold : DayEarningsLedger.EstimateCurrentGold();
        DayEarningsReport report = DayEarningsLedger.BuildReport(endedDay, goldNow);

        // 하루 스냅샷 — N일차 골드·깊이 커브의 원천 데이터(설계 §3.3)
        var settlement = SettlementManager.Instance;

        // 텔레메트리 payload 구성 전체를 try로 감싼다.
        // Telemetry.Log 자신은 내부 예외를 삼키지만, 인자(payload)는 호출 전에 평가되므로
        // 매니저 조회·컬렉션 순회(StockGameManager/PortfolioManager, CountUnlockedNodes,
        // CountWarehouseItems) 중 예외가 나면 Log의 catch에 닿기도 전에 이 수면 코루틴으로
        // 새어나가 페이드아웃된 검은 화면에서 게임이 멈출 수 있다.
        // 정산 로직(BuildReport/ResetForNewDay/저장/DaySummaryUI.Play)은 이 try 밖에 있어
        // 텔레메트리 실패와 무관하게 항상 실행된다.
        try
        {
            // 주식 평가액·원가 — 주식이 아직 초기화되지 않았으면 0
            var sgm = StockGameManager.Instance;
            var portfolio = (sgm != null && sgm.IsInitialized) ? sgm.PortfolioManager : null;
            int portfolioValue = portfolio != null ? (int)portfolio.GetTotalValue() : 0;
            int portfolioCost  = portfolio != null ? (int)portfolio.GetTotalCost()  : 0;

            Telemetry.Log(TelemetryEvents.DaySettled, TelemetryPayload.New()
                .Add("ended_day", endedDay)
                .Add("gold_start", goldNow - report.total)
                .Add("gold_end", goldNow)
                .Add("mineral_sale", report.mineralSale)
                .Add("stock", report.stock)
                .Add("coin", report.coin)
                .Add("shop_purchase", report.shopPurchase)
                .Add("upgrade", report.upgrade)
                .Add("other", report.other)
                .Add("max_depth", settlement != null ? settlement.MaxDepth : 0f)
                .Add("mining_level", stat != null ? stat.MiningLevel : 0)
                .Add("unlocked_nodes", CountUnlockedNodes())
                .Add("warehouse_count", CountWarehouseItems())
                .Add("stock_value", portfolioValue)
                .Add("stock_cost", portfolioCost));
        }
        catch (System.Exception e)
        {
            // 텔레메트리 payload 생성 실패가 수면 시퀀스(게임플레이)로 새어나가지 않게 막는다.
            Debug.LogWarning($"[Bed] day_settled 텔레메트리 payload 생성 실패: {e.Message}");
        }

        // 일차 정산은 밸런스판의 기준선이고, 뷰어(Tools/telemetry/viewer.bat)가 이 줄을 보고
        // 그 일차 일지 화면으로 넘어간다. 기본 flush 주기(60초)를 기다리면 그만큼 늦는다.
        Telemetry.Flush();

        if (Telemetry.Context != null) Telemetry.Context.Day = endedDay + 1;

        DayEarningsLedger.ResetForNewDay(goldNow);

        // 침대 사용 = 자동 저장 트리거 (날짜 변경 확정)
        if (GameManager.Instance?.saveManager != null)
        {
            GameManager.Instance.saveManager.Save();
            Debug.Log("[Bed] 날짜 변경 후 자동 저장 완료.");
        }

        // 3) 검은 화면 위 정산 연출 (클릭 = 한 블럭 스킵, 꾹 누르면 빨리감기)
        yield return DaySummaryUI.Instance.Play(report);

        // 4) 페이드인 — 아침
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn(FadeDuration);

        if (NotificationUI.Instance != null)
            NotificationUI.Instance.ShowNotification("다음 날 아침이 되었습니다.");

        _isSleeping = false;
    }

    /// <summary>해금된 업그레이드 노드 수. 세이브 데이터가 없으면 0.</summary>
    private static int CountUnlockedNodes()
    {
        var sm = GameManager.Instance?.saveManager;
        var state = sm?.playerData?.upgradeTreeState;
        return state?.unlockedNodeIds?.Count ?? 0;
    }

    /// <summary>창고 보관 아이템 총 개수(종류 수가 아니라 수량 합).</summary>
    private static int CountWarehouseItems()
    {
        var wd = GameManager.Instance?.saveManager?.playerData?.warehouseData;
        if (wd == null) return 0;

        int total = 0;
        if (wd.mineralInventory?.slots != null)
            foreach (var s in wd.mineralInventory.slots) total += s.quantity;
        if (wd.itemInventory?.slots != null)
            foreach (var s in wd.itemInventory.slots) total += s.quantity;
        return total;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        Debug.Log($"[Bed] 무언가 닿았습니다: {collision.gameObject.name}");
        if (collision.CompareTag("Player"))
        {
            isPlayerInRange = true;
            Debug.Log("[Bed] 플레이어가 범위에 들어왔습니다.");
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            isPlayerInRange = false;
            Debug.Log("[Bed] 플레이어가 범위를 벗어났습니다.");
        }
    }
}