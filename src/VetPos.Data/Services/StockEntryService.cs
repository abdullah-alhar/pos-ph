using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;
using VetPos.Core.Stock;

namespace VetPos.Data.Services;

public sealed record ReceiptLineInput(int ProductId, int Qty, decimal CostPrice, decimal SellingPrice, string? BatchNo, DateOnly? ExpiryDate);

public sealed record ReceiptInput(int? SupplierId, string? SupplierInvoiceNo, DateOnly InvoiceDate, string? Note, IReadOnlyList<ReceiptLineInput> Lines);

/// <summary>What to do when a delivery's selling price differs from stock already on the shelf.</summary>
public enum PriceDecision
{
    /// <summary>All stock (old and new) sells at the new price.</summary>
    ChangeAll,

    /// <summary>Old stock keeps its price and sells first; then the new price.</summary>
    KeepOldUntilSold,

    /// <summary>The cashier chooses the price each time the item is billed.</summary>
    AskAtBilling,
}

/// <summary>A product whose new selling price differs from stock on hand: the screen asks what to do.</summary>
public sealed record PriceQuestion(int ProductId, string ProductName, decimal NewPrice, IReadOnlyList<decimal> CurrentPrices, int QtyOnHand);

public sealed record ReceiptRow(int Id, string Number, string Supplier, string? SupplierInvoiceNo, DateOnly InvoiceDate, int Lines, decimal Total, DateTime CreatedAt);

/// <summary>Goods received from suppliers: stock goes up, one batch per line (with expiry and its own prices).</summary>
public sealed class StockEntryService(IDbContextFactory<VetPosDbContext> dbFactory, ShopClock clock)
{
    /// <summary>Products in this delivery whose new selling price differs from stock still on the shelf.</summary>
    public async Task<IReadOnlyList<PriceQuestion>> PriceQuestionsAsync(IReadOnlyList<ReceiptLineInput> lines, CancellationToken ct = default)
    {
        var today = clock.Today;
        var ids = lines.Select(l => l.ProductId).Distinct().ToList();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var products = await db.Products.AsNoTracking().Include(p => p.Batches).Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var questions = new List<PriceQuestion>();
        foreach (var p in products)
        {
            var newPrice = lines.Last(l => l.ProductId == p.Id).SellingPrice;
            var onHand = Fefo.Sellable(p.Batches.Select(CatalogService.ToStock), today).ToList();
            var prices = onHand.Select(b => b.SellingPrice).Distinct().ToList();
            if (onHand.Count > 0 && prices.Any(x => x != newPrice))
                questions.Add(new PriceQuestion(p.Id, p.Name, newPrice, prices, onHand.Sum(b => b.QtyOnHand)));
        }
        return questions;
    }

