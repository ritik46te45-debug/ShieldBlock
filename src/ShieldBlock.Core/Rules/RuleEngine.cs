using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace ShieldBlock.Core.Rules
{
    public class RuleEngine
    {
        // Exact and suffix domain indexes
        private readonly Dictionary<string, List<FilterRule>> _domainAllowIndex = new Dictionary<string, List<FilterRule>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FilterRule>> _domainBlockIndex = new Dictionary<string, List<FilterRule>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FilterRule>> _cosmeticIndex = new Dictionary<string, List<FilterRule>>(StringComparer.OrdinalIgnoreCase);

        // L1 Hot Decision Cache (stores up to 50,000 recent domain decisions)
        private readonly ConcurrentDictionary<string, MatchResult> _decisionCache = new ConcurrentDictionary<string, MatchResult>(StringComparer.OrdinalIgnoreCase);
        private const int MaxCacheSize = 50000;

        public bool SimulationMode { get; set; } = false;
        public long TotalRulesCount { get; private set; }
        public long LoadedRuleCount => TotalRulesCount;

        public void Clear()
        {
            _domainAllowIndex.Clear();
            _domainBlockIndex.Clear();
            _cosmeticIndex.Clear();
            _decisionCache.Clear();
            TotalRulesCount = 0;
        }

        public int LoadRules(IEnumerable<string> rawLines, string sourceList = "Default")
        {
            int count = 0;
            foreach (var line in rawLines)
            {
                var rule = AbpRuleParser.ParseRule(line, sourceList);
                if (rule == null) continue;

                AddRule(rule);
                count++;
            }
            return count;
        }

        public void AddRule(FilterRule rule)
        {
            TotalRulesCount++;
            _decisionCache.Clear(); // Invalidate cache on new rules

            if (rule.Kind == RuleKind.DomainAllow)
            {
                if (!_domainAllowIndex.TryGetValue(rule.Domain, out var list))
                {
                    list = new List<FilterRule>();
                    _domainAllowIndex[rule.Domain] = list;
                }
                list.Add(rule);
            }
            else if (rule.Kind == RuleKind.DomainBlock)
            {
                if (!_domainBlockIndex.TryGetValue(rule.Domain, out var list))
                {
                    list = new List<FilterRule>();
                    _domainBlockIndex[rule.Domain] = list;
                }
                list.Add(rule);
            }
            else if (rule.Kind == RuleKind.CosmeticElement)
            {
                if (!_cosmeticIndex.TryGetValue(rule.Domain, out var list))
                {
                    list = new List<FilterRule>();
                    _cosmeticIndex[rule.Domain] = list;
                }
                list.Add(rule);
            }
        }

        public MatchResult Evaluate(string domain, string? url = null)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return MatchResult.NoRuleMatched();

            domain = domain.Trim().ToLowerInvariant();

            // 1. L1 Fast Path: Check decision cache
            if (_decisionCache.TryGetValue(domain, out var cached))
            {
                return cached;
            }

            var sw = Stopwatch.StartNew();

            // 2. Hierarchical Domain Suffix Decomposition
            // e.g. "pagead2.googlesyndication.com" checks:
            // - "pagead2.googlesyndication.com"
            // - "googlesyndication.com"
            // - "com" (skipped)
            string current = domain;
            while (true)
            {
                // A. Check ALLOW rules first (Exceptions take absolute precedence)
                if (_domainAllowIndex.TryGetValue(current, out var allowRules) && allowRules.Count > 0)
                {
                    var res = MatchResult.Allow(allowRules[0], SimulationMode, sw.ElapsedTicks * 1000000 / Stopwatch.Frequency);
                    CacheDecision(domain, res);
                    return res;
                }

                // B. Check BLOCK rules
                if (_domainBlockIndex.TryGetValue(current, out var blockRules) && blockRules.Count > 0)
                {
                    var res = MatchResult.Block(blockRules[0], SimulationMode, sw.ElapsedTicks * 1000000 / Stopwatch.Frequency);
                    CacheDecision(domain, res);
                    return res;
                }

                // Step up one subdomain level
                int dotIndex = current.IndexOf('.');
                if (dotIndex < 0 || dotIndex == current.Length - 1)
                    break;

                current = current.Substring(dotIndex + 1);

                // Do not match top-level domain alone (e.g. don't check "com")
                if (!current.Contains('.'))
                    break;
            }

            // No rule matched
            var noMatch = MatchResult.NoRuleMatched(sw.ElapsedTicks * 1000000 / Stopwatch.Frequency);
            CacheDecision(domain, noMatch);
            return noMatch;
        }

        public List<FilterRule> GetCosmeticRules(string domain)
        {
            var results = new List<FilterRule>();
            if (string.IsNullOrWhiteSpace(domain)) return results;

            domain = domain.Trim().ToLowerInvariant();

            if (_cosmeticIndex.TryGetValue(domain, out var rules))
            {
                results.AddRange(rules);
            }
            if (_cosmeticIndex.TryGetValue(string.Empty, out var globalRules))
            {
                results.AddRange(globalRules);
            }

            return results;
        }

        private void CacheDecision(string domain, MatchResult result)
        {
            if (_decisionCache.Count < MaxCacheSize)
            {
                _decisionCache[domain] = result;
            }
        }
    }
}
