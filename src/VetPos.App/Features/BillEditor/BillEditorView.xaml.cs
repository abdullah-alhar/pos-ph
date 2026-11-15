using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace VetPos.App.Features.BillEditor;

public partial class BillEditorView : UserControl
{
    public BillEditorView()
    {
        InitializeComponent();
        // Any option changed in the properties panel → re-draw the preview and remember for undo.
        Props.AddHandler(TextBoxBase.TextChangedEvent, new RoutedEventHandler((_, _) => Vm?.Changed()));
        Props.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => Vm?.Changed()));
        Props.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => Vm?.Changed()));
        Props.AddHandler(Selector.SelectionChangedEvent, new RoutedEventHandler((_, _) => Vm?.Changed()));
        AiBox.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter || Vm is not { } vm) return;
            e.Handled = true;
            await vm.AskAiCommand.ExecuteAsync(null);
        };
        Vm_ChatChanged();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && Vm is { Selected: not null } sel && Keyboard.FocusedElement is not ComboBox)
            {
                sel.ClearSelectionCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox && Vm is { } vm)
            {
                vm.RemoveCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    private BillEditorViewModel? Vm => DataContext as BillEditorViewModel;

    /// <summary>Keep the newest AI message in view.</summary>
    private void Vm_ChatChanged()
    {
        DataContextChanged += (_, _) =>
        {
            if (Vm is { } vm)
                vm.AiChat.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(ChatScroll.ScrollToEnd);
        };
    }

    private void OnPaperBackground(object sender, MouseButtonEventArgs e)
    {
        if (!e.Handled) Vm?.ClearSelectionCommand.Execute(null);
    }

    private void OnPreviewClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not { } vm || ((FrameworkElement)sender).DataContext is not PreviewLine line) return;
        var piece = vm.Pieces.FirstOrDefault(p => p.Id == line.PieceId);
        e.Handled = true;
        if (piece is not null)
        {
            vm.Selected = piece;
            PieceList.ScrollIntoView(piece);
        }
    }

    private async void OnTemplate(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateBox.SelectedItem is not string name || Vm is not { } vm) return;
        TemplateBox.SelectedItem = null;
        await vm.ApplyTemplateCommand.ExecuteAsync(name);
    }
}
