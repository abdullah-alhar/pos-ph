using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VetPos.AI;
using VetPos.Core.Billing;
using VetPos.Core.Rules;
using VetPos.Core.Settings;
using VetPos.Data.Services;

namespace VetPos.Data.Tests;

internal sealed class PlainKeys : IAiKeyProtector
{
    public string Protect(string plain) => "enc:" + plain;
    public string? Unprotect(string protectedValue) => protectedValue.StartsWith("enc:", StringComparison.Ordinal) ? protectedValue[4..] : null;
}

/// <summary>A fake AI service: answers each request with the next canned reply and keeps what was sent.</summary>
internal sealed class FakeAi(params Func<HttpRequestMessage, HttpResponseMessage>[] replies) : HttpMessageHandler
{
    private int _next;
    public List<JsonNode> Sent { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Sent.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!);
        return replies[Math.Min(_next++, replies.Length - 1)](request);
    }

    public static Func<HttpRequestMessage, HttpResponseMessage> ToolCall(string name, string args) => _ => Json(new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject
        {
            ["message"] = new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = null,
                ["tool_calls"] = new JsonArray(new JsonObject { ["id"] = "c1", ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["arguments"] = args } }),
            },
        }),
    });

    public static Func<HttpRequestMessage, HttpResponseMessage> Answer(string text) => _ =>
        Json(new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text } }) });

    private static HttpResponseMessage Json(JsonObject o) => new(HttpStatusCode.OK) { Content = new StringContent(o.ToJsonString(), Encoding.UTF8, "application/json") };
}

public class AssistantTests
{
    internal static AssistantActions Actions(TestDatabase test) => new(test.Get<CatalogService>(), test.Get<StockEntryService>(),
        new ProductAssistant(test.Get<CatalogService>()),
        test.Get<ShopClock>());

    internal static AssistantTools Tools(TestDatabase test) =>
        new(test.Get<ReportService>(), test.Get<CatalogService>(), test.Get<SaleService>(), test.Get<ShopClock>(), Actions(test));

