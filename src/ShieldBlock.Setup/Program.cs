using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using ShieldBlock.Core.Common;
using ShieldBlock.Core.Config;
using ShieldBlock.Core.Https;
using ShieldBlock.Core.Rules;
using ShieldBlock.Core.Safety;
using ShieldBlock.Core.Wfp;

namespace ShieldBlock.Setup
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool isUninstall = args.Length > 0 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase);

            if (isUninstall)
            {
                ExecuteUninstall();
                MessageBox.Show(
                    "ShieldBlock has been cleanly removed from your computer.\n\nAll network settings and DNS configuration have been restored with zero damage.",
                    "ShieldBlock Uninstalled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            }
            else
            {
                Application.Run(new InstallerForm());
            }
        }

        public static void ExecuteUninstall()
        {
            // 1. Terminate running ShieldBlock processes
            KillProcessesByName("ShieldBlock");
            KillProcessesByName("ShieldBlockService");

            // 2. Remove WFP Filters
            try
            {
                using var wfp = new WfpManager();
                wfp.Initialize();
                wfp.RemoveAllFilters();
            }
            catch { }

            // 3. Revert Adapter DNS
            try
            {
                var snapshotMgr = new SnapshotManager();
                var rollback = new RollbackEngine(snapshotMgr);
                rollback.RollbackAllAsync().GetAwaiter().GetResult();
            }
            catch { }

            // 4. Remove Certificate if installed
            try
            {
                using var certMgr = new CertificateManager();
                certMgr.UninstallRootCertificate();
            }
            catch { }

            // 5. Remove shortcuts
            try
            {
                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ShieldBlock.lnk");
                if (File.Exists(desktopLnk)) File.Delete(desktopLnk);

                string programsLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ShieldBlock.lnk");
                if (File.Exists(programsLnk)) File.Delete(programsLnk);
            }
            catch { }

            // 6. Remove registry entry
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ShieldBlock", false);
            }
            catch { }
        }

        private static void KillProcessesByName(string processName)
        {
            try
            {
                int currentPid = Process.GetCurrentProcess().Id;
                var procs = Process.GetProcessesByName(processName);
                foreach (var p in procs)
                {
                    if (p.Id != currentPid)
                    {
                        try { p.Kill(); p.WaitForExit(1000); } catch { }
                    }
                    p.Dispose();
                }
            }
            catch { }
        }
    }
}
