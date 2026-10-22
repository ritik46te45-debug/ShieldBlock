using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ShieldBlock.Core.Common;

namespace ShieldBlock.Core.Safety
{
    public class SnapshotManager
    {
        private readonly string _backupDir;
        private readonly string _lastKnownGoodPath;

        public SnapshotManager(string? backupDir = null)
        {
            _backupDir = backupDir ?? Constants.BackupDir;
            _lastKnownGoodPath = Path.Combine(_backupDir, "last_known_good.json");
            EnsureDirectoryExists();
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(_backupDir))
            {
                Directory.CreateDirectory(_backupDir);
            }
        }

        public Task<SystemSnapshot> CreateSnapshotAsync(string reason = "System modification") => Task.FromResult(CreateSnapshot(reason));

        public SystemSnapshot CreateSnapshot(string reason = "System modification")
        {
            EnsureDirectoryExists();

            var snapshot = new SystemSnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                Reason = reason,
                ShieldBlockVersion = Constants.Version
            };

            // Capture active network interfaces
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var nic in interfaces)
                {
                    // Skip loopback and tunnel interfaces
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    var ipProps = nic.GetIPProperties();
                    var adapterSnap = new NetworkAdapterSnapshot
                    {
                        AdapterId = nic.Id,
                        Name = nic.Name,
                        Description = nic.Description,
                        Status = nic.OperationalStatus.ToString(),
                        InterfaceType = nic.NetworkInterfaceType.ToString(),
                        DhcpEnabled = OperatingSystem.IsWindows() ? (ipProps.GetIPv4Properties()?.IsDhcpEnabled ?? false) : false
                    };

                    foreach (var dns in ipProps.DnsAddresses)
                    {
                        adapterSnap.DnsServers.Add(dns.ToString());
                    }

                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        adapterSnap.IpAddresses.Add(addr.Address.ToString());
                    }

                    foreach (var gw in ipProps.GatewayAddresses)
                    {
                        adapterSnap.Gateways.Add(gw.Address.ToString());
                    }

                    snapshot.Adapters.Add(adapterSnap);
                }
            }
            catch (Exception ex)
            {
                // In fail-open principle, log but continue capturing
                Console.WriteLine($"[SnapshotManager] Warning capturing network interfaces: {ex.Message}");
            }

            // Compute hash
            var jsonPreview = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = false });
            using (var sha = SHA256.Create())
            {
                var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(jsonPreview));
                snapshot.ConfigHash = Convert.ToHexString(hashBytes);
            }

            // Save timestamped file
            var filename = $"snapshot_{snapshot.TimestampUtc:yyyyMMdd_HHmmss}_{snapshot.SnapshotId.Substring(0, 8)}.json";
            var filePath = Path.Combine(_backupDir, filename);

            var options = new JsonSerializerOptions { WriteIndented = true };
            var fullJson = JsonSerializer.Serialize(snapshot, options);

            File.WriteAllText(filePath, fullJson, Encoding.UTF8);

            // Also update last_known_good.json atomically
            var tempGood = _lastKnownGoodPath + ".tmp";
            File.WriteAllText(tempGood, fullJson, Encoding.UTF8);
            File.Move(tempGood, _lastKnownGoodPath, overwrite: true);

            return snapshot;
        }

        public SystemSnapshot? LoadLastKnownGood()
        {
            if (!File.Exists(_lastKnownGoodPath))
                return null;

            try
            {
                var json = File.ReadAllText(_lastKnownGoodPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<SystemSnapshot>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SnapshotManager] Error reading last_known_good: {ex.Message}");
                return null;
            }
        }
    }
}
