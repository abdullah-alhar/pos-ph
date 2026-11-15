using Microsoft.EntityFrameworkCore;
using VetPos.Core.Billing;
using VetPos.Core.Rules;
using VetPos.Data.Services;

namespace VetPos.Data.Tests;

public class ExpiryAndSalesGraphTests
{
    [Fact]
    public async Task Expiry_date_from_stock_in_is_kept_and_marks_the_product()
    {
        var (test, owner, _, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Drontal Plus", 450m, expiry: false);

        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 10, 300m, 450m, null, Shop.Today.AddDays(90)));

        var d = await test.Get<CatalogService>().GetAsync(p);
        Assert.True(d!.TracksExpiry);
        Assert.Equal(Shop.Today.AddDays(90), d.Batches.Single().ExpiryDate);
    }

    [Fact]
    public async Task Stock_in_with_a_passed_expiry_date_is_refused()
    {
        var (test, owner, _, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Frontline Dog M", 2400m);

        var ex = await Assert.ThrowsAsync<PosException>(() =>
            Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 3, 2000m, 2400m, null, Shop.Today.AddDays(-1))));
        Assert.Contains("already passed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Batch_expiry_date_can_be_changed_and_is_logged()
    {
        var (test, owner, _, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Ivermectin 1% 50 ml", 1200m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 4, 900m, 1200m, "IV-7", Shop.Today.AddDays(20)));
        var batch = (await test.Get<CatalogService>().GetAsync(p))!.Batches.Single();

        await test.Get<StockEntryService>().SetBatchExpiryAsync(batch.Id, Shop.Today.AddDays(400), owner);

        Assert.Equal(Shop.Today.AddDays(400), (await test.Get<CatalogService>().GetAsync(p))!.Batches.Single().ExpiryDate);
        Assert.DoesNotContain(await test.Get<CatalogService>().ExpiringAsync(), r => r.ProductId == p);
        await using var db = test.NewContext();
        Assert.Contains(await db.AuditLog.Select(a => a.Summary).ToListAsync(), s => s.Contains("expiry", StringComparison.Ordinal) && s.Contains("IV-7", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sales_graph_lists_every_day_and_every_hour()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Kitten 2 kg", 5000m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 10, 4000m, 5000m, "K", Shop.Today.AddDays(200)));
        await Shop.SellAsync(test, cashier, new BillCart(Guid.NewGuid(), [Shop.Line(p, 2, 5000m)], null));
        var reports = test.Get<ReportService>();

        var days = await reports.SalesByPeriodAsync(Shop.Today.AddDays(-6), Shop.Today, SalesGrouping.Day);
        Assert.Equal(7, days.Count);
        Assert.Equal(10000m, days[^1].Sales);
        Assert.Equal(10000m, days.Sum(d => d.Sales));

        var hours = await reports.SalesByPeriodAsync(Shop.Today, Shop.Today, SalesGrouping.Hour);
        Assert.True(hours.Count >= 13); // 8:00 to 20:00 at least
        Assert.Equal(1, hours.Sum(h => h.Bills));
        Assert.Equal(10000m, hours.Sum(h => h.Sales));

        var months = await reports.SalesByPeriodAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), SalesGrouping.Month);
        Assert.Equal(12, months.Count);
        Assert.Equal(10000m, months[10].Sales);
    }

    [Fact]
    public async Task One_product_report_counts_only_its_lines()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var kitten = await Shop.ProductAsync(test, owner, cat, "Kitten 2 kg", 5000m);
        var bowl = await Shop.ProductAsync(test, owner, cat, "Steel bowl", 800m, expiry: false);
        await Shop.ReceiveAsync(test, owner, sup,
            new ReceiptLineInput(kitten, 10, 4000m, 5000m, "K", Shop.Today.AddDays(200)),
            new ReceiptLineInput(bowl, 10, 500m, 800m, null, null));
        await Shop.SellAsync(test, cashier, new BillCart(Guid.NewGuid(), [Shop.Line(kitten, 2, 5000m), Shop.Line(bowl, 1, 800m)], null));
        await Shop.SellAsync(test, cashier, new BillCart(Guid.NewGuid(), [Shop.Line(bowl, 3, 800m)], null));
        var reports = test.Get<ReportService>();

        var s = await reports.SummaryAsync(Shop.Today, Shop.Today, bowl);
        Assert.Equal(2, s.Bills);
        Assert.Equal(4, s.Units);
        Assert.Equal(3200m, s.Sales);
        Assert.Equal(2000m, s.Cost);

        var days = await reports.SalesByPeriodAsync(Shop.Today.AddDays(-6), Shop.Today, SalesGrouping.Day, kitten);
        Assert.Equal(10000m, days.Sum(d => d.Sales));
        Assert.Equal(2, days.Sum(d => d.Units));
        Assert.Equal(1, days.Sum(d => d.Bills));
    }

    [Theory]
    [InlineData(0, SalesGrouping.Hour)]
    [InlineData(6, SalesGrouping.Day)]
    [InlineData(30, SalesGrouping.Day)]
    [InlineData(364, SalesGrouping.Month)]
    public void Graph_grouping_follows_the_length_of_the_range(int days, SalesGrouping expected) =>
        Assert.Equal(expected, ReportService.GroupingFor(Shop.Today.AddDays(-days), Shop.Today));
}
