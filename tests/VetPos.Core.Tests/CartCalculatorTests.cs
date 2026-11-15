using VetPos.Core.Domain;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;

namespace VetPos.Core.Tests;

public class CartCalculatorTests
{
    private static CartLineInput Line(decimal price, int qty, Discount? d = null, string key = "a") => new(key, price, qty, d);

    [Fact]
    public void Line_total_is_qty_times_price()
    {
        var t = CartCalculator.Calculate([Line(5450m, 2), Line(1050.5m, 3, key: "b")], null);
        Assert.Equal(10900m, t.Lines[0].LineTotal);
        Assert.Equal(3151.5m, t.Lines[1].LineTotal);
        Assert.Equal(14051.5m, t.Total);
        Assert.Equal(5, t.UnitCount);
    }

    [Fact]
    public void Rupees_off_each_unit_multiplies_by_qty()
    {
        // 5 × Rs 1,200 with Rs 100 off each = Rs 500 off.
        var t = CartCalculator.Calculate([Line(1200m, 5, new Discount(DiscountType.PerUnit, 100m))], null);
        Assert.Equal(6000m, t.Lines[0].Gross);
        Assert.Equal(500m, t.Lines[0].DiscountAmount);
        Assert.Equal(5500m, t.Total);
    }

    [Fact]
    public void Rupees_off_the_whole_line()
    {
        var t = CartCalculator.Calculate([Line(1200m, 5, new Discount(DiscountType.Amount, 500m))], null);
        Assert.Equal(5500m, t.Total);
    }

    [Fact]
    public void Percent_line_discount_rounds_to_the_cent()
    {
        // 3 × 333.33 = 999.99; 12.5 % = 124.99875 → 125.00
        var t = CartCalculator.Calculate([Line(333.33m, 3, new Discount(DiscountType.Percent, 12.5m))], null);
        Assert.Equal(125.00m, t.Lines[0].DiscountAmount);
        Assert.Equal(874.99m, t.Total);
    }

    [Fact]
    public void Bill_discount_comes_after_line_discounts()
    {
        var t = CartCalculator.Calculate(
            [Line(1000m, 1, new Discount(DiscountType.Percent, 10m)), Line(500m, 2, key: "b")],
            new Discount(DiscountType.Percent, 10m));
        // Lines after their discounts: 900 + 1000 = 1900; 10 % of 1900 = 190.
        Assert.Equal(100m, t.LineDiscountTotal);
        Assert.Equal(190m, t.BillDiscountAmount);
        Assert.Equal(1710m, t.Total);
        Assert.Equal(290m, t.DiscountTotal);
    }

    [Fact]
    public void Bill_discount_is_shared_over_lines_and_adds_up_exactly()
    {
        var t = CartCalculator.Calculate([Line(100m, 1), Line(100m, 1, key: "b"), Line(100m, 1, key: "c")], new Discount(DiscountType.Amount, 100m));
        Assert.Equal(100m, t.Lines.Sum(l => l.BillDiscountShare));
        Assert.Equal([33.34m, 33.33m, 33.33m], t.Lines.Select(l => l.BillDiscountShare));
        Assert.Equal(200m, t.Total);
        Assert.Equal(t.Total, t.Lines.Sum(l => l.LineTotal));
    }

    [Fact]
    public void Changed_price_is_used_for_this_bill()
    {
        // Price changed from 450 to 400 on the bill: only the bill's price matters here.
        var t = CartCalculator.Calculate([Line(400m, 2)], null);
        Assert.Equal(800m, t.Total);
    }

    [Fact]
    public void Line_discount_can_take_a_line_to_zero_but_not_below()
    {
        var zero = CartCalculator.Calculate([Line(100m, 2, new Discount(DiscountType.PerUnit, 100m))], null);
        Assert.Equal(0m, zero.Total);

        var ex = Assert.Throws<PosException>(() => CartCalculator.Calculate([Line(100m, 2, new Discount(DiscountType.PerUnit, 101m))], null));
        Assert.Contains("more than the line total", ex.Message, StringComparison.Ordinal);
        Assert.Throws<PosException>(() => CartCalculator.Calculate([Line(100m, 2, new Discount(DiscountType.Amount, 200.01m))], null));
    }

    [Fact]
    public void Bill_discount_cannot_go_below_zero()
    {
        Assert.Equal(0m, CartCalculator.Calculate([Line(100m, 1)], new Discount(DiscountType.Amount, 100m)).Total);
        var ex = Assert.Throws<PosException>(() => CartCalculator.Calculate([Line(100m, 1)], new Discount(DiscountType.Amount, 150m)));
        Assert.Contains("more than the bill", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Checks_give_messages_before_applying()
    {
        Assert.Null(CartCalculator.CheckLineDiscount(1200m, 5, new Discount(DiscountType.PerUnit, 100m)));
        Assert.NotNull(CartCalculator.CheckLineDiscount(1200m, 5, new Discount(DiscountType.PerUnit, 1300m)));
        Assert.NotNull(CartCalculator.CheckLineDiscount(1200m, 5, new Discount(DiscountType.Percent, 101m)));
        Assert.Null(CartCalculator.CheckBillDiscount(500m, new Discount(DiscountType.Amount, 500m)));
        Assert.NotNull(CartCalculator.CheckBillDiscount(500m, new Discount(DiscountType.Amount, 500.01m)));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, -1)]
    [InlineData(100_001, 1)]
    public void Bad_qty_or_price_is_refused(int qty, double price) =>
        Assert.Throws<PosException>(() => CartCalculator.Calculate([Line((decimal)price, qty)], null));

    [Fact]
    public void Empty_bill_totals_zero()
    {
        var t = CartCalculator.Calculate([], new Discount(DiscountType.Percent, 10m));
        Assert.Equal(0m, t.Total);
        Assert.Equal(0m, t.BillDiscountAmount);
    }

    [Fact]
    public void Allocate_handles_zero_weights_and_remainders()
    {
        Assert.Equal([0m, 0m], CartCalculator.Allocate(10m, [0m, 0m]));
        var parts = CartCalculator.Allocate(0.05m, [1m, 1m, 1m]);
        Assert.Equal(0.05m, parts.Sum());
        Assert.Equal([0.02m, 0.02m, 0.01m], parts);
    }
}
