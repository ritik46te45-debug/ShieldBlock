using System;

namespace ShieldBlock.Core.Ipc
{
    public class IpcRequest
    {
        public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
        public string Action { get; set; } = string.Empty;
        public string? PayloadJson { get; set; }
    }

    public class IpcResponse
    {
        public string RequestId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? PayloadJson { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        public static IpcResponse Ok(string requestId, string? payloadJson = null) => new IpcResponse
        {
            RequestId = requestId,
            Success = true,
            PayloadJson = payloadJson
        };

        public static IpcResponse Fail(string requestId, string error) => new IpcResponse
        {
            RequestId = requestId,
            Success = false,
            ErrorMessage = error
        };
    }

    public static class IpcActions
    {
        public const string Ping = "Ping";
        public const string GetStatus = "GetStatus";
        public const string SetProtection = "SetProtection";
        public const string EmergencyDisable = "EmergencyDisable";
        public const string GetMetrics = "GetMetrics";
        public const string ReloadRules = "ReloadRules";
        public const string TriggerRollback = "TriggerRollback";
    }

    public class ServiceStatusDto
    {
        public bool ProtectionEnabled { get; set; }
        public bool SafeModeActive { get; set; }
        public bool FailOpenMode { get; set; }
        public int ConsecutiveCrashes { get; set; }
        public bool DnsEngineRunning { get; set; }
        public bool WfpFiltersActive { get; set; }
        public bool HttpsFilteringActive { get; set; }
        public long RulesLoadedCount { get; set; }
        public long TotalQueriesFiltered { get; set; }
        public long TotalAdsBlocked { get; set; }
        public long TrackersBlocked { get; set; }
        public long MalwareBlocked { get; set; }
        public long BandwidthSavedBytes { get; set; }
        public double AverageLatencyMs { get; set; }
        public DateTime UptimeStartUtc { get; set; }
    }
}
