using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace VetPos.App.Features.Assistant;

public partial class AssistantPanelView : UserControl
{
    public AssistantPanelView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) Dispatcher.BeginInvoke(() => InputBox.Focus(), DispatcherPriority.Input);
            else if (DataContext is AssistantViewModel vm) vm.StopVoice();
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is AssistantViewModel previous)
            {
                previous.FocusRequested -= OnFocusRequested;
                previous.Messages.CollectionChanged -= OnMessagesChanged;
            }
            if (e.NewValue is AssistantViewModel vm)
            {
                vm.FocusRequested += OnFocusRequested;
                vm.Messages.CollectionChanged += OnMessagesChanged;
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
            Dispatcher.BeginInvoke(Scroller.ScrollToEnd, DispatcherPriority.Background);
    }

    private void OnFocusRequested(object? sender, EventArgs e)
    {
        if (!IsVisible) return;
        Dispatcher.BeginInvoke(() => { InputBox.Focus(); InputBox.CaretIndex = InputBox.Text.Length; }, DispatcherPriority.Input);
    }
}
