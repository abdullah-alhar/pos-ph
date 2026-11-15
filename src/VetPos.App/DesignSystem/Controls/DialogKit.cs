using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VetPos.App.DesignSystem.Controls;

/// <summary>
/// Builds dialogs that all look and behave the same: title, optional text, fields, an error line,
/// and buttons (Enter = main button, Esc = cancel). Used for every small form in the app.
/// </summary>
public sealed class DialogSheet : Border
{
    private readonly StackPanel _body = new();
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
    private readonly IDialogService _dialogs;

    public DialogSheet(IDialogService dialogs, string title, string? message = null, double width = 440)
    {
        _dialogs = dialogs;
        SetResourceReference(StyleProperty, "Sheet");
        Width = width;
        MaxHeight = 720;
        var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        heading.SetResourceReference(StyleProperty, "Text.DialogTitle");
        _body.Children.Add(heading);
        if (!string.IsNullOrWhiteSpace(message))
        {
            var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
            text.SetResourceReference(StyleProperty, "Text.Secondary");
            text.FontSize = 14;
            _body.Children.Add(text);
        }
        _error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Danger");
        Child = new DockPanel
        {
            Children =
            {
                Dock(_buttons, System.Windows.Controls.Dock.Bottom),
                Dock(_error, System.Windows.Controls.Dock.Bottom),
                new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false },
            },
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.FocusedElement is not System.Windows.Controls.Button && Keyboard.FocusedElement is not TextBox { AcceptsReturn: true } && OnEnter is { } enter)
            {
                enter();
                e.Handled = true;
            }
        };
    }

    /// <summary>What Enter does (normally the main button).</summary>
    public Action? OnEnter { get; set; }

    public static UIElement Dock(UIElement e, Dock dock)
    {
        DockPanel.SetDock(e, dock);
        return e;
    }

    public T Add<T>(T element) where T : UIElement
    {
        _body.Children.Add(element);
        return element;
    }

    /// <summary>A label above a control.</summary>
    public T Field<T>(string label, T control, string? hint = null) where T : FrameworkElement
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        var l = new TextBlock { Text = label };
        l.SetResourceReference(StyleProperty, "FieldLabel");
        panel.Children.Add(l);
        panel.Children.Add(control);
        if (hint is not null)
        {
            var h = new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 6, 0, 0) };
            h.SetResourceReference(StyleProperty, "Text.Caption");
            panel.Children.Add(h);
        }
        _body.Children.Add(panel);
        return control;
    }

    public static TextBox Input(string? text = null, bool number = false, string? placeholder = null)
    {
        var t = new TextBox { Text = text ?? "" };
        if (number) t.SetResourceReference(StyleProperty, "Input.Number");
        if (placeholder is not null) Ds.SetPlaceholder(t, placeholder);
        return t;
    }

    public static PasswordBox PinInput()
    {
        var p = new PasswordBox { MaxLength = 8, Height = 40, FontSize = 18, Padding = new Thickness(12, 0, 12, 0), VerticalContentAlignment = VerticalAlignment.Center };
        p.SetResourceReference(Control.BorderBrushProperty, "Brush.LineStrong");
        p.SetResourceReference(Control.BackgroundProperty, "Brush.Surface");
        p.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        InputMethod.SetIsInputMethodEnabled(p, false);
        return p;
    }

    public System.Windows.Controls.Button Button(string text, Action onClick, string style = "Button.Outline", string? key = null)
    {
        var b = new System.Windows.Controls.Button { Content = text, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
        b.SetResourceReference(StyleProperty, style);
        if (key is not null) Ds.SetKeyHint(b, key);
        b.Click += (_, _) => onClick();
        _buttons.Children.Add(b);
        return b;
    }

    /// <summary>Cancel (Esc) + main button (Enter).</summary>
    public void Buttons(string okText, Action onOk, string cancelText = "Cancel", bool danger = false)
    {
        Button(cancelText, () => _dialogs.Close(null), key: "Esc");
        Button(okText, onOk, danger ? "Button.Danger" : "Button.Primary", "Enter");
        OnEnter = onOk;
    }

    public void ShowError(string? message, FrameworkElement? shake = null)
    {
        _error.Text = message ?? "";
        _error.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrEmpty(message)) Motion.Shake(shake ?? this);
    }

    public void Close(object? result) => _dialogs.Close(result);
}

