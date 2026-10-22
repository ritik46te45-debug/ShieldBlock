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
                if (endOfDomain < 0) endOfDomain = trimmed.IndexOf('/');
                if (endOfDomain < 0) endOfDomain = trimmed.IndexOf('$');

                if (endOfDomain >= 0)
                {
                    rule.Domain = trimmed.Substring(0, endOfDomain).Trim().ToLowerInvariant();
                    if (trimmed.Length > endOfDomain + 1)
                    {
                        rule.PathPattern = trimmed.Substring(endOfDomain + 1);
                    }
                }
                else
                {
                    rule.Domain = trimmed.Trim().ToLowerInvariant();
                }

                rule.Kind = isException ? RuleKind.DomainAllow : RuleKind.DomainBlock;
                return rule;
            }

            // 4. Standard path/URL pattern
            rule.Domain = string.Empty;
            rule.PathPattern = trimmed;
            rule.Kind = isException ? RuleKind.UrlAllow : RuleKind.UrlBlock;

            return rule;
        }

        private static int InterlockedIncrement()
        {
            return System.Threading.Interlocked.Increment(ref _globalRuleId);
        }
    }
}
