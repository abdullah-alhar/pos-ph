using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace VetPos.App.Features.Start;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(FocusFirst, DispatcherPriority.Input);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && ReferenceEquals(Keyboard.FocusedElement, UserBox))
            {
                Pin.FocusInput();
                e.Handled = true;
            }
            // ← → choose another person while typing the PIN
            else if (e.Key is Key.Left or Key.Right && Keyboard.FocusedElement is PasswordBox && Vm is { ShowUserTiles: true } vm && vm.Users.Count > 1)
            {
                var i = vm.SelectedUser is null ? 0 : vm.Users.IndexOf(vm.SelectedUser);
                vm.SelectedUser = vm.Users[Math.Clamp(i + (e.Key == Key.Left ? -1 : 1), 0, vm.Users.Count - 1)];
                Pin.Clear();
                e.Handled = true;
            }
        };
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is LoginViewModel vm)
                vm.PropertyChanged += (_, a) =>
                {
                    if (a.PropertyName == nameof(LoginViewModel.ShowUserTiles)) Dispatcher.BeginInvoke(FocusFirst, DispatcherPriority.Input);
                };
        };
    }

    private LoginViewModel? Vm => DataContext as LoginViewModel;

    private void FocusFirst()
    {
        if (Vm is { ShowUserTiles: false } && string.IsNullOrEmpty(UserBox.Text)) UserBox.Focus();
        else Pin.FocusInput();
    }

    private void OnPersonClick(object sender, MouseButtonEventArgs e)
    {
        Pin.Clear();
        Pin.FocusInput();
    }

    private void OnSubmitted(object? sender, EventArgs e) => Vm?.SignInCommand.Execute(Pin.Password);
}
