using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VetPos.Data.Services;

namespace VetPos.App.Features.StockEntry;

public partial class StockEntryView : UserControl
{
    public StockEntryView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not StockEntryViewModel vm) return;
            vm.FocusSearch += (_, _) => Dispatcher.BeginInvoke(() => SearchBox.Focus(), DispatcherPriority.Input);
            vm.FocusLine += (_, row) => Dispatcher.BeginInvoke(() =>
            {
                var container = LineList.ItemContainerGenerator.ContainerFromItem(row) as DependencyObject;
                var qty = container is null ? null : Find<TextBox>(container, "QtyBox");
                if (qty is null) return;
                qty.Focus();
                qty.SelectAll();
            }, DispatcherPriority.Loaded);
        };
        Loaded += (_, _) => SearchBox.Focus();
        Unloaded += (_, _) => { if (DataContext is StockEntryViewModel vm) vm.ShowSuggestions = false; };
        // Enter moves to the next field in the lines (like Tab).
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox tb && !ReferenceEquals(tb, SearchBox) && tb.DataContext is ReceiptLineRow)
            {
                tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        };
    }

    private StockEntryViewModel Vm => (StockEntryViewModel)DataContext;

    private async void OnSearchKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: e.Handled = true; await Vm.SubmitSearchCommand.ExecuteAsync(null); break;
            case Key.Down when Vm.ShowSuggestions: Vm.MoveSuggestion(1); SuggestionList.ScrollIntoView(Vm.Suggestion); e.Handled = true; break;
            case Key.Up when Vm.ShowSuggestions: Vm.MoveSuggestion(-1); SuggestionList.ScrollIntoView(Vm.Suggestion); e.Handled = true; break;
            case Key.Escape: Vm.Search = ""; Vm.ShowSuggestions = false; e.Handled = true; break;
        }
    }

    private async void OnSuggestionClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject) is { } d && ItemsControl.ContainerFromElement(SuggestionList, d) is ListBoxItem { DataContext: ProductRow row })
            await Vm.AddLineAsync(row.Id);
    }

    private static T? Find<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t && t.Name == name) return t;
            if (Find<T>(child, name) is { } found) return found;
        }
        return null;
    }
}
