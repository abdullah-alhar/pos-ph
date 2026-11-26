using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;

namespace VetPos.App.Features.Assistant;

public partial class AssistantView : UserControl
{
    public AssistantView()
    {
        InitializeComponent();
        Loaded += (_, _) => InputBox.Focus();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false && DataContext is AssistantViewModel vm) vm.StopVoice();
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is AssistantViewModel previous)
            {
                previous.Messages.CollectionChanged -= OnMessagesChanged;
                previous.FocusRequested -= OnFocusRequested;
            }
            if (e.NewValue is AssistantViewModel vm)
            {
                vm.Messages.CollectionChanged += OnMessagesChanged;
                vm.FocusRequested += OnFocusRequested;
            }
        };
        InputBox.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            e.Handled = true;
            if (DataContext is AssistantViewModel vm && vm.AskCommand.CanExecute(null)) await vm.AskCommand.ExecuteAsync(null);
        };
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && IsVisible)
            Dispatcher.BeginInvoke(Scroller.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnFocusRequested(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
    }
}
