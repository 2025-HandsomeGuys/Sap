using NUnit.Framework;
using Relic;
using Relic.Data;

public class RelicInventorySaveTests
{
    [Test]
    public void Grant_SetsLevel1_AndIsOwned()
    {
        var inv = new RelicInventory();
        inv.Grant(RelicID.Magnet);
        Assert.IsTrue(inv.IsOwned(RelicID.Magnet));
        Assert.AreEqual(1, inv.GetLevel(RelicID.Magnet));
    }

    [Test]
    public void TryUpgrade_RespectsMaxLevel()
    {
        var inv = new RelicInventory();
        inv.Grant(RelicID.Magnet);                         // Lv1
        Assert.IsTrue(inv.TryUpgrade(RelicID.Magnet, 3));  // ->2
        Assert.IsTrue(inv.TryUpgrade(RelicID.Magnet, 3));  // ->3
        Assert.IsFalse(inv.TryUpgrade(RelicID.Magnet, 3)); // max, 실패
        Assert.AreEqual(3, inv.GetLevel(RelicID.Magnet));
    }

    [Test]
    public void NewInventory_HasNoSlots_UntilUpgrade()
    {
        // 유물 칸은 전부 업그레이드(RelicSlotUp)로 열린다 — RelicManager.BaseSlotCount = 0.
        var inv = new RelicInventory();
        Assert.AreEqual(0, inv.SlotCount, "업그레이드 전에는 유물 칸이 없다");

        inv.Grant(RelicID.Magnet);
        Assert.IsFalse(inv.Equip(0, RelicID.Magnet), "칸이 없으면 장착 불가");

        inv.SetSlotCount(1);
        Assert.IsTrue(inv.Equip(0, RelicID.Magnet), "칸이 열리면 장착 가능");
    }

    [Test]
    public void Equip_RejectsUnowned_AndDuplicates()
    {
        var inv = new RelicInventory();
        inv.SetSlotCount(2);   // 업그레이드로 2칸 열린 상태를 가정
        Assert.IsFalse(inv.Equip(0, RelicID.Magnet), "미보유 장착 거부");
        inv.Grant(RelicID.Magnet);
        Assert.IsTrue(inv.Equip(0, RelicID.Magnet));
        Assert.IsFalse(inv.Equip(1, RelicID.Magnet), "같은 유물 중복 장착 거부");
    }

    [Test]
    public void SaveLoad_Roundtrips()
    {
        var inv = new RelicInventory();
        inv.SetSlotCount(2);   // 업그레이드로 2칸 열린 상태를 가정
        inv.Grant(RelicID.Magnet);
        inv.Grant(RelicID.PigeonFeather);
        inv.TryUpgrade(RelicID.Magnet, 3);   // Lv2
        inv.Equip(0, RelicID.Magnet);
        inv.Equip(1, RelicID.PigeonFeather);

        var data = inv.ToSaveData();

        var restored = new RelicInventory();
        restored.LoadFrom(data);

        Assert.AreEqual(2, restored.GetLevel(RelicID.Magnet));
        Assert.AreEqual(1, restored.GetLevel(RelicID.PigeonFeather));
        Assert.AreEqual(RelicID.Magnet, restored.GetEquipped(0));
        Assert.AreEqual(RelicID.PigeonFeather, restored.GetEquipped(1));
        Assert.AreEqual(2, restored.SlotCount, "칸 수도 함께 왕복한다");
    }
}
