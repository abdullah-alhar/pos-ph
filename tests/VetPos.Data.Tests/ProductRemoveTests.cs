using VetPos.Core.Billing;
using VetPos.Core.Rules;
using VetPos.Data.Services;

namespace VetPos.Data.Tests;

public class ProductRemoveTests
{
    [Fact]
    public async Task Unused_products_are_deleted_and_used_ones_taken_off_sale()
    {
        var (test, owner, cashier, sup, cat) = await Shop.OpenAsync();
        await using var _ = test;
        var catalog = test.Get<CatalogService>();
        var unused = await Shop.ProductAsync(test, owner, cat, "Test collar", 500m);
        var sold = await Shop.ProductAsync(test, owner, cat, "Test shampoo", 900m);
        await Shop.ReceiveAsync(test, owner, sup, new ReceiptLineInput(sold, 5, 600m, 900m, null, null));
        await Shop.SellAsync(test, cashier, new BillCart(Guid.NewGuid(), [Shop.Line(sold, 1, 900m)], null));

        await Assert.ThrowsAsync<PosException>(() => catalog.RemoveAsync(unused, cashier));   // owner only

        Assert.True(await catalog.RemoveAsync(unused, owner));
        Assert.Null(await catalog.GetAsync(unused));

        Assert.False(await catalog.RemoveAsync(sold, owner));
        var kept = await catalog.GetAsync(sold);
        Assert.NotNull(kept);
        Assert.False(kept.IsActive);
        Assert.Empty(await catalog.SearchAsync("Test shampoo"));                              // gone from billing
    }
}
