using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace VetPos.App.Shell;

public partial class ShellView : UserControl
{
    public ShellView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not ShellViewModel vm) return;
            vm.PropertyChanged += (_, a) =>
            {
                if (a.PropertyName == nameof(ShellViewModel.IsPaletteOpen) && vm.IsPaletteOpen)
                    Dispatcher.BeginInvoke(() => { PaletteBox.Focus(); Keyboard.Focus(PaletteBox); }, DispatcherPriority.Input);
            };
        };
        // Double-click outside the AI drawer closes it (the click still does its normal job).
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || DataContext is not ShellViewModel { IsAssistantOpen: true } shell) return;
            if (e.OriginalSource is DependencyObject d && IsInside(d, AiDrawer)) return;
            shell.CloseAssistant();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DataContext is ShellViewModel { IsPaletteOpen: true } or ShellViewModel { IsHelpOpen: true } or ShellViewModel { IsUserMenuOpen: true }
                    or ShellViewModel { IsNoticesOpen: true })
            {
                ((ShellViewModel)DataContext).CloseOverlaysCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    private static bool IsInside(DependencyObject d, DependencyObject container)
    {
        for (DependencyObject? x = d; x is not null; x = x is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                 ? System.Windows.Media.VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            if (ReferenceEquals(x, container)) return true;
        return false;
    }

    private void OnOverlayClick(object sender, MouseButtonEventArgs e) => ((ShellViewModel)DataContext).CloseOverlaysCommand.Execute(null);

    private void OnPaletteKey(object sender, KeyEventArgs e)
    {
        var vm = (ShellViewModel)DataContext;
        switch (e.Key)
        {
            case Key.Down: vm.MovePalette(1); PaletteList.ScrollIntoView(vm.PaletteSelected); e.Handled = true; break;
            case Key.Up: vm.MovePalette(-1); PaletteList.ScrollIntoView(vm.PaletteSelected); e.Handled = true; break;
            case Key.Enter: vm.RunPaletteCommand.Execute(null); e.Handled = true; break;
        }
    }

    private void OnPaletteClick(object sender, MouseButtonEventArgs e) => ((ShellViewModel)DataContext).RunPaletteCommand.Execute(null);
}
