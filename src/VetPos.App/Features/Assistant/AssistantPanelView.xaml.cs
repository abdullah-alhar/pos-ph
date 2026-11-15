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
        };
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is AssistantViewModel vm)
                vm.FocusRequested += (_, _) => Dispatcher.BeginInvoke(() => { InputBox.Focus(); InputBox.CaretIndex = InputBox.Text.Length; }, DispatcherPriority.Input);
            if (e.NewValue is AssistantViewModel vm2)
                vm2.Messages.CollectionChanged += (_, a) =>
                {
                    if (a.Action == NotifyCollectionChangedAction.Add) Dispatcher.BeginInvoke(Scroller.ScrollToEnd, DispatcherPriority.Background);
                };
        };
        InputBox.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            e.Handled = true;
            if (DataContext is AssistantViewModel vm) await vm.AskCommand.ExecuteAsync(null);
        };
    }
}
