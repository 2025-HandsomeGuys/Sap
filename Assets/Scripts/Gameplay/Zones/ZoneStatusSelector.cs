// @tags: zone, status, layer, selector, pure, tile-data, frostbite, burn, radiation
using System;

/// <summary>
/// tileData.json의 zoneStatusType 문자열 → ZoneStatusType 변환.
///
/// MonoBehaviour와 분리한 순수 로직이라 EditMode에서 테스트한다.
/// 실제 상태이상 누적은 PlayerZoneChecker가 한다.
/// </summary>
public static class ZoneStatusSelector
{
    /// <summary>
    /// 빈 값은 None으로 조용히 통과시키고(필드 미기입 층 = 효과 없음),
    /// 오타처럼 값은 있는데 파싱이 안 되는 경우만 false를 돌려준다.
    /// 오타가 조용히 무효과로 넘어가면 층 밸런스가 통째로 사라지므로 호출측이 경고를 띄운다.
    /// </summary>
    public static bool TryParse(string raw, out ZoneStatusType type)
    {
        type = ZoneStatusType.None;

        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (Enum.TryParse(raw.Trim(), true, out ZoneStatusType parsed) &&
            Enum.IsDefined(typeof(ZoneStatusType), parsed))
        {
            type = parsed;
            return true;
        }

        return false;
    }

    /// <summary>파싱 실패를 None으로 흡수하는 간편 버전.</summary>
    public static ZoneStatusType Parse(string raw)
    {
        TryParse(raw, out ZoneStatusType type);
        return type;
    }
}
