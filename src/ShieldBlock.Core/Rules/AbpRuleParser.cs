using System;
using System.IO;

namespace ShieldBlock.Core.Rules
{
    public static class AbpRuleParser
    {
        private static int _globalRuleId = 1;

        public static FilterRule? ParseRule(string line, string sourceList = "Default")
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            var trimmed = line.Trim();

            // Ignore comments and metadata headers
            if (trimmed.StartsWith("!") || trimmed.StartsWith("#") || trimmed.StartsWith("[Adblock"))
            {
                return null;
            }

            // ----- Hosts-file format support -----
            // Lines like "0.0.0.0 domain.com" or "127.0.0.1 domain.com" from hosts-file blocklists
            if (trimmed.StartsWith("0.0.0.0 ") || trimmed.StartsWith("127.0.0.1 ") ||
                trimmed.StartsWith("0.0.0.0\t") || trimmed.StartsWith("127.0.0.1\t"))
            {
                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string hostDomain = parts[1].Trim().ToLowerInvariant();
                    // Skip localhost entries and empty domains
                    if (!string.IsNullOrEmpty(hostDomain) &&
                        hostDomain != "localhost" &&
                        hostDomain != "localhost.localdomain" &&
                        hostDomain != "local" &&
                        hostDomain != "broadcasthost" &&
                        hostDomain != "ip6-localhost" &&
                        hostDomain != "ip6-loopback" &&
                        hostDomain.Contains('.'))
                    {
                        return new FilterRule
                        {
                            RuleId = InterlockedIncrement(),
                            RawRule = trimmed,
                            SourceList = sourceList,
                            Kind = RuleKind.DomainBlock,
                            Domain = hostDomain
                        };
                    }
                }
                return null;
            }

            var rule = new FilterRule
            {
                RuleId = InterlockedIncrement(),
                RawRule = trimmed,
                SourceList = sourceList
            };

            // 1. Check for Cosmetic Rules: domain##selector or ##selector
            int cosmeticIdx = trimmed.IndexOf("##", StringComparison.Ordinal);
            if (cosmeticIdx >= 0)
            {
                rule.Kind = RuleKind.CosmeticElement;
                rule.Domain = trimmed.Substring(0, cosmeticIdx).Trim().ToLowerInvariant();
                rule.Selector = trimmed.Substring(cosmeticIdx + 2).Trim();
                return rule;
            }

            // 2. Check for Exception / Allowlist: starts with @@
            bool isException = false;
            if (trimmed.StartsWith("@@"))
            {
                isException = true;
                trimmed = trimmed.Substring(2);
            }

            // 3. Domain Anchor: starts with ||
            if (trimmed.StartsWith("||"))
            {
                trimmed = trimmed.Substring(2);
                int endOfDomain = trimmed.IndexOf('^');
                if (endOfDomain < 0) endOfDomain = trimmed.IndexOf('$');

                string domainPart;
                if (endOfDomain >= 0)
                {
                    domainPart = trimmed.Substring(0, endOfDomain).Trim().ToLowerInvariant();
                }
                else
                {
                    domainPart = trimmed.Trim().ToLowerInvariant();
                }

                // DNS-level blocking: if the "domain" contains a slash, it's a URL-path pattern.
                // Extract just the domain portion for DNS blocking.
                int slashIdx = domainPart.IndexOf('/');
                if (slashIdx >= 0)
                {
                    rule.PathPattern = domainPart.Substring(slashIdx);
                    domainPart = domainPart.Substring(0, slashIdx);
                }

                // Skip rules that are pure path patterns with no domain
                if (string.IsNullOrEmpty(domainPart) || !domainPart.Contains('.'))
                {
                    return null;
                }

                // Skip rules that contain wildcards in the domain (can't do DNS-level matching)
                if (domainPart.Contains('*') || domainPart.Contains('?'))
                {
                    return null;
                }

                rule.Domain = domainPart;
                rule.Kind = isException ? RuleKind.DomainAllow : RuleKind.DomainBlock;
                return rule;
            }

            // 4. Plain domain lines (no prefix, no path) — common in domain-list blocklists
            if (!trimmed.Contains('/') && !trimmed.Contains('*') && !trimmed.Contains('$') &&
                !trimmed.Contains('#') && trimmed.Contains('.') && !trimmed.Contains(' '))
            {
                rule.Domain = trimmed.ToLowerInvariant();
                rule.Kind = isException ? RuleKind.DomainAllow : RuleKind.DomainBlock;
                return rule;
            }

            // 5. Standard path/URL pattern (skip for DNS-only blocking)
            return null;
        }

        private static int InterlockedIncrement()
        {
            return System.Threading.Interlocked.Increment(ref _globalRuleId);
        }
    }
}
