using System;

namespace ShieldBlock.Core.Common
{
    public static class Constants
    {
        public const string ApplicationName = "ShieldBlock";
        public const string ServiceName = "ShieldBlockService";
        public const string ServiceDisplayName = "ShieldBlock Core Protection Service";
        public const string ServiceDescription = "Provides safe, privacy-first, fail-open network ad-blocking and tracking protection.";
        public const string Version = "1.0.0";

        // File System Paths - Zero Registry Storage
        public static readonly string ProgramDataDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ApplicationName);

        public static readonly string AppDataDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ApplicationName);

        public static readonly string ConfigDir = System.IO.Path.Combine(ProgramDataDir, "config");
        public static readonly string BackupDir = System.IO.Path.Combine(ProgramDataDir, "backups");
        public static readonly string DataDir = System.IO.Path.Combine(ProgramDataDir, "data");
        public static readonly string LogsDir = System.IO.Path.Combine(ProgramDataDir, "logs");
        public static readonly string CertsDir = System.IO.Path.Combine(ProgramDataDir, "certificates");

        public static readonly string ServiceSettingsPath = System.IO.Path.Combine(ConfigDir, "service_settings.json");
        public static readonly string UserSettingsPath = System.IO.Path.Combine(AppDataDir, "user_settings.json");
        public static readonly string LastKnownGoodSnapshot = System.IO.Path.Combine(BackupDir, "last_known_good.json");
        public static readonly string DatabasePath = System.IO.Path.Combine(DataDir, "shieldblock.db");

        // Named Pipe IPC
        public const string IpcPipeName = "ShieldBlock_IPC";
        public const string IpcFullPipeName = @"\\.\pipe\" + IpcPipeName;

        // WFP Constants
        // Unique Provider GUID: {5147424C-0001-4000-8000-536869656C64} (ASCII: QAGL...Shield)
        public static readonly Guid WfpProviderGuid = new Guid("5147424C-0001-4000-8000-536869656C64");
        public static readonly Guid WfpSublayerGuid = new Guid("5147424C-0002-4000-8000-536869656C64");
        public const string WfpProviderName = "ShieldBlock-Provider";
        public const string WfpSublayerName = "ShieldBlock-Sublayer";
        public const string WfpRulePrefix = "ShieldBlock-";

        // DNS Engine
        public const string LocalDnsIp = "127.0.0.1";
        public const int LocalDnsPort = 53;
        public const string UpstreamDohCloudflare = "https://1.1.1.1/dns-query";
        public const string UpstreamDohQuad9 = "https://9.9.9.9/dns-query";
        public const string UpstreamDohAdGuard = "https://dns.adguard-dns.com/dns-query";

        // HTTPS Engine
        public const string CaCommonName = "ShieldBlock Local Filtering CA";
        public const string CaOrganization = "ShieldBlock Privacy";

        // Hotkey
        public const string EmergencyHotkeyDisplay = "Ctrl + Shift + F12";
    }
}
