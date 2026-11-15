using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VetPos.Core.Infrastructure;

namespace VetPos.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>The SQLite connection string for a database file.</summary>
    public static string ConnectionStringFor(string databaseFile) => new SqliteConnectionStringBuilder
    {
        DataSource = databaseFile,
        Mode = SqliteOpenMode.ReadWriteCreate,
        ForeignKeys = true,
        // Wait for a moment instead of failing if a backup is reading the file.
        DefaultTimeout = 10,
        Pooling = true,
    }.ToString();

    public static IServiceCollection AddVetPosData(this IServiceCollection services, AppPaths paths)
    {
        services.AddSingleton(paths);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<EntityStampInterceptor>();
        services.AddDbContextFactory<VetPosDbContext>((sp, options) => options
            .UseSqlite(ConnectionStringFor(paths.DatabaseFile))
            .AddInterceptors(sp.GetRequiredService<EntityStampInterceptor>()));
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<Services.ShopClock>();
        services.AddSingleton<Services.SettingsService>();
        services.AddSingleton<Services.AuthService>();
        services.AddSingleton<Services.CatalogService>();
        services.AddSingleton<Services.StockEntryService>();
        services.AddSingleton<Services.SaleService>();
        services.AddSingleton<Services.BillStore>();
        services.AddSingleton<Services.ReportService>();
        services.AddSingleton<Services.BackupService>();
        services.AddSingleton<Services.NotificationService>();
        return services;
    }
}
