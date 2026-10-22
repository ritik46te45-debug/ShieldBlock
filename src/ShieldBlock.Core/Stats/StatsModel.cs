using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace ShieldBlock.Core.Stats
{
    public class QueryLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Domain { get; set; } = string.Empty;
        public string ClientIp { get; set; } = string.Empty;
        public string RecordType { get; set; } = "A";
        public bool IsBlocked { get; set; }
        public string RuleMatched { get; set; } = string.Empty;
        public double LatencyMs { get; set; }
    }

    public class ShieldBlockStats
    {
        public long TotalQueries { get; set; }
        public long BlockedQueries { get; set; }
        public long TrackersBlocked { get; set; }
        public long MalwareBlocked { get; set; }
        public long BandwidthSavedBytes { get; set; }
        public double AverageLatencyMs { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, long> TopBlockedDomains { get; set; } = new();
    }
}
