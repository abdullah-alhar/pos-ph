using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;
using VetPos.Data.Configuration;

namespace VetPos.Data.Tests;

public class StorageRulesTests
{
    [Theory]
    [InlineData("0", 0L)]
    [InlineData("1234.56", 123456L)]
    [InlineData("-45.10", -4510L)]
    [InlineData("99999999.99", 9999999999L)]
    public void Money_is_stored_as_exact_cents(string value, long cents)
    {
        var money = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(cents, MoneyConverter.ToCents(money));
        Assert.Equal(money, MoneyConverter.FromCents(cents));
    }

    [Fact]
    public void Money_with_more_than_two_decimals_is_refused() =>
        Assert.Throws<InvalidOperationException>(() => MoneyConverter.ToCents(1.005m));

    [Fact]
    public async Task Money_round_trips_through_the_database()
    {
        await using var test = await TestDatabase.CreateInitializedAsync();
        int id;
        await using (var db = test.NewContext())
        {
            var category = new Category { Name = "Test" };
            var product = new Product { Name = "Item", Category = category, CostPrice = 1999.99m, SellingPrice = 2450.50m };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            id = product.Id;
        }
        await using var db2 = test.NewContext();
        var loaded = await db2.Products.SingleAsync(p => p.Id == id);
        Assert.Equal(1999.99m, loaded.CostPrice);
        Assert.Equal(2450.50m, loaded.SellingPrice);
        Assert.Equal(1, await db2.Products.CountAsync(p => p.SellingPrice > 2450.49m && p.SellingPrice < 2450.51m));
    }

    [Fact]
    public async Task Every_row_is_stamped_with_time_and_user()
    {
        await using var test = await TestDatabase.CreateInitializedAsync();
        test.User.UserId = null;
        var created = test.Clock.Now.UtcDateTime;
        int id;
        await using (var db = test.NewContext())
        {
            var user = new User { Username = "nimal", FullName = "Nimal", PinHash = "x" };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            test.User.UserId = user.Id;
            var supplier = new Supplier { Name = "Lanka Feeds" };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            id = supplier.Id;
            Assert.Equal(user.Id, supplier.CreatedById);
        }

        test.Clock.Now = test.Clock.Now.AddHours(2);
        test.User.UserId = 999;
        await using (var db = test.NewContext())
        {
            var s = await db.Suppliers.SingleAsync(x => x.Id == id);
            s.Phone = "077 123 4567";
            await db.SaveChangesAsync();
        }

        await using var check = test.NewContext();
        var saved = await check.Suppliers.SingleAsync(x => x.Id == id);
        Assert.Equal(created, saved.CreatedAt);
        Assert.Equal(created.AddHours(2), saved.UpdatedAt);
        Assert.NotEqual(999, saved.CreatedById);
    }

    [Fact]
    public async Task Negative_selling_price_is_rejected_by_the_database()
    {
        await using var test = await TestDatabase.CreateInitializedAsync();
        await using var db = test.NewContext();
        db.Products.Add(new Product { Name = "Bad", Category = new Category { Name = "C" }, SellingPrice = -1m });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Barcode_must_be_unique_but_may_be_empty()
    {
        await using var test = await TestDatabase.CreateInitializedAsync();
        await using var db = test.NewContext();
        var c = new Category { Name = "C" };
        db.Products.AddRange(new Product { Name = "A", Category = c }, new Product { Name = "B", Category = c });
        await db.SaveChangesAsync(); // two products without barcode are fine

        db.Products.AddRange(new Product { Name = "X", Category = c, Barcode = "123" }, new Product { Name = "Y", Category = c, Barcode = "123" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
