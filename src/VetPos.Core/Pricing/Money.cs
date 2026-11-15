using System.Globalization;

namespace VetPos.Core.Pricing;

/// <summary>
/// Money is <see cref="decimal"/> in rupees with cents. This is the only place that rounds:
/// two decimals, half away from zero (0.005 → 0.01), like a calculator.
/// </summary>
public static class Money
{
    /// <summary>Largest amount accepted anywhere (one hundred million rupees).</summary>
    public const decimal Max = 100_000_000m;

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>12450.5 → "12,450.50"; with symbol → "Rs 12,450.50". Same on every Windows region.</summary>
    public static string Format(decimal value, bool withSymbol = false)
    {
        var text = Math.Abs(value).ToString("#,##0.00", CultureInfo.InvariantCulture);
        var sign = value < 0 ? "−" : "";
        return withSymbol ? $"{sign}Rs {text}" : sign + text;
    }

    /// <summary>
    /// Reads what was typed: "1250", "1,250.50", "Rs 1250". Refuses negatives, more than two
    /// decimals and amounts above <see cref="Max"/>.
    /// </summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var cleaned = text.Trim();
        if (cleaned.StartsWith("Rs", StringComparison.OrdinalIgnoreCase)) cleaned = cleaned[2..].Trim();
        cleaned = cleaned.Replace(",", "", StringComparison.Ordinal);
        if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)) return false;
        if (v != Round(v) || v > Max) return false;
        value = v;
        return true;
    }

    /// <summary>Text for an input box: 1250.5 → "1250.50", 1250 → "1250".</summary>
    public static string ToInput(decimal value) =>
        value == decimal.Truncate(value)
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.00", CultureInfo.InvariantCulture);
}
