using VetPos.Data.Services;

namespace VetPos.Data.Tests;

public class SearchRankingTests
{
    [Fact]
    public async Task One_letter_finds_products_and_names_starting_with_it_come_first()
    {
        var (test, owner, _, _, cat) = await Shop.OpenAsync();
        await using var _ = test;
        await Shop.ProductAsync(test, owner, cat, "Royal Canin Kitten 2 kg", 5450m);
        await Shop.ProductAsync(test, owner, cat, "Kitten food 1 kg", 1899m);
        await Shop.ProductAsync(test, owner, cat, "Whiskas Kitten 1 kg", 1800m);

        var rows = await test.Get<CatalogService>().SearchAsync("k");
        Assert.True(rows.Count >= 3);
        Assert.Equal("Kitten food 1 kg", rows[0].Name);            // starts with "k"

        var kit = await test.Get<CatalogService>().SearchAsync("kitten");
        Assert.Equal("Kitten food 1 kg", kit[0].Name);              // name starts with the word
        Assert.Equal(3, kit.Count(r => r.Name.Contains("Kitten", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Percent_and_underscore_are_searched_as_plain_text()
    {
        var (test, owner, _, _, cat) = await Shop.OpenAsync();
        await using var _ = test;
        await Shop.ProductAsync(test, owner, cat, "Ivermectin 1% 50 ml", 1650m);
        await Shop.ProductAsync(test, owner, cat, "Ivermectin 10 ml", 900m);

        var rows = await test.Get<CatalogService>().SearchAsync("1%");
        Assert.Single(rows, r => r.Name.StartsWith("Ivermectin", StringComparison.Ordinal));
    }
}
