using System.Globalization;
using System.Text.Json.Nodes;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;
using VetPos.Data.Services;

namespace VetPos.AI;

/// <summary>
/// A change the AI prepared. It is shown to the person as a card ("Create 3 products …") and is
/// only saved when they press Confirm. Saving goes through the same services as the screens, so the
/// same checks, permissions and activity log apply.
/// </summary>
public abstract class AiAction(string title, IReadOnlyList<string> lines)
{
    public string Title { get; } = title;
    public IReadOnlyList<string> Lines { get; } = lines;
}

public sealed class CreateCategoryAction(string name) : AiAction("Create product category", [name])
{
    public string Name { get; } = name;
}

public sealed class CreateProductsAction(IReadOnlyList<ProductProposal> products)
    : AiAction(
        $"Create {products.Count} product{(products.Count == 1 ? "" : "s")}",
        products.Select(p => $"{p.Name} · {p.Category} · sell {Money.Format(p.SellingPrice, true)}" + (p.CostPrice > 0 ? $" · cost {Money.Format(p.CostPrice, true)}" : "")
            + (p.Problem is null ? "" : $"  (skipped: {p.Problem})")).ToList())
{
    public IReadOnlyList<ProductProposal> Products { get; } = products;
}

public sealed class UpdateProductAction(int productId, string productName, ProductChange change, IReadOnlyList<string> lines)
    : AiAction($"Change {productName}", lines)
{
    public int ProductId { get; } = productId;
    public ProductChange Change { get; } = change;
}

public sealed class RemoveProductAction(int productId, string productName)
    : AiAction($"Remove {productName}", ["Deleted if it was never sold or stocked; otherwise taken off sale (old bills keep it)."])
{
    public int ProductId { get; } = productId;
}

public sealed record ProductChange(string? NewName, string? Category, decimal? SellingPrice, decimal? CostPrice, int? ReorderLevel, bool? Active);

/// <summary>Cost / selling price null = the product's price at the moment it is saved.</summary>
public sealed record StockLine(int ProductId, string Name, int Qty, decimal? CostPrice, decimal? SellingPrice);

public sealed class AddStockAction(IReadOnlyList<StockLine> items, string? invoiceNo)
    : AiAction(
        $"Add stock: {items.Sum(i => i.Qty)} unit{(items.Sum(i => i.Qty) == 1 ? "" : "s")} of {items.Count} product{(items.Count == 1 ? "" : "s")}",
        (invoiceNo is null ? Enumerable.Empty<string>() : [$"Invoice: {invoiceNo}"]).Concat(items.Select(i => $"{i.Name} · +{i.Qty}" + (i.CostPrice is { } c ? $" · cost {Money.Format(c, true)}" : "") + (i.SellingPrice is { } sp ? $" · selling price for all stock {Money.Format(sp, true)}" : ""))).ToList())
{
    public IReadOnlyList<StockLine> Items { get; } = items;
    public string? InvoiceNo { get; } = invoiceNo;
}

/// <summary>Builds the AI's proposed changes from its tool arguments, and saves them after the person confirms.</summary>
public sealed class AssistantActions(CatalogService catalog, StockEntryService stock, ProductAssistant products, ShopClock clock)
{
    // ───────── Building (from the AI's tool call) ─────────

    public static CreateCategoryAction BuildCategory(JsonObject args)
    {
        var name = Str(args, "name");
        if (name is null || name.Length > 60) throw new PosException("give a category name between 1 and 60 characters");
        return new CreateCategoryAction(name);
    }

    public static CreateProductsAction BuildCreate(JsonObject args)
    {
        var list = (args["products"] as JsonArray ?? []).OfType<JsonObject>().Take(100).Select(ProductAssistant.Read).ToList();
        if (list.Count == 0) throw new PosException("no products were given");
        return new CreateProductsAction(list);
    }

    public async Task<UpdateProductAction> BuildUpdateAsync(JsonObject args, CancellationToken ct)
    {
        var (id, name) = await FindProductAsync(Str(args, "product"), ct);
        var p = await catalog.GetAsync(id, ct) ?? throw new PosException($"the product \"{name}\" no longer exists");
        var change = new ProductChange(Str(args, "new_name"), Str(args, "category"), Dec(args, "selling_price"), Dec(args, "cost_price"), Int(args, "reorder_level"),
            args["active"] is JsonValue a && a.TryGetValue<bool>(out var on) ? on : null);
        var lines = new List<string>();
        if (change.NewName is { } n && n != p.Name) lines.Add($"Name: {p.Name} → {n}");
        if (change.SellingPrice is { } sp && sp != p.SellingPrice) lines.Add($"Selling price: {Money.Format(p.SellingPrice, true)} → {Money.Format(sp, true)}");
        if (change.CostPrice is { } cp && cp != p.CostPrice) lines.Add($"Cost price: {Money.Format(p.CostPrice, true)} → {Money.Format(cp, true)}");
        if (change.ReorderLevel is { } r && r != p.ReorderLevel) lines.Add($"Reorder level: {p.ReorderLevel} → {r}");
        if (change.Category is { } c) lines.Add($"Category: {c}");
        if (change.Active is { } act && act != p.IsActive) lines.Add(act ? "Switch on (sell again)" : "Switch off (hide from billing)");
        if (lines.Count == 0) throw new PosException($"nothing would change for \"{p.Name}\"; say what to change");
        if (change.SellingPrice is <= 0) throw new PosException("a selling price must be more than 0");
        return new UpdateProductAction(id, p.Name, change, lines);
    }

