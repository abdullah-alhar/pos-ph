using System.Windows.Controls;

namespace VetPos.App.Features.Start;

public partial class LockView : UserControl
{
    public LockView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is not true) return;
            Pin.Clear();
            Pin.FocusInput();
        };
    }

    private void OnSubmitted(object? sender, EventArgs e) => (DataContext as LockViewModel)?.UnlockCommand.Execute(Pin.Password);
}
