using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VetPos.App.Features.Start;

/// <summary>The teal brand side of the start screens: logo, shop name and a live clock.</summary>
public sealed class BrandPanel : ContentControl
{
    public static readonly DependencyProperty LogoProperty = DependencyProperty.Register(nameof(Logo), typeof(ImageSource), typeof(BrandPanel));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(BrandPanel));
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(BrandPanel));
    public static readonly DependencyProperty TimeTextProperty = DependencyProperty.Register(nameof(TimeText), typeof(string), typeof(BrandPanel));
    public static readonly DependencyProperty DateTextProperty = DependencyProperty.Register(nameof(DateText), typeof(string), typeof(BrandPanel));

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };

    public BrandPanel()
    {
        Focusable = false;
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { Tick(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
    }

    public ImageSource? Logo { get => (ImageSource?)GetValue(LogoProperty); set => SetValue(LogoProperty, value); }
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string? TimeText { get => (string?)GetValue(TimeTextProperty); private set => SetValue(TimeTextProperty, value); }
    public string? DateText { get => (string?)GetValue(DateTextProperty); private set => SetValue(DateTextProperty, value); }

    private void Tick()
    {
        var now = DateTime.Now;
        TimeText = now.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
        DateText = now.ToString("dddd, d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }
}
