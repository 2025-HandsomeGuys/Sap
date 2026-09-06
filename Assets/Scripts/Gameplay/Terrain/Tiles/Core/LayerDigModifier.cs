// @tags: digging, radius, tile-data, layer, environment-resistance, level-design, stat

using UnityEngine;

/// <summary>
/// 층(TileType)이 삽 파기 반경에 거는 감쇠와, 그걸 상쇄하는 환경 저항을 한곳에서 푼다.
///
/// 레벨디자인 의도 — 층을 내려갈수록 <c>digRadiusModifier</c>가 반경을 깎고,
/// 플레이어는 **범위 업그레이드(넓은 삽날)를 더 사서** 다시 밀어 올린다(톱니 곡선).
/// 층 진입 직후의 실효 반경이 층마다 비슷하게 수렴하므로 "새 층에 오면 삽이 작아진다"는
/// 체감이 일정하게 유지된다.
///
/// ⚠ 2026-08-24: 되돌리는 통로를 **범위 업그레이드 하나로 통일**했다.
///    예전에는 EnvironmentResistance(방한복)가 이 계수를 상쇄했는데,
///    "두꺼운 옷을 입으니 땅이 더 크게 파인다"가 말이 안 되고, 층마다 전용 장비를
///    따로 사야 하는 규칙이 하나 더 생겨 복잡했다(사용자 결정).
///    방한복은 원래 일(환경 피해 저항)만 한다.
///    범위 업그레이드도 같은 이유로 곱연산 -> 합연산(MiningRangeUp)으로 바꿨다.
///
/// ⚠ 이 감쇠는 플레이어 스탯(MiningRange)을 건드리지 않는다. 스탯은 그대로 두고
///    파기 순간에만 곱한다 — 스탯창이 "원래 값 / 감쇠 후 값"을 나란히 보여줄 수 있어야 하고,
///    지상으로 나오면 아무 흔적 없이 원래 반경으로 돌아와야 하기 때문이다.
/// </summary>
public static class LayerDigModifier
{
    /// <summary>
    /// 감쇠를 먹인 뒤에도 보장되는 최소 파기 반경(월드 유닛).
    ///
    /// 플레이어 캡슐은 0.2유닛(20px) 폭이다. 파기 지름이 여기에 근접하면 자기가 판 세로 굴에
    /// 몸이 끼고, TerrainSeamWatchdog가 EMBEDDED로 울리기 시작한다. 0.15 = 지름 0.3유닛으로
    /// 몸통의 1.5배를 남긴다. 층 계수를 더 내리더라도 이 값 아래로는 못 내려간다.
    ///
    /// 최소 차징(ShovelMinChargeRatio)까지 곱해진 뒤의 값이 여기 걸리므로,
    /// 계수를 조정할 때는 풀차징이 아니라 '최소 차징 × 최저 층 계수'를 기준으로 봐야 한다.
    /// </summary>
    public const float MinEffectiveRadius = 0.15f;

    /// <summary>
    /// 그 층에서 파기 반경에 곱할 최종 계수. 1.0이면 감쇠 없음.
    ///
    /// 층 데이터의 <c>digRadiusModifier</c> 그대로다. 스탯으로 상쇄하지 않는다 —
    /// 되돌리는 건 범위 업그레이드(MiningRangeUp)가 스탯을 키우는 쪽이다.
    ///
    /// <paramref name="stats"/>는 호출부 호환을 위해 남겨둔 인자다(지금은 쓰지 않는다).
    /// </summary>
    public static float Resolve(TileType tileType, PlayerStat stats = null)
    {
        return RawModifier(tileType);
    }

    /// <summary>저항을 빼고 층 데이터에 적힌 감쇠 계수만. 스탯창의 "동결 −20%" 표기용.</summary>
    public static float RawModifier(TileType tileType)
    {
        var mgr = TileDataManager.Instance;
        if (mgr == null) return 1f;

        var data = mgr.GetData(tileType);
        if (data == null) return 1f;

        // 0 이하는 데이터 오타로 본다 — 그대로 쓰면 아무것도 못 파는 층이 된다.
        return (data.digRadiusModifier > 0f) ? Mathf.Min(1f, data.digRadiusModifier) : 1f;
    }

    /// <summary>
    /// 월드 Y 좌표가 속한 층의 TileType. 지상 씬처럼 InfinityMapManager가 없으면 Dirt(감쇠 없음).
    /// 파기 경로는 이미 대상 타일을 알고 있으므로 이건 UI 표기용이다.
    /// </summary>
    public static TileType TileTypeAtWorldY(float worldY)
    {
        var map = InfinityMapManager.Instance;
        if (map == null || map.chunkHeightWorld <= 0f) return TileType.Dirt;
        if (TileDataManager.Instance == null) return TileType.Dirt;

        int chunkY = Mathf.FloorToInt(worldY / map.chunkHeightWorld);
        return TileDataManager.Instance.GetTileTypeAtDepth(chunkY);
    }

    /// <summary>플레이어가 지금 서 있는 층의 계수. 스탯창이 매 갱신 호출한다.</summary>
    public static float ResolveAtPlayer(PlayerStat stats)
    {
        if (stats == null) return 1f;
        return Resolve(TileTypeAtWorldY(stats.transform.position.y), stats);
    }

    /// <summary>
    /// 감쇠를 뭐라고 부를지. 층의 zoneStatusType을 그대로 재사용한다 —
    /// 얼음층이면 "동결", 마그마층이면 "열기". 새 층을 넣을 때 라벨을 따로 관리하지 않아도
    /// 상태이상 종류만 맞춰 두면 스탯창 표기가 따라온다.
    /// </summary>
    public static void PenaltyLabel(TileType tileType, out string locKey, out string fallback)
    {
        string zone = null;
        var mgr = TileDataManager.Instance;
        if (mgr != null)
        {
            var data = mgr.GetData(tileType);
            if (data != null) zone = data.zoneStatusType;
        }

        switch (zone)
        {
            case "Frostbite": locKey = "ui_stat_dig_penalty_frost"; fallback = "동결"; return;
            case "Burn":      locKey = "ui_stat_dig_penalty_burn";  fallback = "열기"; return;
            case "Radiation": locKey = "ui_stat_dig_penalty_rad";   fallback = "침식"; return;
            default:          locKey = "ui_stat_dig_penalty";       fallback = "지층"; return;
        }
    }

    /// <summary>플레이어가 지금 서 있는 층의 감쇠 계수(저항 미적용).</summary>
    public static float RawModifierAtPlayer(PlayerStat stats)
    {
        if (stats == null) return 1f;
        return RawModifier(TileTypeAtWorldY(stats.transform.position.y));
    }
}
