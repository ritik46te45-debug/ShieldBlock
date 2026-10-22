using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;
using ShieldBlock.Core.Common;
using ShieldBlock.Core.Dns;
using ShieldBlock.Core.Https;
using ShieldBlock.Core.Safety;
using ShieldBlock.Core.Wfp;

namespace ShieldBlock.Recovery
{
    public static class Program
    {
        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
        private static extern uint DnsFlushResolverCache();

        public static async Task<int> Main(string[] args)
        {
            Console.Title = "ShieldBlock Emergency Recovery Tool";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================");
            Console.WriteLine("  ShieldBlock Emergency Rescue & Recovery Tool   ");
            Console.WriteLine("=================================================");
            Console.ResetColor();
            Console.WriteLine("This tool restores full network and DNS connectivity to default.");
            Console.WriteLine("It safely reverses all ShieldBlock changes without damaging Windows.\n");

            bool isElevated = IsAdministrator();
            if (!isElevated)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Warning] This utility is running without administrator privileges.");
                Console.WriteLine("For full adapter DNS reset and WFP filter removal, administrator elevation is recommended.");
                Console.ResetColor();
                Console.WriteLine();
            }

            // Step 1: Terminate running ShieldBlock processes
            Console.Write("[1/6] Stopping active ShieldBlock processes... ");
            KillProcessesByName("ShieldBlock");
            KillProcessesByName("ShieldBlockService");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("DONE");
            Console.ResetColor();

            // Step 2: Remove WFP Filters
            Console.Write("[2/6] Purging ShieldBlock WFP filters... ");
            try
            {
                using var wfp = new WfpManager();
                wfp.Initialize();
                wfp.RemoveAllFilters();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("DONE");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"SKIPPED ({ex.Message})");
            }
            Console.ResetColor();

            // Step 3: Revert Adapter DNS
            Console.Write("[3/6] Reverting network adapter DNS to original / DHCP... ");
            try
            {
                var snapshotMgr = new SnapshotManager();
                var rollbackEngine = new RollbackEngine(snapshotMgr);
                bool restored = await rollbackEngine.RollbackAllAsync();
                if (restored)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("RESTORED");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("DEFAULTED");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"FAILED ({ex.Message})");
            }
            Console.ResetColor();

            // Step 4: Flush Windows DNS cache
            Console.Write("[4/6] Flushing Windows DNS resolver cache... ");
            try
            {
                DnsFlushResolverCache();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("FLUSHED");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"ERROR ({ex.Message})");
            }
            Console.ResetColor();

            // Step 5: Remove ShieldBlock Root CA from Certificate Store
            Console.Write("[5/6] Checking and cleaning Certificate Stores... ");
            try
            {
                using var certMgr = new CertificateManager();
                certMgr.UninstallRootCertificate();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("CLEAN");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"SKIPPED ({ex.Message})");
            }
            Console.ResetColor();

            // Step 6: Verify Connectivity
            Console.Write("[6/6] Verifying Internet connectivity... ");
            var tester = new ConnectivityTester();
            var check = await tester.VerifyConnectivityAsync();

            if (check.IsConnected && !check.IsCaptivePortalDetected)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("ONLINE");
                Console.ResetColor();
                Console.WriteLine($"\nDNS Latency: {check.DnsLatency.TotalMilliseconds:F1} ms | Probe Latency: {check.ProbeLatency.TotalMilliseconds:F1} ms");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("OFFLINE OR RESTRICTED");
                Console.ResetColor();
                Console.WriteLine($"\nDiagnostics: {check.ErrorMessage}");
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n=================================================");
            Console.WriteLine("  Recovery Complete. Normal Internet Confirmed.  ");
            Console.WriteLine("=================================================");
            Console.ResetColor();

            return check.IsConnected ? 0 : 1;
        }

        private static bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
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
                        try
                        {
                            p.Kill();
                            p.WaitForExit(1000);
                        }
                        catch { }
                    }
                    p.Dispose();
                }
            }
            catch { }
        }
    }
}
