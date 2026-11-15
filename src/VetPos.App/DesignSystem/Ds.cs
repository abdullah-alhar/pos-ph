using System.Windows;

namespace VetPos.App.DesignSystem;

/// <summary>
/// Attached properties the design-system templates read, so screens stay declarative:
/// <c>ds:Ds.KeyHint="F12"</c> on a button shows the shortcut chip, <c>ds:Ds.Icon</c> adds an
/// icon, <c>ds:Ds.Error</c> on an input shows the message under it and turns the border red.
/// </summary>
public static class Ds
{
    public static readonly DependencyProperty KeyHintProperty = DependencyProperty.RegisterAttached(
        "KeyHint", typeof(string), typeof(Ds), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static string? GetKeyHint(DependencyObject d) => (string?)d.GetValue(KeyHintProperty);
    public static void SetKeyHint(DependencyObject d, string? value) => d.SetValue(KeyHintProperty, value);

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Ds), new PropertyMetadata(null));

    public static string? GetIcon(DependencyObject d) => (string?)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, string? value) => d.SetValue(IconProperty, value);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Ds), new PropertyMetadata(null));

    public static string? GetPlaceholder(DependencyObject d) => (string?)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d, string? value) => d.SetValue(PlaceholderProperty, value);

    /// <summary>Message shown under an input; null or empty = no error. Setting it shakes the input.</summary>
    public static readonly DependencyProperty ErrorProperty = DependencyProperty.RegisterAttached(
        "Error", typeof(string), typeof(Ds), new PropertyMetadata(null, OnErrorChanged));

    public static string? GetError(DependencyObject d) => (string?)d.GetValue(ErrorProperty);
    public static void SetError(DependencyObject d, string? value) => d.SetValue(ErrorProperty, value);

    private static readonly DependencyPropertyKey HasErrorKey = DependencyProperty.RegisterAttachedReadOnly(
        "HasError", typeof(bool), typeof(Ds), new PropertyMetadata(false));

    /// <summary>True while <see cref="ErrorProperty"/> has a message (templates use it for the red frame).</summary>
    public static readonly DependencyProperty HasErrorProperty = HasErrorKey.DependencyProperty;

    public static bool GetHasError(DependencyObject d) => (bool)d.GetValue(HasErrorProperty);

    private static void OnErrorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var has = !string.IsNullOrEmpty(e.NewValue as string);
        d.SetValue(HasErrorKey, has);
        if (has && d is FrameworkElement fe) Motion.Shake(fe);
    }

    /// <summary>Set to true for a moment to flash a row (an item just added to the bill).</summary>
    public static readonly DependencyProperty FlashProperty = DependencyProperty.RegisterAttached(
        "Flash", typeof(bool), typeof(Ds), new PropertyMetadata(false, OnFlashChanged));

    public static bool GetFlash(DependencyObject d) => (bool)d.GetValue(FlashProperty);
    public static void SetFlash(DependencyObject d, bool value) => d.SetValue(FlashProperty, value);

    private static void OnFlashChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is System.Windows.Controls.Panel or System.Windows.Controls.Border && e.NewValue is true)
            Motion.Flash((FrameworkElement)d);
    }

    /// <summary>Background from a resource key that follows the theme (used by the colour swatches).</summary>
    public static readonly DependencyProperty BackgroundKeyProperty = DependencyProperty.RegisterAttached(
        "BackgroundKey", typeof(string), typeof(Ds), new PropertyMetadata(null, (d, e) =>
        {
            if (d is System.Windows.Controls.Border b && e.NewValue is string key)
                b.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, key);
        }));

    public static string? GetBackgroundKey(DependencyObject d) => (string?)d.GetValue(BackgroundKeyProperty);
    public static void SetBackgroundKey(DependencyObject d, string? value) => d.SetValue(BackgroundKeyProperty, value);

    /// <summary>Style from a resource key chosen by data (e.g. a badge tone).</summary>
    public static readonly DependencyProperty StyleKeyProperty = DependencyProperty.RegisterAttached(
        "StyleKey", typeof(string), typeof(Ds), new PropertyMetadata(null, (d, e) =>
        {
            if (d is FrameworkElement fe && e.NewValue is string key) fe.SetResourceReference(FrameworkElement.StyleProperty, key);
        }));

    public static string? GetStyleKey(DependencyObject d) => (string?)d.GetValue(StyleKeyProperty);
    public static void SetStyleKey(DependencyObject d, string? value) => d.SetValue(StyleKeyProperty, value);

    /// <summary>
    /// On a form container: Enter moves to the next field (like Tab). On the last field, Enter runs
    /// <see cref="EnterSubmitProperty"/> (e.g. "Save this product?"). Multi-line boxes and open
    /// drop-downs keep their own Enter.
    /// </summary>
    public static readonly DependencyProperty EnterNextProperty = DependencyProperty.RegisterAttached(
        "EnterNext", typeof(bool), typeof(Ds), new PropertyMetadata(false, (d, e) =>
        {
            if (d is not UIElement el) return;
            if (e.NewValue is true) el.PreviewKeyDown += OnEnterNext; else el.PreviewKeyDown -= OnEnterNext;
        }));

    public static bool GetEnterNext(DependencyObject d) => (bool)d.GetValue(EnterNextProperty);
    public static void SetEnterNext(DependencyObject d, bool value) => d.SetValue(EnterNextProperty, value);

    public static readonly DependencyProperty EnterSubmitProperty = DependencyProperty.RegisterAttached(
        "EnterSubmit", typeof(System.Windows.Input.ICommand), typeof(Ds), new PropertyMetadata(null));

    public static System.Windows.Input.ICommand? GetEnterSubmit(DependencyObject d) => (System.Windows.Input.ICommand?)d.GetValue(EnterSubmitProperty);
    public static void SetEnterSubmit(DependencyObject d, System.Windows.Input.ICommand? value) => d.SetValue(EnterSubmitProperty, value);

    private static void OnEnterNext(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || sender is not FrameworkElement container) return;
        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None) return;
        var focused = System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
        switch (focused)
        {
            case System.Windows.Controls.TextBox { AcceptsReturn: true }:
            case System.Windows.Controls.Button:
                return;
            case System.Windows.Controls.ComboBoxItem:
                return;
        }
        if (focused is System.Windows.Controls.ComboBox { IsDropDownOpen: true }) return;
        if (focused is not UIElement current) return;
        // Is there a next field inside this form?
        var next = current.PredictFocus(System.Windows.Input.FocusNavigationDirection.Down);
        current.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
        var now = System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
        var stillInside = now is not null && IsInside(now, container) && now is not System.Windows.Controls.Button;
        if (!stillInside)
        {
            current.Focus();
            var submit = GetEnterSubmit(container);
            if (submit?.CanExecute(null) == true) submit.Execute(null);
        }
        else if (now is System.Windows.Controls.TextBox tb) tb.SelectAll();
        _ = next;
        e.Handled = true;
    }

    private static bool IsInside(DependencyObject child, DependencyObject parent)
    {
        for (var d = child; d is not null; d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, parent)) return true;
        return false;
    }
}
