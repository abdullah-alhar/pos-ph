using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VetPos.App.DesignSystem;
using VetPos.App.DesignSystem.Controls;
using VetPos.App.Features.Assistant;
using VetPos.App.Features.BillEditor;
using VetPos.App.Features.Billing;
using VetPos.App.Features.Products;
using VetPos.App.Features.Reports;
using VetPos.App.Features.Settings;
using VetPos.App.Features.StockEntry;
using VetPos.App.Infrastructure;
using VetPos.Core.Settings;
using VetPos.Data.Services;

namespace VetPos.App.Shell;

/// <summary>
/// One page in the menu. Each page has its own colour, like apps on a phone, so staff can find it by
/// colour and picture even without reading the word.
/// </summary>
public sealed record NavItem(string Title, string Icon, string Shortcut, Type PageType, string Color, bool OwnerOnly = false)
{
    /// <summary>The word under the icon in the slim rail.</summary>
    public string ShortTitle => Title switch
    {
        "Bill editor" => "Receipt",
        "AI Assistant" => "AI",
        _ => Title,
    };

    public System.Windows.Media.Brush Tint { get; } = Freeze(Color, 1);
    public System.Windows.Media.Brush TintSoft { get; } = Freeze(Color, 0.13);

    private static System.Windows.Media.SolidColorBrush Freeze(string hex, double opacity)
    {
        var b = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)) { Opacity = opacity };
        b.Freeze();
        return b;
    }
}

public sealed record PaletteCommand(string Title, string Section, string? Shortcut, Func<Task> Run);

