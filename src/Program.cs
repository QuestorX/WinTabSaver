using System;
using System.Windows.Forms;

namespace WinTabSaver
{
    /// <summary>
    /// Application entry point. Configures and launches the System Tray application.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Enable visual styles for modern UI rendering
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Ensure only one instance is running at a time
            using var mutex = new System.Threading.Mutex(true, "WinTabSaverSingleInstance", out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "WinTabSaver is already running.\nCheck the system tray.",
                    "WinTabSaver",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Run the application using the tray application context (no main window)
            using var appContext = new TrayApplicationContext();
            Application.Run(appContext);
        }
    }
}
