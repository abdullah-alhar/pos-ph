using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VetPos.Data.Configuration;

/// <summary>
/// SQLite has no decimal type. Money is stored as a whole number of cents (INTEGER), which is
/// exact, sorts correctly and can be compared in SQL. Values with more than two decimals are a
/// bug (all rounding happens in <c>Money.Round</c>), so they are refused instead of rounded here.
/// </summary>
internal sealed class MoneyConverter() : ValueConverter<decimal, long>(v => ToCents(v), v => FromCents(v))
{
    internal static long ToCents(decimal value)
    {
        var cents = value * 100m;
        if (cents != decimal.Truncate(cents))
            throw new InvalidOperationException($"Money value {value} has more than two decimals.");
        return (long)cents;
    }

    internal static decimal FromCents(long cents) => cents / 100m;
}
