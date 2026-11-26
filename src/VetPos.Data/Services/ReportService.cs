using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;

namespace VetPos.Data.Services;

public sealed record AmountRow(string Label, int Bills, decimal Amount);

public sealed record SalesSummary(
    DateOnly From, DateOnly To, int Bills, int VoidedBills, int Units, decimal Subtotal, decimal Discounts, decimal Sales,
    decimal Cost, IReadOnlyList<AmountRow> ByPayment, IReadOnlyList<AmountRow> ByCashier)
{
    public decimal Profit => Sales - Cost;
    public decimal AverageBill => Bills == 0 ? 0 : Money.Round(Sales / Bills);
    public decimal MarginPercent => Sales == 0 ? 0 : Math.Round(Profit / Sales * 100, 1);
}

public sealed record DayRow(DateOnly Day, int Bills, decimal Sales, decimal Discounts, decimal Cost)
{
    public decimal Profit => Sales - Cost;
}

/// <summary>How the sales graph groups bills: per hour (one day), per day, or per month (long ranges).</summary>
public enum SalesGrouping { Hour, Day, Month }

/// <summary>One bar of the sales graph / one row of the table under it.</summary>
public sealed record PeriodRow(DateTime Start, string Label, string LongLabel, int Bills, decimal Sales, decimal Discounts, decimal Cost, int Units = 0)
{
    public decimal Profit => Sales - Cost;
}

public sealed record ProductSalesRow(int ProductId, string Name, int Units, decimal Sales, decimal Cost)
{
    public decimal Profit => Sales - Cost;
    public decimal MarginPercent => Sales == 0 ? 0 : Math.Round(Profit / Sales * 100, 1);
}

public sealed record StockRow(int ProductId, string Name, string Category, int Qty, int ExpiredQty, decimal ValueAtCost, decimal ValueAtPrice, int ReorderLevel);

public sealed record ChangeLogRow(DateTime AtLocal, string Action, string User, string Summary, string? OldValue, string? NewValue);

/// <summary>Reports. Voided bills never count as sales (they are counted separately).</summary>
public sealed class ReportService(IDbContextFactory<VetPosDbContext> dbFactory, ShopClock clock)
{
    public async Task<(int Bills, decimal Sales)> OverviewAsync(DateOnly day, CancellationToken ct = default)
    {
        var (from, to) = clock.Range(day, day);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var totals = await db.Sales.Where(s => s.Status == SaleStatus.Completed && s.CreatedAt >= from && s.CreatedAt < to)
            .Select(s => s.Total).ToListAsync(ct);
        return (totals.Count, totals.Sum());
    }

    private async Task<List<Sale>> SalesAsync(VetPosDbContext db, DateOnly from, DateOnly to, bool withLines, CancellationToken ct)
    {
        var (f, t) = clock.Range(from, to);
        IQueryable<Sale> q = db.Sales.AsNoTracking().Include(s => s.Payments).Include(s => s.Cashier);
        if (withLines) q = q.Include(s => s.Lines);
        return await q.Where(s => s.CreatedAt >= f && s.CreatedAt < t).ToListAsync(ct);
    }

    /// <summary>
    /// Totals for a period. With <paramref name="productId"/>: only that product's lines (its sales,
    /// units, discounts, cost and profit; bills = bills that had it). Payment split is left empty then,
    /// because a payment covers the whole bill.
    /// </summary>
    public async Task<SalesSummary> SummaryAsync(DateOnly from, DateOnly to, int? productId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var all = await SalesAsync(db, from, to, withLines: true, ct);
        var sales = all.Where(s => s.Status == SaleStatus.Completed).ToList();
        if (productId is { } pid)
        {
            var withIt = sales.Select(s => (Sale: s, Lines: s.Lines.Where(l => l.ProductId == pid).ToList())).Where(x => x.Lines.Count > 0).ToList();
            var lines = withIt.SelectMany(x => x.Lines).ToList();
            var cashiers = withIt.GroupBy(x => x.Sale.Cashier?.FullName ?? "?").OrderByDescending(g => g.Sum(x => x.Lines.Sum(l => l.LineTotal)))
                .Select(g => new AmountRow(g.Key, g.Count(), g.Sum(x => x.Lines.Sum(l => l.LineTotal)))).ToList();
            return new SalesSummary(from, to, withIt.Count, all.Count(s => s.Status != SaleStatus.Completed && s.Lines.Any(l => l.ProductId == pid)),
                lines.Sum(l => l.Qty), lines.Sum(l => l.ListPrice * l.Qty), lines.Sum(l => l.DiscountAmount + l.BillDiscountShare),
                lines.Sum(l => l.LineTotal), lines.Sum(l => l.CostTotal), [], cashiers);
        }
        var byPayment = sales.SelectMany(s => s.Payments.Select(p => (s.Id, p.Method, p.Amount)))
            .GroupBy(x => x.Method).OrderBy(g => g.Key)
            .Select(g => new AmountRow(SaleService.MethodName(g.Key), g.Select(x => x.Id).Distinct().Count(), g.Sum(x => x.Amount))).ToList();
        var byCashier = sales.GroupBy(s => s.Cashier?.FullName ?? "?").OrderByDescending(g => g.Sum(s => s.Total))
            .Select(g => new AmountRow(g.Key, g.Count(), g.Sum(s => s.Total))).ToList();
        return new SalesSummary(from, to, sales.Count, all.Count - sales.Count, sales.Sum(s => s.Lines.Sum(l => l.Qty)),
            sales.Sum(s => s.Subtotal), sales.Sum(s => s.LineDiscountTotal + s.BillDiscountAmount), sales.Sum(s => s.Total),
            sales.Sum(s => s.CostTotal), byPayment, byCashier);
    }