    internal static async Task<(TestDatabase Test, Actor Owner, Actor Cashier, AssistantService Ai, FakeAi Fake)> SetupAsync(FakeAi fake, bool enabled = true)
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        var p = await Shop.ProductAsync(test, owner, cat, "Royal Canin Kitten 2 kg", 5450m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(p, 10, 4600m, 5450m, "RC", Shop.Today.AddDays(20)));
        await Shop.SellAsync(test, cashier, new BillCart(Guid.NewGuid(), [Shop.Line(p, 2, 5450m)], null));
        await test.Get<SettingsService>().SaveAsync(AiSettings.Key, new AiSettings { Enabled = enabled, Provider = "groq", EncryptedKey = "enc:gsk_test_1234" }, owner);
        var ai = new AssistantService(new AiClient(new HttpClient(fake)),
            Tools(test),
            test.Get<SettingsService>(), new PlainKeys(), test.Get<IDbContextFactory<VetPosDbContext>>(), test.Get<ShopClock>(), NullLogger<AssistantService>.Instance);
        return (test, owner, cashier, ai, fake);
    }

    [Fact]
    public async Task Answers_from_tool_results_and_logs_everything()
    {
        var fake = new FakeAi(FakeAi.ToolCall("get_sales_summary", """{"from_date":"2026-11-13","to_date":"2026-11-13"}"""), FakeAi.Answer("Today: 1 bill, Rs 10,900.00."));
        var (test, owner, _, ai, _) = await SetupAsync(fake);
        await using var _ = test;

        var answer = await ai.AskAsync("How much did we sell today?", [], owner);

        Assert.Equal("Today: 1 bill, Rs 10,900.00.", answer.Text);
        var tool = Assert.Single(answer.Tools);
        Assert.Contains("\"sales\":\"Rs 10,900.00\"", tool.Result, StringComparison.Ordinal);
        // The tool result went back to the AI, with the key in the header.
        Assert.Equal("tool", fake.Sent[1]["messages"]!.AsArray().Last()!["role"]!.GetValue<string>());
        Assert.Contains("Never invent", fake.Sent[0]["messages"]![0]!["content"]!.GetValue<string>(), StringComparison.Ordinal);
        await using var db = test.NewContext();
        var log = await db.AiLog.SingleAsync();
        Assert.Equal("How much did we sell today?", log.Question);
        Assert.Contains("get_sales_summary", log.ToolsJson, StringComparison.Ordinal);
        Assert.Equal(answer.Text, log.Answer);
    }

    [Fact]
    public async Task Offline_service_gives_a_clear_message_and_is_logged()
    {
        var fake = new FakeAi(_ => throw new HttpRequestException("no network"));
        var (test, owner, _, ai, _) = await SetupAsync(fake);
        await using var _ = test;

        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => ai.AskAsync("Sales today?", [], owner));
        Assert.StartsWith("AI is offline", ex.Message, StringComparison.Ordinal);
        await using var db = test.NewContext();
        Assert.NotNull((await db.AiLog.SingleAsync()).Error);
    }

    [Fact]
    public async Task Switched_off_assistant_says_so()
    {
        var (test, owner, _, ai, _) = await SetupAsync(new FakeAi(FakeAi.Answer("x")), enabled: false);
        await using var _ = test;
        Assert.NotNull(await ai.ProblemAsync());
        await Assert.ThrowsAsync<AiUnavailableException>(() => ai.AskAsync("hi", [], owner));
    }

    [Fact]
    public async Task Cashier_cannot_see_profit()
    {
        var fake = new FakeAi(FakeAi.ToolCall("get_profit", """{"from_date":"2026-11-01","to_date":"2026-11-13"}"""), FakeAi.Answer("I cannot see profit."));
        var (test, _, cashier, ai, _) = await SetupAsync(fake);
        await using var _ = test;

        var answer = await ai.AskAsync("What is the profit?", [], cashier);

        Assert.Contains("only the owner", answer.Tools.Single().Result, StringComparison.Ordinal);
        var tools = fake.Sent[0]["tools"]!.AsArray().Select(t => t!["function"]!["name"]!.GetValue<string>());
        Assert.DoesNotContain("get_profit", tools);
    }

    [Fact]
    public async Task Product_search_uses_real_data_and_removed_tools_answer_clearly()
    {
        var fake = new FakeAi(FakeAi.Answer("unused"));
        var (test, owner, _, _, _) = await SetupAsync(fake);
        await using var _ = test;
        var tools = Tools(test);

        Assert.Contains("no tool", await tools.RunAsync("get_expiring_items", """{"days":30}""", owner, default), StringComparison.Ordinal);
        var search = await tools.RunAsync("search_product", """{"name_or_barcode":"kitten"}""", owner, default);
        Assert.Contains("\"stock\":8", search, StringComparison.Ordinal);
        var bad = await tools.RunAsync("get_sales_summary", """{"from_date":"yesterday"}""", owner, default);
        Assert.Contains("error", bad, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Category_changes_and_privileged_navigation_require_owner_permissions()
    {
        var (test, owner, cashier, _, _) = await SetupAsync(new FakeAi(FakeAi.Answer("unused")));
        await using var _ = test;
        var tools = Tools(test);
        var proposals = new List<AiAction>();

        var category = """{"name":"Pet grooming"}""";
        Assert.Contains("error", await tools.RunAsync("propose_create_category", category, cashier, default, proposals), StringComparison.Ordinal);
        Assert.Empty(proposals);
        Assert.Contains("waiting_for_confirmation", await tools.RunAsync("propose_create_category", category, owner, default, proposals), StringComparison.Ordinal);
        var action = Assert.IsType<CreateCategoryAction>(Assert.Single(proposals));
        var catalog = test.Get<CatalogService>();
        Assert.DoesNotContain(await catalog.CategoriesAsync(), c => c.Name == "Pet grooming");

        var executor = new AssistantActions(catalog, test.Get<StockEntryService>(), new ProductAssistant(catalog), test.Get<ShopClock>());
        await Assert.ThrowsAsync<VetPos.Core.Rules.PosException>(() => executor.ExecuteAsync(action, cashier));
        await executor.ExecuteAsync(action, owner);
        Assert.Contains(await catalog.CategoriesAsync(), c => c.Name == "Pet grooming");

        var open = """{"target":"reports:profit"}""";
        var denied = await tools.RunAsync("open_screen", open, cashier, default);
        Assert.Empty(AssistantService.LinksFor([new ToolUse("open_screen", open, denied)], cashier));
        var allowed = await tools.RunAsync("open_screen", open, owner, default);
        Assert.Equal("reports:profit", Assert.Single(AssistantService.LinksFor([new ToolUse("open_screen", open, allowed)], owner)).Target);
        Assert.Contains("error", await tools.RunAsync("search_product", """{"name_or_barcode":[]}""", cashier, default), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gsk_abcdefgh9f3a", "gsk_…9f3a")]
    [InlineData("AIzaSyD-abcdefg1234", "…1234")]
    public void Key_hint_never_shows_the_key(string key, string hint) => Assert.Equal(hint, AiProviders.KeyHint(key));

    [Theory]
    [InlineData("https://api.example.com/v1", true)]
    [InlineData("http://localhost:11434/v1", true)]
    [InlineData("http://192.168.1.20:1234/v1", true)]
    [InlineData("http://example.com/v1", false)]
    [InlineData("ftp://x", false)]
    public void Only_safe_ai_addresses(string url, bool ok) => Assert.Equal(ok, AiProviders.IsSafeUrl(url));
}

public class ProductAssistantTests
{
    [Fact]
    public async Task Reads_a_list_into_products_and_creates_only_after_confirming()
    {
        var (test, owner, _, _, _) = await Shop.OpenAsync();
        await using var _ = test;
        await test.Get<SettingsService>().SaveAsync(AiSettings.Key, new AiSettings { Enabled = true, Provider = "groq", EncryptedKey = "enc:gsk_test_1234" }, owner);
        var args = """{"products":[{"name":"Whiskas Adult 1.2 kg","category":"Pet food","unit":"pack","cost_price":1900,"selling_price":2350,"has_expiry":true},{"name":"Ivermectin 1% 50 ml","category":"Animal medicine","unit":"bottle","selling_price":1650,"is_medicine":true,"generic_name":"Ivermectin","animals":"Cattle"},{"name":"Mystery","selling_price":0}]}""";
        var ai = new ProductAssistant(test.Get<CatalogService>());

        // What the AI chat's "create products" tool receives.
        var list = AssistantActions.BuildCreate((System.Text.Json.Nodes.JsonNode.Parse(args) as System.Text.Json.Nodes.JsonObject)!).Products;
        Assert.Equal(3, list.Count);
        Assert.Equal("No selling price", list[2].Problem);
        Assert.Equal("Ivermectin", list[1].Medicine!.GenericName);
        Assert.Empty(await test.Get<CatalogService>().SearchAsync("Whiskas")); // nothing saved yet

        Assert.Equal(2, await ai.CreateAsync(list, owner));
        Assert.Single(await test.Get<CatalogService>().SearchAsync("ivermectin"));
        Assert.Contains(await test.Get<CatalogService>().CategoriesAsync(), c => c.Name == "Animal medicine");
    }

    [Fact]
    public void Answers_get_links_to_the_right_screens()
    {
        var owner = new VetPos.Core.Rules.Actor(1, "O", VetPos.Core.Domain.UserRole.Owner);
        var links = AssistantService.LinksFor([new ToolUse("get_low_stock", "{}", "{}")], owner);
        Assert.Contains(links, l => l.Target == "products:low");
    }
}
