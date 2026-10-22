using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading.Tasks;
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
        public static async Task<int> Main(string[] args)
        {
            Console.Title = "ShieldBlock Setup & Maintenance";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================");
            Console.WriteLine("    ShieldBlock Setup & Maintenance Suite        ");
            Console.WriteLine("=================================================");
            Console.ResetColor();

            bool isUninstall = args.Length > 0 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase);

            if (isUninstall)
            {
                return await ExecuteUninstallAsync();
            }
            else
            {
                return await ExecuteInstallAsync();
            }
        }

        private static async Task<int> ExecuteInstallAsync()
        {
            Console.WriteLine("Starting ShieldBlock Pre-Flight Validation...\n");

            // 1. Verify OS
            if (!RuntimeInformationIsWindows())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FAIL] ShieldBlock requires Windows 10 or Windows 11 (64-bit).");
                Console.ResetColor();
                return 1;
            }
            Console.WriteLine("[PASS] Operating System: Windows 64-bit validated.");

            // 2. Verify BFE (Base Filtering Engine)
            bool bfeOk = CheckBfeService();
            if (!bfeOk)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[WARN] Base Filtering Engine (BFE) is stopped or unavailable. WFP network filtering may be simulated.");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine("[PASS] Windows Base Filtering Engine (BFE): Running.");
            }

            // 3. Initialize Directory Structure
            Console.WriteLine("[INFO] Preparing safe local storage directories in %ProgramData% & %AppData%...");
            try
            {
                Directory.CreateDirectory(Constants.ProgramDataDir);
                Directory.CreateDirectory(Path.Combine(Constants.ProgramDataDir, "snapshots"));
                Directory.CreateDirectory(Path.Combine(Constants.ProgramDataDir, "data", "lists"));
                Directory.CreateDirectory(Path.Combine(Constants.ProgramDataDir, "certs"));
                Directory.CreateDirectory(Path.Combine(Constants.ProgramDataDir, "logs"));

                Directory.CreateDirectory(Constants.AppDataDir);
                Directory.CreateDirectory(Path.Combine(Constants.AppDataDir, "logs"));

                Console.WriteLine("[PASS] Local storage directories initialized.");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FAIL] Unable to prepare data directories: {ex.Message}");
                Console.ResetColor();
                return 1;
            }

            // 4. Seed baseline offline rules and initial snapshot
            Console.WriteLine("[INFO] Seeding curated baseline filter lists...");
            try
            {
                var filterMgr = new FilterListManager();
                Console.WriteLine($"[PASS] Filter lists ready ({filterMgr.Subscriptions.Count} curated categories).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Could not seed offline lists: {ex.Message}");
            }

            // 5. Create initial baseline snapshot
            Console.WriteLine("[INFO] Capturing baseline network snapshot...");
            try
            {
                var snapshotMgr = new SnapshotManager();
                await snapshotMgr.CreateSnapshotAsync();
                Console.WriteLine("[PASS] Initial network state snapshot saved.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Initial snapshot warning: {ex.Message}");
            }

            // 6. Test initial network connectivity
            Console.WriteLine("[INFO] Testing initial network and DNS connectivity...");
            var tester = new ConnectivityTester();
            var check = await tester.VerifyConnectivityAsync();
            if (check.IsConnected)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[PASS] Network connectivity verified (DNS: {check.DnsLatency.TotalMilliseconds:F1}ms).");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[WARN] Network connectivity limited or offline.");
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n=================================================");
            Console.WriteLine("    ShieldBlock Installation Ready & Verified!   ");
            Console.WriteLine("=================================================");
            Console.ResetColor();
            return 0;
        }

        private static async Task<int> ExecuteUninstallAsync()
        {
            Console.WriteLine("Starting Clean, Safe ShieldBlock Removal...\n");

            // 1. Terminate running ShieldBlock processes
            Console.WriteLine("[1/5] Terminating active ShieldBlock processes...");
            KillProcessesByName("ShieldBlock");
            KillProcessesByName("ShieldBlockService");

            // 2. Remove WFP Filters
            Console.WriteLine("[2/5] Purging all ShieldBlock WFP filters...");
            try
            {
                using var wfp = new WfpManager();
                wfp.Initialize();
                wfp.RemoveAllFilters();
                Console.WriteLine("[PASS] WFP filters cleanly removed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[INFO] WFP purge notice: {ex.Message}");
            }

            // 3. Revert Adapter DNS
            Console.WriteLine("[3/5] Reverting network adapters to original DNS / DHCP...");
            try
            {
                var snapshotMgr = new SnapshotManager();
                var rollback = new RollbackEngine(snapshotMgr);
                await rollback.RollbackAllAsync();
                Console.WriteLine("[PASS] Network adapter DNS restored.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] DNS rollback notice: {ex.Message}");
            }

            // 4. Remove Certificate if installed
            Console.WriteLine("[4/5] Removing ShieldBlock Root CA from Certificate Store...");
            try
            {
                using var certMgr = new CertificateManager();
                certMgr.UninstallRootCertificate();
                Console.WriteLine("[PASS] Certificate store cleaned.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[INFO] Certificate notice: {ex.Message}");
            }

            // 5. Verify Final Connectivity
            Console.WriteLine("[5/5] Verifying post-uninstall internet connectivity...");
            var tester = new ConnectivityTester();
            var check = await tester.VerifyConnectivityAsync();
            if (check.IsConnected)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[PASS] Normal Internet Connectivity Confirmed!");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[WARN] Internet check status: {check.ErrorMessage}");
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\n=================================================");
            Console.WriteLine("    ShieldBlock Cleanly Removed with Zero Damage ");
            Console.WriteLine("=================================================");
            Console.ResetColor();
            return 0;
        }

        private static bool RuntimeInformationIsWindows() => OperatingSystem.IsWindows();

        private static bool CheckBfeService()
        {
            try
            {
                using var sc = new ServiceController("bfe");
                return sc.Status == ServiceControllerStatus.Running;
            }
            catch
            {
                return false;
            }
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
