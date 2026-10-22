using System;
using System.Collections.Generic;

namespace ShieldBlock.Core.Safety
{
    public enum ThreatSeverity
    {
        Informational,
        Low,
        Medium,
        High,
        Critical
    }

    public enum SystemInvariant
    {
        ZeroArbitraryRegistryAccess,
        FailOpenInternetAccess,
        NoKernelDriversAllowed,
        WindowsDefenderUntouched,
        WindowsFirewallUntouched,
        StrictWfpFilterOwnership,
        AtomicConfigurationRollback,
        LocalOnlyIpcSecurity,
        ZeroRemoteTelemetry
    }

    public class SecurityAuditViolation : Exception
    {
        public SystemInvariant Invariant { get; }
        public ThreatSeverity Severity { get; }

        public SecurityAuditViolation(SystemInvariant invariant, ThreatSeverity severity, string message)
            : base($"[SECURITY AUDIT VIOLATION] {invariant} ({severity}): {message}")
        {
            Invariant = invariant;
            Severity = severity;
        }
    }

    public static class ThreatModelValidator
    {
        private static readonly HashSet<string> BannedExecutableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bcdedit.exe",
            "diskpart.exe",
            "format.com"
        };

        private static readonly HashSet<string> BannedCommandPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "netsh int ip reset",
            "netsh winsock reset",
            "advfirewall reset",
            "reg delete",
            "reg add hklm\\system",
            "sc delete mpssvc",
            "sc config windefend"
        };

        public static void AssertSafeCommandExecution(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) return;

            foreach (var banned in BannedCommandPatterns)
            {
                if (commandLine.IndexOf(banned, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new SecurityAuditViolation(
                        SystemInvariant.ZeroArbitraryRegistryAccess,
                        ThreatSeverity.Critical,
                        $"Prohibited system-destructive command attempt detected: '{commandLine}'");
                }
            }
        }

        public static void AssertRegistrySafety(string? keyPath)
        {
            if (string.IsNullOrWhiteSpace(keyPath)) return;

            // ShieldBlock must NEVER write to arbitrary registry keys.
            // All application config MUST be stored in %ProgramData% or %AppData%.
            throw new SecurityAuditViolation(
                SystemInvariant.ZeroArbitraryRegistryAccess,
                ThreatSeverity.Critical,
                $"Direct registry write blocked. ShieldBlock stores all state in JSON/SQLite. Target: {keyPath}");
        }

        public static bool IsCommandSafe(string command, string arguments)
        {
            string full = $"{command} {arguments}".Trim();
            foreach (var banned in BannedCommandPatterns)
            {
                if (full.IndexOf(banned, StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }
            return true;
        }

        public static void AssertFilterOwnership(string ruleName, Guid providerGuid)
        {
            if (!ruleName.StartsWith(Common.Constants.WfpRulePrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityAuditViolation(
                    SystemInvariant.StrictWfpFilterOwnership,
                    ThreatSeverity.High,
                    $"Attempted to manipulate non-ShieldBlock WFP filter: {ruleName}");
            }

            if (providerGuid != Common.Constants.WfpProviderGuid)
            {
                throw new SecurityAuditViolation(
                    SystemInvariant.StrictWfpFilterOwnership,
                    ThreatSeverity.High,
                    $"Attempted to register WFP filter under non-ShieldBlock Provider GUID: {providerGuid}");
            }
        }
    }

    public static class ThreatModel
    {
        public static bool IsCommandSafe(string command, string arguments) => ThreatModelValidator.IsCommandSafe(command, arguments);
        public static void AssertSafeCommandExecution(string commandLine) => ThreatModelValidator.AssertSafeCommandExecution(commandLine);
        public static void AssertRegistrySafety(string? keyPath) => ThreatModelValidator.AssertRegistrySafety(keyPath);
        public static void AssertFilterOwnership(string ruleName, Guid providerGuid) => ThreatModelValidator.AssertFilterOwnership(ruleName, providerGuid);
    }
}
