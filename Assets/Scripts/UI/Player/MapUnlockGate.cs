// @tags: map, minimap, worldmap, upgrade, unlock, gate, explore

using UnityEngine;

/// <summary>
/// 지도(미니맵 + M 전체지도)의 업그레이드 배선 한 곳.
/// <list type="bullet">
///   <item><b>해금</b>: <see cref="UnlockNodeId"/> 노드를 사기 전에는 지도가 아예 존재하지 않는다
///         (미니맵은 그려지지 않고 M 키도 안 먹는다). 시설 노드와 같은 방식 —
///         효과(<see cref="UpgradeEffectType"/>)가 아니라 <b>nodeId 문자열</b>로 걸린다.</item>
///   <item><b>강화</b>: <see cref="UpgradeEffectType.MapExploreRadiusUp"/>(합연산)이
///         <c>DigPathTracker.ExploreArea</c>에 들어가는 탐사 반경을 셀 단위로 올린다.
///         이건 미니맵 표시가 아니라 <b>기록</b>이라 M 전체지도에도 함께 반영된다.</item>
/// </list>
///
/// ⚠ <c>UpgradeManager.Instance</c>를 쓰지 않는다. 그 프로퍼티는 매니저가 없으면
/// 빈 오브젝트를 새로 만들어 <c>DontDestroyOnLoad</c>로 박아버리는데, 그게 씬의 진짜
/// 매니저보다 먼저 생기면 진짜 쪽이 중복으로 자폭해 <c>upgradeTree</c>가 통째로 날아간다.
/// 지도 게이트는 씬 로드 직후·키 입력 등 이른 시점에 불리므로 <b>탐색만</b> 한다.
///
/// ⚠ 호출은 싸지 않다 — <c>UpgradeManager.GetState()</c>가 매번
/// <c>FindFirstObjectByType&lt;SaveManager&gt;()</c>를 돈다. 매 프레임 부르지 말고
/// 결과를 캐시할 것(미니맵은 1초 주기 폴링).
/// </summary>
public static class MapUnlockGate
{
    /// <summary>지도 장비 해금 노드. 트리(UpgradeTreeGenerator)의 id와 문자열로 맞춰져 있다.</summary>
    public const string UnlockNodeId = "Facility_Map_T0";

    private static UpgradeManager _cached;

    private static UpgradeManager Manager
    {
        get
        {
            if (_cached == null) _cached = UnityEngine.Object.FindFirstObjectByType<UpgradeManager>();
            return _cached;
        }
    }

    /// <summary>지도를 손에 넣었는가. 매니저가 없으면 잠긴 것으로 본다.</summary>
    public static bool IsUnlocked
    {
        get
        {
            UpgradeManager mgr = Manager;
            return mgr != null && mgr.IsNodeUnlocked(UnlockNodeId);
        }
    }

    /// <summary>
    /// 업그레이드가 반영된 탐사 반경(셀). 매니저가 없으면 기본값 그대로 돌려준다.
    /// </summary>
    public static int GetExploreRadius(int baseCells)
    {
        UpgradeManager mgr = Manager;
        if (mgr == null) return baseCells;

        float v = mgr.GetStatValue(UpgradeEffectType.MapExploreRadiusUp, baseCells);
        return Mathf.Max(1, Mathf.RoundToInt(v));
    }
}
