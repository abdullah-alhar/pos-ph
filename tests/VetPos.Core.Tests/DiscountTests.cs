using VetPos.Core.Domain;
using VetPos.Core.Pricing;

namespace VetPos.Core.Tests;

public class DiscountTests
{
    [Fact]
    public void Plain_number_on_a_line_is_rupees_off_each_unit()
    {
        Assert.True(Discount.TryParse("100", DiscountType.PerUnit, out var d, out _));
        Assert.Equal(new Discount(DiscountType.PerUnit, 100m), d);
    }

    [Fact]
    public void Percent_sign_makes_a_percentage()
    {
        Assert.True(Discount.TryParse("12.5%", DiscountType.PerUnit, out var d, out _));
        Assert.Equal(new Discount(DiscountType.Percent, 12.5m), d);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0%")]
    public void Empty_or_zero_removes_the_discount(string text)
    {
        Assert.True(Discount.TryParse(text, DiscountType.Amount, out var d, out var error));
        Assert.Null(d);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("150%", "more than 100%")]
    [InlineData("abc", "Type an amount")]
    [InlineData("-10", "Type an amount")]
    public void Bad_input_gives_a_clear_message(string text, string part)
    {
        Assert.False(Discount.TryParse(text, DiscountType.Amount, out _, out var error));
        Assert.Contains(part, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_reads_like_the_receipt()
    {
        Assert.Equal("10%", new Discount(DiscountType.Percent, 10m).Describe());
        Assert.Equal("Rs 100.00 each", new Discount(DiscountType.PerUnit, 100m).Describe());
        Assert.Equal("Rs 500.00", new Discount(DiscountType.Amount, 500m).Describe());
    }
}
