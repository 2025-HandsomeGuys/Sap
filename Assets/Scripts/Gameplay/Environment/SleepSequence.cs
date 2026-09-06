// @tags: environment, sleep, daysummary, fade, save, ledger, stock
using System.Collections;
using UnityEngine;
using Sap.UI.Notification;
using Stock.Core;

/// <summary>
/// 수면(하루 마무리) 시퀀스 한 벌.
/// 페이드아웃 → 날짜 전환·주식 틱 → 장부 스냅샷 → 저장 → 정산 연출 → 페이드인.
///
/// <b>순서가 곧 명세다</b> — 아무 데서나 바꾸면 정산이 어긋난다.
///  · 주식 틱을 저장보다 먼저: 변동된 시세·강제매각 수익이 세이브와 오늘 장부에 들어가야 한다.
///  · 장부 리셋을 저장보다 먼저: 리셋된 장부가 세이브에 반영돼야 다음 날이 0에서 시작한다.
///  · 정산 연출은 검은 화면 위에서: 밤 → 아침이 끊기지 않고 이어진다.
///
/// 침대 오브젝트가 하나가 아닐 수 있어(그리고 기존 <c>BedInteractable</c>과 새 통합 컴포넌트가
/// 같은 동작을 써야 해서) 코루틴만 static으로 떼어 두었다.
/// </summary>
public static class SleepSequence
{
    private const float FadeDuration = 0.6f;

    /// <summary>코고는 소리 음량(0~1). 원본이 커서 낮춰 둔다. 조절은 이 값만 바꾸면 된다.</summary>
    private const float SnoreVolume = 0.5f;

    /// <summary>진행 중인지. 두 침대가 동시에 재생되는 것을 막는다.</summary>
    public static bool IsPlaying { get; private set; }

    /// <summary>
    /// 수면 시퀀스를 재생한다. 호출부에서 <c>yield return SleepSequence.Run()</c> 로 기다린다.
    /// </summary>
    public static IEnumerator Run()
    {
        if (IsPlaying) yield break;
        if (DayCycleManager.Instance == null)
        {
            Debug.LogError("[SleepSequence] DayCycleManager가 없습니다.");
            yield break;
        }

        IsPlaying = true;

        // 자는 동안 플레이어가 움직이지 못하게 UIState를 잠근다(플레이어 코드는 IsInputBlocked을 이미 본다).
        // 지금 None일 때만 잡고, 끝나면 되돌린다 — 다른 상태를 덮어쓰지 않게. (낮잠 NapSequence와 동일)
        var ui = UIStateManager.Instance;
        bool lockedInput = ui != null && ui.CurrentState == UIState.None;
        if (lockedInput) ui.SetState(UIState.BedRest);

        int endedDay = DayCycleManager.Instance.CurrentDay; // 방금 끝나는 날 (정산 제목용)

        // 1) 페이드아웃 — 화면을 검게 덮고 시작
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeOut(FadeDuration);

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SfxKeys.SleepSnore, 1f, SnoreVolume);

        DayCycleManager.Instance.AdvanceToNextDay();

        if (!TutorialProgress.IsCompleted)
        {
            TutorialProgress.MarkCompleted();
        }

        // 하루 경과 = 주식 시장 SkipTicks(4)틱. 앞 틱은 뉴스 없이 조용히 지나가고 '마지막 1틱만' 뉴스를
        // 태운다 → 아침에 일어나는 순간 갓 나온 뉴스 1건과 그 여파가 시세에 반영돼 있다.
        if (StockGameManager.Instance != null && StockGameManager.Instance.IsInitialized)
            StockGameManager.Instance.ProcessSleepTicks(StockGameManager.SkipTicks);

        // 2) 하루 장부 스냅샷 → 새 하루로 리셋
        var stat = Object.FindFirstObjectByType<PlayerStat>(FindObjectsInactive.Include);
        int goldNow = stat != null ? stat.Gold : DayEarningsLedger.EstimateCurrentGold();
        DayEarningsReport report = DayEarningsLedger.BuildReport(endedDay, goldNow);

        LogDaySettled(endedDay, goldNow, report, stat);

        DayEarningsLedger.ResetForNewDay(goldNow);

        // 수면 = 자동 저장 트리거 (날짜 변경 확정)
        if (GameManager.Instance != null && GameManager.Instance.saveManager != null)
            GameManager.Instance.saveManager.Save();

        // 3) 검은 화면 위 정산 연출 (클릭 = 한 블럭 스킵, 꾹 누르면 빨리감기)
        yield return DaySummaryUI.Instance.Play(report);

        // 4) 페이드인 — 아침
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn(FadeDuration);

        // 입력 잠금 해제 (내가 잡았고 아직 BedRest면 되돌린다)
        if (lockedInput && ui != null && ui.CurrentState == UIState.BedRest)
            ui.SetState(UIState.None);

        if (NotificationUI.Instance != null)
            NotificationUI.Instance.ShowNotification("다음 날 아침이 되었습니다.");

        IsPlaying = false;
    }

    /// <summary>
    /// 하루 스냅샷 — N일차 골드·깊이 커브의 원천 데이터(텔레메트리 설계 §3.3).
    /// 밸런스 CSV(days.csv)가 전적으로 이 이벤트에서 나오므로, 수면 경로가 늘어나면
    /// 반드시 여기를 거치게 한다. <b>이 호출이 빠지면 가격 사다리 재적합이 불가능해진다</b> —
    /// 실제로 <c>BedInteractable</c>(씬에 배치되지 않은 구 컴포넌트)에만 붙어 있어서
    /// 109개 세션 내내 day_settled가 한 건도 남지 않았다.
    ///
    /// payload 구성 전체를 try로 감싼다. Telemetry.Log 자신은 내부 예외를 삼키지만,
    /// 인자(payload)는 호출 전에 평가되므로 매니저 조회·컬렉션 순회 중 예외가 나면
    /// Log의 catch에 닿기도 전에 수면 코루틴으로 새어나가 페이드아웃된 검은 화면에서
    /// 게임이 멈춘다. 정산 로직(BuildReport/ResetForNewDay/저장/DaySummaryUI.Play)은
    /// 이 메서드 밖에 있어 텔레메트리 실패와 무관하게 항상 실행된다.
    /// </summary>
    private static void LogDaySettled(int endedDay, int goldNow, DayEarningsReport report, PlayerStat stat)
    {
        try
        {
            var settlement = SettlementManager.Instance;

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
            Debug.LogWarning($"[SleepSequence] day_settled 텔레메트리 payload 생성 실패: {e.Message}");
        }

        // 일차 정산은 밸런스판의 기준선이고, 뷰어(Tools/telemetry/viewer.bat)가 이 줄을 보고
        // 그 일차 일지 화면으로 넘어간다. 기본 flush 주기(60초)를 기다리면 그만큼 늦는다.
        Telemetry.Flush();

        // 이후 이벤트가 새 날짜로 찍히도록 컨텍스트를 즉시 넘긴다.
        // 이 줄이 없으면 다음 씬 로드(SaveManager가 Context.Day를 다시 세팅)까지
        // 모든 이벤트의 day 필드가 어제 날짜로 남는다.
        if (Telemetry.Context != null) Telemetry.Context.Day = endedDay + 1;
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
}
