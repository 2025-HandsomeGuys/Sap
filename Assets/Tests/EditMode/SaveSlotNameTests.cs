using NUnit.Framework;

public class SaveSlotNameTests
{
    [Test]
    public void PlainName_PassesThrough()
    {
        Assert.AreEqual("첫 도전", SaveSlotName.Sanitize("첫 도전"));
    }

    [Test]
    public void NullOrEmpty_BecomesNull()
    {
        Assert.IsNull(SaveSlotName.Sanitize(null));
        Assert.IsNull(SaveSlotName.Sanitize(""));
    }

    [Test]
    public void WhitespaceOnly_BecomesNull()
    {
        Assert.IsNull(SaveSlotName.Sanitize("   "));
        Assert.IsNull(SaveSlotName.Sanitize("\n\t  \r"));
    }

    [Test]
    public void LeadingAndTrailingSpaces_AreTrimmed()
    {
        Assert.AreEqual("광부", SaveSlotName.Sanitize("   광부   "));
    }

    [Test]
    public void RepeatedSpaces_CollapseToOne()
    {
        Assert.AreEqual("깊은 굴", SaveSlotName.Sanitize("깊은     굴"));
    }

    [Test]
    public void NewlinesAndTabs_BecomeSingleSpace()
    {
        Assert.AreEqual("A B", SaveSlotName.Sanitize("A\n\tB"));
    }

    [Test]
    public void OverlongName_IsTruncatedToMaxLength()
    {
        string raw = new string('가', SaveSlotName.MaxLength + 10);
        string result = SaveSlotName.Sanitize(raw);
        Assert.AreEqual(SaveSlotName.MaxLength, result.Length);
    }

    [Test]
    public void Truncation_NeverLeavesTrailingSpace()
    {
        // 16번째 글자가 공백이 되는 입력 — 잘린 결과 끝에 공백이 남으면 안 된다
        string raw = new string('a', SaveSlotName.MaxLength) + "   뒤에더";
        string result = SaveSlotName.Sanitize(raw);
        Assert.AreEqual(SaveSlotName.MaxLength, result.Length);
        Assert.AreEqual(result.TrimEnd(), result);
    }

    [Test]
    public void Sanitize_IsIdempotent()
    {
        // 입력창에서 한 번, 저장 직전에 또 한 번 통과해도 결과가 같아야 한다
        string once = SaveSlotName.Sanitize("  긴  이름을   아주 길게 적어본다  ");
        Assert.AreEqual(once, SaveSlotName.Sanitize(once));
    }

    [Test]
    public void HasName_MatchesSanitizeResult()
    {
        Assert.IsFalse(SaveSlotName.HasName("   "));
        Assert.IsTrue(SaveSlotName.HasName(" x "));
    }
}
