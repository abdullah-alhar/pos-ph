using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VetPos.Data.Services;

namespace VetPos.App.Features.Labels;

public partial class LabelsView : UserControl
{
    public LabelsView()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
        Unloaded += (_, _) => { if (DataContext is LabelsViewModel vm) vm.ShowSuggestions = false; };
    }

    private LabelsViewModel Vm => (LabelsViewModel)DataContext;

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
            await Vm.AddAsync(row.Id);
    }
}
