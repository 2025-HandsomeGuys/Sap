using NUnit.Framework;

/// <summary>DayEarningsLedger — 카테고리 집계·기타 흡수·리셋·Capture/Apply 왕복 검증.</summary>
public class DayEarningsLedgerTests
{
    [SetUp]
    public void Setup() => DayEarningsLedger.ResetForNewDay(1000);

    [Test]
    public void Report_AccumulatesPerCategory()
    {
        DayEarningsLedger.Report(DayEarningsCategory.MineralSale, 300);
        DayEarningsLedger.Report(DayEarningsCategory.MineralSale, 200);
        DayEarningsLedger.Report(DayEarningsCategory.Stock, -150);
        DayEarningsLedger.Report(DayEarningsCategory.Coin, 50);

        var report = DayEarningsLedger.BuildReport(3, 1400);
        Assert.AreEqual(3, report.day);
        Assert.AreEqual(500, report.mineralSale);
        Assert.AreEqual(-150, report.stock);
        Assert.AreEqual(50, report.coin);
    }

    [Test]
    public void BuildReport_TotalIsGoldDelta_UntrackedGoesToOther()
    {
        // 추적된 증감 +400, 실제 골드는 +700 → 기타 +300으로 흡수
        DayEarningsLedger.Report(DayEarningsCategory.MineralSale, 400);
        var report = DayEarningsLedger.BuildReport(1, 1700);

        Assert.AreEqual(700, report.total);
        Assert.AreEqual(300, report.other);
        Assert.AreEqual(1700, report.goldAfter);
        // 항목 합계 = 최종 손익 (합계가 어긋나지 않아야 연출 신뢰 가능)
        Assert.AreEqual(report.total,
            report.mineralSale + report.stock + report.coin + report.shopPurchase + report.upgrade + report.other);
    }

    [Test]
    public void ResetForNewDay_ClearsCategoriesAndSetsStartGold()
    {
        DayEarningsLedger.Report(DayEarningsCategory.Upgrade, -500);
        DayEarningsLedger.ResetForNewDay(2500);

        var report = DayEarningsLedger.BuildReport(2, 2500);
        Assert.AreEqual(0, report.upgrade);
        Assert.AreEqual(0, report.total);
        Assert.AreEqual(0, report.other);
    }

    [Test]
    public void CaptureThenApply_RoundTrips()
    {
        DayEarningsLedger.Report(DayEarningsCategory.Coin, -320);
        DayEarningsLedger.Report(DayEarningsCategory.ShopPurchase, -80);
        var data = DayEarningsLedger.Capture();

        DayEarningsLedger.ResetForNewDay(0);
        DayEarningsLedger.Apply(data, fallbackGold: 9999);

        var report = DayEarningsLedger.BuildReport(1, 600);
        Assert.AreEqual(-320, report.coin);
        Assert.AreEqual(-80, report.shopPurchase);
        Assert.AreEqual(600 - 1000, report.total); // dayStartGold=1000 복원됨
    }

    [Test]
    public void Apply_NoData_FallsBackToCurrentGold()
    {
        DayEarningsLedger.Report(DayEarningsCategory.Stock, 777);

        DayEarningsLedger.Apply(null, fallbackGold: 5000);
        var report = DayEarningsLedger.BuildReport(1, 5000);
        Assert.AreEqual(0, report.stock);
        Assert.AreEqual(0, report.total);

        DayEarningsLedger.Apply(new DayEarningsSaveData { hasData = false }, fallbackGold: 300);
        report = DayEarningsLedger.BuildReport(1, 400);
        Assert.AreEqual(100, report.total); // dayStartGold=300 폴백
    }

    [Test]
    public void EstimateCurrentGold_IsStartGoldPlusTracked()
    {
        DayEarningsLedger.Report(DayEarningsCategory.MineralSale, 250);
        DayEarningsLedger.Report(DayEarningsCategory.Upgrade, -100);
        Assert.AreEqual(1150, DayEarningsLedger.EstimateCurrentGold());
    }
}
