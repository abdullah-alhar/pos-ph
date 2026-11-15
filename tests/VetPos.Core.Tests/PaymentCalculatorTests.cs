using VetPos.Core.Domain;
using VetPos.Core.Pricing;

namespace VetPos.Core.Tests;

public class PaymentCalculatorTests
{
    [Fact]
    public void Cash_gives_change()
    {
        var p = PaymentCalculator.Summarize(11805m, [new PaymentInput(PaymentMethod.Cash, 12000m)]);
        Assert.True(p.Complete);
        Assert.Equal(195m, p.Change);
        Assert.Equal(11805m, p.Applied.Single().Amount);
        Assert.Equal(12000m, p.Applied.Single().Tendered);
    }

    [Fact]
    public void Mixed_card_then_cash()
    {
        var p = PaymentCalculator.Summarize(10000m, [new PaymentInput(PaymentMethod.Card, 6000m, " 4821 "), new PaymentInput(PaymentMethod.Cash, 5000m)]);
        Assert.True(p.Complete);
        Assert.Equal(1000m, p.Change);
        Assert.Equal([PaymentMethod.Card, PaymentMethod.Cash], p.Applied.Select(a => a.Method));
        Assert.Equal(4000m, p.Applied[1].Amount);
        Assert.Equal("4821", p.Applied[0].Reference);
    }

    [Fact]
    public void Not_enough_shows_what_is_left()
    {
        var p = PaymentCalculator.Summarize(1000m, [new PaymentInput(PaymentMethod.Cash, 600m)]);
        Assert.False(p.Complete);
        Assert.Equal(400m, p.Remaining);
        Assert.Equal(0m, p.Change);
    }

    [Fact]
    public void Card_cannot_be_more_than_the_bill()
    {
        var p = PaymentCalculator.Summarize(1000m, [new PaymentInput(PaymentMethod.Card, 1500m)]);
        Assert.False(p.Complete);
        Assert.Contains("cannot be more than the bill", p.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Exact_bank_transfer()
    {
        var p = PaymentCalculator.Summarize(2500.5m, [new PaymentInput(PaymentMethod.BankTransfer, 2500.5m)]);
        Assert.True(p.Complete);
        Assert.Equal(0m, p.Change);
    }

    [Fact]
    public void Zero_or_negative_payments_are_refused() =>
        Assert.NotNull(PaymentCalculator.Summarize(100m, [new PaymentInput(PaymentMethod.Cash, 0m)]).Error);

    [Fact]
    public void Quick_cash_offers_exact_and_round_notes() =>
        Assert.Equal([11805m, 11900m, 12000m, 15000m, 20000m], PaymentCalculator.QuickCash(11805m));

    [Fact]
    public void Quick_cash_for_a_round_total_offers_it_and_the_next_big_note() =>
        Assert.Equal([15000m, 20000m], PaymentCalculator.QuickCash(15000m));

    [Fact]
    public void Part_cash_payment_leaves_the_bill_unpaid() =>
        Assert.False(PaymentCalculator.Summarize(5000m, [new PaymentInput(PaymentMethod.Cash, 1000m)]).Complete);
}
