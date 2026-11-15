using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using VetPos.Core.Infrastructure;

namespace VetPos.App.Infrastructure;

/// <summary>
/// The loading window while VetPOS starts: logo (the shop's own when it has one) with a soft pulse,
/// the name, a moving bar and what is happening right now. It closes by itself when the till is
/// ready; if start-up fails it turns into a clear error page (what happened, reference, logs).
/// Built in code with fixed colours: the theme and settings are not loaded yet at this point.
/// </summary>
public sealed class StartSplash
{
    private static readonly Color Ink = Color.FromRgb(0x10, 0x23, 0x22);
    private static readonly Color Muted = Color.FromRgb(0x4D, 0x61, 0x5F);
    private static readonly Color Teal = Color.FromRgb(0x0B, 0x7A, 0x75);
    private static readonly Color Line = Color.FromRgb(0xDD, 0xE5, 0xE3);

    private readonly Window _window;
    private readonly TextBlock _status;
    private readonly StackPanel _loading;
    private bool _closed;

    private StartSplash(Window window, TextBlock status, StackPanel loading)
    {
        _window = window;
        _status = status;
        _loading = loading;
    }

    public static StartSplash Show()
    {
        var logo = LoadShopLogo() ?? new BitmapImage(new Uri("pack://application:,,,/VetPOS;component/Assets/vetpos-256.png"));

        // Logo in a white tile with a soft "breathing" pulse.
        var tile = new Border
        {
            Width = 112, Height = 112, CornerRadius = new CornerRadius(26), Background = Brushes.White, Padding = new Thickness(14),
            BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Image { Source = logo, Stretch = Stretch.Uniform },
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
        };
        RenderOptions.SetBitmapScalingMode(tile.Child, BitmapScalingMode.HighQuality);

        var name = new TextBlock { Text = "VetPOS", FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Ink), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        var status = new TextBlock { Text = "Starting…", FontSize = 14, Foreground = new SolidColorBrush(Muted), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 16) };

        // A short teal bar that slides across a light track.
        var track = new Grid { Width = 220, Height = 4, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Center, Background = new SolidColorBrush(Line) };
        var bar = new Border { Width = 70, Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Teal), HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = new TranslateTransform(-70, 0) };
        track.Children.Add(bar);

        var loading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        loading.Children.Add(tile);
        loading.Children.Add(name);
        loading.Children.Add(status);
        loading.Children.Add(track);

        var window = new Window
        {
            Width = 460, Height = 340, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Title = "VetPOS",
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = true, Topmost = false,
            Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF8, 0xF7)), BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(1),
            Content = loading,
            Icon = new BitmapImage(new Uri("pack://application:,,,/VetPOS;component/Assets/vetpos.ico")),
        };
        window.Loaded += (_, _) =>
        {
            ((TranslateTransform)bar.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-70, 220, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            var pulse = new DoubleAnimation(1, 0.96, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            ((ScaleTransform)tile.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            ((ScaleTransform)tile.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        };
        window.Show();
        // The app closes with its main window: the loading window must never count as that.
        if (ReferenceEquals(Application.Current.MainWindow, window)) Application.Current.MainWindow = null;
        return new StartSplash(window, status, loading);
    }

    /// <summary>What is happening now ("Opening the shop's data…").</summary>
    public void Status(string text)
    {
        if (!_closed) _status.Text = text;
    }

    public void Close(TimeSpan fade)
    {
        if (_closed) return;
        _closed = true;
        if (fade <= TimeSpan.Zero)
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

    private static BitmapImage? LoadShopLogo()
    {
        try
        {
            var file = Path.Combine(AppPaths.Default().AssetsDir, "logo.png");
            if (!File.Exists(file)) return null;
            using var stream = new MemoryStream(File.ReadAllBytes(file));
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = stream;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or ArgumentException
                                   or UnauthorizedAccessException or FileFormatException or System.Runtime.InteropServices.COMException)
        {
            return null; // the VetPOS picture instead
        }
    }
}
