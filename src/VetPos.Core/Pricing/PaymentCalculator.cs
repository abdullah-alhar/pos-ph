using VetPos.Core.Domain;

namespace VetPos.Core.Pricing;

public sealed record PaymentInput(PaymentMethod Method, decimal Amount, string? Reference = null);

/// <summary>A payment as it is saved: what it paid towards the bill, and what was handed over.</summary>
public sealed record AppliedPayment(PaymentMethod Method, decimal Amount, decimal Tendered, string? Reference);

public sealed record PaymentSummary(
    IReadOnlyList<AppliedPayment> Applied,
    decimal Paid,
    decimal Remaining,
    decimal Change,
    string? Error)
{
    public bool Complete => Error is null && Remaining == 0;
}

/// <summary>
/// Cash, card, bank transfer or a mix. Card and bank are exact and cannot be more than what is
/// still due; cash covers the rest and the difference is change.
/// </summary>
public static class PaymentCalculator
{
    public static PaymentSummary Summarize(decimal total, IReadOnlyList<PaymentInput> payments)
    {
        foreach (var p in payments)
        {
            if (p.Amount <= 0) return Fail(total, "Every payment must be more than zero.");
            if (p.Amount != Money.Round(p.Amount)) return Fail(total, "A payment can have at most two decimals.");
            if (p.Amount > Money.Max) return Fail(total, "That amount is too large. Check it again.");
        }

        var nonCash = payments.Where(p => p.Method != PaymentMethod.Cash).Sum(p => p.Amount);
        if (nonCash > total)
            return Fail(total, $"Card and bank payments cannot be more than the bill of {Money.Format(total, true)}.");

        var applied = payments.Where(p => p.Method != PaymentMethod.Cash)
            .Select(p => new AppliedPayment(p.Method, p.Amount, p.Amount, Clean(p.Reference)))
            .ToList();

        var cashGiven = payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount);
        var cashNeeded = total - nonCash;
        var cashKept = Math.Min(cashGiven, cashNeeded);
        if (cashKept > 0) applied.Add(new AppliedPayment(PaymentMethod.Cash, cashKept, cashGiven, null));

        var paid = nonCash + cashKept;
        return new PaymentSummary(applied, paid, total - paid, Math.Max(0, cashGiven - cashNeeded), null);
    }

    private static PaymentSummary Fail(decimal total, string error) => new([], 0, total, 0, error);

    private static string? Clean(string? reference) =>
        string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();

    /// <summary>Quick "customer gave" amounts: exact, then the next 500 / 1,000 / 5,000.</summary>
    public static IReadOnlyList<decimal> QuickCash(decimal due)
    {
        if (due <= 0) return [];
        var options = new SortedSet<decimal> { due };
        foreach (var step in new[] { 100m, 500m, 1000m, 5000m, 10000m })
        {
            var up = Math.Ceiling(due / step) * step;
            if (up > due) options.Add(up);
        }
        return options.Take(5).ToList();
    }
}
