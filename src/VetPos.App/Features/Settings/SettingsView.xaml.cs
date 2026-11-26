namespace VetPos.App.Features.Settings;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView() => InitializeComponent();

    private void OnAiProviderSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => KeyBox?.Clear();
}