    public static IReadOnlyList<string> Validate(ReceiptInput input, DateOnly today)
    {
        var errors = new List<string>();
        if (input.Lines.Count == 0) errors.Add("Add at least one item.");
        if (input.InvoiceDate > today.AddDays(1)) errors.Add("The invoice date is in the future.");
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var l = input.Lines[i];
            var n = $"Line {i + 1}: ";
            if (l.ProductId <= 0) errors.Add(n + "choose the product.");
            if (l.Qty <= 0) errors.Add(n + "quantity must be 1 or more.");
            if (l.Qty > 1_000_000) errors.Add(n + "quantity is too large.");
            if (l.CostPrice < 0 || l.SellingPrice < 0) errors.Add(n + "prices cannot be negative.");
            if (l.CostPrice != Money.Round(l.CostPrice) || l.SellingPrice != Money.Round(l.SellingPrice)) errors.Add(n + "prices can have at most two decimals.");
            if (l.BatchNo is { Length: > 60 }) errors.Add(n + "batch number is too long.");
            if (l.ExpiryDate is { } exp && exp < today) errors.Add(n + $"the expiry date {exp:dd MMM yyyy} has already passed.");
        }
        return errors;
    }

    /// <summary>
    /// Saves the delivery in one transaction: receipt, lines, batches, stock, movements, product
    /// prices and audit. <paramref name="decisions"/> answers each <see cref="PriceQuestion"/>.
    /// </summary>
    public async Task<ReceiptRow> SaveAsync(ReceiptInput input, IReadOnlyDictionary<int, PriceDecision> decisions, Actor actor, CancellationToken ct = default)
    {
        var errors = Validate(input, clock.Today).ToList();
        if (errors.Count > 0) throw new PosException(string.Join("\n", errors));

        var questions = await PriceQuestionsAsync(input.Lines, ct);
        var unanswered = questions.Where(q => !decisions.ContainsKey(q.ProductId)).Select(q => q.ProductName).ToList();
        if (unanswered.Count > 0) throw new PosException("Choose what to do with the old price of: " + string.Join(", ", unanswered));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        Supplier? supplier = null;
        if (input.SupplierId is { } supplierId)
            supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == supplierId, ct) ?? throw new PosException("That supplier no longer exists.");
        var invoiceNo = string.IsNullOrWhiteSpace(input.SupplierInvoiceNo) ? null : input.SupplierInvoiceNo.Trim();
        if (invoiceNo is not null && supplier is not null && await db.GoodsReceipts.AnyAsync(r => r.SupplierId == supplier.Id && r.SupplierInvoiceNo == invoiceNo, ct))
            throw new PosException($"Invoice {invoiceNo} from {supplier.Name} is already entered.");

        var ids = input.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.Include(p => p.Batches).Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var l in input.Lines)
        {
            if (!products.TryGetValue(l.ProductId, out var p) || !p.IsActive) throw new PosException("A product on this delivery no longer exists.");
        }

        var number = await Counters.NextAsync(db, Counters.GoodsReceipt, 1, ct);
        var receipt = new GoodsReceipt
        {
            Number = $"GRN-{number:D5}",
            SupplierId = supplier?.Id,
            SupplierInvoiceNo = invoiceNo,
            InvoiceDate = input.InvoiceDate,
            Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim(),
        };
        foreach (var l in input.Lines)
        {
            receipt.Lines.Add(new GoodsReceiptLine
            {
                ProductId = l.ProductId, Qty = l.Qty, CostPrice = l.CostPrice, SellingPrice = l.SellingPrice,
                BatchNo = string.IsNullOrWhiteSpace(l.BatchNo) ? null : l.BatchNo.Trim(), ExpiryDate = l.ExpiryDate,
                LineTotal = Money.Round(l.CostPrice * l.Qty),
            });
        }
        receipt.Total = receipt.Lines.Sum(x => x.LineTotal);
        db.GoodsReceipts.Add(receipt);
        await db.SaveChangesAsync(ct);

        // Price decisions first (they look at stock already on the shelf).
        foreach (var q in questions)
        {
            var p = products[q.ProductId];
            switch (decisions[q.ProductId])
            {
                case PriceDecision.ChangeAll:
                    foreach (var b in p.Batches.Where(b => b.QtyOnHand > 0 && b.SellingPrice != q.NewPrice))
                    {
                        Audit.Add(db, actor, AuditActions.BatchPrice,
                            $"{p.Name} batch {b.BatchNo ?? "-"}: {Money.Format(b.SellingPrice)} → {Money.Format(q.NewPrice)} (new delivery {receipt.Number})",
                            "StockBatch", b.Id, Money.Format(b.SellingPrice), Money.Format(q.NewPrice));
                        b.SellingPrice = q.NewPrice;
                    }
                    p.PriceChoice = PriceChoice.OnePrice;
                    break;
                case PriceDecision.KeepOldUntilSold:
                    p.PriceChoice = PriceChoice.OldFirst;
                    break;
                case PriceDecision.AskAtBilling:
                    p.PriceChoice = PriceChoice.AskAtBilling;
                    break;
            }
        }

        foreach (var line in receipt.Lines)
        {
            var p = products[line.ProductId];
            var batch = new StockBatch
            {
                ProductId = p.Id, GoodsReceiptLineId = line.Id, BatchNo = line.BatchNo, ExpiryDate = line.ExpiryDate,
                CostPrice = line.CostPrice, SellingPrice = line.SellingPrice, QtyReceived = line.Qty, QtyOnHand = line.Qty,
            };
            p.Batches.Add(batch);
            p.StockQty += line.Qty;
            if (p.SellingPrice != line.SellingPrice)
                Audit.Add(db, actor, AuditActions.ProductPrice, $"Price of {p.Name}: {Money.Format(p.SellingPrice)} → {Money.Format(line.SellingPrice)} ({receipt.Number})",
                    "Product", p.Id, Money.Format(p.SellingPrice), Money.Format(line.SellingPrice));
            p.CostPrice = line.CostPrice;
            p.SellingPrice = line.SellingPrice;
            if (line.ExpiryDate is not null) p.TracksExpiry = true;
            await db.SaveChangesAsync(ct);
            db.StockMovements.Add(new StockMovement
            {
                ProductId = p.Id, BatchId = batch.Id, Change = line.Qty, QtyAfter = p.StockQty,
                Reason = StockReason.Receipt, Reference = receipt.Number,
            });
        }

        Audit.Add(db, actor, AuditActions.StockReceive,
            $"{receipt.Number}{(supplier is null ? "" : $" from {supplier.Name}")}{(invoiceNo is null ? "" : $" (invoice {invoiceNo})")}: {receipt.Lines.Count} line(s), {Money.Format(receipt.Total, true)}",
            "GoodsReceipt", receipt.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ReceiptRow(receipt.Id, receipt.Number, supplier?.Name ?? "No supplier", invoiceNo, receipt.InvoiceDate, receipt.Lines.Count, receipt.Total, receipt.CreatedAt);
    }

    public async Task<IReadOnlyList<ReceiptRow>> ListAsync(int take = 100, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.GoodsReceipts.AsNoTracking().Include(r => r.Supplier).Include(r => r.Lines)
            .OrderByDescending(r => r.Id).Take(take).ToListAsync(ct);
        return rows.Select(r => new ReceiptRow(r.Id, r.Number, r.Supplier?.Name ?? "No supplier", r.SupplierInvoiceNo, r.InvoiceDate, r.Lines.Count, r.Total, r.CreatedAt)).ToList();
    }

    /// <summary>
    /// Changes the expiry date of one batch (typed wrong, or the maker extended it). Null = no
    /// expiry. Logged with the old and new date.
    /// </summary>
    public async Task SetBatchExpiryAsync(int batchId, DateOnly? expiry, Actor actor, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var batch = await db.StockBatches.Include(b => b.Product).SingleOrDefaultAsync(b => b.Id == batchId, ct) ?? throw new PosException("That batch no longer exists.");
        if (batch.ExpiryDate == expiry) return;
        static string Text(DateOnly? d) => d?.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "no expiry";
        Audit.Add(db, actor, AuditActions.StockAdjust, $"{batch.Product!.Name} batch {batch.BatchNo ?? "-"}: expiry {Text(batch.ExpiryDate)} → {Text(expiry)}",
            "StockBatch", batch.Id, Text(batch.ExpiryDate), Text(expiry));
        batch.ExpiryDate = expiry;
        if (expiry is not null) batch.Product.TracksExpiry = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Owner only: correct a batch (damaged, expired and thrown away, counted wrong). A negative
    /// change takes units off; the reason goes in the audit log.
    /// </summary>
    public async Task AdjustBatchAsync(int batchId, int change, string reason, Actor actor, CancellationToken ct = default)
    {
        actor.RequireOwner("change stock by hand");
        if (change == 0) return;
        if (string.IsNullOrWhiteSpace(reason)) throw new PosException("Write why the stock is changed (e.g. \"expired, thrown away\").");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.StockBatches.Include(b => b.Product).SingleOrDefaultAsync(b => b.Id == batchId, ct) ?? throw new PosException("That batch no longer exists.");
        var after = batch.QtyOnHand + change;
        if (after < 0) throw new PosException($"Only {batch.QtyOnHand} left in this batch.");
        if (after > batch.QtyReceived) batch.QtyReceived = after;
        batch.QtyOnHand = after;
        batch.Product!.StockQty += change;
        db.StockMovements.Add(new StockMovement
        {
            ProductId = batch.ProductId, BatchId = batch.Id, Change = change, QtyAfter = batch.Product.StockQty,
            Reason = StockReason.Adjustment, Note = reason.Trim(),
        });
        Audit.Add(db, actor, AuditActions.StockAdjust, $"{batch.Product.Name} batch {batch.BatchNo ?? "-"}: {change:+#;-#} ({reason.Trim()})",
            "StockBatch", batch.Id, (after - change).ToString(System.Globalization.CultureInfo.InvariantCulture), after.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
