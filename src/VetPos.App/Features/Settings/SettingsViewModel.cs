using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Velopack;
using VetPos.AI;
using VetPos.App.DesignSystem;
using VetPos.App.DesignSystem.Controls;
using VetPos.App.Infrastructure;
using VetPos.Core.Domain;
using VetPos.Core.Infrastructure;
using VetPos.Core.Rules;
using VetPos.Core.Settings;
using VetPos.Data.Services;

namespace VetPos.App.Features.Settings;

public enum SettingsSection { Shop, Billing, Printer, Users, Backups, AI, Appearance, About }

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    private readonly AuthService _auth;
    private readonly BackupService _backups;
    private readonly UpdateService _updates;
    private readonly PrintService _printer;
    private readonly IAiKeyProtector _keys;
    private readonly AiClient _ai;
    private readonly ThemeManager _theme;
    private readonly AppPaths _paths;

    private readonly LogoService _logo;

    public SettingsViewModel(Ui ui, SettingsService settings, AuthService auth, BackupService backups, PrintService printer,
        IAiKeyProtector keys, AiClient ai, ThemeManager theme, AppPaths paths, LogoService logo, UpdateService updates) : base(ui)
    {
        _logo = logo;
        _updates = updates;
        LogoImage = logo.Load();
        _settings = settings;
        _auth = auth;
        _backups = backups;
        _printer = printer;
        _keys = keys;
        _ai = ai;
        _theme = theme;
        _paths = paths;
    }

    [ObservableProperty] private SettingsSection _section;

    // Shop
    [ObservableProperty] private string _shopName = "";
    [ObservableProperty] private string _shopAddress = "";
    [ObservableProperty] private string _shopPhone = "";
    [ObservableProperty] private string _shopEmail = "";

    [ObservableProperty] private System.Windows.Media.ImageSource? _logoImage;

    [RelayCommand]
    private Task ChooseLogo() => Ui.Run(() =>
    {
        var dialog = new OpenFileDialog { Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp", Title = "Choose the shop logo" };
        if (dialog.ShowDialog() != true) return Task.CompletedTask;
        _logo.SetLogo(dialog.FileName);
        LogoImage = _logo.Load();
        Ui.Toasts.Show(LogoImage is null ? "The logo was saved but could not be shown." : "Logo saved. It shows in the app and prints on receipts.");
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task RemoveLogo() => Ui.Run(() =>
    {
        _logo.RemoveLogo();
        LogoImage = null;
        Ui.Toasts.Show("Logo removed.");
        return Task.CompletedTask;
    });

    // Billing
    [ObservableProperty] private bool _allowSellAboveStock;
    [ObservableProperty] private bool _autoPrint;
    [ObservableProperty] private bool _confirmRemoveLine;
    [ObservableProperty] private string _autoLockMinutes = "0";

    // Printer
    [ObservableProperty] private PrinterMode _printerMode;
    [ObservableProperty] private string _windowsPrinterName = "";
    [ObservableProperty] private string _networkHost = "";
    [ObservableProperty] private string _networkPort = "9100";
    [ObservableProperty] private bool _narrowPaper;
    [ObservableProperty] private bool _autoCut;
    [ObservableProperty] private bool _openDrawer;
    [ObservableProperty] private string _copies = "1";
    public ObservableCollection<string> InstalledPrinters { get; } = [];
    public IReadOnlyList<PrinterMode> PrinterModes { get; } = Enum.GetValues<PrinterMode>();

    // Users
    public ObservableCollection<UserInfo> Users { get; } = [];

    // Backups
    public ObservableCollection<BackupFile> BackupFiles { get; } = [];
    [ObservableProperty] private bool _dailyBackup;
    [ObservableProperty] private string _keepBackups = "30";
    [ObservableProperty] private string _extraFolder = "";

    // AI
    [ObservableProperty] private bool _aiEnabled;
    [ObservableProperty] private AiProvider? _aiProvider;
    [ObservableProperty] private string _aiModel = "";
    [ObservableProperty] private string _aiCustomUrl = "";
    [ObservableProperty] private string _aiKeyHint = "";
    public IReadOnlyList<AiProvider> ProviderOptions { get; } = AiProviders.All;
    public string? AiKeyHelp => AiProvider?.KeyHelp;
    [ObservableProperty] private string? _aiTestStatus;
    private AiSettings _savedAi = new();

    // Appearance / about
    [ObservableProperty] private string _updateSource = "";
    public string Version { get; } = App.Version;
    public string DataFolder => _paths.Root;

    partial void OnAiProviderChanged(AiProvider? value)
    {
        OnPropertyChanged(nameof(AiKeyHelp));
        AiModel = value?.Id == _savedAi.Provider ? _savedAi.Model : "";
        AiKeyHint = AiKeyLabel(value?.Id);
        AiTestStatus = null;
    }

    private string AiKeyLabel(string? providerId) =>
        providerId == _savedAi.Provider && !string.IsNullOrEmpty(_savedAi.EncryptedKey)
            ? $"Saved key: {_savedAi.KeyHint}"
            : "Paste the key for this provider.";

    public override Task OnShownAsync() => LoadAsync(async () =>
    {
        var shop = await _settings.GetAsync<ShopSettings>(ShopSettings.Key);
        (ShopName, ShopAddress, ShopPhone, ShopEmail) = (shop.Name, shop.Address, shop.Phone, shop.Email);

        var billing = await _settings.GetAsync<BillingSettings>(BillingSettings.Key);
        (AllowSellAboveStock, AutoPrint, ConfirmRemoveLine, AutoLockMinutes) = (billing.AllowSellAboveStock, billing.AutoPrint, billing.ConfirmRemoveLine, billing.AutoLockMinutes.ToString(CultureInfo.InvariantCulture));

        var p = await _settings.GetAsync<PrinterSettings>(PrinterSettings.Key);
        PrinterMode = p.Mode;
        InstalledPrinters.Clear();
        foreach (var name in PrintService.InstalledPrinters()) InstalledPrinters.Add(name);
        WindowsPrinterName = p.WindowsPrinterName;
        NetworkHost = p.NetworkHost;
        NetworkPort = p.NetworkPort.ToString(CultureInfo.InvariantCulture);
        NarrowPaper = p.CharsPerLine < 40;
        AutoCut = p.AutoCut;
        OpenDrawer = p.OpenDrawerOnCash;
        Copies = p.Copies.ToString(CultureInfo.InvariantCulture);

        await LoadUsersAsync();
        LoadBackups();
        var b = await _settings.GetAsync<BackupSettings>(BackupSettings.Key);
        (DailyBackup, KeepBackups, ExtraFolder) = (b.DailyBackup, b.Keep.ToString(CultureInfo.InvariantCulture), b.ExtraFolder);

        var ai = await _settings.GetAsync<AiSettings>(AiSettings.Key);
        _savedAi = ai;
        AiEnabled = ai.Enabled;
        AiProvider = AiProviders.Get(ai.Provider);
        AiModel = ai.Model;
        AiCustomUrl = ai.CustomUrl;
        AiKeyHint = AiKeyLabel(ai.Provider);

        AccentChoice = _theme.Accent;
        UpdateSource = (await _settings.GetAsync<UpdateSettings>(UpdateSettings.Key)).Source;
    });

    private async Task LoadUsersAsync()
    {
        Users.Clear();
        foreach (var u in await _auth.ListAsync()) Users.Add(u);
    }

    private void LoadBackups()
    {
        BackupFiles.Clear();
        foreach (var b in _backups.List()) BackupFiles.Add(b);
    }

    private static int Int(string text, int min, int max, string what) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max
            ? v : throw new PosException($"{what} must be a number from {min} to {max}.");

    [RelayCommand]
    private Task SaveShop() => Ui.Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(ShopName)) throw new PosException("Enter the shop name.");
        var current = await _settings.GetAsync<ShopSettings>(ShopSettings.Key);
        await _settings.SaveAsync(ShopSettings.Key, current with { Name = ShopName.Trim(), Address = ShopAddress.Trim(), Phone = ShopPhone.Trim(), Email = ShopEmail.Trim() }, Ui.Session.Actor);
        Ui.Toasts.Show("Shop details saved.");
    });

    [RelayCommand]
    private Task SaveBilling() => Ui.Run(async () =>
    {
        await _settings.SaveAsync(BillingSettings.Key, new BillingSettings
        {
            AllowSellAboveStock = AllowSellAboveStock, AutoPrint = AutoPrint, ConfirmRemoveLine = ConfirmRemoveLine,
            AutoLockMinutes = Int(AutoLockMinutes, 0, 240, "Auto-lock minutes"),
        }, Ui.Session.Actor);
        Ui.Toasts.Show("Billing settings saved.");
    });

    private PrinterSettings PrinterFromForm() => new()
    {
        Mode = PrinterMode,
        WindowsPrinterName = WindowsPrinterName ?? "",
        NetworkHost = NetworkHost.Trim(),
        NetworkPort = Int(NetworkPort, 1, 65535, "Port"),
        CharsPerLine = NarrowPaper ? 32 : 48,
        AutoCut = AutoCut,
        OpenDrawerOnCash = OpenDrawer,
        Copies = Int(Copies, 1, 3, "Copies"),
    };

    [RelayCommand]
    private Task SavePrinter() => Ui.Run(async () =>
    {
        await _settings.SaveAsync(PrinterSettings.Key, PrinterFromForm(), Ui.Session.Actor);
        Ui.Toasts.Show("Printer settings saved.");
    });

    [RelayCommand]
    private Task TestPrinter() => Ui.Run(async () =>
    {
        var p = PrinterFromForm();
        if (p.Mode == PrinterMode.None) throw new PosException("Choose how the printer is connected first.");
        await _printer.TestAsync(p);
        Ui.Toasts.Show(p.Mode == PrinterMode.File ? $"Test receipt saved in {_paths.Root}\\printed" : "Test receipt sent. Check the printer.");
    });

    [RelayCommand]
    private Task OpenDrawerNow() => Ui.Run(_printer.OpenDrawerAsync);

    [RelayCommand]
    private async Task EditUser(UserInfo? user)
    {
        var sheet = new DialogSheet(Ui.Dialogs, user is null ? "Add a user" : $"Change {user.FullName}", "People sign in with their username and PIN.");
        var name = sheet.Field("Full name", DialogSheet.Input(user?.FullName));
        var username = sheet.Field("Username", DialogSheet.Input(user?.Username));
        var role = sheet.Field("Role", new System.Windows.Controls.ComboBox { ItemsSource = Enum.GetValues<UserRole>(), SelectedItem = user?.Role ?? UserRole.Cashier, MinHeight = 40 },
            "Owner: everything. Cashier: billing, stock in, products, reports (no profit, no settings).");
        var pin = sheet.Field(user is null ? "PIN (4–8 digits)" : "New PIN (leave empty to keep)", DialogSheet.PinInput());
        var active = sheet.Add(new System.Windows.Controls.CheckBox { Content = "Can sign in", IsChecked = user?.IsActive ?? true, Margin = new System.Windows.Thickness(2, 14, 0, 0) });
        name.Loaded += (_, _) => name.Focus();
        sheet.Buttons(user is null ? "Add user" : "Save", async () =>
        {
            try
            {
                await _auth.SaveAsync(new UserInput(user?.Id, username.Text, name.Text, (UserRole)role.SelectedItem, pin.Password, active.IsChecked == true), Ui.Session.Actor);
                sheet.Close(true);
            }
            catch (PosException ex) { sheet.ShowError(ex.Message); }
        });
        if (await Ui.Dialogs.ShowAsync(sheet) is true)
        {
            await LoadUsersAsync();
            Ui.Toasts.Show("User saved.");
        }
    }

    [RelayCommand]
    private Task SaveBackupSettings() => Ui.Run(async () =>
    {
        await _settings.SaveAsync(BackupSettings.Key, new BackupSettings { DailyBackup = DailyBackup, Keep = Int(KeepBackups, 3, 365, "Backups to keep"), ExtraFolder = ExtraFolder.Trim() }, Ui.Session.Actor);
        Ui.Toasts.Show("Backup settings saved.");
    });

    [RelayCommand]
    private void ChooseExtraFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder for a second copy (e.g. a USB drive)" };
        if (dialog.ShowDialog() == true) ExtraFolder = dialog.FolderName;
    }

    [RelayCommand]
    private Task BackupNow() => Ui.Run(async () =>
    {
        IsBusy = true;
        try
        {
            var b = await _backups.CreateAsync(BackupKind.Manual, Ui.Session.Actor);
            LoadBackups();
            Ui.Toasts.Show($"Backup saved and checked: {b.FileName}");
        }
        finally { IsBusy = false; }
    });

    [RelayCommand]
    private async Task Restore(BackupFile? file)
    {
        string? path = file?.FullPath;
        if (path is null)
        {
            var dialog = new OpenFileDialog { Filter = "VetPOS backup|*.db", Title = "Choose a backup to restore", InitialDirectory = _paths.BackupsDir };
            if (dialog.ShowDialog() != true) return;
            path = dialog.FileName;
        }
        if (!await Ui.Dialogs.ConfirmAsync("Restore this backup?",
                $"All data is replaced with {System.IO.Path.GetFileName(path)}. A copy of today's data is saved first, so this can be undone. VetPOS restarts after.",
                "Restore", danger: true))
            return;
        if (await Ui.Run(() => _backups.RestoreAsync(path, Ui.Session.Actor)))
        {
            await Ui.Dialogs.AlertAsync("Data restored", "VetPOS will now restart to load the restored data.");
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
            System.Windows.Application.Current.Shutdown();
        }
    }

    [RelayCommand]
    private void OpenBackupFolder() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.BackupsDir}\"") { UseShellExecute = true });

    private (AiProvider Provider, string Url, string Model) SelectedAi()
    {
        var provider = AiProvider ?? AiProviders.All[0];
        if (provider.Id == "github") throw new PosException("GitHub Models has closed. Choose another AI provider.");
        var url = provider.Id == "custom" ? AiCustomUrl.Trim() : provider.BaseUrl;
        if (provider.Id == "custom" && !AiProviders.IsSafeUrl(url))
            throw new PosException("Enter an https:// address, or http:// for a service on the shop network.");
        var model = string.IsNullOrWhiteSpace(AiModel) ? provider.DefaultModel : AiModel.Trim();
        if (model.Length == 0) throw new PosException("Enter the model name for this service.");
        return (provider, url, model);
    }

    private static bool CanReuseKey(AiSettings saved, AiProvider provider, string url) =>
        saved.Provider == provider.Id && (provider.Id != "custom" ||
            string.Equals(saved.CustomUrl.Trim().TrimEnd('/'), url.TrimEnd('/'), StringComparison.Ordinal));

    [RelayCommand]
    private Task SaveAi(System.Windows.Controls.PasswordBox keyBox) => Ui.Run(async () =>
    {
        var (provider, url, _) = SelectedAi();
        var current = await _settings.GetAsync<AiSettings>(AiSettings.Key);
        var next = current with { Enabled = AiEnabled, Provider = provider.Id, Model = AiModel.Trim(), CustomUrl = AiCustomUrl.Trim() };
        if (!CanReuseKey(current, provider, url)) next = next with { EncryptedKey = "", KeyHint = "" };
        var key = keyBox.Password.Trim();
        if (key.Length > 0)
            next = next with { EncryptedKey = _keys.Protect(key), KeyHint = AiProviders.KeyHint(key), Enabled = true };
        if (next.Enabled && provider.NeedsKey && string.IsNullOrEmpty(next.EncryptedKey))
            throw new PosException($"Paste the key for {provider.Name} first.");
        await _settings.SaveAsync(AiSettings.Key, next, Ui.Session.Actor);
        _savedAi = next;
        AiEnabled = next.Enabled;
        AiKeyHint = AiKeyLabel(provider.Id);
        keyBox.Clear();
        Ui.Toasts.Show("AI settings saved.");
    });

    [RelayCommand]
    private async Task TestAi(System.Windows.Controls.PasswordBox keyBox)
    {
        AiTestStatus = "Checking the selected AI service…";
        if (await Ui.Run(async () =>
        {
            var (provider, url, model) = SelectedAi();
            var typedKey = keyBox.Password.Trim();
            var key = typedKey.Length > 0 ? typedKey
                : CanReuseKey(_savedAi, provider, url) && _savedAi.EncryptedKey.Length > 0 ? _keys.Unprotect(_savedAi.EncryptedKey) : null;
            if (provider.NeedsKey && string.IsNullOrWhiteSpace(key))
                throw new PosException($"Paste the key for {provider.Name} first.");
            var result = await _ai.TestConnectionAsync(url, key, model);
            AiTestStatus = $"{provider.Name} can look up data ({result.TotalSeconds:0.0} s). Press Save to use these settings.";
        })) return;
        AiTestStatus = "The connection test failed. See the message on screen.";
    }

    /// <summary>The colour swatches in Settings → Appearance.</summary>
    public IReadOnlyList<AccentOption> Accents { get; } = ThemeManager.Accents;

    [ObservableProperty] private string _accentChoice = "Teal";

    async partial void OnAccentChoiceChanged(string value)
    {
        if (value == _theme.Accent) return;
        _theme.ApplyAccent(value);
        var current = await _settings.GetAsync<AppearanceSettings>(AppearanceSettings.Key);
        await Ui.Run(() => _settings.SaveAsync(AppearanceSettings.Key, current with { Accent = value }, Ui.Session.Actor));
    }

    [RelayCommand]
    private void OpenKeyPage()
    {
        if (AiProvider?.KeyUrl is { Length: > 0 } url) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>
    /// Uninstall through Velopack's Update.exe (removes the program, shortcuts and the Windows
    /// "Apps" entry). The shop data stays unless the owner chooses to delete it; then a copy of the
    /// database and the backups goes to Documents first.
    /// </summary>
    [RelayCommand]
    private async Task Uninstall()
    {
        var sheet = new DialogSheet(Ui.Dialogs, "Uninstall VetPOS?", "VetPOS will close and be removed from this PC.", 460);
        var deleteData = sheet.Add(new System.Windows.Controls.CheckBox
        {
            Content = "Also delete the shop's data (products, bills, backups). A copy is saved to Documents first.",
            Margin = new System.Windows.Thickness(2, 12, 0, 0),
        });
        sheet.Buttons("Uninstall", () => sheet.Close(deleteData.IsChecked == true), danger: true);
        if (await Ui.Dialogs.ShowAsync(sheet) is not bool delete) return;

        await Ui.Run(async () =>
        {
            var updateExe = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "Update.exe"));
            if (!System.IO.File.Exists(updateExe))
            {
                Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
                throw new PosException("This copy was not installed with Setup. Remove it from Windows Settings → Apps, or delete its folder.");
            }
            var b = await _backups.CreateAsync(BackupKind.Manual, Ui.Session.Actor);
            if (delete)
            {
                var keep = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"VetPOS data copy {DateTime.Now:yyyy-MM-dd HHmm}");
                System.IO.Directory.CreateDirectory(keep);
                System.IO.File.Copy(b.FullPath, System.IO.Path.Combine(keep, b.FileName));
                foreach (var f in System.IO.Directory.GetFiles(_paths.BackupsDir)) System.IO.File.Copy(f, System.IO.Path.Combine(keep, System.IO.Path.GetFileName(f)), overwrite: true);
                // Delete the data folder a few seconds after VetPOS has closed, but only when it really
                // is VetPOS's own data folder (never a drive or a shared folder set by VETPOS_DATA).
                var root = new System.IO.DirectoryInfo(_paths.Root);
                var looksLikeOurs = root.Parent is not null
                    && System.IO.File.Exists(_paths.DatabaseFile)
                    && root.EnumerateFileSystemInfos().All(i => i.Name is "vetpos.db" or "vetpos.db-wal" or "vetpos.db-shm" or "backups" or "logs" or "assets"
                        || i.Name.StartsWith("vetpos.", StringComparison.OrdinalIgnoreCase))
                    && !_paths.Root.Contains('"', StringComparison.Ordinal);
                if (looksLikeOurs)
                    Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 4 /nobreak >nul & rmdir /s /q \"{_paths.Root}\"") { CreateNoWindow = true, UseShellExecute = false });
                else
                    Ui.Toasts.Show($"The data folder also holds other files, so it was not deleted: {_paths.Root}. A copy is in Documents.", DesignSystem.Controls.ToastKind.Warning);
            }
            Process.Start(new ProcessStartInfo(updateExe, "--uninstall") { UseShellExecute = true });
            System.Windows.Application.Current.Shutdown();
        });
    }

    [RelayCommand]
    private void OpenDataFolder() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.Root}\"") { UseShellExecute = true });

    [RelayCommand]
    private void OpenLogs() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.LogsDir}\"") { UseShellExecute = true });

    /// <summary>
    /// A zip on the Desktop for support: the last 14 days of logs and a short info file (version,
    /// Windows, screen, printer setting). No bills, customers or PINs are included.
    /// </summary>
    [RelayCommand]
    private Task SaveSupportFile() => Ui.Run(async () =>
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var target = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"VetPOS-support-{stamp}.zip");
        var printer = await _settings.GetAsync<PrinterSettings>(PrinterSettings.Key);
        // Screen and graphics details must be read on the UI thread.
        var info = string.Join(Environment.NewLine,
            $"VetPOS {Version}",
            $"Saved {DateTime.Now:yyyy-MM-dd HH:mm}",
            $"Windows {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})",
            $"Screen {System.Windows.SystemParameters.PrimaryScreenWidth}x{System.Windows.SystemParameters.PrimaryScreenHeight}",
            $"Graphics tier {System.Windows.Media.RenderCapability.Tier >> 16}",
            $"Printer mode {printer.Mode}",
            $"Data folder {_paths.Root}");
        await Task.Run(() =>
        {
            using var zip = System.IO.Compression.ZipFile.Open(target, System.IO.Compression.ZipArchiveMode.Create);
            var entry = zip.CreateEntry("info.txt");
            using (var w = new System.IO.StreamWriter(entry.Open())) w.Write(info);
            var since = DateTime.Now.AddDays(-14);
            foreach (var f in new System.IO.DirectoryInfo(_paths.LogsDir).EnumerateFiles("*.log").Where(f => f.LastWriteTime >= since))
            {
                // The log of today is still open: copy it through a shared read.
                using var src = new System.IO.FileStream(f.FullName, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                using var dst = zip.CreateEntry("logs/" + f.Name).Open();
                src.CopyTo(dst);
            }
        });
        Ui.Toasts.Show($"Support file saved on the Desktop: {System.IO.Path.GetFileName(target)}. Send it to support (WhatsApp or email).");
    });

    [RelayCommand]
    private Task SaveUpdateSource() => Ui.Run(async () =>
    {
        await _settings.SaveAsync(UpdateSettings.Key, new UpdateSettings { Source = UpdateSource.Trim() }, Ui.Session.Actor);
        Ui.Toasts.Show("Update source saved.");
    });

    public UpdateService Updates => _updates;

    [RelayCommand]
    private Task CheckUpdates() => _updates.CheckAsync(quiet: false);
}
