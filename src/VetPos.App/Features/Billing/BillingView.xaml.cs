using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VetPos.Data.Services;

namespace VetPos.App.Features.Billing;

/// <summary>
/// Keyboard handling for the till (view-only): the scan box keeps focus; arrows move through the
/// suggestions or the bill lines; + / − / Del act on the selected line when the box is empty.
/// </summary>
public partial class BillingView : UserControl
{
    public BillingView()
    {
        InitializeComponent();
        Loaded += (_, _) => FocusScan();
        IsVisibleChanged += (_, e) => { if (e.NewValue is true) FocusScan(); };
        // The suggestion list is a popup: close it when leaving the page so it never floats over another screen.
        Unloaded += (_, _) => { if (DataContext is BillingViewModel vm) vm.ShowSuggestions = false; };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is BillingViewModel old) old.FocusScan -= OnFocusRequest;
            if (e.NewValue is BillingViewModel vm) vm.FocusScan += OnFocusRequest;
        };
        PreviewKeyDown += OnKey;
    }

    private BillingViewModel Vm => (BillingViewModel)DataContext;

    private void OnFocusRequest(object? sender, EventArgs e) => FocusScan();

    private void FocusScan() => Dispatcher.BeginInvoke(() =>
    {
        if (!IsVisible) return;
        ScanBox.Focus();
        Keyboard.Focus(ScanBox);
    }, DispatcherPriority.Input);

    private async void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            FocusScan();
            ScanBox.SelectAll();
            e.Handled = true;
            return;
        }
        if (!ReferenceEquals(e.OriginalSource, ScanBox)) return;
        var empty = ScanBox.Text.Length == 0;
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await Vm.SubmitCommand.ExecuteAsync(null);
                break;
            case Key.Down or Key.Up when Vm.ShowSuggestions:
                Vm.MoveSuggestion(e.Key == Key.Down ? 1 : -1);
                SuggestionList.ScrollIntoView(Vm.Suggestion);
                e.Handled = true;
                break;
            case Key.Down or Key.Up:
                Vm.MoveSelection(e.Key == Key.Down ? 1 : -1);
                if (Vm.Selected is not null) LinesList.ScrollIntoView(Vm.Selected);
                e.Handled = true;
                break;
            case Key.Escape when Vm.ShowSuggestions || !empty:
                Vm.Search = "";
                Vm.ShowSuggestions = false;
                e.Handled = true;
                break;
            case Key.Add or Key.OemPlus when empty:
                await Vm.ChangeQtyCommand.ExecuteAsync("1");
                e.Handled = true;
                break;
            case Key.Subtract or Key.OemMinus when empty:
                await Vm.ChangeQtyCommand.ExecuteAsync("-1");
                e.Handled = true;
                break;
            case Key.Delete when empty:
                await Vm.RemoveLineCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
        }
    }

    private async void OnSuggestionClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject) is { } d && ItemsControl.ContainerFromElement(SuggestionList, d) is ListBoxItem { DataContext: ProductRow row })
            await Vm.PickSuggestionAsync(row);
    }

    private async void OnPlus(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is CartLine line) { Vm.Selected = line; await Vm.ChangeQtyCommand.ExecuteAsync("1"); }
    }

    private async void OnMinus(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is CartLine line) { Vm.Selected = line; await Vm.ChangeQtyCommand.ExecuteAsync("-1"); }
    }

    private async void OnLineDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Vm.Selected is not null) await Vm.EditQtyCommand.ExecuteAsync(null);
    }

    // ── Click a price to change it for this bill (Enter keeps it, Esc puts it back) ──

    private void OnPriceFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox { DataContext: CartLine line } box) return;
        Vm.Selected = line;
        box.Text = VetPos.Core.Pricing.Money.ToInput(line.UnitPrice);
        box.SelectAll();
    }

    private void OnPriceKey(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: CartLine line } box) return;
        if (e.Key == Key.Escape)
        {
            box.Text = VetPos.Core.Pricing.Money.ToInput(line.UnitPrice);
            FocusScan();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            FocusScan(); // LostKeyboardFocus commits
            e.Handled = true;
        }
    }

    private void OnPriceCommit(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox { DataContext: CartLine line } box) return;
        var text = box.Text.Trim();
        if (text != VetPos.Core.Pricing.Money.ToInput(line.UnitPrice) && text != VetPos.Core.Pricing.Money.Format(line.UnitPrice))
            Vm.SetPriceFromText(line, text);
        box.Text = VetPos.Core.Pricing.Money.Format(line.UnitPrice);
    }

    private void OnAskAi(object sender, RoutedEventArgs e) => FindParentShell()?.ToggleAssistantCommand.Execute(null);

    private void OnHelp(object sender, RoutedEventArgs e)
    {
        var shell = FindParentShell();
        shell?.ShowHelpCommand.Execute(null);
    }

    private Shell.ShellViewModel? FindParentShell()
    {
        DependencyObject? d = this;
        while (d is not null)
        {
            if (d is FrameworkElement { DataContext: Shell.ShellViewModel s }) return s;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}
