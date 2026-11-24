using System.Runtime.InteropServices;
using K11Control.Stage0;
using K11Control.UI;

namespace K11Control;

internal static class Program
{
    private const int AttachParentProcess = -1;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--stage0-probe")
        {
            if (!Console.IsOutputRedirected && !Console.IsErrorRedirected && AttachConsole(AttachParentProcess))
            {
                try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* ignore if no console CP */ }
            }
            return Stage0ProbeRunner.RunCli(args[1..]);
        }

        using var singleInstance = new Mutex(initiallyOwned: true, "K11Control_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("FiiO K11 R2R Control is already running in the system tray.", "K11Control",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
        return 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);
}
