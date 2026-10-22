using System;
using System.Threading;
using System.Windows.Forms;

namespace ShieldBlock.UI
{
    internal static class Program
    {
        private static Mutex? _singleInstanceMutex;

        [STAThread]
        private static void Main()
        {
            const string mutexName = "Global\\ShieldBlock_UI_SingleInstance_Mutex";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("ShieldBlock is already running in the background or system tray.", "ShieldBlock", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Console.WriteLine($"[UI Unhandled] {ex?.Message}");
            };

            Application.Run(new MainForm());

            GC.KeepAlive(_singleInstanceMutex);
        }
    }
}
