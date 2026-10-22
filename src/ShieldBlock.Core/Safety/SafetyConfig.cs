using System;
using System.Collections.Generic;

namespace ShieldBlock.Core.Safety
{
    public class SafetyConfig
    {
        public bool FailOpenEnabled { get; set; } = true;
        public bool SafeModeActive { get; set; } = false;
        public int ConsecutiveCrashCount { get; set; } = 0;
        public int MaxCrashesBeforeSafeMode { get; set; } = 3;

        public bool EmergencyDisableTriggered { get; set; } = false;
        public DateTime? LastSnapshotTime { get; set; }

        public int HealthCheckIntervalSeconds { get; set; } = 5;
        public int ConnectivityTestTimeoutMs { get; set; } = 3000;

        public bool ZeroRegistryEnforced { get; set; } = true;
        public bool HttpsFilteringEnabled { get; set; } = false; // OFF by default
        public bool SimulationMode { get; set; } = false;

        public List<string> HttpsBypassDomains { get; set; } = new List<string>
        {
            // Critical Banking, Gov, Security & Streaming domains bypassing TLS inspection
            "*.bankofamerica.com",
            "*.chase.com",
            "*.wellsfargo.com",
            "*.citi.com",
            "*.paypal.com",
            "*.stripe.com",
            "*.apple.com",
            "*.icloud.com",
            "*.microsoft.com",
            "*.windowsupdate.com",
            "*.live.com",
            "*.google.com",
            "*.googleapis.com",
            "*.github.com",
            "*.steamcommunity.com",
            "*.steampowered.com",
            "*.epicgames.com",
            "*.discord.com",
            "*.slack.com",
            "*.zoom.us"
        };

        public List<string> BypassApplications { get; set; } = new List<string>
        {
            "steam.exe",
            "discord.exe",
            "slack.exe",
            "ms-teams.exe",
            "zoom.exe",
            "windscribe.exe",
            "nordvpn.exe",
            "openvpn.exe",
            "wireguard.exe"
        };
    }
}
