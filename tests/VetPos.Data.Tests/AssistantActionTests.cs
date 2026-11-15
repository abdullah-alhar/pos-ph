using VetPos.AI;
using VetPos.Core.Rules;
using VetPos.Data.Services;

namespace VetPos.Data.Tests;

public class AssistantActionTests
{
    [Fact]
    public async Task Owner_can_create_products_with_the_AI_after_confirming()
    {
        var args = """{"products":[{"name":"Kitten food 1 kg","category":"Pet food","selling_price":1800},{"name":"Kitten food 2 kg","category":"Pet food","selling_price":3900},{"name":"Kitten food 5 kg","category":"Pet food","selling_price":5900}]}""";
        var fake = new FakeAi(FakeAi.ToolCall("propose_create_products", args), FakeAi.Answer("Check the card and press Confirm."));
        var (test, owner, _, ai, _) = await AssistantTests.SetupAsync(fake);
        await using var _ = test;

        var answer = await ai.AskAsync("create kitten food 1kg 2kg 5kg at 1800 3900 5900", [], owner);
        var action = Assert.IsType<CreateProductsAction>(Assert.Single(answer.Actions!));
        Assert.Equal(3, action.Products.Count);
        static async Task<int> Count(TestDatabase t) => (await t.Get<CatalogService>().SearchAsync("Kitten food")).Count(r => r.Name.StartsWith("Kitten food", StringComparison.Ordinal));
        Assert.Equal(0, await Count(test));      // nothing saved yet

        var done = await AssistantTests.Actions(test).ExecuteAsync(action, owner);
        Assert.Contains("Added 3", done, StringComparison.Ordinal);
        Assert.Equal(3, await Count(test));
    }

    [Fact]
    public async Task Owner_can_change_a_price_and_add_stock_with_the_AI()
    {
        var fake = new FakeAi(FakeAi.Answer("unused"));
        var (test, owner, cashier, _, _) = await AssistantTests.SetupAsync(fake);
        await using var _ = test;
        var tools = AssistantTests.Tools(test);
        var proposals = new List<AiAction>();

        await tools.RunAsync("propose_update_product", """{"product":"Royal Canin Kitten 2 kg","selling_price":5600}""", owner, default, proposals);
        await tools.RunAsync("propose_add_stock", """{"items":[{"product":"Royal Canin Kitten 2 kg","qty":6}]}""", owner, default, proposals);
        Assert.Equal(2, proposals.Count);
        Assert.Contains("5,600.00", proposals[0].Lines[0], StringComparison.Ordinal);

        foreach (var p in proposals) await AssistantTests.Actions(test).ExecuteAsync(p, owner);
        var row = Assert.Single(await test.Get<CatalogService>().SearchAsync("Royal Canin Kitten"));
        Assert.Equal(5600m, row.SellingPrice);
        Assert.Equal(14, row.StockQty);

        // A cashier cannot.
        var refused = await tools.RunAsync("propose_add_stock", """{"items":[{"product":"Royal Canin Kitten 2 kg","qty":1}]}""", cashier, default, proposals);
        Assert.Contains("only the owner", refused, StringComparison.Ordinal);
        await Assert.ThrowsAsync<PosException>(() => AssistantTests.Actions(test).ExecuteAsync(proposals[1], cashier));
    }
}
