using System.Windows;

namespace MicVolumeFixer;

public partial class App : Application
{
    private void App_Startup(object sender, StartupEventArgs e)
    {
        bool trayMode = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(trayMode);
        window.Show();
    }
}
