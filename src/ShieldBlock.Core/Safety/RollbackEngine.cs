using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ShieldBlock.Core.Common;

namespace ShieldBlock.Core.Safety
{
    public class RollbackResult
    {
        public bool Success { get; set; }
        public string? Details { get; set; }
        public ConnectivityResult? PostRollbackConnectivity { get; set; }
    }

    public class RollbackEngine
    {
        private readonly SnapshotManager _snapshotManager;
        private readonly ConnectivityTester _tester;

        public RollbackEngine(SnapshotManager? snapshotManager = null, ConnectivityTester? tester = null)
        {
            _snapshotManager = snapshotManager ?? new SnapshotManager();
            _tester = tester ?? new ConnectivityTester();
        }

        public async Task<bool> RollbackAllAsync()
        {
            var res = await ExecuteEmergencyRollbackAsync();
            return res.Success;
        }

        public async Task<RollbackResult> ExecuteEmergencyRollbackAsync(SystemSnapshot? targetSnapshot = null)
        {
            var result = new RollbackResult();
            var snapshot = targetSnapshot ?? _snapshotManager.LoadLastKnownGood();

            Console.WriteLine("[RollbackEngine] Commencing Emergency Rollback...");

            if (snapshot == null)
            {
                result.Success = false;
                result.Details = "No snapshot found to roll back to.";
                Console.WriteLine("[RollbackEngine] Warning: No snapshot found.");
                return result;
            }

            try
            {
                // 1. Restore adapter DNS settings for each captured adapter
                foreach (var adapter in snapshot.Adapters)
                {
                    if (string.IsNullOrWhiteSpace(adapter.Name)) continue;

                    if (adapter.DhcpEnabled || adapter.DnsServers.Count == 0)
                    {
                        // Restore DHCP DNS
                        RestoreAdapterDnsDhcp(adapter.Name);
                    }
                    else
                    {
                        // Restore static DNS servers
                        RestoreAdapterDnsStatic(adapter.Name, adapter.DnsServers);
                    }
                }

                // 2. Clear any lingering proxy if ShieldBlock set one
                // (ShieldBlock does not set global proxy unless explicitly configured)

                // 3. Run connectivity verification
                var connectivity = await _tester.RunConnectivityCheckAsync(timeoutMs: 3000);
                result.PostRollbackConnectivity = connectivity;
                result.Success = connectivity.IsOverallSuccess;
                result.Details = result.Success
                    ? "Rollback completed successfully. Internet connectivity verified."
                    : $"Rollback applied but connectivity check reported: {connectivity.FailureReason}";

                Console.WriteLine($"[RollbackEngine] Result: {result.Details}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Details = $"Rollback error: {ex.Message}";
                Console.WriteLine($"[RollbackEngine] Error during rollback: {ex}");
            }

            return result;
        }

        private void RestoreAdapterDnsDhcp(string adapterName)
        {
            try
            {
                // Documented safe netsh invocation targeting ONLY this specific adapter name
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"interface ipv4 set dnsservers name=\"{adapterName}\" source=dhcp",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RollbackEngine] Warning setting DHCP DNS on '{adapterName}': {ex.Message}");
            }
        }

        private void RestoreAdapterDnsStatic(string adapterName, System.Collections.Generic.List<string> dnsServers)
        {
            if (dnsServers.Count == 0) return;

            try
            {
                // Set primary
                var primary = dnsServers[0];
                var psiPrimary = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"interface ipv4 set dnsservers name=\"{adapterName}\" static {primary} primary",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p1 = Process.Start(psiPrimary);
                p1?.WaitForExit(3000);

                // Add secondary if present
                for (int i = 1; i < dnsServers.Count; i++)
                {
                    var sec = dnsServers[i];
                    var psiSec = new ProcessStartInfo
                    {
                        FileName = "netsh.exe",
                        Arguments = $"interface ipv4 add dnsservers name=\"{adapterName}\" {sec} index={i + 1}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var pSec = Process.Start(psiSec);
                    pSec?.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RollbackEngine] Warning setting static DNS on '{adapterName}': {ex.Message}");
            }
        }
    }
}
