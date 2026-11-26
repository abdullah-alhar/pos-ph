using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;
using VetPos.Core.Domain;
using VetPos.Core.Rules;
using VetPos.Core.Settings;
using VetPos.Data.Services;

namespace VetPos.App.Infrastructure;

/// <summary>
/// Keeps the shop on the newest version without anyone having to look for it. A new version
/// published to the built-in update source (or the folder / web address set by the installer) is found
/// in the background, downloaded quietly, and then the app shows "Update ready". Restart now makes a
/// backup and restarts into it; otherwise it installs by itself the next time VetPOS closes.
/// </summary>
public sealed partial class UpdateService(SettingsService settings, BackupService backups, ILogger<UpdateService> logger) : ObservableObject
{
    private UpdateManager? _manager;
    private UpdateInfo? _pending;
    private DispatcherTimer? _timer;
    private bool _checking;

    /// <summary>The downloaded version waiting for a restart, e.g. "0.5.0"; null when none.</summary>
    [ObservableProperty] private string? _readyVersion;

    /// <summary>Hidden by "Later" until the next version.</summary>
    [ObservableProperty] private bool _dismissed;

    public string CurrentVersion { get; } = App.Version;

    public static UpdateManager CreateManager(string? source)
    {
        var where = string.IsNullOrWhiteSpace(source) ? UpdateSettings.DefaultSource : source.Trim();
        IUpdateSource src = where.Contains("github.com/", StringComparison.OrdinalIgnoreCase)
            ? new GithubSource(where.TrimEnd('/'), null, false)
            : where.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new SimpleWebSource(where)
                : new SimpleFileSource(new DirectoryInfo(where));
        return new UpdateManager(src);
    }

    /// <summary>First check 30 seconds after sign-in, then every 2 hours.</summary>
    public void Start()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromHours(2);
            await ErrorHandling.Quietly(async () =>
            {
                var s = await settings.GetAsync<UpdateSettings>(UpdateSettings.Key);
                if (s.CheckOnStart) await CheckAsync(quiet: true);
            }, "update check");
        };
        _timer.Start();
    }

    /// <summary>
    /// Looks for a new version and downloads it. Returns the new version, or null when this is the
    /// newest. Quiet checks never show errors (no internet is normal in a shop).
    /// One short line is written to the log on every check; a warning is logged on failure.
    /// </summary>
    public async Task<string?> CheckAsync(bool quiet)
    {
        if (_checking) return ReadyVersion;
        _checking = true;
        logger.LogInformation("Update check started (quiet={Quiet}, current={Current})", quiet, CurrentVersion);
        try
        {
            var source = (await settings.GetAsync<UpdateSettings>(UpdateSettings.Key)).Source;
            var manager = CreateManager(source);
            if (!manager.IsInstalled)
            {
                logger.LogInformation("Update check skipped: app is not installed (dev/copied build)");
                if (quiet) return null;
                throw new PosException("Updates work in the installed app (run Setup.exe), not in a copied folder.");
            }
            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                logger.LogInformation("Update check complete: {Current} is the newest version", CurrentVersion);
                return null;
            }
            var version = update.TargetFullRelease.Version.ToString();
            if (version != ReadyVersion)
            {
                logger.LogInformation("New version {Version} found — downloading", version);
                await manager.DownloadUpdatesAsync(update);
                _manager = manager;
                _pending = update;
                Dismissed = false;
                ReadyVersion = version;
                logger.LogInformation("Update {Version} downloaded and ready to install", version);
            }
            else
            {
                logger.LogInformation("Update {Version} already downloaded and waiting for restart", version);
            }
            return version;
        }
        catch (PosException)
        {
            throw;
        }
        catch (Exception ex) when (quiet)
        {
            // No internet is normal in a shop — log as a warning and move on; never show an error.
            logger.LogWarning(ex, "Quiet update check failed (often no internet — will retry next session)");
            return null;
        }
        catch (Exception ex)
        {
            // Manual "Check now" press: log the warning and surface a friendly message (no crash).
            logger.LogWarning(ex, "Update check failed");
            throw new PosException("Could not reach the update page. Check the internet connection and try again. Billing works as normal.");
        }
        finally
        {
            _checking = false;
        }
    }

    /// <summary>
    /// Backup, then restart into the new version.
    /// A PreUpdate backup is always made first so the shop's data is never at risk.
    /// </summary>
    public async Task RestartNowAsync(Actor actor)
    {
        if (_manager is null || _pending is null) return;
        logger.LogInformation("Pre-update backup started before installing {Version}", _pending.TargetFullRelease.Version);
        var backup = await backups.CreateAsync(BackupKind.PreUpdate, actor);
        logger.LogInformation("Pre-update backup complete: {File} — restarting to apply update", backup.FileName);
        _manager.ApplyUpdatesAndRestart(_pending);
    }

    /// <summary>Called when VetPOS closes: a downloaded update installs then, quietly.</summary>
    public void ApplyOnExit()
    {
        if (_manager is null || _pending is null) return;
        try
        {
            _manager.WaitExitThenApplyUpdates(_pending.TargetFullRelease, silent: true, restart: false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not schedule the update on exit");
        }
    }
}
