using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private CancellationTokenSource? _operation;
    private Task<UpdateInfo?>? _feedCheck;
    private string? _feedSource;
    [ObservableProperty] private bool _isRestarting;
    private bool _backedUpForExit;
    private bool _skipAutomaticChecks;

    public bool NeedsExitBackup => _pending is not null && !IsRestarting && !_backedUpForExit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress), nameof(ShowBackgroundProgress))]
    private bool _isWorking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress), nameof(ShowBackgroundProgress))]
    private bool _isBackgrounded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress), nameof(ShowBackgroundProgress))]
    private bool _isSkipping;

    public bool ShowProgress => IsWorking && !IsBackgrounded && !IsSkipping;
    public bool ShowBackgroundProgress => IsWorking && IsBackgrounded && !IsSkipping;

    [ObservableProperty] private bool _isIndeterminate;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackgroundProgressLabel))]
    private int _progress;

    public string BackgroundProgressLabel => Progress is > 0 and < 100 ? $"Update · {Progress}%" : "Update progress";
    [ObservableProperty] private string _status = "Updates are checked automatically.";

    private void Cancel() => _operation?.Cancel();

    [RelayCommand]
    private void RunInBackground() => IsBackgrounded = true;

    [RelayCommand]
    private void ShowUpdateProgress() => IsBackgrounded = false;

    [RelayCommand]
    private void SkipUpdate()
    {
        if (_operation is null || IsSkipping) return;
        _skipAutomaticChecks = true;
        IsSkipping = true;
        Status = "Update skipped. Check for updates when you’re ready.";
        Cancel();
    }

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

    /// <summary>First check shortly after the main window opens, then every 2 hours.</summary>
    public void Start()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
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

    public async Task CheckAsync(bool quiet)
    {
        if ((quiet && _skipAutomaticChecks) || IsRestarting) return;
        if (IsWorking)
        {
            if (!quiet) ShowUpdateProgress();
            return;
        }
        _skipAutomaticChecks = false;
        IsBackgrounded = false;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        IsWorking = IsIndeterminate = true;
        Progress = 0;
        Status = "Checking for updates…";
        try
        {
            var source = (await settings.GetAsync<UpdateSettings>(UpdateSettings.Key, operation.Token)).Source;
            var manager = await Task.Run(() => CreateManager(source), operation.Token);
            if (!manager.IsInstalled)
            {
                Status = "Updates are available in the installed copy of VetPOS.";
                return;
            }

            if (_feedCheck is null || _feedCheck.IsCompleted || _feedSource != source)
            {
                _feedSource = source;
                _feedCheck = Task.Run(async () => await manager.CheckForUpdatesAsync());
                // Observe failures even if the UI times out before the feed responds.
                _ = _feedCheck.ContinueWith(task =>
                    logger.LogDebug(task.Exception, "Update feed request failed"),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }
            var update = await _feedCheck.WaitAsync(TimeSpan.FromSeconds(25), operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            if (update is null)
            {
                Status = ReadyVersion is { } ready ? $"VetPOS {ready} is ready to install." : $"VetPOS {CurrentVersion} is up to date.";
                return;
            }

            var version = update.TargetFullRelease.Version.ToString();
            var previousVersion = ReadyVersion;
            if (version != previousVersion)
            {
                Status = $"Downloading VetPOS {version}…";
                using var download = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
                download.CancelAfter(TimeSpan.FromMinutes(3));
                var dispatcher = System.Windows.Application.Current.Dispatcher;
                await Task.Run(() => manager.DownloadUpdatesAsync(update, percent =>
                {
                    if (percent <= 0) return;
                    download.CancelAfter(TimeSpan.FromMinutes(3));
                    _ = dispatcher.InvokeAsync(() =>
                    {
                        if (_operation != operation || operation.IsCancellationRequested) return;
                        IsIndeterminate = percent >= 100;
                        Progress = Math.Clamp(percent, 0, 100);
                        Status = percent >= 100
                            ? $"Verifying VetPOS {version}…"
                            : $"Downloading VetPOS {version} · {Progress}%";
                    }, DispatcherPriority.Background);
                }, download.Token), download.Token);
                operation.Token.ThrowIfCancellationRequested();
                _manager = manager;
                _pending = update;
                _backedUpForExit = false;
                ReadyVersion = version;
            }
            if (!quiet || version != previousVersion) Dismissed = false;
            Status = $"VetPOS {version} is ready to install.";
        }
        catch (Exception) when (operation.IsCancellationRequested)
        {
            if (!IsSkipping) Status = "Update cancelled. You can check again any time.";
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Update timed out");
            Status = "The update took too long. Check your connection and try again.";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Update check or download failed (quiet={Quiet})", quiet);
            Status = "Could not check or download the update. Check your connection and try again.";
        }
        finally
        {
            _operation = null;
            IsWorking = IsIndeterminate = false;
            IsSkipping = false;
        }
    }

    /// <summary>
    /// Backup, then restart into the new version.
    /// A PreUpdate backup is always made first so the shop's data is never at risk.
    /// </summary>
    public async Task RestartNowAsync(Actor actor)
    {
        if (_manager is null || _pending is null || IsWorking || IsRestarting) return;
        IsRestarting = true;
        try
        {
            Status = "Backing up your data before restart…";
            logger.LogInformation("Pre-update backup started before installing {Version}", _pending.TargetFullRelease.Version);
            var backup = await Task.Run(() => backups.CreateAsync(BackupKind.PreUpdate, actor));
            logger.LogInformation("Pre-update backup complete: {File} — restarting to apply update", backup.FileName);
            Status = "Restarting to install the update…";
            _manager.ApplyUpdatesAndRestart(_pending);
        }
        catch
        {
            IsRestarting = false;
            Status = "Could not restart for the update. You can try again.";
            throw;
        }
    }

    public async Task BackUpOnCloseAsync()
    {
        Cancel();
        Status = "Backing up your data before installing…";
        try
        {
            await Task.Run(() => backups.CreateAsync(BackupKind.PreUpdate, actor: null));
            _backedUpForExit = true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Pre-update backup failed; update will be deferred");
        }
    }

    /// <summary>Apply on exit only after a verified backup.</summary>
    public void ApplyOnExit()
    {
        Cancel();
        if (_manager is null || _pending is null || IsRestarting || !_backedUpForExit) return;
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
