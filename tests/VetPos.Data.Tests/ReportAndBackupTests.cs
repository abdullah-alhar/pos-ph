using VetPos.Core.Billing;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;
using VetPos.Data.Services;

namespace VetPos.Data.Tests;

public class ReportAndBackupTests
{
    [Fact]
    public async Task Daily_summary_profit_and_change_log()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Kitten 2 kg", 5000m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 10, 4000m, 5000m, "K", Shop.Today.AddDays(200)));
        await Shop.SellAsync(test, cashier, new BillCart(Guid.NewGuid(), [Shop.Line(p, 2, 5000m)], new Discount(DiscountType.Amount, 500m)));
        await test.Get<SaleService>().CheckoutAsync(new CheckoutRequest(new BillCart(Guid.NewGuid(), [Shop.Line(p, 1, 4800m, listPrice: 5000m)], null),
            [new PaymentInput(PaymentMethod.Card, 4800m, "1234")]), owner);

        var reports = test.Get<ReportService>();
        var s = await reports.SummaryAsync(Shop.Today, Shop.Today);
        Assert.Equal(2, s.Bills);
        Assert.Equal(14300m, s.Sales);
        Assert.Equal(12000m, s.Cost);
        Assert.Equal(2300m, s.Profit);
        Assert.Equal(500m, s.Discounts); // a price change is not a discount
        Assert.Equal(["Cash", "Card"], s.ByPayment.Select(b => b.Label));
        Assert.Equal(2, s.ByCashier.Count);

        var product = Assert.Single(await reports.ByProductAsync(Shop.Today, Shop.Today));
        Assert.Equal(3, product.Units);
        Assert.Equal(2300m, product.Profit);

        var stock = Assert.Single(await reports.StockAsync());
        Assert.Equal(7, stock.Qty);
        Assert.Equal(28000m, stock.ValueAtCost);

        var changes = await reports.ChangesAsync(Shop.Today, Shop.Today);
        Assert.Contains(changes, c => c.Action == "Price changed on bill" && c.User == "Shop Owner");
        Assert.Contains(changes, c => c.Action == "Bill discount" && c.User == "Nimal");
    }

    [Fact]
    public async Task Backup_restore_brings_back_the_data_and_keeps_a_safety_copy()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Kitten 2 kg", 5000m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 10, 4000m, 5000m, "K", Shop.Today.AddDays(200)));
        var backups = test.Get<BackupService>();

        var copy = await backups.CreateAsync(BackupKind.Manual, owner);
        Assert.True(File.Exists(copy.FullPath));
        await Shop.SellAsync(test, cashier, new BillCart(Guid.NewGuid(), [Shop.Line(p, 4, 5000m)], null));
        Assert.Equal(6, (await Shop.StockAsync(test, p)).Stock);

        await Assert.ThrowsAsync<PosException>(() => backups.RestoreAsync(copy.FullPath, cashier));
        await backups.RestoreAsync(copy.FullPath, owner);

        Assert.Equal(10, (await Shop.StockAsync(test, p)).Stock);
        Assert.Contains(backups.List(), b => b.Kind == BackupKind.PreRestore);
    }

    [Fact]
    public async Task Daily_backup_is_made_once_a_day()
    {
        var (test, _, _, _, _) = await Shop.OpenAsync();
        await using var _ = test;
        var backups = test.Get<BackupService>();
        Assert.NotNull(await backups.EnsureDailyAsync());
        Assert.Null(await backups.EnsureDailyAsync());
        Assert.Single(backups.List(), b => b.Kind == BackupKind.Auto);
    }

    [Fact]
    public async Task A_file_that_is_not_a_backup_is_refused()
    {
        var (test, owner, _, _, _) = await Shop.OpenAsync();
        await using var _ = test;
        var bad = Path.Combine(test.Paths.Root, "bad.db");
        await File.WriteAllTextAsync(bad, "not a database at all, just some text to fill the header bytes");
        await Assert.ThrowsAsync<PosException>(() => test.Get<BackupService>().RestoreAsync(bad, owner));
    }
}
