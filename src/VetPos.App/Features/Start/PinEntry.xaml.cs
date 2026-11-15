using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VetPos.App.Features.Start;

/// <summary>
/// The sign-in / unlock PIN control: dots that fill as you type, a phone-style keypad
/// (⌫ · 0 · →), Enter or → submits. Shows a hint or an error under the dots.
/// </summary>
public partial class PinEntry : UserControl
{
    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(PinEntry), new PropertyMetadata("Type your PIN", (d, _) => ((PinEntry)d).ShowMessage()));
    public static readonly DependencyProperty ErrorProperty =
        DependencyProperty.Register(nameof(Error), typeof(string), typeof(PinEntry), new PropertyMetadata(null, (d, e) => ((PinEntry)d).OnError(e.NewValue as string)));

    public event EventHandler? Submitted;

    public PinEntry()
    {
        InitializeComponent();
        foreach (var key in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "back", "0", "go" })
        {
            var b = new Button { Tag = key };
            b.SetResourceReference(StyleProperty, key switch { "go" => "PinKey.Go", "back" => "PinKey.Plain", _ => "PinKey" });
            b.Content = key switch
            {
                "back" => Glyph("", 22),
                "go" => Glyph("", 22),
                _ => key,
            };
            b.ToolTip = key switch { "back" => "Delete (Backspace)", "go" => "Sign in (Enter)", _ => null };
            b.Click += OnKey;
            Keys.Children.Add(b);
        }
        Box.PasswordChanged += (_, _) => { DrawDots(); if (Error is not null) SetCurrentValue(ErrorProperty, null); };
        Box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Submit();
        };
        // Only digits.
        Box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsAsciiDigit);
        DataObject.AddPastingHandler(Box, (_, e) => e.CancelCommand());
        Dots.MouseLeftButtonDown += (_, _) => FocusInput();
        Loaded += (_, _) => { DrawDots(); ShowMessage(); };
    }

    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public string? Error { get => (string?)GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }

    public string Password => Box.Password;

    public void Clear() => Box.Clear();

    public void FocusInput() => Dispatcher.BeginInvoke(() => Box.Focus(), System.Windows.Threading.DispatcherPriority.Input);

    private void Submit()
    {
        if (Box.Password.Length == 0) { SetCurrentValue(ErrorProperty, "Type your PIN first."); return; }
        Submitted?.Invoke(this, EventArgs.Empty);
    }

    private void OnKey(object sender, RoutedEventArgs e)
    {
        var key = (string)((Button)sender).Tag;
        var pin = Box.Password;
        switch (key)
        {
            case "go": Submit(); break;
            case "back": Box.Password = pin.Length > 0 ? pin[..^1] : ""; break;
            default: if (pin.Length < Box.MaxLength) Box.Password = pin + key; break;
        }
        Box.Focus();
    }

    private void OnError(string? error)
    {
        ShowMessage();
        if (error is null) return;
        Box.Clear();
        Box.Focus();
        DesignSystem.Motion.Shake(Dots);
    }

    private void ShowMessage()
    {
        Message.Text = Error ?? Hint;
        Message.SetResourceReference(TextBlock.ForegroundProperty, Error is null ? "Brush.TextSecondary" : "Brush.Danger");
        Message.FontWeight = Error is null ? FontWeights.Normal : FontWeights.Medium;
    }

    private void DrawDots()
    {
        var typed = Box.Password.Length;
        var slots = Math.Max(4, typed);
        Dots.Children.Clear();
        for (var i = 0; i < slots; i++)
        {
            var dot = new Ellipse { Width = 16, Height = 16, Margin = new Thickness(7, 0, 7, 0), StrokeThickness = 2 };
            if (i < typed) dot.SetResourceReference(Shape.FillProperty, Error is null ? "Brush.Text" : "Brush.Danger");
            else dot.Fill = Brushes.Transparent;
            dot.SetResourceReference(Shape.StrokeProperty, i < typed ? "Brush.Text" : "Brush.LineStrong");
            Dots.Children.Add(dot);
        }
    }

    private static TextBlock Glyph(string glyph, double size)
    {
        var t = new TextBlock { Text = glyph, FontSize = size };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icons");
        return t;
    }
}
