using Microsoft.EntityFrameworkCore;
using VetPos.Core.Billing;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;
using VetPos.Core.Settings;
using VetPos.Core.Stock;

namespace VetPos.Data.Services;

/// <summary>What billing needs to put an item on the bill.</summary>
public sealed record BillingItem(
    int Id, string Name, string? Barcode, ProductUnit Unit, PriceChoice PriceChoice, decimal ProductPrice,
    IReadOnlyList<BatchStock> Batches, DateOnly Today)
{
    public int SellableQty => Fefo.SellableQty(Batches, Today);
    public int ExpiredQty => Fefo.ExpiredQty(Batches, Today);
    public IReadOnlyList<PriceOption> PriceOptions => Fefo.PriceOptions(Batches, Today);

    /// <summary>The price billing uses when nobody chooses: the batch that sells first, else the product price.</summary>
    public decimal DefaultPrice => PriceOptions.Count > 0 ? PriceOptions[0].Price : ProductPrice;

    /// <summary>Stock is on hand at more than one price today.</summary>
    public bool HasSeveralPrices => PriceOptions.Count > 1;

    public DateOnly? NextExpiry => Fefo.Sellable(Batches, Today).FirstOrDefault()?.ExpiryDate;
}

public sealed record CheckoutRequest(BillCart Cart, IReadOnlyList<PaymentInput> Payments, string? Note = null);

public sealed record CheckoutResult(int SaleId, int Number, decimal Total, decimal Change, bool Duplicate, IReadOnlyList<string> Warnings);

public sealed record SaleLineView(int LineNo, string Name, string? Barcode, int Qty, decimal ListPrice, decimal UnitPrice,
    DiscountType? DiscountType, decimal DiscountValue, decimal DiscountAmount, decimal LineTotal, decimal CostTotal, int ProductId = 0)
{
    public bool PriceChanged => UnitPrice != ListPrice;
    public decimal Gross => UnitPrice * Qty;
}

public sealed record PaymentView(PaymentMethod Method, decimal Amount, decimal Tendered, string? Reference);

public sealed record SaleView(
    int Id, int Number, SaleStatus Status, DateTime CreatedAtLocal, string Cashier, decimal Subtotal, decimal LineDiscountTotal,
    DiscountType? BillDiscountType, decimal BillDiscountValue, decimal BillDiscountAmount, decimal Total, decimal Paid, decimal Change,
    decimal CostTotal, string? Note, IReadOnlyList<SaleLineView> Lines, IReadOnlyList<PaymentView> Payments,
    DateTime? VoidedAtLocal, string? VoidedBy, string? VoidReason)
{
    public decimal DiscountTotal => LineDiscountTotal + BillDiscountAmount;
    public int UnitCount => Lines.Sum(l => l.Qty);
    public decimal Profit => Total - CostTotal;
}

public sealed record BillRow(int Id, int Number, DateTime CreatedAtLocal, string Cashier, int Units, decimal Total, string Methods, SaleStatus Status);

/// <summary>Saving bills, voiding them, and reading them back.</summary>
public sealed class SaleService(IDbContextFactory<VetPosDbContext> dbFactory, ShopClock clock, SettingsService settings)
{
    public const int FirstBillNumber = 1001;

    // ───────── Lookup for billing ─────────

    public async Task<BillingItem?> GetBillingItemAsync(int productId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.Products.AsNoTracking().Include(x => x.Batches.Where(b => b.QtyOnHand > 0))
            .SingleOrDefaultAsync(x => x.Id == productId && x.IsActive, ct);
        return p is null ? null : new BillingItem(p.Id, p.Name, p.Barcode, p.Unit, p.PriceChoice, p.SellingPrice,
            p.Batches.Select(CatalogService.ToStock).ToList(), clock.Today);
    }

    // ───────── Checkout ─────────