/// <summary>The signed-in frame: sidebar, current page, command palette, help and lock.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly ThemeManager _theme;
    private readonly SettingsService _settings;
    private readonly Ui _ui;
    private readonly Dictionary<Type, object> _pages = [];
    private readonly List<PaletteCommand> _commands = [];

    private readonly NotificationService _notifications;

    public ShellViewModel(IServiceProvider services, ThemeManager theme, SettingsService settings, Ui ui, Session session, LogoService logo, NotificationService notifications, UpdateService updates)
    {
        Updates = updates;
        ui.AskAiRequested += async (_, prefill) => await OpenAssistantAsync(prefill);
        _notifications = notifications;
        ShopLogo = logo.Load();
        logo.Changed += (_, _) => ShopLogo = logo.Load();
        _services = services;
        _theme = theme;
        _settings = settings;
        _ui = ui;
        Session = session;
        var all = new NavItem[]
        {
            new("Billing", "", "Ctrl+1", typeof(BillingViewModel), "#16A34A"),
            new("Stock in", "", "Ctrl+2", typeof(StockEntryViewModel), "#2563EB"),
            new("Products", "", "Ctrl+3", typeof(ProductsViewModel), "#EA7A0C"),
            new("Labels", "\uE8EC", "Ctrl+8", typeof(Features.Labels.LabelsViewModel), "#7C3AED"),
            new("Reports", "", "Ctrl+4", typeof(ReportsViewModel), "#DB2777"),
            new("Bill editor", "", "Ctrl+5", typeof(BillEditorViewModel), "#0891B2", OwnerOnly: true),
            new("Settings", "", "Ctrl+6", typeof(SettingsViewModel), "#475569", OwnerOnly: true),
            new("AI Assistant", "", "Ctrl+7", typeof(AssistantViewModel), "#0A8A82"),
        };
        Items = new ObservableCollection<NavItem>(all.Where(i => !i.OwnerOnly || session.IsOwner));
        BuildCommands();
    }

    public Session Session { get; }
    public ObservableCollection<NavItem> Items { get; }

    [ObservableProperty] private NavItem? _selected;
    [ObservableProperty] private System.Windows.Media.ImageSource? _shopLogo;
    [ObservableProperty] private bool _isNoticesOpen;
    [ObservableProperty] private bool _isAssistantOpen;
    [ObservableProperty] private int _noticeCount;

    public System.Collections.ObjectModel.ObservableCollection<Notice> Notices { get; } = [];

    /// <summary>The same assistant as the AI page, in a side panel on any page (Ctrl+J).</summary>
    public AssistantViewModel Assistant
    {
        get
        {
            var a = Page<AssistantViewModel>();
            if (!_linksHooked)
            {
                a.NavigateRequested += (_, target) => OpenTarget(target);
                // A change confirmed in the AI chat: reload the screen that is open so it shows at once.
                a.ChangesSaved += async (_, _) =>
                {
                    if (CurrentPage is PageViewModel p and not AssistantViewModel) await _ui.Run(p.OnShownAsync);
                };
                _linksHooked = true;
            }
            return a;
        }
    }

    private bool _linksHooked;

    /// <summary>The floating AI button shows on every page except billing (it is in the scan bar there) and the AI page.</summary>
    public bool ShowAiButton => !IsAssistantOpen && CurrentPage is not BillingViewModel and not AssistantViewModel;

    partial void OnCurrentPageChanged(object? value) => OnPropertyChanged(nameof(ShowAiButton));
    partial void OnIsAssistantOpenChanged(bool value) => OnPropertyChanged(nameof(ShowAiButton));

    /// <summary>Opens a screen named by the AI ("products:low", "reports:profit" …).</summary>
    public void OpenTarget(string target)
    {
        switch (target)
        {
            case "products": Go<ProductsViewModel>(); Page<ProductsViewModel>().Filter = ProductFilter.All; break;
            case "products:low": Go<ProductsViewModel>(); Page<ProductsViewModel>().Filter = ProductFilter.LowStock; break;
            case "stock": Go<StockEntryViewModel>(); break;
            case "reports:today": Go<ReportsViewModel>(); Page<ReportsViewModel>().Section = ReportSection.Today; break;
            case "reports:sales": Go<ReportsViewModel>(); Page<ReportsViewModel>().Section = ReportSection.Sales; break;
            case "reports:profit" when Session.IsOwner: Go<ReportsViewModel>(); Page<ReportsViewModel>().Section = ReportSection.Profit; break;
            case "settings:ai" when Session.IsOwner: IsAssistantOpen = false; Go<SettingsViewModel>(); Page<SettingsViewModel>().Section = SettingsSection.AI; break;
        }
    }

    public async Task RefreshNoticesAsync()
    {
        if (!Session.IsSignedIn) return;
        try
        {
            var list = await _notifications.ListAsync(Session.Actor);
            Notices.Clear();
            foreach (var n in list) Notices.Add(n);
            NoticeCount = list.Count(n => n.Level != NoticeLevel.Info);
        }
        catch (Exception ex)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(_ui.Logger, ex, "Could not work out the notifications");
        }
    }

    [RelayCommand]
    private async Task ToggleNotices()
    {
        IsUserMenuOpen = false;
        IsAssistantOpen = false;
        IsNoticesOpen = !IsNoticesOpen;
        if (IsNoticesOpen) await RefreshNoticesAsync();
    }

    [RelayCommand]
    private async Task OpenNotice(Notice? notice)
    {
        IsNoticesOpen = false;
        switch (notice?.Target)
        {
            case NoticeTarget.LowStock: Go<ProductsViewModel>(); Page<ProductsViewModel>().Filter = ProductFilter.LowStock; break;
            case NoticeTarget.Backups when Session.IsOwner: Go<SettingsViewModel>(); Page<SettingsViewModel>().Section = SettingsSection.Backups; break;
            case NoticeTarget.HeldBills: Go<BillingViewModel>(); await Page<BillingViewModel>().RecallCommand.ExecuteAsync(null); break;
            case NoticeTarget.Changes: Go<ReportsViewModel>(); Page<ReportsViewModel>().Section = ReportSection.Changes; break;
        }
    }

    /// <summary>Opens the AI drawer (from any screen), with text already typed when given.</summary>
    public async Task OpenAssistantAsync(string? prefill)
    {
        IsUserMenuOpen = false;
        IsNoticesOpen = false;
        IsAssistantOpen = true;
        if (!string.IsNullOrEmpty(prefill)) Assistant.Input = prefill;
        await _ui.Run(Assistant.OnShownAsync);
        Assistant.RequestFocus();
    }

    /// <summary>Double-click anywhere outside the AI drawer closes it.</summary>
    public void CloseAssistant() => IsAssistantOpen = false;

    [RelayCommand]
    private async Task ToggleAssistant()
    {
        IsUserMenuOpen = false;
        IsNoticesOpen = false;
        IsAssistantOpen = !IsAssistantOpen;
        if (IsAssistantOpen) await _ui.Run(Assistant.OnShownAsync);
    }
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private bool _collapsed;
    [ObservableProperty] private bool _isPaletteOpen;
    [ObservableProperty] private bool _isHelpOpen;
    [ObservableProperty] private bool _isUserMenuOpen;
    [ObservableProperty] private string _paletteText = "";
    [ObservableProperty] private PaletteCommand? _paletteSelected;

    public ObservableCollection<PaletteCommand> PaletteResults { get; } = [];

    public event EventHandler? LockRequested;
    public event EventHandler? LogoutRequested;

    /// <summary>New versions: found and downloaded in the background; the card shows when one is ready.</summary>
    public UpdateService Updates { get; }

    [RelayCommand]
    private Task RestartToUpdate() => _ui.Run(() => Updates.RestartNowAsync(Session.Actor));

    [RelayCommand]
    private void UpdateLater()
    {
        Updates.Dismissed = true;
        _ui.Toasts.Show("The update installs by itself the next time VetPOS closes.", DesignSystem.Controls.ToastKind.Info);
    }

    public async Task StartAsync()
    {
        Updates.Start();
        Collapsed = !(await _settings.GetAsync<AppearanceSettings>(AppearanceSettings.Key)).SidebarExpanded;
        Selected = Items[0];
        await RefreshNoticesAsync();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        timer.Tick += async (_, _) => await ErrorHandling.Quietly(RefreshNoticesAsync, "notifications");
        timer.Start();
    }

    public T Page<T>() where T : class => (T)PageFor(typeof(T));

    private object PageFor(Type type)
    {
        if (!_pages.TryGetValue(type, out var page)) _pages[type] = page = _services.GetRequiredService(type);
        return page;
    }

    async partial void OnSelectedChanged(NavItem? value)
    {
        if (value is null) return;
        var page = PageFor(value.PageType);
        CurrentPage = page;
        if (page is PageViewModel p) await _ui.Run(p.OnShownAsync);
    }

    [RelayCommand]
    private void Navigate(string shortcut)
    {
        var item = Items.FirstOrDefault(i => i.Shortcut == shortcut);
        if (item is not null) Selected = item;
    }

    public void Go<T>() => Selected = Items.FirstOrDefault(i => i.PageType == typeof(T)) ?? Selected;

    [RelayCommand]
    private async Task ToggleSidebar()
    {
        Collapsed = !Collapsed;
        var current = await _settings.GetAsync<AppearanceSettings>(AppearanceSettings.Key);
        await _ui.Run(() => _settings.SaveAsync(AppearanceSettings.Key, current with { SidebarExpanded = !Collapsed }, Session.Actor));
    }

    [RelayCommand]
    private void Lock()
    {
        IsUserMenuOpen = false;
        IsPaletteOpen = false;
        LockRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Logout()
    {
        IsUserMenuOpen = false;
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ToggleUserMenu() => IsUserMenuOpen = !IsUserMenuOpen;

    [RelayCommand]
    private void ShowHelp()
    {
        IsPaletteOpen = false;
        IsUserMenuOpen = false;
        IsHelpOpen = !IsHelpOpen;
    }

    [RelayCommand]
    private async Task ChangePin()
    {
        IsUserMenuOpen = false;
        await _ui.Run(async () =>
        {
            var sheet = new DialogSheet(_ui.Dialogs, "Change my PIN", "Your PIN is 4 to 8 digits. Nobody else can see it.", 380);
            var current = sheet.Field("Current PIN", DialogSheet.PinInput());
            var next = sheet.Field("New PIN", DialogSheet.PinInput());
            var again = sheet.Field("New PIN again", DialogSheet.PinInput());
            current.Loaded += (_, _) => current.Focus();
            var auth = _services.GetRequiredService<AuthService>();
            sheet.Buttons("Change PIN", async () =>
            {
                if (next.Password != again.Password) { sheet.ShowError("The new PINs are not the same.", again); return; }
                try
                {
                    await auth.ChangeOwnPinAsync(Session.Actor.Id, current.Password, next.Password);
                    sheet.Close(true);
                    _ui.Toasts.Show("Your PIN is changed.");
                }
                catch (Core.Rules.PosException ex)
                {
                    sheet.ShowError(ex.Message);
                }
            });
            await _ui.Dialogs.ShowAsync(sheet);
        });
    }

    // ───────── Command palette (Ctrl+K) ─────────

    private void BuildCommands()
    {
        foreach (var item in Items)
            _commands.Add(new PaletteCommand($"Go to {item.Title}", "Pages", item.Shortcut, () => { Selected = item; return Task.CompletedTask; }));
        _commands.Add(new("New bill", "Billing", "Ctrl+N", () => { Go<BillingViewModel>(); return Page<BillingViewModel>().NewBillCommand.ExecuteAsync(null); }));
        _commands.Add(new("Recall a held bill", "Billing", "F9", () => { Go<BillingViewModel>(); return Page<BillingViewModel>().RecallCommand.ExecuteAsync(null); }));
        _commands.Add(new("Past bills: print again or change", "Billing", "F7", () => { Go<BillingViewModel>(); return Page<BillingViewModel>().PastBillsCommand.ExecuteAsync(null); }));
        _commands.Add(new("Reprint the last bill", "Billing", "F10", () => Page<BillingViewModel>().ReprintLastCommand.ExecuteAsync(null)));
        _commands.Add(new("Find a bill / reprint / void", "Reports", null, () => { Go<ReportsViewModel>(); Page<ReportsViewModel>().Section = ReportSection.Bills; return Task.CompletedTask; }));
        _commands.Add(new("Low stock list", "Products", null, () => { Go<ProductsViewModel>(); Page<ProductsViewModel>().Filter = ProductFilter.LowStock; return Task.CompletedTask; }));
        _commands.Add(new("Add a product", "Products", null, () => { Go<ProductsViewModel>(); return Page<ProductsViewModel>().NewProductCommand.ExecuteAsync(null); }));
        _commands.Add(new("Today's sales", "Reports", null, () => { Go<ReportsViewModel>(); Page<ReportsViewModel>().Section = ReportSection.Today; return Task.CompletedTask; }));
        if (Session.IsOwner)
        {
            _commands.Add(new("Backup now", "Settings", null, () => _ui.Run(async () =>
            {
                var b = await _services.GetRequiredService<BackupService>().CreateAsync(Core.Domain.BackupKind.Manual, Session.Actor);
                _ui.Toasts.Show($"Backup saved: {b.FileName}");
            })));
            _commands.Add(new("Profit report", "Reports", null, () => { Go<ReportsViewModel>(); Page<ReportsViewModel>().Section = ReportSection.Profit; return Task.CompletedTask; }));
        }
        _commands.Add(new("Ask the AI assistant", "App", "Ctrl+J", () => ToggleAssistantCommand.ExecuteAsync(null)));
        _commands.Add(new("Notifications", "App", null, () => ToggleNoticesCommand.ExecuteAsync(null)));
        _commands.Add(new("Print barcode labels", "Products", "Ctrl+8", () => { Go<Features.Labels.LabelsViewModel>(); return Task.CompletedTask; }));
        if (Session.IsOwner) _commands.Add(new("Activity log", "Reports", null, () => { Go<ReportsViewModel>(); Page<ReportsViewModel>().Section = ReportSection.Activity; return Task.CompletedTask; }));
        _commands.Add(new("Keyboard shortcuts", "App", "F1", () => { ShowHelp(); return Task.CompletedTask; }));
        _commands.Add(new("Change my PIN", "App", null, () => ChangePinCommand.ExecuteAsync(null)));
        _commands.Add(new("Lock screen", "App", "Ctrl+L", () => { Lock(); return Task.CompletedTask; }));
        _commands.Add(new("Log out", "App", "Ctrl+Shift+L", () => { Logout(); return Task.CompletedTask; }));
    }

    [RelayCommand]
    private void OpenPalette()
    {
        IsHelpOpen = false;
        IsUserMenuOpen = false;
        PaletteText = "";
        FilterPalette();
        IsPaletteOpen = true;
    }

    partial void OnPaletteTextChanged(string value) => FilterPalette();

    private void FilterPalette()
    {
        var words = PaletteText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        PaletteResults.Clear();
        foreach (var c in _commands.Where(c => words.All(w => c.Title.Contains(w, StringComparison.OrdinalIgnoreCase) || c.Section.Contains(w, StringComparison.OrdinalIgnoreCase))))
            PaletteResults.Add(c);
        PaletteSelected = PaletteResults.FirstOrDefault();
    }

    public void MovePalette(int delta)
    {
        if (PaletteResults.Count == 0) return;
        var i = PaletteSelected is null ? 0 : PaletteResults.IndexOf(PaletteSelected);
        PaletteSelected = PaletteResults[(i + delta + PaletteResults.Count) % PaletteResults.Count];
    }

    [RelayCommand]
    private async Task RunPalette(PaletteCommand? command)
    {
        command ??= PaletteSelected;
        IsPaletteOpen = false;
        if (command is not null) await _ui.Run(command.Run);
    }

    [RelayCommand]
    private void CloseOverlays()
    {
        IsPaletteOpen = false;
        IsHelpOpen = false;
        IsUserMenuOpen = false;
        IsNoticesOpen = false;
    }

    /// <summary>All shortcuts, for the F1 help panel.</summary>
    public static IReadOnlyList<(string Section, IReadOnlyList<(string Key, string What)> Keys)> Help { get; } =
    [
        ("Everywhere", [("Ctrl+K", "Find any screen or action"), ("Ctrl+1 … Ctrl+8", "Billing, Stock in, Products, Reports, Bill editor, Settings, AI, Labels"),
            ("Ctrl+J", "AI assistant panel on any page"),
            ("F1", "This list"), ("Ctrl+L", "Lock the screen (the bill stays)"), ("Ctrl+Shift+L", "Log out"),
            ("Esc", "Close / cancel / back"), ("Enter", "OK in dialogs, next field in forms")]),
        ("Billing", [("Type or scan", "Barcode adds at once; a name shows a list (↑↓ Enter)"), ("3*", "then scan or Enter: add 3"),
            ("↑ ↓", "Choose a line on the bill"), ("+ / −", "One more / one less"), ("F2", "Back to the scan box"), ("F3", "Change quantity"),
            ("F4 or click the price", "Change price (this bill only)"), ("F5", "Line discount: 100 = Rs 100 off each, Tab for whole line / %"),
            ("F6", "Bill discount"), ("F7", "Past bills: print again or change"), ("F8", "Hold bill"), ("F9", "Recall held bill"), ("F10", "Reprint last bill"), ("F12", "Pay and print"),
            ("Del", "Remove line"), ("Ctrl+N", "New bill")]),
        ("Paying (F12)", [("C / K / B", "Cash / card / bank transfer"), ("Type amount, Enter", "Add the payment"), ("F12", "Exact cash and finish"),
            ("Esc", "Back to the bill")]),
        ("Forms", [("Ctrl+S", "Save"), ("Ctrl+N", "New item (Products)"), ("Tab / Enter", "Next field")]),
    ];
}
