using System;
using System.Collections.Concurrent;

namespace ShieldBlock.Core.Dns
{
    public class DnsCacheEntry
    {
        public byte[] RawResponse { get; }
        public DateTime ExpiresAt { get; }

        public DnsCacheEntry(byte[] rawResponse, uint ttlSeconds)
        {
            RawResponse = rawResponse;
            // Bound TTL between 5 seconds and 86400 seconds (24 hours)
            uint boundedTtl = Math.Clamp(ttlSeconds, 5u, 86400u);
            ExpiresAt = DateTime.UtcNow.AddSeconds(boundedTtl);
        }

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }

    public class DnsCache
    {
        private readonly ConcurrentDictionary<string, DnsCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly int _maxCapacity;

        public DnsCache(int maxCapacity = 10000)
        {
            _maxCapacity = maxCapacity;
        }

        public int Count => _cache.Count;

        private static string GetKey(string domain, DnsRecordType type) => $"{domain.ToLowerInvariant()}:{type}";

        public bool TryGet(string domain, DnsRecordType type, ushort transactionId, out byte[]? response)
        {
            response = null;
            string key = GetKey(domain, type);

            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.IsExpired)
                {
                    _cache.TryRemove(key, out _);
                    return false;
                }

                // Clone response and patch Transaction ID (first 2 bytes)
                byte[] cloned = (byte[])entry.RawResponse.Clone();
                if (cloned.Length >= 2)
                {
                    cloned[0] = (byte)(transactionId >> 8);
                    cloned[1] = (byte)(transactionId & 0xFF);
                }
                response = cloned;
                return true;
            }

            return false;
        }

        public void Set(string domain, DnsRecordType type, byte[] rawResponse, uint ttlSeconds = 300)
        {
            if (_cache.Count >= _maxCapacity)
            {
                // Simple sweep of expired entries
                PruneExpired();

                // If still over capacity, clear oldest or clear half
                if (_cache.Count >= _maxCapacity)
                {
                    _cache.Clear();
                }
            }

            string key = GetKey(domain, type);
            _cache[key] = new DnsCacheEntry(rawResponse, ttlSeconds);
        }

        public void PruneExpired()
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _cache)
            {
                if (kvp.Value.ExpiresAt <= now)
                {
                    _cache.TryRemove(kvp.Key, out _);
                }
            }
        }

        public void Clear()
        {
            _cache.Clear();
        }
    }
}