    /// <summary>
    /// Saves a bill: everything (number, lines, batch use, stock, payments, audit) in one
    /// transaction, so a bill is completely saved or not at all. Sending the same bill again
    /// (same ClientId: double key press, retry) returns the bill saved the first time.
    /// </summary>
    public async Task<CheckoutResult> CheckoutAsync(CheckoutRequest request, Actor cashier, CancellationToken ct = default)
    {
        var cart = request.Cart;
        if (cart.Lines.Count == 0) throw new PosException("The bill is empty.");
        if (cart.Lines.Count > 300) throw new PosException("A bill can have at most 300 lines.");

        await using (var check = await dbFactory.CreateDbContextAsync(ct))
        {
            var existing = await check.Sales.AsNoTracking().SingleOrDefaultAsync(s => s.ClientId == cart.ClientId, ct);
            if (existing is not null) return new CheckoutResult(existing.Id, existing.Number, existing.Total, existing.Change, true, []);
        }

        var totals = cart.Totals();
        var pay = PaymentCalculator.Summarize(totals.Total, request.Payments);
        if (pay.Error is not null) throw new PosException(pay.Error);
        if (!pay.Complete) throw new PosException($"The bill is not fully paid. Still to pay: {Money.Format(pay.Remaining, true)}.");

        var billing = await settings.GetAsync<BillingSettings>(BillingSettings.Key, ct);
        var today = clock.Today;
        var warnings = new List<string>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var ids = cart.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.Include(p => p.Batches).Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var l in cart.Lines)
            if (!products.TryGetValue(l.ProductId, out var p) || !p.IsActive)
                throw new PosException($"\"{l.Name}\" can no longer be sold. Remove it from the bill.");

        // Plan the batches for every line first (units already planned are not used twice).
        var left = products.Values.SelectMany(p => p.Batches).ToDictionary(b => b.Id, b => b.QtyOnHand);
        var plans = new List<(BillLine Line, List<BatchTake> Takes, int Unbatched)>();
        foreach (var line in cart.Lines)
        {
            var p = products[line.ProductId];
            var stock = p.Batches.Select(b => new BatchStock(b.Id, b.ExpiryDate, left[b.Id], b.SellingPrice, b.CostPrice, b.CreatedAt));
            var plan = Fefo.Allocate(stock, line.Qty, today, line.PriceGroup);
            foreach (var t in plan.Takes) left[t.BatchId] -= t.Qty;
            if (plan.Shortage > 0)
            {
                var have = line.Qty - plan.Shortage;
                if (!billing.AllowSellAboveStock)
                {
                    var expired = Fefo.ExpiredQty(p.Batches.Select(CatalogService.ToStock), today);
                    var at = line.PriceGroup is { } g ? $" at {Money.Format(g, true)}" : "";
                    throw new PosException(have <= 0
                        ? $"No stock left{at} for {p.Name}." + (expired > 0 ? $" {expired} expired — take them off the shelf." : "")
                        : $"Not enough stock for {p.Name}: only {have} left{at}." + (expired > 0 ? $" ({expired} more expired.)" : ""));
                }
                warnings.Add($"{p.Name}: {plan.Shortage} sold without stock.");
            }
            plans.Add((line, plan.Takes.ToList(), plan.Shortage));
        }

        var number = await Counters.NextAsync(db, Counters.Bill, FirstBillNumber, ct);
        var sale = new Sale
        {
            ClientId = cart.ClientId,
            Number = number,
            CashierId = cashier.Id,
            Subtotal = totals.Subtotal,
            LineDiscountTotal = totals.LineDiscountTotal,
            BillDiscountType = cart.BillDiscount?.Type,
            BillDiscountValue = cart.BillDiscount?.Value ?? 0,
            BillDiscountAmount = totals.BillDiscountAmount,
            Total = totals.Total,
            Paid = pay.Paid,
            Change = pay.Change,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
        };
        for (var i = 0; i < plans.Count; i++)
        {
            var (line, takes, unbatched) = plans[i];
            var p = products[line.ProductId];
            var r = totals.Lines[i];
            var cost = takes.Sum(t => t.UnitCost * t.Qty) + p.CostPrice * unbatched;
            sale.Lines.Add(new SaleLine
            {
                LineNo = i + 1, ProductId = p.Id, ProductName = p.Name, Barcode = p.Barcode, Qty = line.Qty,
                ListPrice = line.ListPrice, UnitPrice = line.UnitPrice,
                DiscountType = line.Discount?.Type, DiscountValue = line.Discount?.Value ?? 0, DiscountAmount = r.DiscountAmount,
                BillDiscountShare = r.BillDiscountShare, LineTotal = r.LineTotal, CostTotal = Money.Round(cost),
                Batches = takes.Select(t => new SaleLineBatch { BatchId = t.BatchId, Qty = t.Qty, UnitCost = t.UnitCost }).ToList(),
            });
        }
        sale.CostTotal = sale.Lines.Sum(l => l.CostTotal);
        foreach (var a in pay.Applied)
            sale.Payments.Add(new Payment { Method = a.Method, Amount = a.Amount, Tendered = a.Tendered, Reference = a.Reference });
        db.Sales.Add(sale);
        await db.SaveChangesAsync(ct);

