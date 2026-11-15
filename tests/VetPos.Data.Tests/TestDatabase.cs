using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VetPos.Core.Abstractions;
using VetPos.Core.Infrastructure;

namespace VetPos.Data.Tests;

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

internal sealed class TestUser : ICurrentUser
{
    public int? UserId { get; set; }
}

/// <summary>A real SQLite file in a temp folder, wired exactly like the app.</summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private TestDatabase(ServiceProvider services, AppPaths paths)
    {
        _services = services;
        Paths = paths;
    }

    public AppPaths Paths { get; }
    public FixedClock Clock => (FixedClock)_services.GetRequiredService<TimeProvider>();
    public TestUser User => (TestUser)_services.GetRequiredService<ICurrentUser>();
    public DatabaseInitializer Initializer => _services.GetRequiredService<DatabaseInitializer>();

    public T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    public static TestDatabase Create()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "vetpos-test-" + Guid.NewGuid().ToString("N")));
        var services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddSingleton<TimeProvider>(new FixedClock(new DateTimeOffset(2026, 11, 13, 6, 30, 0, TimeSpan.Zero)))
            .AddSingleton<ICurrentUser>(new TestUser())
            .AddVetPosData(paths)
            .BuildServiceProvider();
        return new TestDatabase(services, paths);
    }

    public static async Task<TestDatabase> CreateInitializedAsync(bool sampleData = false)
    {
        var db = Create();
        await db.Initializer.InitializeAsync(sampleData);
        return db;
    }

    public VetPosDbContext NewContext() =>
        _services.GetRequiredService<IDbContextFactory<VetPosDbContext>>().CreateDbContext();

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Paths.Root, recursive: true); }
        catch (IOException) { /* a leftover temp folder is harmless */ }
    }
}
