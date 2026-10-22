using System;
using System.Collections.Generic;

namespace ShieldBlock.Core.Rules
{
    public class FilterListSubscription
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public int RuleCount { get; set; } = 0;
        public DateTime LastUpdated { get; set; } = DateTime.MinValue;
        public string Sha256Hash { get; set; } = string.Empty;
        public string LocalFilePath { get; set; } = string.Empty;
    }

    public class FilterListUpdateResult
    {
        public string ListId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public int RulesLoaded { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public bool Changed { get; set; }
    }
}
