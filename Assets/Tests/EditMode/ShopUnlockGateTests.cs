using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// ShopUnlockGate 순수 판정부 — 빈 nodeId·해금·미해금·조회자 부재 4갈래.
/// 런타임 오버로드(UpgradeManager 의존)는 씬이 필요해서 여기서 다루지 않는다.
/// </summary>
public class ShopUnlockGateTests
{
    private static System.Func<string, bool> Unlocked(params string[] ids)
    {
        var set = new HashSet<string>(ids);
        return set.Contains;
    }

    [Test]
    public void EmptyNodeId_IsAlwaysUnlocked()
    {
        // 게이트를 안 건 항목(기존 58개 엔트리)은 종래대로 즉시 판매돼야 한다
        Assert.IsTrue(ShopUnlockGate.IsUnlocked("", Unlocked()));
        Assert.IsTrue(ShopUnlockGate.IsUnlocked(null, Unlocked()));
    }

    [Test]
    public void UnlockedNode_Opens()
    {
        Assert.IsTrue(ShopUnlockGate.IsUnlocked("PickaxeUnlock_T0_01", Unlocked("PickaxeUnlock_T0_01")));
    }

    [Test]
    public void LockedNode_StaysClosed()
    {
        Assert.IsFalse(ShopUnlockGate.IsUnlocked("DrillCapacity_T1_01", Unlocked("PickaxeUnlock_T0_01")));
    }

    [Test]
    public void NoQuery_FallsBackToOpen()
    {
        // 업그레이드 매니저가 없는 문맥(씬 단독 실행·초기화 전)에서 상점이 통째로 잠겨 보이면 안 된다.
        // 실제 구매는 ShopManager.BuyItem이 매니저가 붙은 뒤 한 번 더 거른다.
        Assert.IsTrue(ShopUnlockGate.IsUnlocked("DrillCapacity_T1_01", null));
    }

    [Test]
    public void NullData_IsClosed()
    {
        Assert.IsFalse(ShopUnlockGate.IsUnlocked((ShopItemData)null));
    }

    [Test]
    public void RequiredNodeName_EmptyForUngatedItem()
    {
        Assert.AreEqual(string.Empty, ShopUnlockGate.RequiredNodeName(""));
    }
}
