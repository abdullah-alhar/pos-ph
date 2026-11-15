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
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is AssistantViewModel vm)
                vm.Messages.CollectionChanged += (_, a) => { if (a.Action == NotifyCollectionChangedAction.Add) Scroller.ScrollToEnd(); };
        };
        InputBox.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            e.Handled = true;
            await ((AssistantViewModel)DataContext).AskCommand.ExecuteAsync(null);
        };
    }
}
