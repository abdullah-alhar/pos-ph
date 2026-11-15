namespace VetPos.Core.Stock;

/// <summary>What billing needs to know about one batch.</summary>
public sealed record BatchStock(int BatchId, DateOnly? ExpiryDate, int QtyOnHand, decimal SellingPrice, decimal CostPrice, DateTime ReceivedAt);

public sealed record BatchTake(int BatchId, int Qty, decimal UnitCost);

public sealed record FefoResult(IReadOnlyList<BatchTake> Takes, int Shortage)
{
    public bool Complete => Shortage == 0;
    public decimal CostTotal => Takes.Sum(t => t.UnitCost * t.Qty);
}

/// <summary>One price the item is on hand at (for the "which price?" picker).</summary>
public sealed record PriceOption(decimal Price, int QtyAvailable, DateOnly? EarliestExpiry);

public enum ExpiryStatus
{
    NoExpiry,
    Ok,
    ExpiringSoon,
    Expired,
}

/// <summary>
/// First expiry, first out. Billing takes units from the batch that expires first, then the next;
/// batches without an expiry come last; expired batches are never sold. Ties go to the batch
/// received first.
/// </summary>
public static class Fefo
{
    public const int ExpiringSoonDays = 30;

    public static ExpiryStatus StatusOf(DateOnly? expiry, DateOnly today) => expiry switch
    {
        null => ExpiryStatus.NoExpiry,
        var e when e < today => ExpiryStatus.Expired,
        var e when e <= today.AddDays(ExpiringSoonDays) => ExpiryStatus.ExpiringSoon,
        _ => ExpiryStatus.Ok,
    };

    /// <summary>Batches that may be sold today, in the order they are sold.</summary>
    public static IEnumerable<BatchStock> Sellable(IEnumerable<BatchStock> batches, DateOnly today) =>
        batches
            .Where(b => b.QtyOnHand > 0 && (b.ExpiryDate is null || b.ExpiryDate >= today))
            .OrderBy(b => b.ExpiryDate is null)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.ReceivedAt)
            .ThenBy(b => b.BatchId);

    /// <summary>
    /// Takes <paramref name="qty"/> units FEFO, only from batches at <paramref name="price"/> when
    /// given. <see cref="FefoResult.Shortage"/> is what could not be found.
    /// </summary>
    public static FefoResult Allocate(IEnumerable<BatchStock> batches, int qty, DateOnly today, decimal? price = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(qty);
        var takes = new List<BatchTake>();
        var left = qty;
        foreach (var b in Sellable(batches, today))
        {
            if (left == 0) break;
            if (price is { } p && b.SellingPrice != p) continue;
            var take = Math.Min(left, b.QtyOnHand);
            takes.Add(new BatchTake(b.BatchId, take, b.CostPrice));
            left -= take;
        }
        return new FefoResult(takes, left);
    }

    /// <summary>Units that may be sold today (not expired).</summary>
    public static int SellableQty(IEnumerable<BatchStock> batches, DateOnly today, decimal? price = null) =>
        Sellable(batches, today).Where(b => price is null || b.SellingPrice == price).Sum(b => b.QtyOnHand);

    /// <summary>Units on hand that have expired (must be taken off the shelf).</summary>
    public static int ExpiredQty(IEnumerable<BatchStock> batches, DateOnly today) =>
        batches.Where(b => b.QtyOnHand > 0 && b.ExpiryDate < today).Sum(b => b.QtyOnHand);

    /// <summary>The prices the item can be sold at today, the FEFO price first.</summary>
    public static IReadOnlyList<PriceOption> PriceOptions(IEnumerable<BatchStock> batches, DateOnly today) =>
        Sellable(batches, today)
            .GroupBy(b => b.SellingPrice)
            .Select(g => new PriceOption(g.Key, g.Sum(b => b.QtyOnHand), g.Min(b => b.ExpiryDate)))
            .ToList();
}
