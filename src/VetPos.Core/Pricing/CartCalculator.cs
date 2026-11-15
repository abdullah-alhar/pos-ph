using VetPos.Core.Domain;
using VetPos.Core.Rules;

namespace VetPos.Core.Pricing;

public sealed record CartLineInput(string Key, decimal UnitPrice, int Qty, Discount? Discount = null);

public sealed record CartLineResult(
    string Key,
    decimal UnitPrice,
    int Qty,
    decimal Gross,
    decimal DiscountAmount,
    decimal BillDiscountShare,
    decimal LineTotal)
{
    /// <summary>The line after its own discount, before the bill discount.</summary>
    public decimal AfterLineDiscount => Gross - DiscountAmount;
}

public sealed record CartTotals(
    IReadOnlyList<CartLineResult> Lines,
    decimal Subtotal,
    decimal LineDiscountTotal,
    decimal BillDiscountAmount,
    decimal Total)
{
    public decimal DiscountTotal => LineDiscountTotal + BillDiscountAmount;
    public int UnitCount => Lines.Sum(l => l.Qty);
    public static CartTotals Empty { get; } = new([], 0, 0, 0, 0);
}

/// <summary>
/// The one and only bill calculation. The billing screen uses it to show totals and the save
/// runs it again, so the screen and the saved bill always agree to the cent.
/// Order: qty × price → line discount → bill discount (shared over the lines in proportion) → total.
/// No rounding of the total: Rs with cents.
/// </summary>
public static class CartCalculator
{
    public const int MaxQty = 100_000;

    public static CartTotals Calculate(IReadOnlyList<CartLineInput> lines, Discount? billDiscount)
    {
        var stage = new List<(CartLineInput Line, decimal Gross, decimal Discount)>(lines.Count);
        foreach (var line in lines)
        {
            CheckLine(line.UnitPrice, line.Qty);
            var gross = Money.Round(line.UnitPrice * line.Qty);
            var discount = LineDiscountAmount(line.UnitPrice, line.Qty, line.Discount);
            if (discount > gross)
                throw new PosException($"The discount ({Money.Format(discount, true)}) is more than the line total of {Money.Format(gross, true)}.");
            stage.Add((line, gross, discount));
        }

        var billBase = stage.Sum(s => s.Gross - s.Discount);
        var billAmount = BillDiscountAmount(billBase, billDiscount);
        if (billAmount > billBase)
            throw new PosException($"The bill discount ({Money.Format(billAmount, true)}) is more than the bill of {Money.Format(billBase, true)}.");

        var shares = Allocate(billAmount, stage.Select(s => s.Gross - s.Discount).ToList());
        var results = stage.Select((s, i) => new CartLineResult(
            s.Line.Key, s.Line.UnitPrice, s.Line.Qty, s.Gross, s.Discount, shares[i], s.Gross - s.Discount - shares[i])).ToList();

        return new CartTotals(
            results,
            results.Sum(r => r.Gross),
            results.Sum(r => r.DiscountAmount),
            billAmount,
            results.Sum(r => r.LineTotal));
    }

    /// <summary>Checks a line discount before it is put on the line; null = fine, else a message.</summary>
    public static string? CheckLineDiscount(decimal unitPrice, int qty, Discount? discount)
    {
        if (discount is null) return null;
        if (discount.Value < 0) return "A discount cannot be negative.";
        if (discount.Type == DiscountType.Percent && discount.Value > 100) return "A discount cannot be more than 100%.";
        var gross = Money.Round(unitPrice * qty);
        var amount = LineDiscountAmount(unitPrice, qty, discount);
        return amount > gross
            ? $"Discount is more than the line total of {Money.Format(gross, true)}."
            : null;
    }

    /// <summary>Checks a bill discount against the bill after line discounts.</summary>
    public static string? CheckBillDiscount(decimal billAfterLineDiscounts, Discount? discount)
    {
        if (discount is null) return null;
        if (discount.Value < 0) return "A discount cannot be negative.";
        if (discount.Type == DiscountType.Percent && discount.Value > 100) return "A discount cannot be more than 100%.";
        return BillDiscountAmount(billAfterLineDiscounts, discount) > billAfterLineDiscounts
            ? $"Discount is more than the bill of {Money.Format(billAfterLineDiscounts, true)}."
            : null;
    }

    public static decimal LineDiscountAmount(decimal unitPrice, int qty, Discount? discount)
    {
        if (discount is null || discount.Value <= 0) return 0;
        if (discount.Value != Money.Round(discount.Value)) throw new PosException("A discount can have at most two decimals.");
        return discount.Type switch
        {
            DiscountType.PerUnit => Money.Round(discount.Value * qty),
            DiscountType.Amount => discount.Value,
            DiscountType.Percent => PercentOf(Money.Round(unitPrice * qty), discount.Value),
            _ => 0,
        };
    }

    public static decimal BillDiscountAmount(decimal billBase, Discount? discount)
    {
        if (discount is null || discount.Value <= 0) return 0;
        if (discount.Value != Money.Round(discount.Value)) throw new PosException("A discount can have at most two decimals.");
        return discount.Type == DiscountType.Percent ? PercentOf(billBase, discount.Value) : discount.Value;
    }

    private static decimal PercentOf(decimal amount, decimal percent)
    {
        if (percent > 100) throw new PosException("A discount cannot be more than 100%.");
        return Money.Round(amount * percent / 100m);
    }

    private static void CheckLine(decimal unitPrice, int qty)
    {
        if (qty < 1) throw new PosException("Quantity must be at least 1.");
        if (qty > MaxQty) throw new PosException($"Quantity cannot be more than {MaxQty:N0}.");
        if (unitPrice < 0) throw new PosException("A price cannot be negative.");
        if (unitPrice > Money.Max) throw new PosException("That price is too large.");
        if (unitPrice != Money.Round(unitPrice)) throw new PosException("A price can have at most two decimals.");
    }

    /// <summary>
    /// Splits <paramref name="total"/> over <paramref name="weights"/> in proportion, in whole
    /// cents, so the parts always add up exactly (largest remainder; ties go to the earlier line).
    /// </summary>
    public static IReadOnlyList<decimal> Allocate(decimal total, IReadOnlyList<decimal> weights)
    {
        var result = new decimal[weights.Count];
        var sum = weights.Sum();
        if (total <= 0 || sum <= 0) return result;
        var totalCents = (long)(total * 100);
        var parts = new long[weights.Count];
        var remainders = new (int Index, decimal Remainder)[weights.Count];
        long used = 0;
        for (var i = 0; i < weights.Count; i++)
        {
            var exact = totalCents * weights[i] / sum;
            parts[i] = (long)decimal.Floor(exact);
            remainders[i] = (i, exact - parts[i]);
            used += parts[i];
        }
        foreach (var (index, _) in remainders.OrderByDescending(r => r.Remainder).ThenBy(r => r.Index).Take((int)(totalCents - used)))
            parts[index]++;
        for (var i = 0; i < parts.Length; i++) result[i] = parts[i] / 100m;
        return result;
    }
}
