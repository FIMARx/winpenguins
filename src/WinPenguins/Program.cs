using WinPenguins.Diagnostics;
using WinPenguins.UI;

namespace WinPenguins;

internal static class Program
{
    private const string SingleInstanceMutexName = "WinPenguins_Modern_InstanceMutex";

    [STAThread]
    private static void Main()
    {
        using var instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        DiagnosticsReporter.InstallProcessHandlers();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetColorMode(SystemColorMode.System);

        using var context = new TrayApplicationContext();
        Application.Run(context);
    }
}