        // Stock out, batch by batch, with a movement for each.
        var batches = products.Values.SelectMany(p => p.Batches).ToDictionary(b => b.Id);
        foreach (var (line, takes, unbatched) in plans)
        {
            var p = products[line.ProductId];
            foreach (var t in takes)
            {
                batches[t.BatchId].QtyOnHand -= t.Qty;
                p.StockQty -= t.Qty;
                db.StockMovements.Add(new StockMovement { ProductId = p.Id, BatchId = t.BatchId, Change = -t.Qty, QtyAfter = p.StockQty, Reason = StockReason.Sale, Reference = $"Bill {number}" });
            }
            if (unbatched > 0)
            {
                p.StockQty -= unbatched;
                db.StockMovements.Add(new StockMovement { ProductId = p.Id, Change = -unbatched, QtyAfter = p.StockQty, Reason = StockReason.Sale, Reference = $"Bill {number}", Note = "Sold without stock" });
                Audit.Add(db, cashier, AuditActions.SaleAboveStock, $"Bill {number}: {p.Name} × {unbatched} sold without stock", "Sale", sale.Id);
            }
        }

        // Every price change and discount: who, when, old → new.
        foreach (var l in sale.Lines)
        {
            if (l.UnitPrice != l.ListPrice)
                Audit.Add(db, cashier, AuditActions.PriceChange,
                    $"Bill {number}: price of {l.ProductName} {Money.Format(l.ListPrice)} → {Money.Format(l.UnitPrice)} (× {l.Qty})",
                    "Sale", sale.Id, Money.Format(l.ListPrice), Money.Format(l.UnitPrice));
            if (l.DiscountAmount > 0)
                Audit.Add(db, cashier, AuditActions.LineDiscount,
                    $"Bill {number}: {l.ProductName} discount {new Discount(l.DiscountType!.Value, l.DiscountValue).Describe()} = {Money.Format(l.DiscountAmount, true)}",
                    "Sale", sale.Id, Money.Format(l.UnitPrice * l.Qty), Money.Format(l.UnitPrice * l.Qty - l.DiscountAmount));
        }
        if (sale.BillDiscountAmount > 0)
            Audit.Add(db, cashier, AuditActions.BillDiscount,
                $"Bill {number}: bill discount {cart.BillDiscount!.Describe()} = {Money.Format(sale.BillDiscountAmount, true)}",
                "Sale", sale.Id, Money.Format(sale.Subtotal - sale.LineDiscountTotal), Money.Format(sale.Total));
        Audit.Add(db, cashier, AuditActions.SaleCreate,
            $"Bill {number}: {Money.Format(sale.Total, true)} ({string.Join(" + ", sale.Payments.Select(x => MethodName(x.Method)).Distinct())})",
            "Sale", sale.Id);

        await db.DraftBills.Where(d => d.UserId == cashier.Id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new CheckoutResult(sale.Id, number, sale.Total, sale.Change, false, warnings);
    }

    // ───────── Void ─────────

