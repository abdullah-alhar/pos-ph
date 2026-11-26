using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;
using VetPos.Core.Stock;

namespace VetPos.Data.Services;

public sealed record ProductRow(
    int Id, string Name, string? Barcode, string Category, ProductUnit Unit, decimal CostPrice, decimal SellingPrice,
    int StockQty, int SellableQty, int ExpiredQty, int ReorderLevel, DateOnly? NextExpiry, bool TracksExpiry, bool IsActive,
    bool IsMedicine = false, string? GenericName = null)
{
    public bool IsLowStock => ReorderLevel > 0 && StockQty <= ReorderLevel;
    public bool IsOut => StockQty <= 0;
    public bool HasExpired => ExpiredQty > 0;

    /// <summary>The stock that sells next expires within 30 days.</summary>
    public bool ExpiresSoon => NextExpiry is { } e && e <= DateOnly.FromDateTime(DateTime.Today).AddDays(Fefo.ExpiringSoonDays);
}

public sealed record BatchRow(int Id, string? BatchNo, DateOnly? ExpiryDate, decimal CostPrice, decimal SellingPrice, int QtyReceived, int QtyOnHand, DateTime ReceivedAt);

/// <summary>Medicine details (null = not a medicine).</summary>
public sealed record MedicineInfo(string? GenericName, string? Strength, string? DosageForm, string? Species);

public sealed record ProductDetail(
    int Id, string Name, string? Barcode, IReadOnlyList<string> ExtraBarcodes, int CategoryId, ProductUnit Unit,
    decimal CostPrice, decimal SellingPrice, int ReorderLevel, bool TracksExpiry, PriceChoice PriceChoice,
    int StockQty, bool IsActive, IReadOnlyList<BatchRow> Batches, MedicineInfo? Medicine = null);

public sealed record ProductInput(
    int? Id, string Name, string? Barcode, IReadOnlyList<string> ExtraBarcodes, int CategoryId, ProductUnit Unit,
    decimal CostPrice, decimal SellingPrice, int ReorderLevel, bool TracksExpiry, bool IsActive, MedicineInfo? Medicine = null);

public sealed record ExpiryRow(int ProductId, string ProductName, int BatchId, string? BatchNo, DateOnly ExpiryDate, int QtyOnHand, int DaysLeft, decimal CostValue)
{
    public bool IsExpired => DaysLeft < 0;
}

public sealed record CategoryRow(int Id, string Name, int ProductCount);

public sealed record SupplierRow(int Id, string Name, string? Phone, string? Address, bool IsActive);

/// <summary>Products, categories, suppliers, and the low-stock / expiring lists.</summary>
public sealed class CatalogService(IDbContextFactory<VetPosDbContext> dbFactory, ShopClock clock)
{
    // ───────── Products ─────────

