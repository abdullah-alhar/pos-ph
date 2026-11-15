using Microsoft.EntityFrameworkCore;
using VetPos.Core.Domain;

namespace VetPos.Data.Tests;

/// <summary>The database itself refuses to change or delete history (bills, payments, stock, audit).</summary>
public class HistoryProtectionTests
{
    private static async Task<(TestDatabase Test, int SaleId)> WithSaleAsync()
    {
        var test = await TestDatabase.CreateInitializedAsync(sampleData: true);
        await using var db = test.NewContext();
        var cashier = new User { Username = "kamala", FullName = "Kamala", PinHash = "x" };
        db.Users.Add(cashier);
        await db.SaveChangesAsync();
        var product = await db.Products.FirstAsync(p => p.StockQty > 0);
        var sale = new Sale
        {
            ClientId = Guid.NewGuid(), Number = 1, CashierId = cashier.Id,
            Subtotal = 100m, Total = 100m, Paid = 100m,
            Lines = { new SaleLine { LineNo = 1, ProductId = product.Id, ProductName = product.Name, Qty = 1, ListPrice = 100m, UnitPrice = 100m, LineTotal = 100m } },
            Payments = { new Payment { Method = PaymentMethod.Cash, Amount = 100m, Tendered = 100m } },
        };
        db.Sales.Add(sale);
        db.AuditLog.Add(new AuditEntry { Action = "TEST", Summary = "test entry" });
        await db.SaveChangesAsync();
        return (test, sale.Id);
    }

    private static Task<int> Sql(VetPosDbContext db, string sql) => db.Database.ExecuteSqlRawAsync(sql);

    [Theory]
    [InlineData("UPDATE \"AuditLog\" SET \"Summary\" = 'edited'")]
    [InlineData("DELETE FROM \"AuditLog\"")]
    [InlineData("UPDATE \"Payments\" SET \"Amount\" = 1")]
    [InlineData("DELETE FROM \"Payments\"")]
    [InlineData("UPDATE \"SaleLines\" SET \"UnitPrice\" = 1")]
    [InlineData("DELETE FROM \"SaleLines\"")]
    [InlineData("UPDATE \"StockMovements\" SET \"Change\" = 0")]
    [InlineData("DELETE FROM \"StockMovements\"")]
    [InlineData("DELETE FROM \"Sales\"")]
    [InlineData("UPDATE \"Sales\" SET \"Total\" = 1")]
    [InlineData("UPDATE \"Sales\" SET \"Number\" = 99")]
    public async Task History_cannot_be_changed(string sql)
    {
        var (test, _) = await WithSaleAsync();
        await using (test)
        {
            await using var db = test.NewContext();
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => Sql(db, sql));
            Assert.Contains("cannot be", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_bill_can_be_voided_once_only()
    {
        var (test, saleId) = await WithSaleAsync();
        await using (test)
        {
            await using var db = test.NewContext();
            var sale = await db.Sales.SingleAsync(s => s.Id == saleId);
            sale.Status = SaleStatus.Voided;
            sale.VoidReason = "Customer changed mind";
            sale.VoidedAt = test.Clock.Now.UtcDateTime;
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                Sql(db, $"UPDATE \"Sales\" SET \"Status\" = 0 WHERE \"Id\" = {saleId}"));
            Assert.Contains("already voided", ex.Message, StringComparison.Ordinal);
        }
    }
}
