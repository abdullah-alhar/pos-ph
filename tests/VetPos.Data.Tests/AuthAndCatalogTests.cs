using VetPos.Core.Domain;
using VetPos.Core.Rules;
using VetPos.Data.Services;

namespace VetPos.Data.Tests;

public class AuthAndCatalogTests
{
    [Fact]
    public async Task Login_with_username_and_pin_and_lockout_after_five_wrong()
    {
        var (test, _, _, _, _) = await Shop.OpenAsync();
        await using var _ = test;
        var auth = test.Get<AuthService>();

        Assert.Equal("Nimal", (await auth.LoginAsync("NIMAL", "2222")).FullName); // username is not case-sensitive
        for (var i = 0; i < 5; i++) await Assert.ThrowsAsync<PosException>(() => auth.LoginAsync("nimal", "0000"));
        var locked = await Assert.ThrowsAsync<PosException>(() => auth.LoginAsync("nimal", "2222"));
        Assert.Contains("Too many wrong PINs", locked.Message, StringComparison.Ordinal);

        test.Clock.Now = test.Clock.Now.AddMinutes(3);
        Assert.NotNull(await auth.LoginAsync("nimal", "2222"));
    }

    [Fact]
    public async Task Cashier_cannot_manage_users_and_last_owner_is_kept()
    {
        var (test, owner, cashier, _, _) = await Shop.OpenAsync();
        await using var _ = test;
        var auth = test.Get<AuthService>();
        await Assert.ThrowsAsync<PosException>(() => auth.SaveAsync(new UserInput(null, "x", "X", UserRole.Cashier, "1234", true), cashier));
        var ex = await Assert.ThrowsAsync<PosException>(() => auth.SaveAsync(new UserInput(owner.Id, "owner", "Shop Owner", UserRole.Cashier, null, true), owner));
        Assert.Contains("at least one active owner", ex.Message, StringComparison.Ordinal);
        Assert.Equal(owner.Id, (await auth.FindOwnerByPinAsync("1111"))!.Id);
        Assert.Null(await auth.FindOwnerByPinAsync("2222"));
    }

    [Fact]
    public async Task Barcodes_are_unique_across_main_and_extra_codes()
    {
        var (test, owner, _, _, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var catalog = test.Get<CatalogService>();
        var a = await catalog.SaveAsync(new ProductInput(null, "A", "111", ["222"], cat, ProductUnit.Piece, 1, 2, 0, false, true), owner);

        await Assert.ThrowsAsync<PosException>(() => catalog.SaveAsync(new ProductInput(null, "B", "222", [], cat, ProductUnit.Piece, 1, 2, 0, false, true), owner));
        Assert.Equal(a, await catalog.FindIdByBarcodeAsync("222"));
        Assert.Equal(a, await catalog.FindIdByBarcodeAsync("111"));
        Assert.Single(await catalog.SearchAsync("222"));
    }

    [Fact]
    public async Task An_exact_extra_barcode_ranks_ahead_of_a_product_name()
    {
        var (test, owner, _, _, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var catalog = test.Get<CatalogService>();
        var product = await catalog.SaveAsync(new ProductInput(null, "Pet food", "111", ["222"], cat, ProductUnit.Piece, 1, 2, 0, false, true), owner);
        await Shop.ProductAsync(test, owner, cat, "222 supplement", 50m);

        Assert.Equal(product, Assert.Single(await catalog.SearchAsync("222", limit: 1)).Id);
    }

    [Theory]
    [InlineData("itte")]      // middle of "Kitten"
    [InlineData("anin")]      // middle of "Canin"
    [InlineData("2 kg")]
    [InlineData("000000001")] // middle of the barcode
    [InlineData("pet")]       // the category
    public async Task Search_finds_text_in_the_middle_of_words(string text)
    {
        var (test, owner, _, _, cat) = await Shop.OpenAsync();
        await using var _ = test;
        await Shop.ProductAsync(test, owner, cat, "Royal Canin Kitten 2 kg", 5450m, barcode: "2000000000015");
        Assert.Contains(await test.Get<CatalogService>().SearchAsync(text), r => r.Name == "Royal Canin Kitten 2 kg");
    }

    [Fact]
    public async Task Search_matches_words_in_any_order()
    {
        var (test, owner, _, _, cat) = await Shop.OpenAsync();
        await using var _ = test;
        await Shop.ProductAsync(test, owner, cat, "Royal Canin Kitten 2 kg", 5450m);
        await Shop.ProductAsync(test, owner, cat, "Pedigree Adult 10 kg", 6200m);
        var rows = await test.Get<CatalogService>().SearchAsync("kitten royal");
        Assert.Equal("Royal Canin Kitten 2 kg", Assert.Single(rows).Name);
    }
}

public class InStoreBarcodeTests
{
    [Fact]
    public async Task Product_without_barcode_gets_a_valid_unique_in_store_ean13()
    {
        var (test, owner, _, _, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var catalog = test.Get<CatalogService>();
        var a = await Shop.ProductAsync(test, owner, cat, "Loose item A", 100m, expiry: false);
        var code = await catalog.AssignInStoreBarcodeAsync(a, owner);
        Assert.True(VetPos.Printing.Barcodes.IsEan13(code));
        Assert.StartsWith("200", code, StringComparison.Ordinal);
        Assert.Equal(a, await catalog.FindIdByBarcodeAsync(code));
        Assert.Equal(code, await catalog.AssignInStoreBarcodeAsync(a, owner)); // asking again keeps it
    }
}
