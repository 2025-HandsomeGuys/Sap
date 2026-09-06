using NUnit.Framework;

public class SfxThrottleTests
{
    [Test]
    public void FirstCall_AlwaysPlays()
    {
        var t = new SfxThrottle(0.04f);
        Assert.IsTrue(t.ShouldPlay("a", 0f));
    }

    [Test]
    public void SameKeyWithinInterval_IsSuppressed()
    {
        var t = new SfxThrottle(0.04f);
        t.ShouldPlay("a", 0f);
        Assert.IsFalse(t.ShouldPlay("a", 0.03f));
    }

    [Test]
    public void SameKeyAfterInterval_PlaysAgain()
    {
        var t = new SfxThrottle(0.04f);
        t.ShouldPlay("a", 0f);
        Assert.IsTrue(t.ShouldPlay("a", 0.05f));
    }

    [Test]
    public void DifferentKeys_DoNotInterfere()
    {
        var t = new SfxThrottle(0.04f);
        t.ShouldPlay("a", 0f);
        Assert.IsTrue(t.ShouldPlay("b", 0.01f));
    }

    [Test]
    public void SuppressedCall_DoesNotExtendWindow()
    {
        // 억제된 호출이 타임스탬프를 갱신하면 연타 중 영원히 막힌다.
        var t = new SfxThrottle(0.04f);
        t.ShouldPlay("a", 0f);
        t.ShouldPlay("a", 0.03f);   // 억제됨
        Assert.IsTrue(t.ShouldPlay("a", 0.045f)); // 최초 재생 기준 0.045초 → 통과해야 한다
    }

    [Test]
    public void Clear_ResetsAllKeys()
    {
        var t = new SfxThrottle(0.04f);
        t.ShouldPlay("a", 0f);
        t.Clear();
        Assert.IsTrue(t.ShouldPlay("a", 0.01f));
    }

    [Test]
    public void NullOrEmptyKey_IsSuppressed()
    {
        var t = new SfxThrottle(0.04f);
        Assert.IsFalse(t.ShouldPlay(null, 0f));
        Assert.IsFalse(t.ShouldPlay("", 0f));
    }
}
