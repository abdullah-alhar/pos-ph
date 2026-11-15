using Microsoft.EntityFrameworkCore;
using VetPos.Core.Billing;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;
using VetPos.Core.Settings;
using VetPos.Data.Services;

namespace VetPos.Data.Tests;

public class SaleServiceTests
{
    [Fact]
    public async Task Full_sale_saves_everything_and_takes_stock_fefo()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Royal Canin Kitten 2 kg", 5450m);
        await Shop.ReceiveAsync(test, owner, sup,
            new ReceiptLineInput(p, 10, 4600m, 5450m, "LATE", Shop.Today.AddDays(300)),
            new ReceiptLineInput(p, 3, 4500m, 5450m, "EARLY", Shop.Today.AddDays(20)));

        var cart = new BillCart(Guid.NewGuid(), [Shop.Line(p, 5, 5450m)], null);
        var r = await Shop.SellAsync(test, cashier, cart, cash: 30000m);

        Assert.Equal(SaleService.FirstBillNumber, r.Number);
        Assert.Equal(27250m, r.Total);
        Assert.Equal(2750m, r.Change);
        // The early batch (3) goes first, then 2 from the late one.
        Assert.Equal((8, "8,0"), await Shop.StockAsync(test, p));
        var sale = await test.Get<SaleService>().GetAsync(r.SaleId);
        Assert.Equal(3 * 4500m + 2 * 4600m, sale!.CostTotal);   // exact cost from the batches
        Assert.Equal(27250m - 22700m, sale.Profit);
        Assert.Equal(PaymentMethod.Cash, sale.Payments.Single().Method);
        Assert.Equal(30000m, sale.Payments.Single().Tendered);
    }

    [Fact]
    public async Task Price_change_and_discounts_are_saved_and_audited()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Frontline M", 450m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 10, 300m, 450m, "F1", Shop.Today.AddDays(400)));

        var cart = new BillCart(Guid.NewGuid(),
            [Shop.Line(p, 5, 400m, listPrice: 450m, discount: new Discount(DiscountType.PerUnit, 20m))],
            new Discount(DiscountType.Amount, 100m));
        var r = await Shop.SellAsync(test, cashier, cart);

        Assert.Equal(5 * 400m - 100m - 100m, r.Total); // 2000 − 5×20 − 100 = 1800
        await using var db = test.NewContext();
        var audit = await db.AuditLog.Where(a => a.EntityType == "Sale").Select(a => a.Action).ToListAsync();
        Assert.Contains(AuditActions.PriceChange, audit);
        Assert.Contains(AuditActions.LineDiscount, audit);
        Assert.Contains(AuditActions.BillDiscount, audit);
        var change = await db.AuditLog.SingleAsync(a => a.Action == AuditActions.PriceChange);
        Assert.Equal("450.00", change.OldValue);
        Assert.Equal("400.00", change.NewValue);
        Assert.Equal(cashier.Id, change.CreatedById);
    }

    [Fact]
    public async Task Selling_more_than_stock_is_blocked_with_a_clear_message()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Bravecto M", 8900m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 3, 7400m, 8900m, "B", Shop.Today.AddDays(200)));

        var ex = await Assert.ThrowsAsync<PosException>(() => Shop.SellAsync(test, cashier, new BillCart(Guid.NewGuid(), [Shop.Line(p, 4, 8900m)], null)));
        Assert.Equal("Not enough stock for Bravecto M: only 3 left.", ex.Message);
        Assert.Equal((3, "3"), await Shop.StockAsync(test, p)); // nothing changed
    }

    [Fact]
    public async Task Expired_batches_are_never_sold()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Oxytetracycline 100 ml", 2100m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 5, 1500m, 2100m, "OLD", Shop.Today.AddDays(5)));
        test.Clock.Now = test.Clock.Now.AddDays(10); // the batch is now expired

        var ex = await Assert.ThrowsAsync<PosException>(() => Shop.SellAsync(test, cashier, new BillCart(Guid.NewGuid(), [Shop.Line(p, 1, 2100m)], null)));
        Assert.Contains("5 expired", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_can_allow_selling_above_stock_and_it_is_logged()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        await test.Get<SettingsService>().SaveAsync(BillingSettings.Key, new BillingSettings { AllowSellAboveStock = true }, owner);
        var p = await Shop.ProductAsync(test, owner, cat, "Collar M", 550m, expiry: false);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 1, 300m, 550m, null, null));

        var r = await Shop.SellAsync(test, cashier, new BillCart(Guid.NewGuid(), [Shop.Line(p, 3, 550m)], null));

        Assert.Single(r.Warnings);
        Assert.Equal((-2, "0"), await Shop.StockAsync(test, p));
        await using var db = test.NewContext();
        Assert.True(await db.AuditLog.AnyAsync(a => a.Action == AuditActions.SaleAboveStock));
    }

    [Fact]
    public async Task Same_bill_sent_twice_is_saved_once()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Litter 5 kg", 1850m, expiry: false);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 5, 1300m, 1850m, null, null));
        var cart = new BillCart(Guid.NewGuid(), [Shop.Line(p, 1, 1850m)], null);

        var first = await Shop.SellAsync(test, cashier, cart);
        var again = await Shop.SellAsync(test, cashier, cart);

        Assert.False(first.Duplicate);
        Assert.True(again.Duplicate);
        Assert.Equal(first.Number, again.Number);
        Assert.Equal((4, "4"), await Shop.StockAsync(test, p));
    }

    [Fact]
    public async Task Price_group_sells_only_from_batches_at_that_price()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Pedigree 10 kg", 6200m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 2, 5300m, 6200m, "OLD", Shop.Today.AddDays(60)));
        await Shop.ReceiveAsync(test, owner, sup, new Dictionary<int, PriceDecision> { [p] = PriceDecision.AskAtBilling },
            new ReceiptLineInput(p, 5, 5550m, 6500m, "NEW", Shop.Today.AddDays(240)));

        var item = await test.Get<SaleService>().GetBillingItemAsync(p);
        Assert.True(item!.HasSeveralPrices);
        Assert.Equal(6200m, item.DefaultPrice);
        Assert.Equal(PriceChoice.AskAtBilling, item.PriceChoice);

        await Shop.SellAsync(test, cashier, new BillCart(Guid.NewGuid(), [Shop.Line(p, 1, 6500m, group: 6500m)], null));
        Assert.Equal((6, "2,4"), await Shop.StockAsync(test, p));
    }

    [Fact]
    public async Task Payment_must_cover_the_bill()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Bowl", 750m, expiry: false);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 5, 400m, 750m, null, null));

        var ex = await Assert.ThrowsAsync<PosException>(() => Shop.SellAsync(test, cashier, new BillCart(Guid.NewGuid(), [Shop.Line(p, 2, 750m)], null), cash: 1000m));
        Assert.Contains("Still to pay: Rs 500.00", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Void_puts_units_back_into_the_same_batches_and_needs_the_owner()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Drontal strip", 520m);
        await Shop.ReceiveAsync(test, owner, sup,
            new ReceiptLineInput(p, 2, 380m, 520m, "A", Shop.Today.AddDays(20)),
            new ReceiptLineInput(p, 5, 390m, 520m, "B", Shop.Today.AddDays(200)));
        var r = await Shop.SellAsync(test, cashier, new BillCart(Guid.NewGuid(), [Shop.Line(p, 4, 520m)], null));
        Assert.Equal((3, "0,3"), await Shop.StockAsync(test, p));

        var sales = test.Get<SaleService>();
        await Assert.ThrowsAsync<PosException>(() => sales.VoidAsync(r.SaleId, "wrong item", cashier, null));
        await sales.VoidAsync(r.SaleId, "wrong item", cashier, owner);

        Assert.Equal((7, "2,5"), await Shop.StockAsync(test, p));
        var view = await sales.GetAsync(r.SaleId);
        Assert.Equal(SaleStatus.Voided, view!.Status);
        Assert.Equal("wrong item", view.VoidReason);
        await Assert.ThrowsAsync<PosException>(() => sales.VoidAsync(r.SaleId, "again", owner, null));
        var summary = await test.Get<ReportService>().SummaryAsync(Shop.Today, Shop.Today);
        Assert.Equal(0, summary.Bills);
        Assert.Equal(1, summary.VoidedBills);
    }

    [Fact]
    public async Task Draft_is_cleared_when_the_bill_is_saved()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Fish feed 1 kg", 650m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 9, 480m, 650m, "F", Shop.Today.AddDays(100)));
        var store = test.Get<BillStore>();
        var cart = new BillCart(Guid.NewGuid(), [Shop.Line(p, 2, 650m)], null);

        await store.SaveDraftAsync(cashier.Id, cart);
        Assert.Equal(cart.ClientId, (await store.LoadDraftAsync(cashier.Id))!.ClientId);

        await Shop.SellAsync(test, cashier, cart);
        Assert.Null(await store.LoadDraftAsync(cashier.Id));
    }

    [Fact]
    public async Task Held_bills_can_be_recalled_once()
    {
        var (test, _, cashier, _, _) = await Shop.OpenAsync();
        await using var __ = test;
        var store = test.Get<BillStore>();
        var cart = new BillCart(Guid.NewGuid(), [Shop.Line(1, 2, 100m)], null);
        await store.HoldAsync(cart, "Mr Perera", cashier);

        var held = await store.HeldAsync();
        Assert.Equal(200m, held.Single().Total);
        Assert.Equal("Mr Perera", held.Single().Label);
        var back = await store.RecallAsync(held.Single().Id);
        Assert.Equal(cart.ClientId, back.ClientId);
        await Assert.ThrowsAsync<PosException>(() => store.RecallAsync(held.Single().Id));
    }
}
