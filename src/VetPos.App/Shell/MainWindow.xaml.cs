using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VetPos.App.DesignSystem.Controls;

using VetPos.App.Infrastructure;

namespace VetPos.App.Shell;

public partial class MainWindow : Window
{
    private DateTime _lastInput = DateTime.UtcNow;

    public MainWindow(RootViewModel root, DialogService dialogs, ToastService toasts, LogoService logo, UpdateService updates)
    {
        InitializeComponent();
        // Window and taskbar icon: the shop's logo when there is one.
        void SetIcon()
        {
            var shopIcon = logo.Load();
            if (shopIcon is not null) Icon = shopIcon;
            else Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/VetPOS;component/Assets/vetpos.ico"));
        }
        SetIcon();
        logo.Changed += (_, _) => SetIcon();
        DataContext = root;
        var closingForUpdate = false;
        var backupAttempted = false;
        Closing += async (_, e) =>
        {
            if (backupAttempted || !updates.NeedsExitBackup) return;
            e.Cancel = true;
            if (closingForUpdate) return;
            closingForUpdate = true;
            root.LoadingText = "Saving the till and preparing the update…";
            try
            {
                await root.SaveCurrentDraftAsync(requireSuccess: true);
                await updates.BackUpOnCloseAsync();
                backupAttempted = true;
                _ = Dispatcher.BeginInvoke(Close);
            }
            catch (Exception ex)
            {
                root.LoadingText = null;
                closingForUpdate = false;
                Serilog.Log.Warning(ex, "Could not save the open bill before closing");
                toasts.Show("Could not save the open bill. Try closing again.", ToastKind.Error);
            }
        };
        Dialogs.Attach(dialogs);
        Toasts.Attach(toasts);

        // Idle timer for auto-lock.
        InputManager.Current.PreProcessInput += (_, e) =>
        {
            if (e.StagingItem.Input is KeyboardEventArgs or MouseButtonEventArgs) _lastInput = DateTime.UtcNow;
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += async (_, _) => await ErrorHandling.Quietly(() => root.CheckIdleAsync(DateTime.UtcNow - _lastInput), "idle lock");
        timer.Start();
    }
}
