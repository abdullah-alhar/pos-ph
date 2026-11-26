using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;
using VetPos.Data.Services;

namespace VetPos.AI;

/// <summary>
/// The tools the AI may call. Reading tools run here, on the shop's own data; the AI only sees
/// their results. Change tools (owner only) never change anything themselves: they prepare a card
/// the person checks and confirms (see <see cref="AssistantActions"/>).
/// </summary>
public sealed class AssistantTools(ReportService reports, CatalogService catalog, SaleService sales, ShopClock clock, AssistantActions actions)
{
    private static readonly (string Target, string Label, bool OwnerOnly)[] Screens =
    [
        ("home", "Open overview", false), ("billing", "Open billing", false),
        ("products", "Open products", false), ("products:low", "Open low stock", false),
        ("stock", "Open stock entry", false), ("labels", "Open labels", false),
        ("reports:today", "Open today's report", false), ("reports:sales", "Open sales report", false),
        ("reports:expiry", "Open stock and expiry report", false), ("reports:bills", "Open bills", false),
        ("reports:profit", "Open profit report", true), ("receipt", "Open bill editor", true),
        ("settings:ai", "Open AI settings", true),
    ];

    internal static AiLink? ScreenLink(string? target, Actor actor) =>
        Screens.Where(s => s.Target == target && (!s.OwnerOnly || actor.IsOwner))
            .Select(s => new AiLink(s.Label, s.Target)).FirstOrDefault();

