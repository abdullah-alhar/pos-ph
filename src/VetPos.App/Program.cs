using Velopack;

namespace VetPos.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must run first: it handles install / update / uninstall hooks and exits when needed.
        // UpdateService applies downloaded packages after a verified backup.
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var app = new App();
        app.InitializeComponent();
        Environment.ExitCode = app.Run();
    }
}
