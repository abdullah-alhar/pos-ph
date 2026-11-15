using VetPos.Core.Pricing;

namespace VetPos.Core.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData("1.005", "1.01")]
    [InlineData("1.004", "1.00")]
    [InlineData("-1.005", "-1.01")]
    [InlineData("2.675", "2.68")]
    public void Round_is_half_away_from_zero(string value, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            Money.Round(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData(12450.5, false, "12,450.50")]
    [InlineData(12450.5, true, "Rs 12,450.50")]
    [InlineData(0, true, "Rs 0.00")]
    [InlineData(-100, true, "−Rs 100.00")]
    public void Format_always_two_decimals_and_grouping(double value, bool symbol, string expected) =>
        Assert.Equal(expected, Money.Format((decimal)value, symbol));

    [Theory]
    [InlineData("1250", 1250)]
    [InlineData("1,250.50", 1250.5)]
    [InlineData(" Rs 99.9 ", 99.9)]
    [InlineData("0", 0)]
    public void TryParse_reads_typed_amounts(string text, double expected)
    {
        Assert.True(Money.TryParse(text, out var v));
        Assert.Equal((decimal)expected, v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("1.005")]
    [InlineData("100000001")]
    public void TryParse_refuses_bad_amounts(string text) => Assert.False(Money.TryParse(text, out _));
}
