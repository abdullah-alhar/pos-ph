using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;
using VetPos.Core.Rules;

namespace VetPos.Data.Services;

public enum NoticeLevel { Info, Warning, Danger }

/// <summary>Where a notice leads when clicked.</summary>
public enum NoticeTarget { LowStock, Backups, HeldBills, Changes }

public sealed record Notice(string Key, NoticeLevel Level, string Title, string Detail, NoticeTarget Target);

/// <summary>
/// The notifications bell: things the shop should act on, worked out from the data each time
/// (nothing to get out of date): low / out-of-stock items, a missed backup, bills held
/// too long, and today's price changes and discounts (owner).
/// </summary>
public sealed class NotificationService(IDbContextFactory<VetPosDbContext> dbFactory, ShopClock clock, CatalogService catalog)
{
    public async Task<IReadOnlyList<Notice>> ListAsync(Actor actor, CancellationToken ct = default)
    {
        var notices = new List<Notice>();
        var today = clock.Today;

        var low = await catalog.LowStockAsync(ct);
        var outOf = low.Where(l => l.StockQty <= 0).ToList();
        if (outOf.Count > 0)
            notices.Add(new("out", NoticeLevel.Danger, $"{outOf.Count} product{(outOf.Count == 1 ? "" : "s")} out of stock", Names(outOf.Select(l => l.Name)), NoticeTarget.LowStock));
        var lowOnly = low.Where(l => l.StockQty > 0).ToList();
        if (lowOnly.Count > 0)
            notices.Add(new("low", NoticeLevel.Warning, $"{lowOnly.Count} product{(lowOnly.Count == 1 ? "" : "s")} running low", Names(lowOnly.Select(l => $"{l.Name} ({l.StockQty})")), NoticeTarget.LowStock));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var oldHeld = await db.HeldBills.CountAsync(h => h.CreatedAt < clock.UtcNow.AddHours(-2), ct);
        if (oldHeld > 0)
            notices.Add(new("held", NoticeLevel.Info, $"{oldHeld} bill{(oldHeld == 1 ? " has" : "s have")} been on hold for over 2 hours",
                "Recall and finish them, or discard them (F9 on the billing screen).", NoticeTarget.HeldBills));

        if (actor.IsOwner)
        {
            var lastBackup = await db.Backups.Where(b => b.Verified).OrderByDescending(b => b.Id).Select(b => (DateTime?)b.CreatedAt).FirstOrDefaultAsync(ct);
            if (lastBackup is null || lastBackup < clock.UtcNow.AddDays(-2))
                notices.Add(new("backup", NoticeLevel.Danger, lastBackup is null ? "No backup yet" : "No backup for more than 2 days",
                    "Make a backup now (Settings → Backups) so the shop's data is safe.", NoticeTarget.Backups));

            var (f, t) = clock.Range(today, today);
            string[] changeActions = [AuditActions.PriceChange, AuditActions.LineDiscount, AuditActions.BillDiscount, AuditActions.SaleVoid];
            var changes = await db.AuditLog.CountAsync(a => changeActions.Contains(a.Action) && a.CreatedAt >= f && a.CreatedAt < t, ct);
            if (changes > 0)
                notices.Add(new("changes", NoticeLevel.Info, $"{changes} price change{(changes == 1 ? "" : "s")}, discount{(changes == 1 ? "" : "s")} or void{(changes == 1 ? "" : "s")} today",
                    "See who gave them in Reports → Discounts & price changes.", NoticeTarget.Changes));
        }
        return notices;
    }

    private static string Names(IEnumerable<string> names)
    {
        var list = names.Distinct().ToList();
        return list.Count <= 3 ? string.Join(", ", list) : string.Join(", ", list.Take(3)) + $" and {list.Count - 3} more";
    }
}