/// <summary>The small dialogs used all over the app.</summary>
public static class Dialogs
{
    /// <summary>One text field. <paramref name="validate"/> returns an error message or null.</summary>
    public static async Task<string?> PromptAsync(this IDialogService dialogs, string title, string label, string? initial = null,
        Func<string, string?>? validate = null, string? message = null, string okText = "OK", bool number = false, string? hint = null)
    {
        var sheet = new DialogSheet(dialogs, title, message);
        var input = sheet.Field(label, DialogSheet.Input(initial, number), hint);
        input.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        sheet.Buttons(okText, () =>
        {
            var error = validate?.Invoke(input.Text.Trim());
            if (error is not null)
            {
                Ds.SetError(input, null);
                Ds.SetError(input, error);
                return;
            }
            sheet.Close(input.Text.Trim());
        });
        return await dialogs.ShowAsync(sheet) as string;
    }

    /// <summary>Asks for the owner's PIN (approvals). Returns the PIN typed, or null.</summary>
    public static async Task<string?> PinAsync(this IDialogService dialogs, string title, string message)
    {
        var sheet = new DialogSheet(dialogs, title, message, 380);
        var pin = sheet.Field("Owner PIN", DialogSheet.PinInput());
        pin.Loaded += (_, _) => pin.Focus();
        sheet.Buttons("Approve", () =>
        {
            if (pin.Password.Length < 4) { sheet.ShowError("Type the 4–8 digit PIN.", pin); return; }
            sheet.Close(pin.Password);
        });
        return await dialogs.ShowAsync(sheet) as string;
    }

    /// <summary>Pick one of several options with ↑↓ and Enter (first is selected).</summary>
    public static async Task<int?> ChooseAsync(this IDialogService dialogs, string title, string? message, IReadOnlyList<(string Title, string Detail)> options, string okText = "Choose")
    {
        var sheet = new DialogSheet(dialogs, title, message, 480);
        var list = new ListBox { Margin = new Thickness(0, 12, 0, 0), BorderThickness = new Thickness(0) };
        list.SetResourceReference(Control.BackgroundProperty, "Brush.Transparent");
        foreach (var (t, d) in options)
        {
            var item = new ListBoxItem { Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 6) };
            var title1 = new TextBlock { Text = t, FontSize = 15, FontWeight = FontWeights.SemiBold };
            title1.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var detail = new TextBlock { Text = d, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            detail.SetResourceReference(FrameworkElement.StyleProperty, "Text.Secondary");
            item.Content = new StackPanel { Children = { title1, detail } };
            item.SetResourceReference(FrameworkElement.StyleProperty, "ChoiceItem");
            item.MouseDoubleClick += (_, _) => sheet.Close(list.SelectedIndex);
            list.Items.Add(item);
        }
        list.SelectedIndex = 0;
        sheet.Add(list);
        list.Loaded += (_, _) => (list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
        sheet.Buttons(okText, () => sheet.Close(list.SelectedIndex));
        return await dialogs.ShowAsync(sheet) as int?;
    }

    /// <summary>A message with one OK button.</summary>
    public static async Task AlertAsync(this IDialogService dialogs, string title, string message)
    {
        var sheet = new DialogSheet(dialogs, title, message, 420);
        var ok = sheet.Button("OK", () => sheet.Close(true), "Button.Primary", "Enter");
        sheet.OnEnter = () => sheet.Close(true);
        ok.Loaded += (_, _) => ok.Focus();
        await dialogs.ShowAsync(sheet);
    }
}
