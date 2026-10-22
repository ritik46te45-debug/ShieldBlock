using System;
using System.Collections.Generic;

namespace ShieldBlock.Core.Safety
{
    public class NetworkAdapterSnapshot
    {
        public string AdapterId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "Up";
        public string InterfaceType { get; set; } = "Ethernet";
        public List<string> DnsServers { get; set; } = new List<string>();
        public bool DhcpEnabled { get; set; }
        public List<string> IpAddresses { get; set; } = new List<string>();
        public List<string> Gateways { get; set; } = new List<string>();
    }

    public class ProxySnapshot
    {
        public bool ProxyEnabled { get; set; }
        public string? ProxyServer { get; set; }
        public string? AutoConfigUrl { get; set; }
        public string? ProxyBypass { get; set; }
    }

    public class SystemSnapshot
    {
        public string SnapshotId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string ShieldBlockVersion { get; set; } = Common.Constants.Version;
        public string ConfigHash { get; set; } = string.Empty;
        public string Reason { get; set; } = "Pre-configuration backup";

        public List<NetworkAdapterSnapshot> Adapters { get; set; } = new List<NetworkAdapterSnapshot>();
        public ProxySnapshot Proxy { get; set; } = new ProxySnapshot();
        public List<ulong> ShieldBlockWfpFilterIds { get; set; } = new List<ulong>();
        public List<string> ShieldBlockInstalledCertThumbprints { get; set; } = new List<string>();
    }
}
