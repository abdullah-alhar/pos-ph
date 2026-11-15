using System.Globalization;
using VetPos.Core.Domain;

namespace VetPos.Core.Pricing;

/// <summary>
/// A discount as typed. PerUnit: rupees off each unit. Amount: rupees off the line or bill.
/// Percent: 12.5 = 12.5 %.
/// </summary>
public sealed record Discount(DiscountType Type, decimal Value)
{
    /// <summary>
    /// "10%" → 10 % · "100" → <paramref name="amountType"/> (Rs off each unit on a line, Rs off the
    /// bill on the bill) · "" or "0" → no discount. Returns an error message for anything else.
    /// </summary>
    public static bool TryParse(string? text, DiscountType amountType, out Discount? discount, out string? error)
    {
        discount = null;
        error = null;
        var t = (text ?? "").Trim();
        if (t.Length == 0) return true;
        if (t.EndsWith('%'))
        {
            var number = t[..^1].Trim();
            if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var pct) || pct != Money.Round(pct))
            {
                error = "Type a percentage like 10% or 12.5%.";
                return false;
            }
            if (pct > 100)
            {
                error = "A discount cannot be more than 100%.";
                return false;
            }
            discount = pct == 0 ? null : new Discount(DiscountType.Percent, pct);
            return true;
        }
        if (!Money.TryParse(t, out var amount))
        {
            error = "Type an amount like 100 or a percentage like 10%.";
            return false;
        }
        discount = amount == 0 ? null : new Discount(amountType, amount);
        return true;
    }

    /// <summary>"10%", "Rs 100.00 each", "Rs 500.00".</summary>
    public string Describe() => Type switch
    {
        DiscountType.Percent => Value.ToString("0.##", CultureInfo.InvariantCulture) + "%",
        DiscountType.PerUnit => Money.Format(Value, true) + " each",
        _ => Money.Format(Value, true),
    };

    /// <summary>What to show in the input box when editing this discount again.</summary>
    public string ToInput() => Type == DiscountType.Percent
        ? Value.ToString("0.##", CultureInfo.InvariantCulture) + "%"
        : Money.ToInput(Value);
}
