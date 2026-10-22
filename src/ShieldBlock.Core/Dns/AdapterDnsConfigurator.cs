using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Threading.Tasks;
using ShieldBlock.Core.Common;
using ShieldBlock.Core.Safety;

namespace ShieldBlock.Core.Dns
{
    public class AdapterDnsConfigurator
    {
        private readonly SnapshotManager _snapshotManager;
        private readonly RollbackEngine _rollbackEngine;
        private readonly ConnectivityTester _connectivityTester;

        public AdapterDnsConfigurator(
            SnapshotManager? snapshotManager = null,
            RollbackEngine? rollbackEngine = null,
            ConnectivityTester? connectivityTester = null)
        {
            _snapshotManager = snapshotManager ?? new SnapshotManager();
            _rollbackEngine = rollbackEngine ?? new RollbackEngine(_snapshotManager);
            _connectivityTester = connectivityTester ?? new ConnectivityTester();
        }

        public static bool IsAdministrator()
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public async Task<bool> ConfigureDnsProxyAsync(string proxyIp = "127.0.0.1")
        {
            if (!IsAdministrator())
            {
                Console.WriteLine("[AdapterDnsConfigurator] Warning: Administrator privileges required to change adapter DNS. Skipping live adapter reconfiguration.");
                return false;
            }

            // 1. Mandatory Step: Create snapshot of current network state
            Console.WriteLine("[AdapterDnsConfigurator] Taking pre-configuration snapshot...");
            var snapshot = await _snapshotManager.CreateSnapshotAsync();
            if (snapshot.Adapters.Count == 0)
            {
                Console.WriteLine("[AdapterDnsConfigurator] No active network adapters found to configure.");
                return false;
            }

            // 2. Iterate through operational physical/Wi-Fi adapters
            bool anyConfigured = false;
            foreach (var adapter in snapshot.Adapters)
            {
                if (adapter.Status != OperationalStatus.Up.ToString()) continue;
                if (adapter.InterfaceType.Contains("Loopback") || adapter.InterfaceType.Contains("Tunnel")) continue;

                Console.WriteLine($"[AdapterDnsConfigurator] Configuring DNS for adapter '{adapter.Name}' to {proxyIp}...");
                bool ok = SetAdapterDns(adapter.Name, proxyIp);
                if (ok) anyConfigured = true;
            }

            if (!anyConfigured)
            {
                Console.WriteLine("[AdapterDnsConfigurator] Failed to configure any active adapters.");
                return false;
            }

            // Give Windows TCP/IP stack a brief moment to apply DNS and flush cache
            await Task.Delay(300);
            try
            {
                var psi = new ProcessStartInfo("ipconfig.exe", "/flushdns") { CreateNoWindow = true, UseShellExecute = false };
                using var p = Process.Start(psi);
                p?.WaitForExit(2000);
            }
            catch { }

            // 3. Safety Step: Verify connectivity with retry
            Console.WriteLine("[AdapterDnsConfigurator] Verifying network connectivity post-DNS configuration...");
            var connectivity = await _connectivityTester.VerifyConnectivityAsync(timeoutMs: 4000);

            if (!connectivity.IsConnected)
            {
                await Task.Delay(500);
                connectivity = await _connectivityTester.VerifyConnectivityAsync(timeoutMs: 4000);
            }

            if (!connectivity.IsConnected || connectivity.IsCaptivePortalDetected)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[AdapterDnsConfigurator] CRITICAL: Connectivity check failed after DNS change! Triggering immediate rollback...");
                Console.ResetColor();

                await _rollbackEngine.RollbackAllAsync();
                return false;
            }

            Console.WriteLine("[AdapterDnsConfigurator] DNS proxy configured successfully and connectivity verified.");
            return true;
        }

        public async Task<bool> RevertDnsAsync()
        {
            Console.WriteLine("[AdapterDnsConfigurator] Reverting adapter DNS to original configuration...");
            return await _rollbackEngine.RollbackAllAsync();
        }

        private bool SetAdapterDns(string adapterName, string dnsIp)
        {
            try
            {
                // Execute netsh safely via ThreatModel validation
                string arguments = $"interface ip set dns name=\"{adapterName}\" static {dnsIp} primary validate=no";
                if (!ThreatModel.IsCommandSafe("netsh", arguments))
                {
                    Console.WriteLine($"[AdapterDnsConfigurator] Command rejected by ThreatModel: netsh {arguments}");
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                proc.WaitForExit(5000);
                if (proc.ExitCode == 0) return true;

                // Fallback to interface ipv4 set dnsservers syntax
                var fallbackPsi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"interface ipv4 set dnsservers name=\"{adapterName}\" static {dnsIp} primary validate=no",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc2 = Process.Start(fallbackPsi);
                proc2?.WaitForExit(5000);
                return proc2?.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdapterDnsConfigurator] Error setting DNS on '{adapterName}': {ex.Message}");
                return false;
            }
        }
    }
}
