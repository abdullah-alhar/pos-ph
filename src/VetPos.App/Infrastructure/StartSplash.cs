using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using VetPos.Core.Infrastructure;

namespace VetPos.App.Infrastructure;

/// <summary>Shows local startup progress before the main window opens.</summary>
public sealed class StartSplash
{
    private static readonly Color Ink = Color.FromRgb(0x10, 0x23, 0x22);
    private static readonly Color Muted = Color.FromRgb(0x4D, 0x61, 0x5F);
    private static readonly Color Teal = Color.FromRgb(0x0B, 0x7A, 0x75);
    private static readonly Color Line = Color.FromRgb(0xDD, 0xE5, 0xE3);

    private readonly Window _window;
    private readonly TextBlock _status;
    private readonly StackPanel _loading;
    private readonly ProgressBar _progress;
    private bool _closed;

    private StartSplash(Window window, TextBlock status, StackPanel loading, ProgressBar progress)
    {
        _window = window;
        _status = status;
        _loading = loading;
        _progress = progress;
    }

    public static StartSplash Show()
    {
        var logo = LogoService.Read(Path.Combine(AppPaths.Default().AssetsDir, "logo.png")) ?? new BitmapImage(new Uri("pack://application:,,,/VetPOS;component/Assets/vetpos-256.png"));

        var tile = new Border
        {
            Width = 76, Height = 76, CornerRadius = new CornerRadius(10), Background = Brushes.White, Padding = new Thickness(8),
            BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new Image { Source = logo, Stretch = Stretch.Uniform },
        };

        var name = new TextBlock { Text = "Starting VetPOS", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Ink), Margin = new Thickness(0, 18, 0, 0) };
        var status = new TextBlock { Text = "Preparing startup…", FontSize = 14, Foreground = new SolidColorBrush(Muted), Margin = new Thickness(0, 6, 0, 14) };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var progress = new ProgressBar
        {
            Maximum = 4, Height = 5, Foreground = new SolidColorBrush(Teal), Background = new SolidColorBrush(Line),
        };
        AutomationProperties.SetName(progress, "Startup steps completed");

        var loading = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36, 24, 36, 24) };
        loading.Children.Add(tile);
        loading.Children.Add(name);
        loading.Children.Add(status);
        loading.Children.Add(progress);

        var window = new Window
        {
            Width = 420, Height = 260, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Title = "VetPOS",
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = true, Topmost = false,
            Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xF7)), BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(1),
            Content = loading,
            Icon = new BitmapImage(new Uri("pack://application:,,,/VetPOS;component/Assets/vetpos.ico")),
        };
        window.Show();
        // The app closes with its main window: the loading window must never count as that.
        if (ReferenceEquals(Application.Current.MainWindow, window)) Application.Current.MainWindow = null;
        return new StartSplash(window, status, loading, progress);
    }

    public void Status(string text, int completedSteps)
    {
        if (_closed) return;
        _status.Text = text;
        _progress.Value = completedSteps;
    }

    public void Close(TimeSpan fade)
    {
        if (_closed) return;
        _closed = true;
        _progress.Value = _progress.Maximum;
        if (fade <= TimeSpan.Zero || !DesignSystem.Motion.Enabled)
        {
            _window.Close();
            return;
        }
        var anim = new DoubleAnimation(1, 0, fade);
        anim.Completed += (_, _) => _window.Close();
        _window.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    /// <summary>
    /// Start-up failed: the loading window becomes an error page and waits until the person closes it.
    /// </summary>
    public Task ShowErrorAsync(string message, string reference, string logsFolder)
    {
        var done = new TaskCompletionSource();
        if (_closed) { done.SetResult(); return done.Task; }
        _window.Height = 380;
        var icon = new Border
        {
            Width = 56, Height = 56, CornerRadius = new CornerRadius(28), Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xE9, 0xE7)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 24, Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var title = new TextBlock { Text = "VetPOS could not start", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Ink), Margin = new Thickness(0, 14, 0, 6) };
        var body = new TextBlock { Text = message, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Ink), LineHeight = 20 };
        var refText = new TextBlock { Text = $"Reference for support: {reference}", FontSize = 13, Foreground = new SolidColorBrush(Muted), Margin = new Thickness(0, 12, 0, 0) };
        var logs = new Button { Content = "Open logs folder", Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 0) };
        logs.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{logsFolder}\"") { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { /* nothing more to do */ }
        };
        var close = new Button { Content = "Close", Padding = new Thickness(18, 6, 18, 6), IsDefault = true, IsCancel = true };
        close.Click += (_, _) => _window.Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        buttons.Children.Add(logs);
        buttons.Children.Add(close);
        var page = new StackPanel { Margin = new Thickness(32, 28, 32, 24) };
        page.Children.Add(icon);
        page.Children.Add(title);
        page.Children.Add(new ScrollViewer { Content = body, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        page.Children.Add(refText);
        page.Children.Add(buttons);
        _loading.Children.Clear();
        _window.Content = page;
        _window.Closed += (_, _) => done.TrySetResult();
        _closed = true;
        _window.Activate();
        return done.Task;
    }

}
