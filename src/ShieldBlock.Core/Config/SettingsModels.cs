using System;
using System.Collections.Generic;

namespace ShieldBlock.Core.Config
{
    public class UserSettings
    {
        public string Theme { get; set; } = "Dark";
        public bool MinimizeToTray { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public bool AutoUpdateFilters { get; set; } = true;
        public string PreferredDnsUpstream { get; set; } = "Cloudflare"; // Cloudflare, Quad9, AdGuard
        public string PerformanceMode { get; set; } = "Balanced"; // Balanced, Maximum, LowResource
        public bool ShowNotifications { get; set; } = true;
        public bool EnableSoundEffects { get; set; } = false;
    }

    public class ServiceSettings
    {
        public bool ProtectionEnabled { get; set; } = true;
        public bool FailOpenMode { get; set; } = true;
        public bool SafeModeActive { get; set; } = false;
        public bool SimulationMode { get; set; } = false;

        public string DnsBindAddress { get; set; } = Common.Constants.LocalDnsIp;
        public int DnsPort { get; set; } = Common.Constants.LocalDnsPort;
        public string UpstreamDohUrl { get; set; } = Common.Constants.UpstreamDohCloudflare;

        public bool HttpsFilteringEnabled { get; set; } = false; // OFF by default
        public string? InstalledRootCertThumbprint { get; set; }

        public List<string> EnabledFilterLists { get; set; } = new List<string>
        {
            "adblock",
            "privacy",
            "annoyances",
            "security",
            "youtube"
        };

        public List<string> BypassDomains { get; set; } = new List<string>
        {
            "*.bankofamerica.com",
            "*.chase.com",
            "*.wellsfargo.com",
            "*.paypal.com",
            "*.microsoft.com",
            "*.windowsupdate.com",
            "*.apple.com",
            "*.steamcommunity.com"
        };

        public List<string> BypassApplications { get; set; } = new List<string>
        {
            "steam.exe",
            "discord.exe",
            "slack.exe",
            "windscribe.exe",
            "nordvpn.exe",
            "openvpn.exe"
        };

        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
