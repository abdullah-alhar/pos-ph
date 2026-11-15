using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace VetPos.Data.Tests;

public class DatabaseInitializerTests
{
    [Fact]
    public async Task New_database_gets_every_migration_and_wal_mode()
    {
        await using var test = TestDatabase.Create();

        var result = await test.Initializer.InitializeAsync(addSampleData: false);

        Assert.True(result.Created);
        Assert.Contains(result.AppliedMigrations, m => m.EndsWith("_Initial", StringComparison.Ordinal));
        Assert.Contains(result.AppliedMigrations, m => m.EndsWith("_ProtectHistory", StringComparison.Ordinal));
        await using var db = test.NewContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await db.Database.OpenConnectionAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", (string?)await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Second_start_changes_nothing_and_does_not_duplicate_sample_data()
    {
        await using var test = TestDatabase.Create();
        await test.Initializer.InitializeAsync(addSampleData: true);
        int products;
        await using (var db = test.NewContext()) products = await db.Products.CountAsync();

        var again = await test.Initializer.InitializeAsync(addSampleData: true);

        Assert.False(again.Created);
        Assert.Empty(again.AppliedMigrations);
        Assert.False(again.SampleDataAdded);
        await using var db2 = test.NewContext();
        Assert.Equal(products, await db2.Products.CountAsync());
    }

    [Fact]
    public async Task Damaged_file_is_reported_not_overwritten()
    {
        await using var test = TestDatabase.Create();
        test.Paths.EnsureCreated();
        await File.WriteAllTextAsync(test.Paths.DatabaseFile, "this is not a database, just text that fills the header area of the file");

        await Assert.ThrowsAnyAsync<Exception>(() => test.Initializer.InitializeAsync(false));

        Assert.StartsWith("this is not a database", await File.ReadAllTextAsync(test.Paths.DatabaseFile), StringComparison.Ordinal);
        SqliteConnection.ClearAllPools();
    }
}
