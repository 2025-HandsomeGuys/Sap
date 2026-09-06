using NUnit.Framework;
using Gameplay.Dungeon.Authoring.Generation;

public class DungeonRoomTypesTests
{
    [Test]
    public void TryParseOpens_Combination_ReturnsFlags()
    {
        Assert.IsTrue(RoomTypeUtil.TryParseOpens("LR", out var o));
        Assert.AreEqual(RoomOpen.L | RoomOpen.R, o);
    }

    [Test]
    public void TryParseOpens_IsCaseInsensitiveAndIgnoresSpaces()
    {
        Assert.IsTrue(RoomTypeUtil.TryParseOpens(" l d ", out var o));
        Assert.AreEqual(RoomOpen.L | RoomOpen.D, o);
    }

    [Test]
    public void TryParseOpens_Empty_ReturnsNone()
    {
        Assert.IsTrue(RoomTypeUtil.TryParseOpens("", out var o));
        Assert.AreEqual(RoomOpen.None, o);
    }

    [Test]
    public void TryParseOpens_UnknownChar_Fails()
    {
        Assert.IsFalse(RoomTypeUtil.TryParseOpens("LX", out _));
    }

    [Test]
    public void FormatOpens_AlwaysLRUDOrder()
    {
        Assert.AreEqual("LRUD", RoomTypeUtil.FormatOpens(RoomOpen.D | RoomOpen.U | RoomOpen.R | RoomOpen.L));
        Assert.AreEqual("", RoomTypeUtil.FormatOpens(RoomOpen.None));
    }

    [Test]
    public void Opposite_SwapsDirections()
    {
        Assert.AreEqual(RoomOpen.R, RoomTypeUtil.Opposite(RoomOpen.L));
        Assert.AreEqual(RoomOpen.L, RoomTypeUtil.Opposite(RoomOpen.R));
        Assert.AreEqual(RoomOpen.D, RoomTypeUtil.Opposite(RoomOpen.U));
        Assert.AreEqual(RoomOpen.U, RoomTypeUtil.Opposite(RoomOpen.D));
        Assert.AreEqual(RoomOpen.None, RoomTypeUtil.Opposite(RoomOpen.None));
    }
}
