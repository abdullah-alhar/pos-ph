using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VetPos.Core.Infrastructure;
using VetPos.Data.Seed;

namespace VetPos.Data;

/// <summary>What happened when the database was opened at start-up.</summary>
public sealed record DatabaseStartResult(bool Created, IReadOnlyList<string> AppliedMigrations, bool SampleDataAdded);

/// <summary>Thrown when the database file is damaged; the app offers to restore a backup.</summary>
public sealed class DatabaseDamagedException(string detail, Exception? inner = null)
    : Exception($"The database file is damaged: {detail}", inner);

/// <summary>
/// Opens the database at start-up: makes the folders, switches SQLite to WAL (safe against
/// power cuts and lets backups run while selling), checks the file is healthy, brings the
/// tables up to date and, when asked, adds the sample products.
/// </summary>
public sealed class DatabaseInitializer(
    IDbContextFactory<VetPosDbContext> dbFactory,
    AppPaths paths,
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger)
{
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    public async Task<DatabaseStartResult> InitializeAsync(bool addSampleData, CancellationToken ct = default)
    {
        paths.EnsureCreated();
        var created = !File.Exists(paths.DatabaseFile);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        try
        {
            await connection.OpenAsync(ct);
            if (!created)
            {
                var check = await ScalarAsync(connection, "PRAGMA quick_check;", ct);
                if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new DatabaseDamagedException(check ?? "no answer");
            }
        }
        catch (Exception ex) when (!created && (ex is DatabaseDamagedException
            or SqliteException { SqliteErrorCode: SqliteCorrupt or SqliteNotADatabase }))
        {
            // Discard this connection when the context closes so Windows releases the file.
            SqliteConnection.ClearPool(connection);
            logger.LogCritical(ex, "Database quick_check failed");
            if (ex is DatabaseDamagedException) throw;
            throw new DatabaseDamagedException(ex.Message, ex);
        }

        // WAL is stored in the file, so this only really changes something the first time.
        // synchronous stays FULL (the default): a finished bill survives a power cut.
        await ScalarAsync(connection, "PRAGMA journal_mode=WAL;", ct);

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count > 0)
        {
            logger.LogInformation("Applying {Count} database migration(s): {Migrations}", pending.Count, pending);
            await db.Database.MigrateAsync(ct);
        }

        var seeded = false;
        if (addSampleData && !await db.Products.AnyAsync(ct))
        {
            await SampleData.AddAsync(db, DateOnly.FromDateTime(clock.GetLocalNow().DateTime), ct);
            seeded = true;
            logger.LogInformation("Sample products added");
        }

        logger.LogInformation("Database ready at {Path} (new: {Created})", paths.DatabaseFile, created);
        return new DatabaseStartResult(created, pending, seeded);
    }

    /// <summary>Adds the sample products if there are no products yet (first-run "try it" option).</summary>
    public async Task<bool> AddSampleDataIfEmptyAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Products.AnyAsync(ct)) return false;
        await SampleData.AddAsync(db, DateOnly.FromDateTime(clock.GetLocalNow().DateTime), ct);
        logger.LogInformation("Sample products added from the setup screen");
        return true;
    }

    private static async Task<string?> ScalarAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return (await cmd.ExecuteScalarAsync(ct))?.ToString();
    }
}
