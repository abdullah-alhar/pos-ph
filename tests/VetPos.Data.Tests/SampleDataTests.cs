using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;
using VetPos.Data.Seed;

namespace VetPos.Data.Tests;

public class SampleDataTests
{
    [Fact]
    public async Task Stock_on_each_product_equals_its_batches_and_its_movements()
    {
        await using var test = await TestDatabase.CreateInitializedAsync(sampleData: true);
        await using var db = test.NewContext();

        var products = await db.Products.Include(p => p.Batches).ToListAsync();
        var moves = await db.StockMovements.ToListAsync();

        Assert.True(products.Count >= 25);
        Assert.Contains(products, p => p.IsMedicine && p.GenericName == "Ivermectin");
        foreach (var p in products)
        {
            Assert.Equal(p.Batches.Sum(b => b.QtyOnHand), p.StockQty);
            Assert.Equal(p.StockQty, moves.Where(m => m.ProductId == p.Id).Sum(m => m.Change));
        }
    }

    [Fact]
    public async Task Sample_covers_every_billing_case()
    {
        await using var test = await TestDatabase.CreateInitializedAsync(sampleData: true);
        await using var db = test.NewContext();
        var today = new DateOnly(2026, 11, 13);
        var batches = await db.StockBatches.ToListAsync();
        var products = await db.Products.Include(p => p.Batches).ToListAsync();

        Assert.All(batches, b => Assert.Null(b.ExpiryDate));                                  // expiry is not used
        Assert.Contains(products, p => p.StockQty == 0);                                      // out of stock
        Assert.Contains(products, p => p.StockQty > 0 && p.StockQty <= p.ReorderLevel);       // low stock
        Assert.Contains(products, p => p.PriceChoice == PriceChoice.AskAtBilling
                                       && p.Batches.Select(b => b.SellingPrice).Distinct().Count() > 1);
        Assert.Contains(products, p => p.PriceChoice == PriceChoice.OldFirst);
        Assert.Equal(4, await db.Categories.CountAsync());
    }

    [Fact]
    public async Task Barcodes_are_unique_valid_in_store_ean13()
    {
        await using var test = await TestDatabase.CreateInitializedAsync(sampleData: true);
        await using var db = test.NewContext();
        var codes = await db.Products.Select(p => p.Barcode!).ToListAsync();

        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, c =>
        {
            Assert.Matches("^200[0-9]{10}$", c);
            var sum = 0;
            for (var i = 0; i < 12; i++) sum += (c[i] - '0') * (i % 2 == 0 ? 1 : 3);
            Assert.Equal((10 - sum % 10) % 10, c[12] - '0');
        });
    }

    [Theory]
    [InlineData(1, "2000000000015")]
    [InlineData(24, "2000000000244")]
    public void InStoreEan13_has_correct_check_digit(int n, string expected) =>
        Assert.Equal(expected, SampleData.InStoreEan13(n));
}