    public async Task<IReadOnlyList<DayRow>> ByDayAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sales = (await SalesAsync(db, from, to, withLines: false, ct)).Where(s => s.Status == SaleStatus.Completed).ToList();
        var byDay = sales.GroupBy(s => DateOnly.FromDateTime(clock.ToLocal(s.CreatedAt))).ToDictionary(g => g.Key);
        if (to < from) (from, to) = (to, from);
        var rows = new List<DayRow>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var g = byDay.GetValueOrDefault(d);
            rows.Add(g is null ? new DayRow(d, 0, 0, 0, 0)
                : new DayRow(d, g.Count(), g.Sum(s => s.Total), g.Sum(s => s.LineDiscountTotal + s.BillDiscountAmount), g.Sum(s => s.CostTotal)));
        }
        return rows;
    }

    /// <summary>The grouping that suits a date range: hours for one day, days up to three months, then months.</summary>
    public static SalesGrouping GroupingFor(DateOnly from, DateOnly to) =>
        from == to ? SalesGrouping.Hour : Math.Abs(to.DayNumber - from.DayNumber) <= 92 ? SalesGrouping.Day : SalesGrouping.Month;

    /// <summary>
    /// Completed sales grouped for the graph. Every hour / day / month in the range is listed, even
    /// with no sales, so the graph has no gaps. Hours run from the first to the last trading hour
    /// (at least 8:00 to 20:00).
    /// </summary>
    public async Task<IReadOnlyList<PeriodRow>> SalesByPeriodAsync(DateOnly from, DateOnly to, SalesGrouping grouping, int? productId = null, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sales = (await SalesAsync(db, from, to, withLines: true, ct)).Where(s => s.Status == SaleStatus.Completed)
            .Where(s => productId is null || s.Lines.Any(l => l.ProductId == productId))
            .Select(s => (At: clock.ToLocal(s.CreatedAt), Sale: s)).ToList();
        // One product: count only its lines. Whole shop: the bill totals.
        IEnumerable<SaleLine> Lines(Sale s) => s.Lines.Where(l => productId is null || l.ProductId == productId);
        decimal Amount(Sale s) => productId is null ? s.Total : Lines(s).Sum(l => l.LineTotal);
        decimal Discount(Sale s) => productId is null ? s.LineDiscountTotal + s.BillDiscountAmount : Lines(s).Sum(l => l.DiscountAmount + l.BillDiscountShare);
        decimal Cost(Sale s) => productId is null ? s.CostTotal : Lines(s).Sum(l => l.CostTotal);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        DateTime Key(DateTime at) => grouping switch
        {
            SalesGrouping.Hour => at.Date.AddHours(at.Hour),
            SalesGrouping.Day => at.Date,
            _ => new DateTime(at.Year, at.Month, 1),
        };
        var groups = sales.GroupBy(x => Key(x.At)).ToDictionary(g => g.Key, g => g.Select(x => x.Sale).ToList());

        var keys = new List<DateTime>();
        switch (grouping)
        {
            case SalesGrouping.Hour:
                var day = from.ToDateTime(TimeOnly.MinValue);
                var first = Math.Min(8, groups.Count == 0 ? 8 : groups.Keys.Min(k => k.Hour));
                var last = Math.Max(20, groups.Count == 0 ? 20 : groups.Keys.Max(k => k.Hour));
                for (var h = first; h <= last; h++) keys.Add(day.AddHours(h));
                break;
            case SalesGrouping.Day:
                for (var d = from; d <= to; d = d.AddDays(1)) keys.Add(d.ToDateTime(TimeOnly.MinValue));
                break;
            default:
                for (var m = new DateTime(from.Year, from.Month, 1); m <= to.ToDateTime(TimeOnly.MinValue); m = m.AddMonths(1)) keys.Add(m);
                break;
        }
        var spansYears = from.Year != to.Year;
        return keys.Select(k =>
        {
            var (label, longLabel) = grouping switch
            {
                SalesGrouping.Hour => (k.ToString("h tt", inv).ToLowerInvariant(), $"{k:HH:mm} - {k.AddHours(1):HH:mm}"),
                SalesGrouping.Day => (Math.Abs(to.DayNumber - from.DayNumber) <= 7 ? k.ToString("ddd d", inv) : k.ToString("d MMM", inv), k.ToString("ddd, dd MMM yyyy", inv)),
                _ => (k.ToString(spansYears ? "MMM yy" : "MMM", inv), k.ToString("MMMM yyyy", inv)),
            };
            var g = groups.GetValueOrDefault(k);
            return g is null ? new PeriodRow(k, label, longLabel, 0, 0, 0, 0)
                : new PeriodRow(k, label, longLabel, g.Count, g.Sum(Amount), g.Sum(Discount), g.Sum(Cost), g.Sum(s => Lines(s).Sum(l => l.Qty)));
        }).ToList();
    }

    /// <summary>Sales and profit per product. Line totals already include the bill discount share.</summary>
    public async Task<IReadOnlyList<ProductSalesRow>> ByProductAsync(DateOnly from, DateOnly to, int? top = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var sales = (await SalesAsync(db, from, to, withLines: true, ct)).Where(s => s.Status == SaleStatus.Completed);
        var rows = sales.SelectMany(s => s.Lines).GroupBy(l => l.ProductId)
            .Select(g => new ProductSalesRow(g.Key, g.OrderByDescending(l => l.Id).First().ProductName, g.Sum(l => l.Qty), g.Sum(l => l.LineTotal), g.Sum(l => l.CostTotal)))
            .OrderByDescending(r => r.Sales).ThenBy(r => r.Name);
        return (top is { } n ? rows.Take(n) : rows).ToList();
    }

    /// <summary>Stock on hand and its value, from each batch's own cost.</summary>
    public async Task<IReadOnlyList<StockRow>> StockAsync(CancellationToken ct = default)
    {
        var today = clock.Today;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var products = await db.Products.AsNoTracking().Include(p => p.Category).Include(p => p.Batches.Where(b => b.QtyOnHand > 0))
            .Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(ct);
        return products.Select(p => new StockRow(p.Id, p.Name, p.Category?.Name ?? "", p.StockQty,
            p.Batches.Where(b => b.ExpiryDate < today).Sum(b => b.QtyOnHand),
            p.Batches.Sum(b => b.CostPrice * b.QtyOnHand), p.Batches.Sum(b => b.SellingPrice * b.QtyOnHand), p.ReorderLevel)).ToList();
    }

    /// <summary>Price changes, discounts, voids and stock edits: who, when, old → new.</summary>
    public async Task<IReadOnlyList<ChangeLogRow>> ChangesAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        string[] actions =
        [
            AuditActions.PriceChange, AuditActions.LineDiscount, AuditActions.BillDiscount, AuditActions.SaleVoid, AuditActions.SaleAboveStock,
            AuditActions.ProductPrice, AuditActions.BatchPrice, AuditActions.StockAdjust,
        ];
        var (f, t) = clock.Range(from, to);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.AuditLog.AsNoTracking().Where(a => actions.Contains(a.Action) && a.CreatedAt >= f && a.CreatedAt < t)
            .OrderByDescending(a => a.Id).Take(5000).ToListAsync(ct);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return rows.Select(a => new ChangeLogRow(clock.ToLocal(a.CreatedAt), ActionName(a.Action),
            a.CreatedById is { } u && names.TryGetValue(u, out var n) ? n : "", a.Summary, a.OldValue, a.NewValue)).ToList();
    }

    /// <summary>Everything that happened (activity log): sign-ins, sales, stock, settings, backups …</summary>
    public async Task<IReadOnlyList<ChangeLogRow>> ActivityAsync(DateOnly from, DateOnly to, string? search = null, CancellationToken ct = default)
    {
        var (f, t) = clock.Range(from, to);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.AuditLog.AsNoTracking().Where(a => a.CreatedAt >= f && a.CreatedAt < t);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(a => EF.Functions.Like(a.Summary, "%" + search.Trim() + "%"));
        var rows = await q.OrderByDescending(a => a.Id).Take(5000).ToListAsync(ct);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return rows.Select(a => new ChangeLogRow(clock.ToLocal(a.CreatedAt), ActionName(a.Action),
            a.CreatedById is { } u && names.TryGetValue(u, out var n) ? n : "System", a.Summary, a.OldValue, a.NewValue)).ToList();
    }

    public static string ActionName(string action) => action switch
    {
        AuditActions.PriceChange => "Price changed on bill",
        AuditActions.LineDiscount => "Item discount",
        AuditActions.BillDiscount => "Bill discount",
        AuditActions.SaleVoid => "Bill voided",
        AuditActions.SaleAboveStock => "Sold without stock",
        AuditActions.ProductPrice => "Product price",
        AuditActions.BatchPrice => "Batch price",
        AuditActions.StockAdjust => "Stock adjusted",
        AuditActions.SaleCreate => "Bill saved",
        AuditActions.StockReceive => "Stock received",
        AuditActions.ProductSave => "Product saved",
        AuditActions.UserSave => "User changed",
        AuditActions.Login => "Signed in",
        AuditActions.LoginFailed => "Wrong PIN",
        AuditActions.SettingsChange => "Settings changed",
        AuditActions.Backup => "Backup",
        AuditActions.Restore => "Restore",
        AuditActions.HoldBill => "Bill held",
        _ => action,
    };
}