    public async Task<RemoveProductAction> BuildRemoveAsync(JsonObject args, CancellationToken ct)
    {
        var (id, name) = await FindProductAsync(Str(args, "product"), ct);
        return new RemoveProductAction(id, name);
    }

    public async Task<AddStockAction> BuildAddStockAsync(JsonObject args, CancellationToken ct)
    {
        var items = new List<StockLine>();
        foreach (var o in (args["items"] as JsonArray ?? []).OfType<JsonObject>().Take(100))
        {
            var (id, _) = await FindProductAsync(Str(o, "product"), ct);
            var p = await catalog.GetAsync(id, ct) ?? throw new PosException("a product no longer exists");
            var qty = Int(o, "qty") ?? 0;
            if (qty is <= 0 or > 1_000_000) throw new PosException($"give a quantity of 1 or more for \"{p.Name}\"");
            items.Add(new StockLine(id, p.Name, qty, Dec(o, "cost_price"), Dec(o, "selling_price")));
        }
        if (items.Count == 0) throw new PosException("no items were given");
        return new AddStockAction(items, Str(args, "invoice_no"));
    }

    /// <summary>One product from a name or barcode; when several match, the AI gets the names to ask which one.</summary>
    private async Task<(int Id, string Name)> FindProductAsync(string? text, CancellationToken ct)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) throw new PosException("say which product");
        var found = await catalog.SearchAsync(text, includeInactive: true, limit: 8, ct: ct);
        var exact = found.Where(p => string.Equals(p.Name, text, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Barcode, text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) return (exact[0].Id, exact[0].Name);
        if (found.Count == 1) return (found[0].Id, found[0].Name);
        if (found.Count == 0) throw new PosException($"no product matches \"{text}\"");
        throw new PosException($"several products match \"{text}\": {string.Join("; ", found.Select(f => f.Name))}. Ask the person which one");
    }

    // ───────── Saving (after Confirm) ─────────

    /// <summary>Saves a confirmed change and returns what was done, in words for the person.</summary>
    public async Task<string> ExecuteAsync(AiAction action, Actor actor, CancellationToken ct = default)
    {
        actor.RequireOwner("change products or stock with the AI");
        switch (action)
        {
            case CreateCategoryAction c:
                await catalog.SaveCategoryAsync(null, c.Name, actor, ct);
                return $"Category {c.Name} created.";
            case CreateProductsAction c:
            {
                var added = await products.CreateAsync(c.Products, actor, ct);
                var skipped = c.Products.Count - added;
                return $"Added {added} product{(added == 1 ? "" : "s")}." + (skipped > 0 ? $" {skipped} skipped (missing name or price)." : "");
            }
            case UpdateProductAction u:
            {
                var p = await catalog.GetAsync(u.ProductId, ct) ?? throw new PosException("That product no longer exists.");
                var categoryId = p.CategoryId;
                if (u.Change.Category is { Length: > 0 } cat)
                {
                    var existing = (await catalog.CategoriesAsync(ct)).FirstOrDefault(x => string.Equals(x.Name, cat, StringComparison.OrdinalIgnoreCase));
                    categoryId = existing?.Id ?? await catalog.SaveCategoryAsync(null, cat, actor, ct);
                }
                await catalog.SaveAsync(new ProductInput(p.Id, u.Change.NewName ?? p.Name, p.Barcode, p.ExtraBarcodes, categoryId, p.Unit,
                    u.Change.CostPrice ?? p.CostPrice, u.Change.SellingPrice ?? p.SellingPrice, u.Change.ReorderLevel ?? p.ReorderLevel,
                    p.TracksExpiry, u.Change.Active ?? p.IsActive, p.Medicine), actor, ct);
                return $"{u.Change.NewName ?? p.Name} saved.";
            }
            case AddStockAction s:
            {
                var lines = new List<ReceiptLineInput>();
                foreach (var i in s.Items)
                {
                    var p = await catalog.GetAsync(i.ProductId, ct) ?? throw new PosException($"{i.Name} no longer exists.");
                    lines.Add(new ReceiptLineInput(i.ProductId, i.Qty, i.CostPrice ?? p.CostPrice, i.SellingPrice ?? p.SellingPrice, null, null));
                }
                // A new selling price: all stock sells at the new price (the person saw it on the card).
                var questions = await stock.PriceQuestionsAsync(lines, ct);
                var decisions = questions.ToDictionary(q => q.ProductId, _ => PriceDecision.ChangeAll);
                var receipt = await stock.SaveAsync(new ReceiptInput(null, s.InvoiceNo, clock.Today, "Added with the AI assistant", lines), decisions, actor, ct);
                return $"Stock added (delivery {receipt.Number}): {s.Items.Sum(i => i.Qty)} units.";
            }
            case RemoveProductAction r:
            {
                var deleted = await catalog.RemoveAsync(r.ProductId, actor, ct);
                return deleted ? "Product deleted." : "Product taken off sale (it has bills or stock history). Find it under Products → Switched off.";
            }
            default:
                throw new PosException("Unknown change.");
        }
    }

    private static string? Str(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    private static decimal? Dec(JsonObject o, string k) =>
        o[k] is JsonValue v && (v.TryGetValue<decimal>(out var d) || decimal.TryParse(v.ToString().Replace(",", "", StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out d))
            ? Money.Round(d) : null;

    private static int? Int(JsonObject o, string k) =>
        o[k] is JsonValue v && (v.TryGetValue<int>(out var i) || (v.TryGetValue<decimal>(out var d) && d is >= int.MinValue and <= int.MaxValue && d == Math.Floor(d) && (i = (int)d) == d)) ? i : null;
}
