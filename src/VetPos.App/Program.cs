using Velopack;

namespace VetPos.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must run first: it handles install / update / uninstall hooks and exits when needed.
        VelopackApp.Build().Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
