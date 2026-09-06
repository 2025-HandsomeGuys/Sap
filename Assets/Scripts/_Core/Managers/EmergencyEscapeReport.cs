// @tags: emergency, escape, penalty, mineral, report, settlement, static
using System.Collections.Generic;

/// <summary>정산창을 띄운 사유 — 문구가 갈린다(짐을 떨어뜨렸다 vs 쓰러졌다).</summary>
public enum CarryLossReason
{
    /// <summary>일시정지 메뉴의 긴급 탈출.</summary>
    EmergencyEscape,
    /// <summary>스태미나 고갈 등 지하 사망.</summary>
    Death,
}

/// <summary>
/// 긴급 탈출 페널티로 '잃은 광물 / 챙겨온 광물'을 잠깐 담아 두는 정적 장부.
/// (DayEarningsLedger 처럼 씬 전환을 넘어 살아남는 static — 페널티가 적용되는 지하에서 기록하고,
///  지상에 올라온 뒤 <c>EmergencyEscapeOverlayUI</c>가 읽어 보여준 다음 <see cref="Clear"/>한다.)
///
/// 매 판 새로 쓰므로 세이브에 넣지 않는다(강제종료 시 사라져도 무방 — 연출용 데이터).
/// </summary>
public static class EmergencyEscapeReport
{
    /// <summary>보여줄 데이터가 남아 있는지. Open 시 소비하고 Clear로 지운다.</summary>
    public static bool HasPending { get; private set; }

    private static readonly Dictionary<MineralID, int> _lost = new Dictionary<MineralID, int>();
    private static readonly Dictionary<MineralID, int> _kept = new Dictionary<MineralID, int>();

    public static IReadOnlyDictionary<MineralID, int> Lost => _lost;
    public static IReadOnlyDictionary<MineralID, int> Kept => _kept;

    /// <summary>잃은 광물 총 개수.</summary>
    public static int LostTotal { get; private set; }
    /// <summary>챙겨온 광물 총 개수.</summary>
    public static int KeptTotal { get; private set; }

    /// <summary>이번 기록의 사유(탈출 / 사망). 정산창 제목·부제가 이걸로 갈린다.</summary>
    public static CarryLossReason Reason { get; private set; } = CarryLossReason.EmergencyEscape;

    /// <summary>페널티 적용 시 잃은/챙긴 광물을 기록한다(<see cref="CarryLossPenalty.Apply"/>에서 호출).</summary>
    public static void Record(Dictionary<MineralID, int> lost, Dictionary<MineralID, int> kept,
        CarryLossReason reason = CarryLossReason.EmergencyEscape)
    {
        Reason = reason;
        _lost.Clear();
        _kept.Clear();
        LostTotal = 0;
        KeptTotal = 0;

        if (lost != null)
            foreach (var kv in lost)
            {
                if (kv.Value <= 0) continue;
                _lost[kv.Key] = kv.Value;
                LostTotal += kv.Value;
            }

        if (kept != null)
            foreach (var kv in kept)
            {
                if (kv.Value <= 0) continue;
                _kept[kv.Key] = kv.Value;
                KeptTotal += kv.Value;
            }

        // 뭔가 하나라도 잃었을 때 연출을 띄운다(전부 챙겨왔으면 알릴 손실이 없다).
        // 사망은 예외 — 빈손으로 죽어도 "무슨 일이 있었는지"는 알려줘야 하므로 항상 띄운다.
        HasPending = LostTotal > 0 || reason == CarryLossReason.Death;

        Telemetry.Log(TelemetryEvents.EmergencyEscape, TelemetryPayload.New()
            .Add("lost_count", LostTotal)
            .Add("kept_count", KeptTotal)
            .Add("lost_kinds", _lost.Count)
            .Add("kept_kinds", _kept.Count)
            .Add("reason", reason.ToString()));
    }

    /// <summary>연출을 띄웠음을 표시(중복 트리거 방지). 데이터는 Clear 전까지 유지된다.</summary>
    public static void MarkShown() => HasPending = false;

    public static void Clear()
    {
        _lost.Clear();
        _kept.Clear();
        LostTotal = 0;
        KeptTotal = 0;
        HasPending = false;
        Reason = CarryLossReason.EmergencyEscape;
    }
}
