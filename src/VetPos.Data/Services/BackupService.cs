using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VetPos.Core.Domain;
using VetPos.Core.Infrastructure;
using VetPos.Core.Rules;
using VetPos.Core.Settings;

namespace VetPos.Data.Services;

public sealed record BackupFile(string FileName, string FullPath, DateTime CreatedLocal, long SizeBytes, BackupKind Kind);

/// <summary>
/// Copies of the database made with SQLite's online backup (safe while the till is selling).
/// Each copy is checked after it is made. Automatic and manual copies: the newest N are kept.
/// Restore first saves a copy of the current data, then copies the backup in and updates it to
/// the current version — the app keeps running.
/// </summary>
public sealed class BackupService(
    IDbContextFactory<VetPosDbContext> dbFactory,
    AppPaths paths,
    ShopClock clock,
    SettingsService settings,
    ILogger<BackupService> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public async Task<BackupFile> CreateAsync(BackupKind kind, Actor? actor, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(paths.BackupsDir);
            var name = string.Create(CultureInfo.InvariantCulture, $"vetpos_{clock.LocalNow:yyyy-MM-dd_HHmmss}_{kind.ToString().ToLowerInvariant()}.db");
            var path = Path.Combine(paths.BackupsDir, name);
            await Task.Run(() =>
            {
                using var source = new SqliteConnection(DataServiceCollectionExtensions.ConnectionStringFor(paths.DatabaseFile));
                using var target = new SqliteConnection($"Data Source={path};Pooling=False");
                source.Open();
                target.Open();
                source.BackupDatabase(target);
            }, ct);
            Verify(path);
            var size = new FileInfo(path).Length;

            var record = new BackupRecord { FileName = name, Kind = kind, SizeBytes = size, Verified = true, CreatedById = actor?.Id };
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                db.Backups.Add(record);
                Audit.Add(db, actor, AuditActions.Backup, $"Backup made ({kind}): {name}");
                await db.SaveChangesAsync(ct);
            }

            var options = await settings.GetAsync<BackupSettings>(BackupSettings.Key, ct);
            CopyToExtraFolder(path, options.ExtraFolder);
            Prune(options.Keep);
            logger.LogInformation("Backup {Name} ({Size} bytes)", name, size);
            return new BackupFile(name, path, clock.LocalNow, size, kind);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Makes today's automatic backup if there is none yet (called at start and every hour).</summary>
    public async Task<BackupFile?> EnsureDailyAsync(CancellationToken ct = default)
    {
        var options = await settings.GetAsync<BackupSettings>(BackupSettings.Key, ct);
        if (!options.DailyBackup) return null;
        var today = clock.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (List().Any(b => b.Kind == BackupKind.Auto && b.FileName.Contains(today, StringComparison.Ordinal))) return null;
        return await CreateAsync(BackupKind.Auto, null, ct);
    }

    public IReadOnlyList<BackupFile> List()
    {
        if (!Directory.Exists(paths.BackupsDir)) return [];
        return Directory.EnumerateFiles(paths.BackupsDir, "vetpos_*.db")
            .Select(Parse).OfType<BackupFile>().OrderByDescending(b => b.CreatedLocal).ToList();
    }

    /// <summary>Replaces all data with the backup. A copy of the current data is saved first.</summary>
    public async Task RestoreAsync(string backupPath, Actor actor, CancellationToken ct = default)
    {
        actor.RequireOwner("restore a backup");
        if (!File.Exists(backupPath)) throw new PosException("That backup file is missing.");
        Verify(backupPath);
        await CreateAsync(BackupKind.PreRestore, actor, ct);
        await _gate.WaitAsync(ct);
        try
        {
            await Task.Run(() =>
            {
                using var source = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly;Pooling=False");
                using var target = new SqliteConnection(DataServiceCollectionExtensions.ConnectionStringFor(paths.DatabaseFile));
                source.Open();
                target.Open();
                source.BackupDatabase(target);
            }, ct);
            SqliteConnection.ClearAllPools();
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Database.MigrateAsync(ct); // an older backup is brought up to this version
            Audit.Add(db, actor, AuditActions.Restore, $"Data restored from {Path.GetFileName(backupPath)}");
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Database restored from {File} by {User}", backupPath, actor.FullName);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void Verify(string path)
    {
        using var c = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        try
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check;";
            var ok = cmd.ExecuteScalar() as string;
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'Sales';";
            if (ok != "ok" || Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
                throw new PosException("That file is not a good VetPOS backup.");
        }
        catch (SqliteException)
        {
            throw new PosException("That file is not a VetPOS backup or it is damaged.");
        }
    }

    private void Prune(int keep)
    {
        keep = Math.Clamp(keep, 3, 365);
        var all = List();
        var old = all.Where(b => b.Kind is BackupKind.Auto or BackupKind.Manual).Skip(keep)
            .Concat(all.Where(b => b.Kind is BackupKind.PreRestore or BackupKind.PreUpdate).Skip(5));
        foreach (var b in old)
        {
            try { File.Delete(b.FullPath); }
            catch (IOException ex) { logger.LogWarning(ex, "Could not delete old backup {File}", b.FileName); }
        }
    }

    private void CopyToExtraFolder(string path, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        try
        {
            Directory.CreateDirectory(folder);
            File.Copy(path, Path.Combine(folder, Path.GetFileName(path)), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The USB drive may be out: the main backup is still made.
            logger.LogWarning(ex, "Could not copy the backup to {Folder}", folder);
        }
    }

    private static BackupFile? Parse(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path); // vetpos_2026-11-13_213000_auto
        var parts = name.Split('_');
        if (parts.Length != 4) return null;
        if (!DateTime.TryParseExact(parts[1] + parts[2], "yyyy-MM-ddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)) return null;
        if (!Enum.TryParse<BackupKind>(parts[3], ignoreCase: true, out var kind)) return null;
        return new BackupFile(Path.GetFileName(path), path, when, new FileInfo(path).Length, kind);
    }
}
