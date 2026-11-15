using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VetPos.App.DesignSystem.Controls;

public enum ToastKind
{
    Success,
    Info,
    Warning,
    Error,
}

/// <summary>Short confirmations: "Bill #1042 saved". Call from any view model; the shell shows them.</summary>
public interface IToastService
{
    void Show(string message, ToastKind kind = ToastKind.Success);
}

public sealed class ToastService : IToastService
{
    private ToastHost? _host;

    internal void Attach(ToastHost host) => _host = host;

    public void Show(string message, ToastKind kind = ToastKind.Success)
    {
        var host = _host;
        if (host is null) return;
        if (host.Dispatcher.CheckAccess()) host.Add(message, kind);
        else host.Dispatcher.BeginInvoke(() => host.Add(message, kind));
    }
}

/// <summary>
/// Stack of toasts at the bottom centre. Each slides 8 px up and fades in, stays 3.5 s
/// (errors 6 s, longer while the mouse is over it), and leaves the way it came. Click to dismiss.
/// At most three at once — the oldest goes first.
/// </summary>
public sealed class ToastHost : StackPanel
{
    private const int MaxVisible = 3;

    public ToastHost()
    {
        VerticalAlignment = VerticalAlignment.Bottom;
        HorizontalAlignment = HorizontalAlignment.Center;
        Margin = new Thickness(0, 0, 0, 64);
        IsHitTestVisible = true;
    }

    public void Attach(ToastService service) => service.Attach(this);

    internal void Add(string message, ToastKind kind)
    {
        while (Children.Count >= MaxVisible) Children.RemoveAt(0);
        var toast = Build(message, kind);
        Children.Add(toast);
        Motion.SlideIn(toast);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == ToastKind.Error ? 6 : 3.5) };
        timer.Tick += (_, _) =>
        {
            if (toast.IsMouseOver) return; // wait while the cashier is reading it
            timer.Stop();
            Remove(toast);
        };
        toast.MouseLeftButtonUp += (_, _) =>
        {
            timer.Stop();
            Remove(toast);
        };
        timer.Start();
    }

    private void Remove(FrameworkElement toast)
    {
        if (!Children.Contains(toast)) return;
        toast.IsHitTestVisible = false;
        Motion.SlideOut(toast, () => Children.Remove(toast));
    }

    private static Border Build(string message, ToastKind kind)
    {
        var (glyph, brushKey) = kind switch
        {
            ToastKind.Success => ("", "Brush.Success"),
            ToastKind.Warning => ("", "Brush.Warning"),
            ToastKind.Error => ("", "Brush.Danger"),
            _ => ("", "Brush.Accent"),
        };
        var icon = new TextBlock
        {
            Text = glyph,
            FontSize = 16,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
        icon.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        var text = new TextBlock
        {
            Text = message,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 440,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        text.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Text");

        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 12, 18, 12),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, text } },
        };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Line");
        border.SetResourceReference(EffectProperty, "Shadow.Sheet");
        AutomationProperties(border, message);
        return border;
    }

    private static void AutomationProperties(DependencyObject d, string message) =>
        System.Windows.Automation.AutomationProperties.SetName(d, message);

    protected override Geometry? GetLayoutClip(Size layoutSlotSize) => null; // let shadows show
}
