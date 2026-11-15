using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;
using VetPos.Data;
using VetPos.Data.Services;

namespace VetPos.AI;

/// <summary>A product the AI read from the person's text; it is only saved after the person confirms.</summary>
public sealed record ProductProposal(
    string Name, string Category, ProductUnit Unit, decimal CostPrice, decimal SellingPrice, int ReorderLevel, string? Barcode,
    bool HasExpiry, MedicineInfo? Medicine, string? Problem)
{
    public bool IsValid => Problem is null;
}

/// <summary>
/// Products the AI chat prepared ("create kitten food 1 kg, 3 kg …"): read from the AI's tool
/// call, shown on a card, and created only after the owner presses Confirm.
/// </summary>
public sealed class ProductAssistant(CatalogService catalog)
{
    /// <summary>Creates the confirmed products (categories that do not exist yet are added). Returns how many were added.</summary>
    public async Task<int> CreateAsync(IReadOnlyList<ProductProposal> products, Actor actor, CancellationToken ct = default)
    {
        var categories = (await catalog.CategoriesAsync(ct)).ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var p in products.Where(p => p.IsValid))
        {
            if (!categories.TryGetValue(p.Category, out var catId))
                categories[p.Category] = catId = await catalog.SaveCategoryAsync(null, p.Category, actor, ct);
            await catalog.SaveAsync(new ProductInput(null, p.Name, p.Barcode, [], catId, p.Unit, p.CostPrice, p.SellingPrice, p.ReorderLevel,
                false, true, p.Medicine), actor, ct);
            added++;
        }
        return added;
    }

    internal static ProductProposal Read(JsonObject o)
    {
        string? S(string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;
        decimal D(string k) => o[k] is JsonValue v && (v.TryGetValue<decimal>(out var d) || decimal.TryParse(v.ToString().Replace(",", "", StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out d)) ? Money.Round(Math.Max(0, d)) : 0;
        var name = S("name") ?? "";
        var unit = Enum.TryParse<ProductUnit>(S("unit") ?? "piece", ignoreCase: true, out var u) ? u : ProductUnit.Piece;
        var medicine = o["is_medicine"] is JsonValue m && m.TryGetValue<bool>(out var isMed) && isMed
            ? new MedicineInfo(S("generic_name"), S("strength"), S("dosage_form"), S("animals"))
            : null;
        var barcode = S("barcode");
        if (barcode is not null && (barcode.Length > 40 || barcode.Any(char.IsWhiteSpace))) barcode = null;
        var cost = D("cost_price");
        var price = D("selling_price");
        string? problem = name.Length == 0 ? "No name" : name.Length > 120 ? "Name too long" : price <= 0 ? "No selling price" : null;
        return new ProductProposal(name, S("category") ?? "General", unit, cost, price,
            o["reorder_level"] is JsonValue r && r.TryGetValue<int>(out var ro) ? Math.Clamp(ro, 0, 100_000) : 0,
            barcode, false, medicine, problem);
    }

    /// <summary>The fields of one product, shared with the assistant's "create products" tool.</summary>
    internal static JsonObject ProductItemSchema() => new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Name with pack size" },
                                    ["category"] = new JsonObject { ["type"] = "string" },
                                    ["unit"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("piece", "pack", "bag", "kg", "bottle", "box", "tin", "sachet", "tube", "strip") },
                                    ["cost_price"] = new JsonObject { ["type"] = "number" },
                                    ["selling_price"] = new JsonObject { ["type"] = "number" },
                                    ["reorder_level"] = new JsonObject { ["type"] = "integer" },
                                    ["barcode"] = new JsonObject { ["type"] = "string" },
                                    ["is_medicine"] = new JsonObject { ["type"] = "boolean" },
                                    ["generic_name"] = new JsonObject { ["type"] = "string" },
                                    ["strength"] = new JsonObject { ["type"] = "string" },
                                    ["dosage_form"] = new JsonObject { ["type"] = "string" },
                                    ["animals"] = new JsonObject { ["type"] = "string" },
                                },
                                ["required"] = new JsonArray("name", "selling_price"),
                            };

}
