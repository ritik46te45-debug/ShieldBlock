using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using ShieldBlock.Core.Common;

namespace ShieldBlock.Core.Stats
{
    public class StatsManager
    {
        private long _totalQueries;
        private long _blockedQueries;
        private long _trackersBlocked;
        private long _malwareBlocked;
        private long _bandwidthSavedBytes;
        private long _totalLatencyTicks;
        private readonly DateTime _startedAt = DateTime.UtcNow;

        private readonly ConcurrentQueue<QueryLogEntry> _recentQueries = new();
        private const int MaxRecentQueries = 500;
        private readonly ConcurrentDictionary<string, long> _domainBlockCounts = new(StringComparer.OrdinalIgnoreCase);

        private readonly string _statsFilePath;
        private readonly Timer? _persistTimer;

        public long TotalQueries => Interlocked.Read(ref _totalQueries);
        public long BlockedQueries => Interlocked.Read(ref _blockedQueries);
        public long TrackersBlocked => Interlocked.Read(ref _trackersBlocked);
        public long MalwareBlocked => Interlocked.Read(ref _malwareBlocked);
        public long BandwidthSavedBytes => Interlocked.Read(ref _bandwidthSavedBytes);

        public double AverageLatencyMs
        {
            get
            {
                long total = TotalQueries;
                if (total == 0) return 0.0;
                long ticks = Interlocked.Read(ref _totalLatencyTicks);
                return (double)ticks / (TimeSpan.TicksPerMillisecond * total);
            }
        }

        public StatsManager(string? statsFilePath = null, bool autoPersist = true)
        {
            _statsFilePath = statsFilePath ?? Path.Combine(Constants.ProgramDataDir, "data", "stats.json");
            LoadStats();

            if (autoPersist)
            {
                _persistTimer = new Timer(_ => SaveStats(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
            }
        }

        public void RecordQuery(string domain, string recordType, bool isBlocked, string ruleMatched, TimeSpan latency, string clientIp = "127.0.0.1")
        {
            Interlocked.Increment(ref _totalQueries);
            Interlocked.Add(ref _totalLatencyTicks, latency.Ticks);

            if (isBlocked)
            {
                Interlocked.Increment(ref _blockedQueries);
                // Estimate 35 KB average bandwidth saved per blocked ad/tracker element
                Interlocked.Add(ref _bandwidthSavedBytes, 35 * 1024);

                if (ruleMatched.Contains("privacy", StringComparison.OrdinalIgnoreCase) ||
                    domain.Contains("analytic", StringComparison.OrdinalIgnoreCase) ||
                    domain.Contains("telemetry", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref _trackersBlocked);
                }
                else if (ruleMatched.Contains("malware", StringComparison.OrdinalIgnoreCase) ||
                         ruleMatched.Contains("phishing", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref _malwareBlocked);
                }

                _domainBlockCounts.AddOrUpdate(domain, 1, (_, count) => count + 1);
            }

            var entry = new QueryLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Domain = domain,
                RecordType = recordType,
                IsBlocked = isBlocked,
                RuleMatched = ruleMatched,
                LatencyMs = latency.TotalMilliseconds,
                ClientIp = clientIp
            };

            _recentQueries.Enqueue(entry);
            while (_recentQueries.Count > MaxRecentQueries && _recentQueries.TryDequeue(out _)) { }
        }

        public IReadOnlyList<QueryLogEntry> GetRecentQueries()
        {
            return _recentQueries.ToArray();
        }

        public ShieldBlockStats GetSnapshot()
        {
            return new ShieldBlockStats
            {
                TotalQueries = TotalQueries,
                BlockedQueries = BlockedQueries,
                TrackersBlocked = TrackersBlocked,
                MalwareBlocked = MalwareBlocked,
                BandwidthSavedBytes = BandwidthSavedBytes,
                AverageLatencyMs = AverageLatencyMs,
                StartedAt = _startedAt,
                TopBlockedDomains = _domainBlockCounts
                    .OrderByDescending(kv => kv.Value)
                    .Take(10)
                    .ToDictionary(kv => kv.Key, kv => kv.Value)
            };
        }

        public void SaveStats()
        {
            try
            {
                var snapshot = GetSnapshot();
                var dir = Path.GetDirectoryName(_statsFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                string tmp = _statsFilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _statsFilePath, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StatsManager] Warning saving stats: {ex.Message}");
            }
        }

        private void LoadStats()
        {
            try
            {
                if (File.Exists(_statsFilePath))
                {
                    string json = File.ReadAllText(_statsFilePath);
                    var snapshot = JsonSerializer.Deserialize<ShieldBlockStats>(json);
                    if (snapshot != null)
                    {
                        _totalQueries = snapshot.TotalQueries;
                        _blockedQueries = snapshot.BlockedQueries;
                        _trackersBlocked = snapshot.TrackersBlocked;
                        _malwareBlocked = snapshot.MalwareBlocked;
                        _bandwidthSavedBytes = snapshot.BandwidthSavedBytes;
                        foreach (var kvp in snapshot.TopBlockedDomains)
                        {
                            _domainBlockCounts[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch { }
        }
    }
}
