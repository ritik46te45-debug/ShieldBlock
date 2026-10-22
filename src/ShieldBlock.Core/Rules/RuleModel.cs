using System;

namespace ShieldBlock.Core.Rules
{
    public enum RuleKind
    {
        DomainBlock,       // ||domain.com^
        DomainAllow,       // @@||domain.com^
        UrlBlock,          // /ads/*
        UrlAllow,          // @@/safe/*
        CosmeticElement,   // domain.com##.ad-banner
        RegexBlock         // /banner\d+/
    }

    public enum RuleAction
    {
        Allow,
        Block,
        CosmeticHide,
        NoMatch
    }

    public class FilterRule
    {
        public int RuleId { get; set; }
        public string RawRule { get; set; } = string.Empty;
        public RuleKind Kind { get; set; }
        public bool IsException => Kind == RuleKind.DomainAllow || Kind == RuleKind.UrlAllow;
        public string Domain { get; set; } = string.Empty;
        public string? PathPattern { get; set; }
        public string? Selector { get; set; }
        public string SourceList { get; set; } = "Default";
    }

    public class MatchResult
    {
        public RuleAction Action { get; set; } = RuleAction.NoMatch;
        public FilterRule? MatchedRule { get; set; }
        public bool IsSimulated { get; set; }
        public long EvaluationTimeMicros { get; set; }

        public bool IsBlocked => Action == RuleAction.Block && !IsSimulated;
        public bool IsAllowed => Action == RuleAction.Allow;

        public static MatchResult Allow(FilterRule rule, bool isSimulated = false, long micros = 0) => new MatchResult
        {
            Action = RuleAction.Allow,
            MatchedRule = rule,
            IsSimulated = isSimulated,
            EvaluationTimeMicros = micros
        };

        public static MatchResult Block(FilterRule rule, bool isSimulated = false, long micros = 0) => new MatchResult
        {
            Action = isSimulated ? RuleAction.NoMatch : RuleAction.Block,
            MatchedRule = rule,
            IsSimulated = isSimulated,
            EvaluationTimeMicros = micros
        };

        public static MatchResult NoRuleMatched(long micros = 0) => new MatchResult
        {
            Action = RuleAction.NoMatch,
            EvaluationTimeMicros = micros
        };
    }
}
