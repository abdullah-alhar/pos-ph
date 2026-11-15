using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using VetPos.App.DesignSystem;
using VetPos.App.Features.Start;
using VetPos.App.Infrastructure;
using VetPos.Core.Settings;
using VetPos.Data.Services;

namespace VetPos.App.Shell;

/// <summary>Which screen fills the window: first-run setup, login, or the shop (with an optional lock on top).</summary>
public sealed partial class RootViewModel(IServiceProvider services, AuthService auth, Session session, SettingsService settings, ThemeManager theme, LogoService logo) : ObservableObject
{
    /// <summary>The shop's logo on the loading screen (null = the VetPOS paw).</summary>
    public System.Windows.Media.ImageSource? ShopLogo => logo.Load();

    [ObservableProperty] private object? _screen;
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private LockViewModel? _lock;
    [ObservableProperty] private string? _loadingText;

    private ShellViewModel? _shell;

    public async Task StartAsync()
    {
        logo.Changed += (_, _) => OnPropertyChanged(nameof(ShopLogo));
        var appearance = await settings.GetAsync<AppearanceSettings>(AppearanceSettings.Key);
        theme.Apply(Enum.TryParse<AppTheme>(appearance.Theme, out var t) ? t : AppTheme.System);
        theme.ApplyAccent(appearance.Accent);

        if (!await auth.HasUsersAsync())
        {
            var setup = services.GetRequiredService<SetupViewModel>();
            setup.Done += async (_, user) => await SignedInAsync(user);
            Screen = setup;
        }
        else
        {
            await ShowLoginAsync();
        }
    }

    private async Task ShowLoginAsync(string? username = null)
    {
        var login = services.GetRequiredService<LoginViewModel>();
        login.Username = username ?? "";
        login.Done += async (_, user) => await SignedInAsync(user);
        await login.LoadAsync();
        Screen = login;
    }

    private async Task SignedInAsync(UserInfo user)
    {
        LoadingText = $"Opening the till for {user.FullName}…";
        await Task.Yield();
        try
        {
            await OpenShellAsync(user);
        }
        finally
        {
            LoadingText = null;
        }
    }

    private async Task OpenShellAsync(UserInfo user)
    {
        session.User = user;
        _shell = services.GetRequiredService<ShellViewModel>();
        _shell.LockRequested += (_, _) => LockScreen();
        _shell.LogoutRequested += async (_, _) => await LogoutAsync();
        Lock = services.GetRequiredService<LockViewModel>();
        Lock.Unlocked += (_, _) => IsLocked = false;
        Lock.SwitchUser += async (_, _) => await LogoutAsync();
        Screen = _shell;
        IsLocked = false;
        await _shell.StartAsync();
    }

    public void LockScreen()
    {
        if (session.User is null || IsLocked) return;
        Lock?.Refresh();
        IsLocked = true;
    }

    public async Task LogoutAsync()
    {
        var name = session.User?.Username;
        if (_shell?.Page<Features.Billing.BillingViewModel>() is { } billing) await billing.SaveDraftNowAsync();
        session.User = null;
        _shell = null;
        IsLocked = false;
        await ShowLoginAsync(name);
    }

    /// <summary>Auto-lock after the idle minutes set in Settings → Billing (0 = never).</summary>
    public async Task CheckIdleAsync(TimeSpan idle)
    {
        if (session.User is null || IsLocked) return;
        var minutes = (await settings.GetAsync<BillingSettings>(BillingSettings.Key)).AutoLockMinutes;
        if (minutes > 0 && idle >= TimeSpan.FromMinutes(minutes)) LockScreen();
    }
}