    /// <summary>
    /// Search by name (any words, any order) or exact barcode. Empty text lists everything.
    /// </summary>
    public async Task<IReadOnlyList<ProductRow>> SearchAsync(string? text, bool includeInactive = false, int limit = 200, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Products.AsNoTracking().Include(p => p.Category).Include(p => p.Batches).AsQueryable();
        if (!includeInactive) q = q.Where(p => p.IsActive);
        var t = (text ?? "").Trim();
        if (t.Length > 0)
        {
            // Exact barcode (main or extra) …
            var exactIds = (await db.Products.Where(p => p.Barcode == t).Select(p => p.Id)
                .Concat(db.ProductBarcodes.Where(b => b.Code == t).Select(b => b.ProductId)).ToListAsync(ct)).ToHashSet();
            // … or every word somewhere (start, middle or end) in the name, generic name, barcode or category.
            var words = db.Products.AsQueryable();
            var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var w in parts)
            {
                var like = "%" + w.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
                words = words.Where(p => EF.Functions.Like(p.Name, like, "\\")
                    || (p.GenericName != null && EF.Functions.Like(p.GenericName, like, "\\"))
                    || (p.Barcode != null && EF.Functions.Like(p.Barcode, like, "\\"))
                    || EF.Functions.Like(p.Category!.Name, like, "\\"));
            }
            var ids = await words.Select(p => p.Id).Take(Math.Max(limit * 8, 200)).ToListAsync(ct);
            ids.AddRange(exactIds);
            q = q.Where(p => ids.Contains(p.Id));
            // Best match first: exact barcode, then names that start with the text, then a word that
            // starts with it, then anywhere.
            var found = await q.ToListAsync(ct);
            return found
                .OrderBy(p => exactIds.Contains(p.Id) ? 0 : Rank(p, t, parts))
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit).Select(Row).ToList();
        }
        var list = await q.OrderBy(p => p.Name).Take(limit).ToListAsync(ct);
        return list.Select(Row).ToList();
    }

    private static int Rank(Product p, string text, string[] words)
    {
        if (p.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase)) return 1;
        var nameWords = p.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.All(w => nameWords.Any(n => n.StartsWith(w, StringComparison.OrdinalIgnoreCase)))) return 2;
        if (words.All(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase))) return 3;
        return 4;
    }

    /// <summary>A product by its main or extra barcode (scanner).</summary>
    public async Task<int?> FindIdByBarcodeAsync(string code, CancellationToken ct = default)
    {
        code = code.Trim();
        if (code.Length == 0) return null;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var id = await db.Products.Where(p => p.Barcode == code).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
        return id ?? await db.ProductBarcodes.Where(b => b.Code == code).Select(b => (int?)b.ProductId).FirstOrDefaultAsync(ct);
    }

    public async Task<ProductRow?> GetRowAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.Products.AsNoTracking().Include(p => p.Category).Include(p => p.Batches)
            .SingleOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? null : Row(product);
    }

    public async Task<ProductDetail?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var p = await db.Products.AsNoTracking().Include(x => x.Barcodes).Include(x => x.Batches).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;
        return new ProductDetail(p.Id, p.Name, p.Barcode, p.Barcodes.Select(b => b.Code).Order().ToList(), p.CategoryId, p.Unit,
            p.CostPrice, p.SellingPrice, p.ReorderLevel, p.TracksExpiry, p.PriceChoice, p.StockQty, p.IsActive,
            p.Batches.OrderBy(b => b.ExpiryDate is null).ThenBy(b => b.ExpiryDate).ThenBy(b => b.CreatedAt)
                .Select(b => new BatchRow(b.Id, b.BatchNo, b.ExpiryDate, b.CostPrice, b.SellingPrice, b.QtyReceived, b.QtyOnHand, b.CreatedAt)).ToList(),
            p.IsMedicine ? new MedicineInfo(p.GenericName, p.Strength, p.DosageForm, p.Species) : null);
    }

    public async Task<int> SaveAsync(ProductInput input, Actor actor, CancellationToken ct = default)
    {
        var errors = Validate(input);
        if (errors.Count > 0) throw new PosException(string.Join("\n", errors));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var name = input.Name.Trim();
        var barcode = string.IsNullOrWhiteSpace(input.Barcode) ? null : input.Barcode.Trim();
        var extras = input.ExtraBarcodes.Select(b => b.Trim()).Where(b => b.Length > 0 && b != barcode).Distinct().ToList();
        var codes = extras.Concat(barcode is null ? [] : [barcode]).ToList();
        if (codes.Count > 0)
        {
            var clash = await db.Products.Where(p => p.Id != input.Id && p.Barcode != null && codes.Contains(p.Barcode)).Select(p => p.Name)
                .Union(db.ProductBarcodes.Where(b => b.ProductId != input.Id && codes.Contains(b.Code)).Join(db.Products, b => b.ProductId, p => p.Id, (b, p) => p.Name))
                .FirstOrDefaultAsync(ct);
            if (clash is not null) throw new PosException($"That barcode is already used by \"{clash}\".");
        }
        if (!await db.Categories.AnyAsync(c => c.Id == input.CategoryId, ct)) throw new PosException("Choose a category.");
        if (await db.Products.AnyAsync(p => p.Id != input.Id && p.Name == name && p.IsActive, ct))
            throw new PosException($"A product called \"{name}\" already exists.");

        Product product;
        if (input.Id is { } id)
        {
            product = await db.Products.Include(p => p.Barcodes).SingleOrDefaultAsync(p => p.Id == id, ct)
                ?? throw new PosException("That product no longer exists.");
            if (product.SellingPrice != input.SellingPrice)
                Audit.Add(db, actor, AuditActions.ProductPrice,
                    $"Price of {name}: {Money.Format(product.SellingPrice)} → {Money.Format(input.SellingPrice)}",
                    "Product", id, Money.Format(product.SellingPrice), Money.Format(input.SellingPrice));
            db.ProductBarcodes.RemoveRange(product.Barcodes.Where(b => !extras.Contains(b.Code)));
            foreach (var code in extras.Where(c => product.Barcodes.All(b => b.Code != c)))
                product.Barcodes.Add(new ProductBarcode { Code = code });
        }
        else
        {
            product = new Product { Barcodes = extras.Select(c => new ProductBarcode { Code = c }).ToList() };
            db.Products.Add(product);
        }

        product.Name = name;
        product.Barcode = barcode;
        product.CategoryId = input.CategoryId;
        product.Unit = input.Unit;
        product.CostPrice = input.CostPrice;
        product.SellingPrice = input.SellingPrice;
        product.ReorderLevel = input.ReorderLevel;
        product.TracksExpiry = input.TracksExpiry || product.TracksExpiry;
        product.IsActive = input.IsActive;
        var med = input.Medicine;
        product.IsMedicine = med is not null;
        product.GenericName = Clean(med?.GenericName, 120);
        product.Strength = Clean(med?.Strength, 60);
        product.DosageForm = Clean(med?.DosageForm, 60);
        product.Species = Clean(med?.Species, 120);
        await db.SaveChangesAsync(ct);
        Audit.Add(db, actor, AuditActions.ProductSave, $"Product saved: {name}", "Product", product.Id);
        await db.SaveChangesAsync(ct);
        return product.Id;
    }

    private static string? Clean(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length > max ? s.Trim()[..max] : s.Trim();

    public static IReadOnlyList<string> Validate(ProductInput p)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(p.Name)) errors.Add("Enter the product name (with the pack size, e.g. \"Whiskas Adult 1.2 kg\").");
        else if (p.Name.Trim().Length > 120) errors.Add("The name is too long (120 letters at most).");
        if (p.Barcode is { Length: > 40 }) errors.Add("The barcode is too long.");
        if (p.CostPrice < 0 || p.SellingPrice < 0) errors.Add("Prices cannot be negative.");
        if (p.CostPrice != Money.Round(p.CostPrice) || p.SellingPrice != Money.Round(p.SellingPrice)) errors.Add("Prices can have at most two decimals.");
        if (p.CostPrice > Money.Max || p.SellingPrice > Money.Max) errors.Add("That price is too large.");
        if (p.ReorderLevel < 0) errors.Add("The reorder level cannot be negative.");
        if (p.CategoryId <= 0) errors.Add("Choose a category.");
        return errors;
    }

    /// <summary>
    /// Removes a product. Never sold or stocked: deleted for good. Has bills or stock history: it is
    /// switched off instead (gone from billing and the product list, kept on old bills and reports,
    /// can be switched on again). Returns true when it was deleted, false when it was switched off.
    /// </summary>
    public async Task<bool> RemoveAsync(int productId, Actor actor, CancellationToken ct = default)
    {
        actor.RequireOwner("remove products");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == productId, ct) ?? throw new PosException("That product no longer exists.");
        var used = await db.SaleLines.AnyAsync(l => l.ProductId == productId, ct)
                   || await db.StockMovements.AnyAsync(m => m.ProductId == productId, ct)
                   || await db.GoodsReceiptLines.AnyAsync(l => l.ProductId == productId, ct)
                   || await db.StockBatches.AnyAsync(b => b.ProductId == productId, ct);
        if (used)
        {
            if (!product.IsActive) throw new PosException($"\"{product.Name}\" is already removed from sale. It stays only because old bills use it.");
            product.IsActive = false;
            Audit.Add(db, actor, AuditActions.ProductRemove, $"Product switched off (has history): {product.Name}", "Product", product.Id);
            await db.SaveChangesAsync(ct);
            return false;
        }
        db.ProductBarcodes.RemoveRange(db.ProductBarcodes.Where(b => b.ProductId == productId));
        db.Products.Remove(product);
        Audit.Add(db, actor, AuditActions.ProductRemove, $"Product deleted: {product.Name}", "Product", productId);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>A new, unused in-store EAN-13 (200…) for a product being created (the "Generate" button).</summary>
    public async Task<string> NextInStoreBarcodeAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var start = (await db.Products.MaxAsync(p => (int?)p.Id, ct) ?? 0) + 1;
        for (var n = start; ; n++)
        {
            var code = InStoreCode(n);
            if (!await db.Products.AnyAsync(p => p.Barcode == code, ct) && !await db.ProductBarcodes.AnyAsync(b => b.Code == code, ct)) return code;
        }
    }

    public static string InStoreCode(int n)
    {
        var body = "200" + (n % 1_000_000_000).ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += (body[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return body + ((10 - sum % 10) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Gives a product without a barcode an in-store EAN-13 (200…, never clashes with makers'
    /// codes) so it can be labelled and scanned.
    /// </summary>
    public async Task<string> AssignInStoreBarcodeAsync(int productId, Actor actor, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == productId, ct) ?? throw new PosException("That product no longer exists.");
        if (!string.IsNullOrEmpty(product.Barcode)) return product.Barcode;
        for (var n = product.Id; ; n += 100_000)
        {
            var body = "200" + n.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
            var sum = 0;
            for (var i = 0; i < 12; i++) sum += (body[i] - '0') * (i % 2 == 0 ? 1 : 3);
            var code = body + ((10 - sum % 10) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (await db.Products.AnyAsync(p => p.Barcode == code, ct) || await db.ProductBarcodes.AnyAsync(b => b.Code == code, ct)) continue;
            product.Barcode = code;
            Audit.Add(db, actor, AuditActions.ProductSave, $"In-store barcode {code} given to {product.Name}", "Product", product.Id);
            await db.SaveChangesAsync(ct);
            return code;
        }
    }

    /// <summary>Products at or below their reorder level (active only), lowest first.</summary>
    public async Task<IReadOnlyList<ProductRow>> LowStockAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = await db.Products.AsNoTracking().Include(p => p.Category).Include(p => p.Batches)
            .Where(p => p.IsActive && p.ReorderLevel > 0 && p.StockQty <= p.ReorderLevel).ToListAsync(ct);
        return list.Select(Row).OrderBy(r => r.StockQty).ThenBy(r => r.Name).ToList();
    }

    /// <summary>Batches on hand that expire within <paramref name="days"/> days, including expired ones.</summary>
    public async Task<IReadOnlyList<ExpiryRow>> ExpiringAsync(int days = Fefo.ExpiringSoonDays, CancellationToken ct = default)
    {
        var today = clock.Today;
        var limit = today.AddDays(days);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.StockBatches.AsNoTracking().Include(b => b.Product)
            .Where(b => b.QtyOnHand > 0 && b.ExpiryDate != null && b.ExpiryDate <= limit && b.Product!.IsActive)
            .ToListAsync(ct);
        return rows.Select(b => new ExpiryRow(b.ProductId, b.Product!.Name, b.Id, b.BatchNo, b.ExpiryDate!.Value, b.QtyOnHand,
                b.ExpiryDate.Value.DayNumber - today.DayNumber, b.CostPrice * b.QtyOnHand))
            .OrderBy(r => r.ExpiryDate).ThenBy(r => r.ProductName).ToList();
    }

    private ProductRow Row(Product p)
    {
        var today = clock.Today;
        var batches = p.Batches.Select(ToStock).ToList();
        return new ProductRow(p.Id, p.Name, p.Barcode, p.Category?.Name ?? "", p.Unit, p.CostPrice, p.SellingPrice, p.StockQty,
            Fefo.SellableQty(batches, today), Fefo.ExpiredQty(batches, today), p.ReorderLevel,
            p.Batches.Where(b => b.QtyOnHand > 0 && b.ExpiryDate >= today).Min(b => b.ExpiryDate), p.TracksExpiry, p.IsActive,
            p.IsMedicine, p.GenericName);
    }

    internal static BatchStock ToStock(StockBatch b) => new(b.Id, b.ExpiryDate, b.QtyOnHand, b.SellingPrice, b.CostPrice, b.CreatedAt);

    // ───────── Categories ─────────

    public async Task<IReadOnlyList<CategoryRow>> CategoriesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryRow(c.Id, c.Name, db.Products.Count(p => p.CategoryId == c.Id && p.IsActive))).ToListAsync(ct);
    }

    public async Task<int> SaveCategoryAsync(int? id, string name, Actor actor, CancellationToken ct = default)
    {
        name = (name ?? "").Trim();
        if (name.Length is 0 or > 60) throw new PosException("Enter a category name (60 letters at most).");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Categories.AnyAsync(c => c.Name == name && c.Id != id, ct)) throw new PosException($"\"{name}\" already exists.");
        var c = id is { } i ? await db.Categories.SingleAsync(x => x.Id == i, ct) : db.Categories.Add(new Category { SortOrder = 99 }).Entity;
        c.Name = name;
        await db.SaveChangesAsync(ct);
        return c.Id;
    }

    // ───────── Suppliers ─────────

    public async Task<IReadOnlyList<SupplierRow>> SuppliersAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Suppliers.AsNoTracking().Where(s => includeInactive || s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SupplierRow(s.Id, s.Name, s.Phone, s.Address, s.IsActive)).ToListAsync(ct);
    }

    public async Task<int> SaveSupplierAsync(SupplierRow input, Actor actor, CancellationToken ct = default)
    {
        var name = (input.Name ?? "").Trim();
        if (name.Length is 0 or > 120) throw new PosException("Enter the supplier's name.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Suppliers.AnyAsync(s => s.Name == name && s.Id != input.Id, ct)) throw new PosException($"The supplier \"{name}\" already exists.");
        var s = input.Id > 0 ? await db.Suppliers.SingleAsync(x => x.Id == input.Id, ct) : db.Suppliers.Add(new Supplier()).Entity;
        s.Name = name;
        s.Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim();
        s.Address = string.IsNullOrWhiteSpace(input.Address) ? null : input.Address.Trim();
        s.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return s.Id;
    }
}