    /// <summary>
    /// Cancels a bill: it stays in the records marked Voided (with reason, who and who approved),
    /// and its units go back into the batches they came from.
    /// </summary>
    public async Task VoidAsync(int saleId, string reason, Actor actor, Actor? approvedBy, CancellationToken ct = default)
    {
        if (!actor.IsOwner && approvedBy is not { IsOwner: true }) throw new PosException("Voiding a bill needs the owner's PIN.");
        if (string.IsNullOrWhiteSpace(reason)) throw new PosException("Write why the bill is voided.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sale = await db.Sales.Include(s => s.Lines).ThenInclude(l => l.Batches).SingleOrDefaultAsync(s => s.Id == saleId, ct)
            ?? throw new PosException("That bill no longer exists.");
        if (sale.Status == SaleStatus.Voided) throw new PosException($"Bill {sale.Number} is already voided.");

        sale.Status = SaleStatus.Voided;
        sale.VoidedAt = clock.UtcNow;
        sale.VoidedById = actor.Id;
        sale.VoidReason = reason.Trim();

        var ids = sale.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var batchIds = sale.Lines.SelectMany(l => l.Batches).Select(b => b.BatchId).ToList();
        var batches = await db.StockBatches.Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        foreach (var l in sale.Lines)
        {
            var p = products[l.ProductId];
            foreach (var b in l.Batches)
            {
                batches[b.BatchId].QtyOnHand += b.Qty;
                p.StockQty += b.Qty;
                db.StockMovements.Add(new StockMovement { ProductId = p.Id, BatchId = b.BatchId, Change = b.Qty, QtyAfter = p.StockQty, Reason = StockReason.SaleVoid, Reference = $"Void {sale.Number}" });
            }
            var unbatched = l.Qty - l.Batches.Sum(b => b.Qty);
            if (unbatched > 0)
            {
                p.StockQty += unbatched;
                db.StockMovements.Add(new StockMovement { ProductId = p.Id, Change = unbatched, QtyAfter = p.StockQty, Reason = StockReason.SaleVoid, Reference = $"Void {sale.Number}" });
            }
        }
        Audit.Add(db, actor, AuditActions.SaleVoid, $"Bill {sale.Number} voided ({Money.Format(sale.Total, true)}): {reason.Trim()}",
            "Sale", sale.Id, approvedById: approvedBy?.Id ?? (actor.IsOwner ? actor.Id : null));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ───────── Reading ─────────

    public async Task<SaleView?> GetAsync(int saleId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await db.Sales.AsNoTracking().Include(x => x.Cashier).Include(x => x.Lines).Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == saleId, ct);
        if (s is null) return null;
        var voidedBy = s.VoidedById is { } v ? await db.Users.Where(u => u.Id == v).Select(u => u.FullName).SingleOrDefaultAsync(ct) : null;
        return new SaleView(s.Id, s.Number, s.Status, clock.ToLocal(s.CreatedAt), s.Cashier?.FullName ?? "", s.Subtotal, s.LineDiscountTotal,
            s.BillDiscountType, s.BillDiscountValue, s.BillDiscountAmount, s.Total, s.Paid, s.Change, s.CostTotal, s.Note,
            s.Lines.OrderBy(l => l.LineNo).Select(l => new SaleLineView(l.LineNo, l.ProductName, l.Barcode, l.Qty, l.ListPrice, l.UnitPrice,
                l.DiscountType, l.DiscountValue, l.DiscountAmount, l.LineTotal, l.CostTotal, l.ProductId)).ToList(),
            s.Payments.OrderBy(p => p.Id).Select(p => new PaymentView(p.Method, p.Amount, p.Tendered, p.Reference)).ToList(),
            s.VoidedAt is { } va ? clock.ToLocal(va) : null, voidedBy, s.VoidReason);
    }

    public async Task<SaleView?> FindByNumberAsync(int number, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var id = await db.Sales.Where(s => s.Number == number).Select(s => (int?)s.Id).SingleOrDefaultAsync(ct);
        return id is { } i ? await GetAsync(i, ct) : null;
    }

    /// <summary>The newest bill this cashier saved (F10 reprint).</summary>
    public async Task<int?> LastSaleIdAsync(int cashierId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Sales.Where(s => s.CashierId == cashierId).OrderByDescending(s => s.Id).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<BillRow>> ListAsync(DateOnly from, DateOnly to, string? search = null, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = clock.Range(from, to);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Sales.AsNoTracking().Include(s => s.Cashier).Include(s => s.Lines).Include(s => s.Payments)
            .Where(s => s.CreatedAt >= fromUtc && s.CreatedAt < toUtc);
        if (int.TryParse(search?.Trim().TrimStart('#'), out var n)) q = db.Sales.AsNoTracking().Include(s => s.Cashier).Include(s => s.Lines).Include(s => s.Payments).Where(s => s.Number == n);
        var rows = await q.OrderByDescending(s => s.Id).Take(2000).ToListAsync(ct);
        return rows.Select(s => new BillRow(s.Id, s.Number, clock.ToLocal(s.CreatedAt), s.Cashier?.FullName ?? "", s.Lines.Sum(l => l.Qty), s.Total,
            string.Join(" + ", s.Payments.Select(p => MethodName(p.Method)).Distinct()), s.Status)).ToList();
    }

    public static string MethodName(PaymentMethod m) => m switch
    {
        PaymentMethod.Cash => "Cash",
        PaymentMethod.Card => "Card",
        _ => "Bank",
    };
}
