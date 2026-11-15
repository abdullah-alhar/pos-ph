using VetPos.Core.Stock;

namespace VetPos.Core.Tests;

public class FefoTests
{
    private static readonly DateOnly Today = new(2026, 11, 13);
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BatchStock B(int id, int? expiresInDays, int qty, decimal price = 100m, decimal cost = 70m, int receivedDay = 0) =>
        new(id, expiresInDays is { } d ? Today.AddDays(d) : null, qty, price, cost, T0.AddDays(receivedDay));

    [Fact]
    public void Sells_the_batch_that_expires_first()
    {
        var r = Fefo.Allocate([B(1, 200, 10), B(2, 30, 5), B(3, null, 10)], 3, Today);
        Assert.Equal([new BatchTake(2, 3, 70m)], r.Takes);
        Assert.True(r.Complete);
    }

    [Fact]
    public void Spills_into_the_next_batch()
    {
        var r = Fefo.Allocate([B(1, 200, 10, cost: 80m), B(2, 30, 2, cost: 60m)], 5, Today);
        Assert.Equal([new BatchTake(2, 2, 60m), new BatchTake(1, 3, 80m)], r.Takes);
        Assert.Equal(360m, r.CostTotal);
    }

    [Fact]
    public void Never_sells_expired_stock()
    {
        var batches = new[] { B(1, -1, 10), B(2, 10, 1) };
        var r = Fefo.Allocate(batches, 3, Today);
        Assert.Equal([new BatchTake(2, 1, 70m)], r.Takes);
        Assert.Equal(2, r.Shortage);
        Assert.Equal(10, Fefo.ExpiredQty(batches, Today));
        Assert.Equal(1, Fefo.SellableQty(batches, Today));
    }

    [Fact]
    public void Expiring_today_is_still_sellable()
    {
        var r = Fefo.Allocate([B(1, 0, 1)], 1, Today);
        Assert.True(r.Complete);
    }

    [Fact]
    public void Items_without_expiry_come_last_then_oldest_received_first()
    {
        var order = Fefo.Sellable([B(1, null, 1, receivedDay: 5), B(2, null, 1, receivedDay: 1), B(3, 400, 1)], Today).Select(b => b.BatchId);
        Assert.Equal([3, 2, 1], order);
    }

    [Fact]
    public void Price_filter_only_takes_batches_at_that_price()
    {
        var batches = new[] { B(1, 30, 2, price: 5450m), B(2, 300, 6, price: 5750m) };
        var r = Fefo.Allocate(batches, 3, Today, price: 5750m);
        Assert.Equal([new BatchTake(2, 3, 70m)], r.Takes);
    }

    [Fact]
    public void Price_options_list_the_fefo_price_first()
    {
        var options = Fefo.PriceOptions([B(1, 300, 6, price: 5750m), B(2, 120, 4, price: 5450m), B(3, -5, 9, price: 5000m)], Today);
        Assert.Equal([5450m, 5750m], options.Select(o => o.Price));
        Assert.Equal(4, options[0].QtyAvailable);
        Assert.Equal(Today.AddDays(120), options[0].EarliestExpiry);
    }

    [Fact]
    public void Empty_stock_is_a_full_shortage() => Assert.Equal(4, Fefo.Allocate([], 4, Today).Shortage);

    [Theory]
    [InlineData(null, ExpiryStatus.NoExpiry)]
    [InlineData(-1, ExpiryStatus.Expired)]
    [InlineData(0, ExpiryStatus.ExpiringSoon)]
    [InlineData(30, ExpiryStatus.ExpiringSoon)]
    [InlineData(31, ExpiryStatus.Ok)]
    public void Expiry_status(int? days, ExpiryStatus expected) =>
        Assert.Equal(expected, Fefo.StatusOf(days is { } d ? Today.AddDays(d) : null, Today));
}
