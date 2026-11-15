using Microsoft.EntityFrameworkCore;
using VetPos.Core.Billing;
using VetPos.Core.Domain;
using VetPos.Core.Pricing;
using VetPos.Core.Rules;
using VetPos.Data.Services;

namespace VetPos.Data.Tests;

/// <summary>Helpers that build a small shop: an owner, a cashier, a supplier and products with batches.</summary>
internal static class Shop
{
    public static async Task<(TestDatabase Db, Actor Owner, Actor Cashier, int SupplierId, int CategoryId)> OpenAsync()
    {
        var test = await TestDatabase.CreateInitializedAsync();
        var auth = test.Get<AuthService>();
        var owner = (await auth.CreateFirstOwnerAsync("owner", "Shop Owner", "1111")).ToActor();
        var cashier = (await auth.SaveAsync(new UserInput(null, "nimal", "Nimal", UserRole.Cashier, "2222", true), owner)).ToActor();
        var catalog = test.Get<CatalogService>();
        var cat = await catalog.SaveCategoryAsync(null, "Pet food", owner);
        var sup = await catalog.SaveSupplierAsync(new SupplierRow(0, "Lanka Pet Supplies", null, null, true), owner);
        return (test, owner, cashier, sup, cat);
    }

    public static Task<int> ProductAsync(TestDatabase test, Actor owner, int categoryId, string name, decimal price, bool expiry = true, string? barcode = null) =>
        test.Get<CatalogService>().SaveAsync(new ProductInput(null, name, barcode, [], categoryId, ProductUnit.Bag, price * 0.8m, price, 3, expiry, true), owner);

    public static Task<ReceiptRow> ReceiveAsync(TestDatabase test, Actor owner, int supplierId, params ReceiptLineInput[] lines) =>
        ReceiveAsync(test, owner, supplierId, new Dictionary<int, PriceDecision>(), lines);

    public static Task<ReceiptRow> ReceiveAsync(TestDatabase test, Actor owner, int supplierId, Dictionary<int, PriceDecision> decisions, params ReceiptLineInput[] lines) =>
        test.Get<StockEntryService>().SaveAsync(new ReceiptInput(supplierId, null, new DateOnly(2026, 11, 13), null, lines), decisions, owner);

    public static DateOnly Today => new(2026, 11, 13);

    public static BillLine Line(int productId, int qty, decimal price, decimal? listPrice = null, Discount? discount = null, decimal? group = null) =>
        new(productId, "Item", null, qty, listPrice ?? price, price, discount, group);

    public static Task<CheckoutResult> SellAsync(TestDatabase test, Actor cashier, BillCart cart, decimal? cash = null) =>
        test.Get<SaleService>().CheckoutAsync(new CheckoutRequest(cart, [new PaymentInput(PaymentMethod.Cash, cash ?? cart.Totals().Total)]), cashier);

    public static async Task<(int Stock, string Batches)> StockAsync(TestDatabase test, int productId)
    {
        await using var db = test.NewContext();
        var p = await db.Products.Include(x => x.Batches).SingleAsync(x => x.Id == productId);
        return (p.StockQty, string.Join(",", p.Batches.OrderBy(b => b.Id).Select(b => b.QtyOnHand)));
    }
}
