using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;
using VetPos.Core.Rules;
using VetPos.Data.Services;

namespace VetPos.Data.Tests;

public class StockEntryTests
{
    [Fact]
    public async Task Receipt_adds_batches_stock_movements_and_new_prices()
    {
        var (test, owner, _, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Layer Mash 50 kg", 9800m);

        var r = await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 40, 8900m, 9900m, "LM-1", Shop.Today.AddDays(60)));

        Assert.Equal("GRN-00001", r.Number);
        Assert.Equal(356000m, r.Total);
        var detail = await test.Get<CatalogService>().GetAsync(p);
        Assert.Equal(40, detail!.StockQty);
        Assert.Equal(9900m, detail.SellingPrice);
        Assert.Equal(8900m, detail.CostPrice);
        Assert.Equal("LM-1", detail.Batches.Single().BatchNo);
        await using var db = test.NewContext();
        Assert.Equal(40, await db.StockMovements.Where(m => m.ProductId == p).SumAsync(m => m.Change));
    }

    [Fact]
    public async Task New_price_with_old_stock_must_be_answered()
    {
        var (test, owner, _, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Whiskas 1.2 kg", 2350m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 5, 1900m, 2350m, "A", Shop.Today.AddDays(100)));
        var line = new ReceiptLineInput(p, 10, 2000m, 2500m, "B", Shop.Today.AddDays(300));

        var questions = await test.Get<StockEntryService>().PriceQuestionsAsync([line]);
        var q = Assert.Single(questions);
        Assert.Equal([2350m], q.CurrentPrices);
        Assert.Equal(5, q.QtyOnHand);
        await Assert.ThrowsAsync<PosException>(() => Shop.ReceiveAsync(test, owner, sup, line));
    }

    [Theory]
    [InlineData(PriceDecision.ChangeAll, PriceChoice.OnePrice, 2500)]
    [InlineData(PriceDecision.KeepOldUntilSold, PriceChoice.OldFirst, 2350)]
    [InlineData(PriceDecision.AskAtBilling, PriceChoice.AskAtBilling, 2350)]
    public async Task Price_decision_sets_how_billing_prices_the_item(PriceDecision decision, PriceChoice expected, int oldBatchPrice)
    {
        var (test, owner, _, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Whiskas 1.2 kg", 2350m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 5, 1900m, 2350m, "A", Shop.Today.AddDays(100)));

        await Shop.ReceiveAsync(test, owner, sup, new Dictionary<int, PriceDecision> { [p] = decision },
            new ReceiptLineInput(p, 10, 2000m, 2500m, "B", Shop.Today.AddDays(300)));

        var d = await test.Get<CatalogService>().GetAsync(p);
        Assert.Equal(expected, d!.PriceChoice);
        Assert.Equal((decimal)oldBatchPrice, d.Batches.Single(b => b.BatchNo == "A").SellingPrice);
        Assert.Equal(2500m, d.Batches.Single(b => b.BatchNo == "B").SellingPrice);
        var item = await test.Get<SaleService>().GetBillingItemAsync(p);
        Assert.Equal((decimal)oldBatchPrice, item!.DefaultPrice); // old stock expires first
    }


    [Fact]
    public async Task Same_supplier_invoice_cannot_be_entered_twice()
    {
        var (test, owner, _, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Bowl M", 750m, expiry: false);
        var input = new ReceiptInput(sup, "INV-77", Shop.Today, null, [new ReceiptLineInput(p, 1, 400m, 750m, null, null)]);
        var entry = test.Get<StockEntryService>();
        await entry.SaveAsync(input, new Dictionary<int, PriceDecision>(), owner);

        var ex = await Assert.ThrowsAsync<PosException>(() => entry.SaveAsync(input, new Dictionary<int, PriceDecision>(), owner));
        Assert.Contains("already entered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_can_take_expired_units_off_with_a_reason()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "AD3E 1 L", 2950m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 5, 2350m, 2950m, "X", Shop.Today.AddDays(3)));
        var batchId = (await test.Get<CatalogService>().GetAsync(p))!.Batches.Single().Id;
        var entry = test.Get<StockEntryService>();

        await Assert.ThrowsAsync<PosException>(() => entry.AdjustBatchAsync(batchId, -5, "expired", cashier));
        await entry.AdjustBatchAsync(batchId, -5, "expired, thrown away", owner);

        Assert.Equal((0, "0"), await Shop.StockAsync(test, p));
    }
}

public class StockWithoutSupplierTests
{
    [Fact]
    public async Task Stock_can_be_added_without_supplier_or_invoice()
    {
        var (test, owner, _, _, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var p = await Shop.ProductAsync(test, owner, cat, "Collar S", 450m, expiry: false);
        var r = await test.Get<StockEntryService>().SaveAsync(new ReceiptInput(null, null, Shop.Today, "Opening stock", [new ReceiptLineInput(p, 12, 250m, 450m, null, null)]),
            new Dictionary<int, PriceDecision>(), owner);
        Assert.Equal("No supplier", r.Supplier);
        Assert.Equal(12, (await Shop.StockAsync(test, p)).Stock);
    }
}
