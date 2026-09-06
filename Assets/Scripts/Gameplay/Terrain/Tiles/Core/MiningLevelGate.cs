// @tags: digging, mining-level, gate, tile-data, upgrade, license, notification

using System;
using UnityEngine;

/// <summary>
/// 채광 면허(채광 레벨) 게이트의 단일 원천.
///
/// 층 데이터의 <c>tier</c>가 그 땅을 파는 데 필요한 채광 레벨이고,
/// 플레이어의 <c>PlayerStat.MiningLevel</c>이 그에 못 미치면 파기 반경이
/// <see cref="BlockedRadiusMultiplier"/>로 깎인다(사실상 못 판다).
///
/// ⚠ 판정과 알림을 나눠 둔 이유 — <c>GetDigParameters</c>는 한 번의 스윙에도 여러 번 불린다
/// (비용 계산·유물 후처리·연출). 거기서 이벤트를 쏘면 UI가 초당 수십 번 얻어맞는다.
/// 그래서 <see cref="IsBlocked"/>는 순수 판정만 하고 <see cref="DigParameters"/>에 실려 나가며,
/// 실제로 한 번 판 지점(각 전략의 Perform*DigInternal, 드릴은 Digger.DigAt)에서만
/// <see cref="Notify"/>를 부른다. 그마저도 아래 스로틀을 한 번 더 탄다.
/// </summary>
public static class MiningLevelGate
{
    /// <summary>
    /// 채광 레벨이 모자랄 때 파기 반경에 곱하는 계수.
    ///
    /// 하드 블록(CanDig=false)이 아닌 이유는 "곡괭이가 튕겨나가는" 연출 없이도
    /// 삽질이 먹히지 않는다는 걸 손맛으로 알려주기 위해서다. 완전히 막으려면
    /// 이 값을 0으로 내리지 말고 소비처에서 CanDig을 끄는 쪽으로 바꿔야 한다 —
    /// 0을 곱하면 반경 0으로 지형 파기 잡이 돌아 헛일을 한다.
    /// </summary>
    public const float BlockedRadiusMultiplier = 0.05f;

    /// <summary>UI가 쓸 로컬라이제이션 키. 인자는 (요구 레벨, 현재 레벨) 순서.</summary>
    public const string LocKey = "ui_dig_mining_level_required";

    /// <summary>로컬라이제이션이 없을 때 UI가 쓸 폴백 포맷. 인자 순서는 <see cref="LocKey"/>와 같다.</summary>
    public const string LocFallback = "채광 레벨 {0} 필요 (현재 {1})";

    /// <summary>같은 상황을 다시 알리기까지의 최소 간격(초). 스윙 연타 스팸을 막는다.</summary>
    public const float NotifyInterval = 1.0f;

    /// <summary>
    /// 채광 레벨이 모자라 파기가 막혔을 때 발화. UI가 구독해 안내를 띄운다.
    ///
    /// static 이벤트라 씬을 넘어 살아남는다 — 구독하는 쪽(UI)은 OnDisable/OnDestroy에서
    /// 반드시 해제할 것. 안 하면 파괴된 오브젝트가 계속 불린다.
    /// </summary>
    public static event Action<MiningLevelBlockInfo> OnBlocked;

    // 스로틀 상태. 상황(층·요구 레벨)이 바뀌면 간격을 무시하고 즉시 알린다 —
    // 새 층에 내려온 순간이야말로 알려줘야 할 때다.
    private static float _lastNotifyTime = -999f;
    private static TileType _lastTile;
    private static int _lastRequired = -1;

    /// <summary>
    /// 이 층을 파기에 채광 레벨이 모자란가. 판정만 하고 아무것도 알리지 않는다.
    /// </summary>
    /// <param name="required">그 층이 요구하는 채광 레벨(층 데이터의 tier). 없으면 0.</param>
    /// <param name="current">플레이어의 현재 채광 레벨. 스탯이 없으면 0.</param>
    public static bool IsBlocked(TileType tileType, PlayerStat stats, out int required, out int current)
    {
        var tileData = (TileDataManager.Instance != null)
            ? TileDataManager.Instance.GetData(tileType)
            : null;

        required = (tileData != null) ? tileData.tier : 0;
        current = (stats != null) ? stats.MiningLevel : 0;

        return current < required;
    }

    /// <summary>
    /// 막혔다는 걸 UI에 알린다. 스윙마다 불러도 되도록 스로틀이 들어 있다.
    /// 실제로 파기를 시도한 지점에서만 부를 것 — 파라미터를 조회만 하는 곳에서 부르면
    /// 파지도 않았는데 경고가 뜬다.
    /// </summary>
    public static void Notify(TileType tileType, int required, int current)
    {
        if (OnBlocked == null) return;

        bool situationChanged = (tileType != _lastTile) || (required != _lastRequired);
        if (!situationChanged && Time.unscaledTime - _lastNotifyTime < NotifyInterval) return;

        _lastNotifyTime = Time.unscaledTime;
        _lastTile = tileType;
        _lastRequired = required;

        OnBlocked.Invoke(new MiningLevelBlockInfo(tileType, required, current));
    }

    /// <summary><see cref="DigParameters"/>에 실려 온 판정을 그대로 알린다.</summary>
    public static void Notify(in DigParameters p)
    {
        if (!p.MiningLevelBlocked) return;
        Notify(p.BlockedTileType, p.RequiredMiningLevel, p.CurrentMiningLevel);
    }

    /// <summary>
    /// 스로틀 상태 초기화. 새 게임·씬 재진입처럼 "직전에 알렸다"는 기억이
    /// 의미를 잃는 시점에서 부른다.
    /// </summary>
    public static void ResetThrottle()
    {
        _lastNotifyTime = -999f;
        _lastRequired = -1;
    }
}

/// <summary>채광 레벨 부족으로 파기가 막혔을 때 UI에 넘어가는 정보.</summary>
public readonly struct MiningLevelBlockInfo
{
    /// <summary>파려던 지점의 층.</summary>
    public readonly TileType Tile;

    /// <summary>그 층이 요구하는 채광 레벨.</summary>
    public readonly int Required;

    /// <summary>플레이어의 현재 채광 레벨.</summary>
    public readonly int Current;

    public MiningLevelBlockInfo(TileType tile, int required, int current)
    {
        Tile = tile;
        Required = required;
        Current = current;
    }
}
