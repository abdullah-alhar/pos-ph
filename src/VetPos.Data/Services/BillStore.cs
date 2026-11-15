using Microsoft.EntityFrameworkCore;
using VetPos.Core.Billing;
using VetPos.Core.Domain;
using VetPos.Core.Rules;

namespace VetPos.Data.Services;

public sealed record HeldBillRow(int Id, string? Label, DateTime CreatedAtLocal, string HeldBy, int ItemCount, decimal Total, BillCart Cart);

/// <summary>
/// Held bills (customer stepped away) and the draft of the bill in progress (saved after every
/// change, so a power cut loses nothing).
/// </summary>
public sealed class BillStore(IDbContextFactory<VetPosDbContext> dbFactory, ShopClock clock)
{
    public async Task SaveDraftAsync(int userId, BillCart cart, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.DraftBills.SingleOrDefaultAsync(d => d.UserId == userId, ct);
        if (cart.Lines.Count == 0)
        {
            if (row is not null) db.DraftBills.Remove(row);
        }
        else
        {
            if (row is null) db.DraftBills.Add(row = new DraftBill { UserId = userId });
            row.CartJson = cart.ToJson();
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<BillCart?> LoadDraftAsync(int userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var json = await db.DraftBills.Where(d => d.UserId == userId).Select(d => d.CartJson).SingleOrDefaultAsync(ct);
        var cart = BillCart.FromJson(json);
        return cart is { Lines.Count: > 0 } ? cart : null;
    }

    public async Task ClearDraftAsync(int userId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.DraftBills.Where(d => d.UserId == userId).ExecuteDeleteAsync(ct);
    }

    public async Task HoldAsync(BillCart cart, string? label, Actor actor, CancellationToken ct = default)
    {
        if (cart.Lines.Count == 0) throw new PosException("There is nothing to hold.");
        var totals = cart.Totals();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.HeldBills.Add(new HeldBill
        {
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            CartJson = cart.ToJson(),
            Total = totals.Total,
            ItemCount = totals.UnitCount,
            CreatedById = actor.Id,
        });
        Audit.Add(db, actor, AuditActions.HoldBill, $"Bill held: {totals.UnitCount} item(s), {VetPos.Core.Pricing.Money.Format(totals.Total, true)}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HeldBillRow>> HeldAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.HeldBills.AsNoTracking().OrderBy(h => h.Id).ToListAsync(ct);
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        return rows.Select(h => new HeldBillRow(h.Id, h.Label, clock.ToLocal(h.CreatedAt), h.CreatedById is { } u && names.TryGetValue(u, out var n) ? n : "",
            h.ItemCount, h.Total, BillCart.FromJson(h.CartJson) ?? BillCart.New())).ToList();
    }

    public async Task<int> HeldCountAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.HeldBills.CountAsync(ct);
    }

    /// <summary>Takes a held bill back to the till (it leaves the held list in the same step).</summary>
    public async Task<BillCart> RecallAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.HeldBills.SingleOrDefaultAsync(h => h.Id == id, ct) ?? throw new PosException("That held bill is gone (someone recalled it).");
        db.HeldBills.Remove(row);
        await db.SaveChangesAsync(ct);
        return BillCart.FromJson(row.CartJson) ?? throw new PosException("That held bill could not be read.");
    }

    public async Task DiscardAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.HeldBills.Where(h => h.Id == id).ExecuteDeleteAsync(ct);
    }
}
