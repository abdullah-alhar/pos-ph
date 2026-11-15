using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VetPos.App.Features.Start;

public partial class SetupView : UserControl
{
    public SetupView()
    {
        InitializeComponent();
        Loaded += (_, _) => Shop.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (ReferenceEquals(Keyboard.FocusedElement, Confirm)) OnCreate(this, new RoutedEventArgs());
            else ((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        };
    }

    private void OnCreate(object sender, RoutedEventArgs e) =>
        ((SetupViewModel)DataContext).CreateCommand.Execute((Pin.Password, Confirm.Password));
}
