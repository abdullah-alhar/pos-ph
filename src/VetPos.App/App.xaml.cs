using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using VetPos.App.DesignSystem;
using VetPos.App.DesignSystem.Controls;
using VetPos.AI;
using VetPos.App.Features.Assistant;
using VetPos.App.Features.BillEditor;
using VetPos.App.Features.Billing;
using VetPos.App.Features.Products;
using VetPos.App.Features.Reports;
using VetPos.App.Features.Settings;
using VetPos.App.Features.Start;
using VetPos.App.Features.StockEntry;
using VetPos.App.Infrastructure;
using VetPos.App.Shell;
using VetPos.Core.Abstractions;
using VetPos.Core.Infrastructure;
using VetPos.Data;

namespace VetPos.App;

public partial class App : Application
{
    private IHost? _host;
    private SingleInstance? _singleInstance;

    public static string Version { get; } =
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public static string VersionLabel { get; } = FormatVersionLabel();

    private static string FormatVersionLabel()
    {
        var dash = Version.IndexOf('-');
        if (dash < 0) return $"v{Version}";
        var prerelease = Version[(dash + 1)..].Replace('.', ' ');
        return char.ToUpperInvariant(prerelease[0]) + prerelease[1..];
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        var preview = e.Args is ["--startup-preview", var imagePath] ? imagePath : null;
        // The loading window shows at once, so the shop never wonders whether VetPOS is opening.
        StartSplash? splash = null;
        try { splash = StartSplash.Show(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Windows.Markup.XamlParseException or NotSupportedException)
        {
            splash = null; // VetPOS still starts, just without the loading window
        }
        base.OnStartup(e);

        if (preview is null)
        {
            _singleInstance = SingleInstance.TryAcquire(() => Dispatcher.BeginInvoke(BringToFront));
            if (_singleInstance is null)
            {
                CloseSplash(splash, TimeSpan.Zero);
                Shutdown(0);
                return;
            }
        }

        if (SystemCheck.Blocking() is { } blocking)
        {
            CloseSplash(splash, TimeSpan.Zero);
            if (preview is null) MessageBox.Show(blocking, "VetPOS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(3);
            return;
        }

        AppPaths paths;
        try
        {
            paths = preview is null ? AppPaths.Default() : StartupPreview.CreatePaths(preview);
            paths.EnsureCreated();
        }
        catch (Exception ex)
        {
            CloseSplash(splash, TimeSpan.Zero);
            if (preview is null) MessageBox.Show(
                "VetPOS cannot create its data folder.\n\n" + ex.Message,
                "VetPOS", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        Log.Logger = Logging.Create(paths);
        ErrorHandling.Register(this);
        Log.Information("VetPOS {Version} starting on {OS}, .NET {Runtime}", Version, Environment.OSVersion, Environment.Version);

        try
        {
            splash?.Status("Opening the shop's data…");
            _host = BuildHost(preview is null ? e.Args : [], paths);
            await _host.StartAsync();

            var addSampleData = e.Args.Contains("--sample-data", StringComparer.OrdinalIgnoreCase);
#if DEBUG
            addSampleData = true;
#endif
            splash?.Status("Checking the data is healthy…");
            var start = await _host.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync(addSampleData);
            splash?.Status("Getting the till ready…");
            _host.Services.GetRequiredService<StartupInfo>().Database = start;

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            var logo = _host.Services.GetRequiredService<LogoService>();
            logo.EnsureIcon();
            if (preview is null) logo.RefreshShortcuts();
            var root = _host.Services.GetRequiredService<RootViewModel>();
            await root.StartAsync(); // theme is applied here, before the window shows
            ErrorHandling.Toasts = _host.Services.GetRequiredService<IToastService>();
            window.Show();
            CloseSplash(splash, TimeSpan.FromMilliseconds(200));
            if (preview is not null)
            {
                await StartupPreview.CaptureScreensAsync(window, root, _host.Services, preview);
                Shutdown(0);
                return;
            }
            if (SystemCheck.Warning(paths) is { } warning)
                _host.Services.GetRequiredService<IToastService>().Show(warning, ToastKind.Warning);
        }
        catch (DatabaseDamagedException ex) when (preview is null)
        {
            CloseSplash(splash, TimeSpan.Zero);
            var reference = ErrorHandling.NewReference();
            Log.Fatal(ex, "Database damaged {Reference}", reference);
            var newest = new DirectoryInfo(paths.BackupsDir).Exists
                ? new DirectoryInfo(paths.BackupsDir).GetFiles("vetpos_*.db").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                : null;
            if (newest is not null && MessageBox.Show(
                    "The shop's database file is damaged, so VetPOS cannot open it safely.\n\n" +
                    $"Restore the newest backup ({newest.LastWriteTime:dd MMM yyyy HH:mm}) now?\n\n" +
                    "The damaged file is kept (not deleted) so support can look at it. Reference: " + reference,
                    "VetPOS", MessageBoxButton.YesNo, MessageBoxImage.Error) == MessageBoxResult.Yes)
            {
                try
                {
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                    foreach (var ext in new[] { "", "-wal", "-shm" })
                        if (File.Exists(paths.DatabaseFile + ext)) File.Move(paths.DatabaseFile + ext, Path.Combine(paths.Root, $"vetpos.damaged-{stamp}.db{ext}"));
                    File.Copy(newest.FullName, paths.DatabaseFile);
                    Log.Warning("Restored {Backup} after a damaged database ({Reference})", newest.Name, reference);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
                }
                catch (Exception restoreError)
                {
                    Log.Fatal(restoreError, "Automatic restore failed");
                    MessageBox.Show("The backup could not be restored automatically. Call support with this number: " + reference, "VetPOS", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show(
                    "The shop's database file is damaged, so VetPOS cannot open it safely.\n\n" +
                    "Your backups are in:\n" + paths.BackupsDir + "\n\n" +
                    "Call support and give this number: " + reference,
                    "VetPOS", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown(2);
        }
        catch (Exception ex)
        {
            var reference = ErrorHandling.NewReference();
            Log.Fatal(ex, "Start-up failed {Reference}", reference);
            if (preview is not null)
            {
                Shutdown(1);
                return;
            }
            var message = ErrorHandling.IsBlockedByWindows(ex)
                ? ErrorHandling.BlockedMessage
                : (ErrorHandling.Describe(ex) is { } known ? known + "\n\n" : "") +
                  "Restart the computer and open VetPOS again. If it still does not open, send support this reference and the newest file from the logs folder." +
                  $"\n\n({ex.GetType().Name}: {(ex.Message.Length > 160 ? ex.Message[..160] + "…" : ex.Message)})";
            if (splash is not null)
            {
                await splash.ShowErrorAsync(message, reference, paths.LogsDir);
            }
            else
            {
                MessageBox.Show("VetPOS could not start.\n\n" + message + "\n\nReference: " + reference, "VetPOS", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown(1);
        }
    }

    private static void CloseSplash(StartSplash? splash, TimeSpan fade) => splash?.Close(fade);

    private static IHost BuildHost(string[] args, AppPaths paths)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            // No appsettings files, environment config or console logging: the till is configured in the app.
            DisableDefaults = true,
        });

        builder.Services.AddSerilog(dispose: false);
        builder.Services.AddVetPosData(paths);

        builder.Services.AddSingleton<Session>();
        builder.Services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<Session>());

        builder.Services.AddSingleton<StartupInfo>();
        builder.Services.AddSingleton<ThemeManager>();
        builder.Services.AddSingleton<DialogService>();
        builder.Services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());
        builder.Services.AddSingleton<ToastService>();
        builder.Services.AddSingleton<IToastService>(sp => sp.GetRequiredService<ToastService>());
        builder.Services.AddSingleton<Ui>();
        builder.Services.AddSingleton<UpdateService>();
        builder.Services.AddSingleton<LogoService>();
        builder.Services.AddSingleton<PrintService>();
        builder.Services.AddSingleton<IAiKeyProtector, DpapiKeyProtector>();
        builder.Services.AddVetPosAi();
        builder.Services.AddHostedService<BackupScheduler>();

        builder.Services.AddSingleton<RootViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddTransient<SetupViewModel>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LockViewModel>();
        builder.Services.AddTransient<ShellViewModel>();
        // Pages: a fresh set for every sign-in.
        builder.Services.AddTransient<VetPos.App.Features.Home.HomeViewModel>();
        builder.Services.AddTransient<BillingViewModel>();
        builder.Services.AddTransient<StockEntryViewModel>();
        builder.Services.AddTransient<ProductsViewModel>();
        builder.Services.AddTransient<ReportsViewModel>();
        builder.Services.AddTransient<BillEditorViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<AssistantViewModel>();
        builder.Services.AddTransient<VetPos.App.Features.Labels.LabelsViewModel>();

        return builder.Build();
    }

    private void BringToFront()
    {
        if (MainWindow is not { } window) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                _host.Services.GetService<UpdateService>()?.ApplyOnExit();
                _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                _host.Dispose();
            }
            Log.Information("VetPOS closed (exit code {Code})", e.ApplicationExitCode);
        }
        finally
        {
            Log.CloseAndFlush();
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }
}
