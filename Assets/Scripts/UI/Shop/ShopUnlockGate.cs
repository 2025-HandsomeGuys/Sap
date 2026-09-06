using UnityEngine;

/// <summary>
/// 상점 항목 해금 판정. <see cref="ShopItemData.unlockNodeId"/>에 적힌 업그레이드 노드를
/// 이미 샀는지로 "지금 살 수 있는가"를 가른다.
///
/// 업그레이드 쪽에 상점 지식을 심지 않으려고 <b>소비자(상점)에 nodeId 문자열을 두고 대조</b>한다 —
/// 유물(<c>RelicSO.unlockNodeId</c>)·도구(toolConfig.json)·시설(<c>WorldInteractable</c>)·
/// 지도(<c>MapUnlockGate</c>)가 전부 같은 방식이다.
///
/// 게이트로 쓰는 노드는 트리에서 <b>부모가 여럿인 '모이는 노드'</b>(곡괭이 입수·단말기 개통·
/// 채광 면허·드릴 입수·코인 거래 개통)다. 장비 해금 전용 노드를 따로 만들지 않으므로
/// <c>UpgradeTree.csv</c>는 건드리지 않는다.
/// </summary>
public static class ShopUnlockGate
{
    /// <summary>
    /// 순수 판정부. nodeId가 비면 항상 열림, 아니면 <paramref name="isNodeUnlocked"/>에 위임한다.
    ///
    /// 조회자가 없으면(매니저가 아직/이미 없는 순간 — 종료 중 등) <b>열린 것으로 본다.</b>
    /// 못 사게 막는 쪽으로 폴백하면 매니저를 못 얻는 순간에 상점이 통째로 잠겨 보인다.
    /// 실제 구매는 <see cref="ShopManager.BuyItem"/>에서 한 번 더 걸러진다.
    /// </summary>
    public static bool IsUnlocked(string unlockNodeId, System.Func<string, bool> isNodeUnlocked)
    {
        if (string.IsNullOrEmpty(unlockNodeId)) return true;
        if (isNodeUnlocked == null) return true;
        return isNodeUnlocked(unlockNodeId);
    }

    /// <summary>런타임 판정. 업그레이드 매니저에 물어본다.</summary>
    public static bool IsUnlocked(ShopItemData data)
    {
        if (data == null) return false;
        return IsUnlocked(data.unlockNodeId, ResolveQuery());
    }

    /// <summary>
    /// 잠금 사유로 보여줄 노드 이름. 노드를 못 찾으면 ID를 그대로 준다 —
    /// 데이터 오타를 조용히 삼키는 대신 화면에 드러나게 한다.
    /// </summary>
    public static string RequiredNodeName(string unlockNodeId)
    {
        if (string.IsNullOrEmpty(unlockNodeId)) return string.Empty;

        var mgr = UpgradeManager.Instance;
        var node = mgr != null ? mgr.GetNodeFromCache(unlockNodeId) : null;
        if (node == null) return unlockNodeId;

        string name = node.DisplayName;
        return string.IsNullOrEmpty(name) ? unlockNodeId : name;
    }

    private static System.Func<string, bool> ResolveQuery()
    {
        var mgr = UpgradeManager.Instance;
        if (mgr == null) return null;
        return mgr.IsNodeUnlocked;
    }
}
