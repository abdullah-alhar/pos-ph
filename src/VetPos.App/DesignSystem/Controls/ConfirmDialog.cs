using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VetPos.App.DesignSystem.Controls;

/// <summary>A yes/no question. Enter = the main button, Esc = cancel. Built in code so it is one file.</summary>
public sealed class ConfirmDialog : Border
{
    public ConfirmDialog(IDialogService dialogs, string title, string message, string confirmText, string cancelText, bool danger)
    {
        SetResourceReference(StyleProperty, "Sheet");
        Width = 420;

        var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        heading.SetResourceReference(StyleProperty, "Text.Title");
        var body = new TextBlock { Text = message, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        body.SetResourceReference(StyleProperty, "Text.Secondary");
        body.FontSize = 14;

        var cancel = new Button { Content = cancelText, IsCancel = true, MinWidth = 96 };
        Ds.SetKeyHint(cancel, "Esc");
        cancel.Click += (_, _) => dialogs.Close(false);

        var confirm = new Button { Content = confirmText, IsDefault = true, MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
        confirm.SetResourceReference(StyleProperty, danger ? "Button.Danger" : "Button.Primary");
        Ds.SetKeyHint(confirm, "Enter");
        confirm.Click += (_, _) => dialogs.Close(true);

        Child = new StackPanel
        {
            Children =
            {
                heading,
                body,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 24, 0, 0),
                    Children = { cancel, confirm },
                },
            },
        };

        Loaded += (_, _) => confirm.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            dialogs.Close(true);
            e.Handled = true;
        };
    }
}
