using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VetPos.App.Infrastructure;
using VetPos.App.Shell;

namespace VetPos.App.Features.Home;

/// <summary>
/// The first page shown after login: greeting, quick start buttons, big section cards, tips.
/// The update-available banner is shown here (and dismissed per session) when a new version is ready.
/// </summary>
public sealed partial class HomeViewModel(Ui ui, UpdateService updates) : PageViewModel(ui)
{
    public UpdateService Updates { get; } = updates;

    // ---------- Header text ----------

    // CA1822 suppressed: these are instance properties bound in XAML via {Binding}; making them
    // static would break the binding even though they don't reference instance fields.
#pragma warning disable CA1822
    public string Greeting => DateTime.Now.Hour switch
    {
        < 12 => "Good morning",
        < 17 => "Good afternoon",
        _    => "Good evening",
    };

    public string TodayText => DateTime.Now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);

    // ---------- Tip of the day (changes once a day) ----------

    private static readonly string[] Tips =
    [
        "Every card shows its shortcut key. Press it on any page to jump straight there.",
        "Use the arrow keys to move between the cards, then press Enter to open one.",
        "Made a mistake on a bill? Open Bill editor to fix or reprint it.",
        "Your data is saved on this computer. Copy your backups to a USB drive now and then.",
        "No internet? VetPOS keeps working. It checks for updates the next time you are online.",
    ];

    public string TipText => Tips[DateTime.Now.DayOfYear % Tips.Length];
#pragma warning restore CA1822

    // ---------- Update status next to the "Check for updates" button ----------

    private DateTime? _lastChecked;

    public string UpdateStatus =>
        Updates.ReadyVersion is { } v ? $"Version {v} is ready to install"
        : _lastChecked is { } t ? $"Last checked at {t:h:mm tt}"
        : "Updates are checked when the app opens";

    // ---------- "Today" strip: stays hidden while BillsToday is null ----------
    // Fill these from your billing/stock services (see the follow-up prompt), for example:
    //   BillsToday = "12";  SalesToday = "Rs. 48,500";  LowStock = "3";

    [ObservableProperty] private string? _billsToday;
    [ObservableProperty] private string? _salesToday;
    [ObservableProperty] private string? _lowStock;

    // ---------- Commands ----------

    /// <summary>Navigated to by the shell when a card is clicked or activated with the keyboard.</summary>
    public event EventHandler<NavItem>? CardActivated;

    [RelayCommand]
    private void Open(NavItem item) => CardActivated?.Invoke(this, item);

    /// <summary>
    /// "Check for updates" button. Shows a toast with the result.
    /// No internet is handled inside UpdateService (quiet = true), so the user never sees an error.
    /// </summary>
    [RelayCommand]
    private Task CheckUpdates() => Ui.Run(async () =>
    {
        var version = await Updates.CheckAsync(quiet: true);

        _lastChecked = DateTime.Now;
        OnPropertyChanged(nameof(UpdateStatus));

        if (version is null && Updates.ReadyVersion is null)
            Ui.Toasts.Show($"VetPOS {App.Version} is up to date.", DesignSystem.Controls.ToastKind.Info);
        else if (version is not null)
            Ui.Toasts.Show($"VetPOS {version} is ready to install.", DesignSystem.Controls.ToastKind.Info);
        // If ReadyVersion was already set by an earlier check, the banner stays visible.
    });

    [RelayCommand]
    private Task RestartToUpdate() => Ui.Run(() => Updates.RestartNowAsync(Ui.Session.Actor));

    [RelayCommand]
    private void DismissUpdate()
    {
        Updates.Dismissed = true;
        OnPropertyChanged(nameof(UpdateStatus));
    }
}