    private static JsonObject Fn(string name, string description, JsonObject properties, params string[] required) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()),
            },
        },
    };

    private static JsonObject Date(string what) => new() { ["type"] = "string", ["description"] = $"{what}, format YYYY-MM-DD" };

    public static JsonArray Definitions(Actor actor)
    {
        var list = new JsonArray
        {
            Fn("get_sales_summary", "Sales between two dates (inclusive): bills, sales total, discounts, by payment type and by cashier.",
                new JsonObject { ["from_date"] = Date("First day"), ["to_date"] = Date("Last day") }, "from_date", "to_date"),
            Fn("get_top_products", "Best-selling products between two dates, by sales amount.",
                new JsonObject { ["from_date"] = Date("First day"), ["to_date"] = Date("Last day"), ["limit"] = new JsonObject { ["type"] = "integer", ["description"] = "How many, 1 to 30" } },
                "from_date", "to_date"),
            Fn("get_expiring_stock", "List stock batches that are expired or will expire soon, with remaining quantities.",
                new JsonObject { ["days"] = new JsonObject { ["type"] = "integer", ["description"] = "Days ahead, 1 to 90 (default 30)" } }),
            Fn("get_categories", "List product categories and product counts.", []),
            Fn("open_screen", "Offer a button that opens the requested VetPOS screen. The user decides whether to open it.",
                new JsonObject { ["target"] = new JsonObject { ["type"] = "string",
                    ["enum"] = new JsonArray(Screens.Where(s => !s.OwnerOnly || actor.IsOwner).Select(s => (JsonNode)JsonValue.Create(s.Target)).ToArray()) } }, "target"),
            Fn("get_low_stock", "Products at or below their reorder level.", []),
            Fn("search_product", "Find products by name, generic name or barcode: price and stock.",
                new JsonObject { ["name_or_barcode"] = new JsonObject { ["type"] = "string" } }, "name_or_barcode"),
        };
        if (actor.IsOwner)
        {
            list.Add(Fn("propose_create_category", "Prepare a new product category for the owner to confirm.",
                new JsonObject { ["name"] = new JsonObject { ["type"] = "string" } }, "name"));
            list.Add(Fn("propose_create_products",
                "Prepare NEW products for the owner to confirm (nothing is saved until they press Confirm). One product per pack size; put the size in the name.",
                new JsonObject { ["products"] = new JsonObject { ["type"] = "array", ["items"] = ProductAssistant.ProductItemSchema() } }, "products"));
            list.Add(Fn("propose_update_product",
                "Prepare a change to ONE existing product (price, cost, name, category, reorder level, or switch it on/off) for the owner to confirm.",
                new JsonObject
                {
                    ["product"] = new JsonObject { ["type"] = "string", ["description"] = "Current product name or barcode" },
                    ["new_name"] = new JsonObject { ["type"] = "string" },
                    ["selling_price"] = new JsonObject { ["type"] = "number" },
                    ["cost_price"] = new JsonObject { ["type"] = "number" },
                    ["category"] = new JsonObject { ["type"] = "string" },
                    ["reorder_level"] = new JsonObject { ["type"] = "integer" },
                    ["active"] = new JsonObject { ["type"] = "boolean", ["description"] = "false = switch off (hide from billing)" },
                }, "product"));
            list.Add(Fn("propose_remove_product",
                "Prepare removing ONE product for the owner to confirm (unused products are deleted; products with history are taken off sale).",
                new JsonObject { ["product"] = new JsonObject { ["type"] = "string", ["description"] = "Product name or barcode" } }, "product"));
            list.Add(Fn("propose_add_stock",
                "Prepare a stock delivery (stock in) for the owner to confirm: products and quantities received, optionally cost and selling price.",
                new JsonObject
                {
                    ["items"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["product"] = new JsonObject { ["type"] = "string", ["description"] = "Product name or barcode" },
                                ["qty"] = new JsonObject { ["type"] = "integer" },
                                ["cost_price"] = new JsonObject { ["type"] = "number" },
                                ["selling_price"] = new JsonObject { ["type"] = "number" },
                            },
                            ["required"] = new JsonArray("product", "qty"),
                        },
                    },
                    ["invoice_no"] = new JsonObject { ["type"] = "string" },
                }, "items"));
            list.Add(Fn("get_profit", "Profit between two dates: sales, cost of goods (from the batches sold) and profit, also per product.",
                new JsonObject { ["from_date"] = Date("First day"), ["to_date"] = Date("Last day") }, "from_date", "to_date"));
        }
        return list;
    }

    /// <summary>Runs one tool. Bad arguments come back as text the AI can read and correct.</summary>
    public async Task<string> RunAsync(string name, string argumentsJson, Actor actor, CancellationToken ct, ICollection<AiAction>? proposals = null)
    {
        if (argumentsJson.Length > 64_000) return Error("arguments exceed the tool limit");
        JsonObject args;
        try
        {
            if (JsonNode.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson) is not JsonObject parsed)
                return Error("arguments must be a JSON object");
            args = parsed;
        }
        catch (JsonException) { return Error("arguments are not valid JSON"); }

        try
        {
            switch (name)
            {
                case "propose_create_products" or "propose_update_product" or "propose_add_stock" or "propose_remove_product" or "propose_create_category":
                {
                    if (!actor.IsOwner) return Error("only the owner can change products or stock; tell the person to ask the owner");
                    if (proposals is null) return Error("changes are not possible here");
                    if (proposals.Count >= 5) return Error("review the pending changes before proposing more");
                    AiAction action = name switch
                    {
                        "propose_create_category" => AssistantActions.BuildCategory(args),
                        "propose_create_products" => AssistantActions.BuildCreate(args),
                        "propose_update_product" => await actions.BuildUpdateAsync(args, ct),
                        "propose_remove_product" => await actions.BuildRemoveAsync(args, ct),
                        _ => await actions.BuildAddStockAsync(args, ct),
                    };
                    proposals.Add(action);
                    return Json(new
                    {
                        status = "waiting_for_confirmation",
                        card = action.Title,
                        details = action.Lines,
                        instruction = "Nothing is saved yet. Tell the person in one short sentence to check the card below and press Confirm.",
                    });
                }
                case "open_screen":
                {
                    var link = ScreenLink(Text(args, "target"), actor);
                    return link is null ? Error("that screen is unavailable for this role") : Json(new { target = link.Target });
                }
                case "get_expiring_stock":
                {
                    var days = Math.Clamp(Int(args, "days") ?? 30, 1, 90);
                    var rows = await catalog.ExpiringAsync(days, ct);
                    return Json(new { count = rows.Count, batches = rows.Take(30).Select(r => new
                    {
                        product = r.ProductName, expiry = Iso(r.ExpiryDate), quantity = r.QtyOnHand, days_left = r.DaysLeft,
                    }) });
                }
                case "get_categories":
                {
                    var rows = await catalog.CategoriesAsync(ct);
                    return Json(new { count = rows.Count, categories = rows.Take(50).Select(r => new { name = r.Name, products = r.ProductCount }) });
                }
                case "get_sales_summary":
                {
                    var (from, to) = Range(args);
                    var s = await reports.SummaryAsync(from, to, ct: ct);
                    return Json(new
                    {
                        today = Iso(clock.Today), from = Iso(from), to = Iso(to), bills = s.Bills, voided_bills = s.VoidedBills, units_sold = s.Units,
                        sales = M(s.Sales), discounts = M(s.Discounts), average_bill = M(s.AverageBill),
                        by_payment = s.ByPayment.Select(p => new { method = p.Label, bills = p.Bills, amount = M(p.Amount) }),
                        by_cashier = actor.IsOwner ? s.ByCashier.Take(30).Select(c => new { cashier = c.Label, bills = c.Bills, amount = M(c.Amount) }) : null,
                    });
                }
                case "get_top_products":
                {
                    var (from, to) = Range(args);
                    var limit = Math.Clamp(Int(args, "limit") ?? 10, 1, 30);
                    var rows = await reports.ByProductAsync(from, to, limit, ct);
                    return Json(new { from = Iso(from), to = Iso(to), products = rows.Select(r => new { name = r.Name, units = r.Units, sales = M(r.Sales) }) });
                }
                case "get_low_stock":
                {
                    var rows = await catalog.LowStockCountsAsync(ct);
                    return Json(new { count = rows.Count, products = rows.Take(30).Select(r => new { name = r.Name, stock = r.StockQty, reorder_level = r.ReorderLevel }) });
                }
                case "search_product":
                {
                    var text = Text(args, "name_or_barcode")?.Trim() ?? "";
                    if (text.Length is 0 or > 200) return Error("name_or_barcode must be between 1 and 200 characters");
                    var rows = await catalog.SearchAsync(text, limit: 8, ct: ct);
                    var result = new List<object>();
                    foreach (var r in rows)
                    {
                        var item = await sales.GetBillingItemAsync(r.Id, ct);
                        var prices = item?.PriceOptions;
                        result.Add(new
                        {
                            name = r.Name, barcode = r.Barcode, category = r.Category, price = M(prices is { Count: > 0 } ? prices[0].Price : r.SellingPrice),
                            stock = r.StockQty, generic_name = r.GenericName,
                            prices_on_hand = prices?.Take(15).Select(p => new { price = M(p.Price), qty = p.QtyAvailable }),
                        });
                    }
                    return Json(new { found = result.Count, products = result });
                }
                case "get_profit":
                {
                    if (!actor.IsOwner) return Error("only the owner can see profit");
                    var (from, to) = Range(args);
                    var s = await reports.SummaryAsync(from, to, ct: ct);
                    var rows = await reports.ByProductAsync(from, to, 10, ct);
                    return Json(new
                    {
                        from = Iso(from), to = Iso(to), sales = M(s.Sales), cost_of_goods = M(s.Cost), profit = M(s.Profit), margin_percent = s.MarginPercent,
                        top_products = rows.Select(r => new { name = r.Name, sales = M(r.Sales), profit = M(r.Profit) }),
                    });
                }
                default:
                    return Error($"there is no tool called {name}");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or PosException)
        {
            return Error(ex.Message);
        }
    }

    private static (DateOnly From, DateOnly To) Range(JsonObject args)
    {
        var from = ParseDate(args, "from_date");
        var to = ParseDate(args, "to_date");
        if (to < from) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > 366) throw new ArgumentException("the range can be at most one year");
        return (from, to);
    }

    private static DateOnly ParseDate(JsonObject args, string key)
    {
        var text = Text(args, key);
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new ArgumentException($"{key} must be a date like 2026-11-13");
    }

    private static string? Text(JsonObject args, string key) =>
        args[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? Int(JsonObject args, string key) =>
        args[key] is JsonValue v && (v.TryGetValue<int>(out var i) || (v.TryGetValue<string>(out var s) && int.TryParse(s, CultureInfo.InvariantCulture, out i))) ? i : null;

    private static string M(decimal v) => "Rs " + Money.Format(v);
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Json(object o) => JsonSerializer.Serialize(o);
    private static string Error(string message) => Json(new { error = message });
}
