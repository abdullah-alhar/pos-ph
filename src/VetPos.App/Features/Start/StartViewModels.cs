using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VetPos.App.Infrastructure;
using VetPos.Core.Rules;
using VetPos.Core.Security;
using VetPos.Core.Settings;
using VetPos.Data.Services;
namespace VetPos.App.Features.Start;

/// <summary>First start: the shop's name and the owner's account.</summary>
public sealed partial class SetupViewModel(AuthService auth, SettingsService settings, VetPos.Data.DatabaseInitializer database, Ui ui) : ObservableObject
{
    [ObservableProperty] private string _shopName = "";
    [ObservableProperty] private string _ownerName = "";
    [ObservableProperty] private string _username = "owner";
    [ObservableProperty] private bool _addSampleProducts;
    [ObservableProperty] private string? _error;

    public event EventHandler<UserInfo>? Done;

    /// <summary>The view passes the two PIN boxes (PasswordBox values are not bindable on purpose).</summary>
    [RelayCommand]
    private async Task Create((string Pin, string Confirm) pins)
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(ShopName)) { Error = "Enter the shop's name."; return; }
        if (string.IsNullOrWhiteSpace(OwnerName)) { Error = "Enter your name."; return; }
        if (PinHasher.CheckPin(pins.Pin) is { } pinError) { Error = pinError; return; }
        if (pins.Pin != pins.Confirm) { Error = "The two PINs are not the same."; return; }
        await ui.Run(async () =>
        {
            try
            {
                var owner = await auth.CreateFirstOwnerAsync(Username, OwnerName, pins.Pin);
                await settings.SaveAsync(ShopSettings.Key, new ShopSettings { Name = ShopName.Trim() }, owner.ToActor());
                if (AddSampleProducts) await database.AddSampleDataIfEmptyAsync();
                Done?.Invoke(this, owner);
            }
            catch (PosException ex)
            {
                Error = ex.Message;
            }
        });
    }
}

/// <summary>One person on the sign-in screen: click your name, type your PIN.</summary>
public sealed record UserTile(UserInfo User)
{
    public string Name => User.FullName;
    public string Role => User.Role == Core.Domain.UserRole.Owner ? "Owner" : "Cashier";

    public string Initials
    {
        get
        {
            var parts = User.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => char.IsLetter(p[0])).ToArray();
            return parts.Length switch
            {
                0 => User.Username[..1].ToUpperInvariant(),
                1 => parts[0][..1].ToUpperInvariant(),
                _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
            };
        }
    }
}

public sealed partial class LoginViewModel(AuthService auth, SettingsService settings, LogoService logo, Ui ui, UpdateService updates) : ObservableObject
{
    public System.Windows.Media.ImageSource? ShopLogo { get; } = logo.Load();

    public System.Collections.ObjectModel.ObservableCollection<UserTile> Users { get; } = [];

    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _shopName = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private UserTile? _selectedUser;
    [ObservableProperty] private bool _typeUsername;

    /// <summary>
    /// The update service: the login screen shows a banner when a new version is ready,
    /// so the shop owner sees it even before signing in.
    /// </summary>
    public UpdateService Updates { get; } = updates;

    public event EventHandler<UserInfo>? Done;

    public bool ShowUserTiles => Users.Count > 0 && !TypeUsername;

    partial void OnTypeUsernameChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUserTiles));
        OnPropertyChanged(nameof(PinHint));
    }

    public string PinHint => SelectedUser is { } u && ShowUserTiles ? $"Enter the PIN for {u.Name}" : "Enter your PIN";

    partial void OnSelectedUserChanged(UserTile? value)
    {
        OnPropertyChanged(nameof(PinHint));
        if (value is null) return;
        Username = value.User.Username;
        Error = null;
    }

    public async Task LoadAsync()
    {
        ShopName = (await settings.GetAsync<ShopSettings>(ShopSettings.Key)).Name;
        var users = (await auth.ListAsync()).Where(u => u.IsActive).ToList();
        Users.Clear();
        // Up to 8 faces; a bigger team types the username.
        if (users.Count <= 8) foreach (var u in users) Users.Add(new UserTile(u));
        SelectedUser = Users.FirstOrDefault(u => u.User.Username.Equals(Username, StringComparison.OrdinalIgnoreCase))
            ?? Users.OrderByDescending(u => u.User.LastLoginAt ?? DateTime.MinValue).FirstOrDefault();
        OnPropertyChanged(nameof(ShowUserTiles));
    }

    [RelayCommand]
    private void ToggleTypeUsername() => TypeUsername = !TypeUsername;

    [RelayCommand]
    private async Task SignIn(string pin)
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Username)) { Error = ShowUserTiles ? "Choose your name." : "Type your username."; return; }
        if (string.IsNullOrEmpty(pin)) { Error = "Type your PIN."; return; }
        IsBusy = true;
        await ui.Run(async () =>
        {
            try
            {
                var user = await auth.LoginAsync(Username, pin);
                Done?.Invoke(this, user);
            }
            catch (PosException ex)
            {
                Error = ex.Message;
            }
        });
        IsBusy = false;
    }
}

/// <summary>Screen lock (Ctrl+L or idle): the bill stays; the same person's PIN opens it again.</summary>
public sealed partial class LockViewModel(AuthService auth, Session session) : ObservableObject
{
    [ObservableProperty] private string? _error;
    private int _fails;
    private DateTime _waitUntil;

    public string Name => session.User?.FullName ?? "";
    public string Initials => session.User is { } u ? new UserTile(u).Initials : "";

    public event EventHandler? Unlocked;
    public event EventHandler? SwitchUser;

    [RelayCommand]
    private async Task Unlock(string pin)
    {
        Error = null;
        if (session.User is null) return;
        if (_waitUntil > DateTime.UtcNow)
        {
            Error = $"Too many wrong PINs. Try again in {(int)Math.Ceiling((_waitUntil - DateTime.UtcNow).TotalSeconds)} seconds.";
            return;
        }
        if (await auth.CheckPinAsync(session.User.Id, pin))
        {
            _fails = 0;
            Unlocked?.Invoke(this, EventArgs.Empty);
            return;
        }
        _fails++;
        if (_fails >= 5)
        {
            _fails = 0;
            _waitUntil = DateTime.UtcNow.AddSeconds(30);
            Error = "Too many wrong PINs. Wait 30 seconds, or sign in as someone else.";
        }
        else
        {
            Error = $"Wrong PIN. {5 - _fails} tries left.";
        }
    }

    [RelayCommand]
    private void Switch() => SwitchUser?.Invoke(this, EventArgs.Empty);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Initials));
    }
}
