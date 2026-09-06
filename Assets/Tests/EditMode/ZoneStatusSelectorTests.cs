using NUnit.Framework;

public class ZoneStatusSelectorTests
{
    [Test]
    public void KnownNames_ParseCaseInsensitively()
    {
        Assert.IsTrue(ZoneStatusSelector.TryParse("Frostbite", out var frost));
        Assert.AreEqual(ZoneStatusType.Frostbite, frost);

        Assert.IsTrue(ZoneStatusSelector.TryParse("burn", out var burn));
        Assert.AreEqual(ZoneStatusType.Burn, burn);

        Assert.IsTrue(ZoneStatusSelector.TryParse("RADIATION", out var rad));
        Assert.AreEqual(ZoneStatusType.Radiation, rad);
    }

    [Test]
    public void SurroundingWhitespace_IsTrimmed()
    {
        Assert.IsTrue(ZoneStatusSelector.TryParse("  Burn  ", out var type));
        Assert.AreEqual(ZoneStatusType.Burn, type);
    }

    [Test]
    public void MissingField_IsNoneAndNotAnError()
    {
        // 필드를 안 적은 층 = 상태이상 없음. 경고를 띄우면 안 된다.
        Assert.IsTrue(ZoneStatusSelector.TryParse(null, out var fromNull));
        Assert.AreEqual(ZoneStatusType.None, fromNull);

        Assert.IsTrue(ZoneStatusSelector.TryParse("", out var fromEmpty));
        Assert.AreEqual(ZoneStatusType.None, fromEmpty);

        Assert.IsTrue(ZoneStatusSelector.TryParse("   ", out var fromBlank));
        Assert.AreEqual(ZoneStatusType.None, fromBlank);
    }

    [Test]
    public void Typo_ReportsFailureSoItCannotPassSilently()
    {
        Assert.IsFalse(ZoneStatusSelector.TryParse("Frostbight", out var type));
        Assert.AreEqual(ZoneStatusType.None, type);
    }

    [Test]
    public void OutOfRangeNumber_IsRejected()
    {
        // Enum.TryParse는 정의되지 않은 숫자도 통과시킨다 — IsDefined 가드가 있어야 한다
        Assert.IsFalse(ZoneStatusSelector.TryParse("99", out var type));
        Assert.AreEqual(ZoneStatusType.None, type);
    }

    [Test]
    public void Parse_AbsorbsFailureAsNone()
    {
        Assert.AreEqual(ZoneStatusType.None, ZoneStatusSelector.Parse("nonsense"));
        Assert.AreEqual(ZoneStatusType.Burn, ZoneStatusSelector.Parse("Burn"));
    }
}
