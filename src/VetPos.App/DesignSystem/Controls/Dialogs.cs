using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VetPos.App.DesignSystem.Controls;

/// <summary>Opens content over the current screen (dims it) and waits until it closes.</summary>
public interface IDialogService
{
    /// <summary>Shows <paramref name="content"/>; returns what it closed with (null = Esc / cancelled).</summary>
    Task<object?> ShowAsync(FrameworkElement content);

    /// <summary>"Remove this item?" — Enter = yes, Esc = no.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "Yes", string cancelText = "Cancel", bool danger = false);

    /// <summary>Closes the open dialog with a result (called by the dialog's buttons).</summary>
    void Close(object? result);
}

public sealed class DialogService : IDialogService
{
    private DialogHost? _host;

    internal void Attach(DialogHost host) => _host = host;

    public Task<object?> ShowAsync(FrameworkElement content) =>
        _host?.Show(content) ?? Task.FromResult<object?>(null);

    public void Close(object? result) => _host?.Close(result);

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Yes", string cancelText = "Cancel", bool danger = false)
    {
        var dialog = new ConfirmDialog(this, title, message, confirmText, cancelText, danger);
        return await ShowAsync(dialog) is true;
    }
}

/// <summary>
/// The layer that holds dialogs: a dim scrim (fades) and a centred sheet (fades and grows from
/// 97 %). Keyboard focus is kept inside the dialog and returned to where it was afterwards.
/// One dialog at a time — a POS never stacks questions.
/// </summary>
public sealed class DialogHost : Grid
{
    private readonly Border _scrim;
    private readonly ContentControl _slot;
    private TaskCompletionSource<object?>? _pending;
    private IInputElement? _focusBefore;

    public DialogHost()
    {
        Visibility = Visibility.Collapsed;
        _scrim = new Border { Opacity = 0 };
        _scrim.SetResourceReference(Border.BackgroundProperty, "Brush.Scrim");
        _scrim.MouseLeftButtonDown += (_, e) => e.Handled = true; // clicks outside do nothing (no accidental cancel)
        _slot = new ContentControl
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            Opacity = 0,
        };
        KeyboardNavigation.SetTabNavigation(_slot, KeyboardNavigationMode.Cycle);
        Children.Add(_scrim);
        Children.Add(_slot);
        PreviewKeyDown += OnKey;
    }

    public void Attach(DialogService service) => service.Attach(this);

    public bool IsOpen => _pending is not null;

    internal Task<object?> Show(FrameworkElement content)
    {
        if (_pending is not null) Close(null);
        _pending = new TaskCompletionSource<object?>();
        _focusBefore = Keyboard.FocusedElement;
        _slot.Content = content;
        Visibility = Visibility.Visible;
        Motion.Fade(_scrim, 1);
        Motion.FadeScaleIn(_slot);
        content.Loaded += FocusFirst;
        if (content.IsLoaded) FocusFirst(content, new RoutedEventArgs());
        return _pending.Task;
    }

    private void FocusFirst(object sender, RoutedEventArgs e)
    {
        var content = (FrameworkElement)sender;
        content.Loaded -= FocusFirst;
        Dispatcher.BeginInvoke(() => content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    internal void Close(object? result)
    {
        var pending = _pending;
        if (pending is null) return;
        _pending = null;
        Motion.Fade(_scrim, 0);
        Motion.FadeScaleOut(_slot, () =>
        {
            if (_pending is not null) return; // a new dialog opened meanwhile
            _slot.Content = null;
            Visibility = Visibility.Collapsed;
        });
        if (_focusBefore is UIElement { IsVisible: true } back) back.Focus();
        pending.TrySetResult(result);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsOpen)
        {
            Close(null);
            e.Handled = true;
        }
    }
}
