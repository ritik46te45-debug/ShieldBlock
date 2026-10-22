using System;
using System.Collections.Generic;

namespace ShieldBlock.Core.Https
{
    public static class BypassList
    {
        // Domains that MUST NEVER be decrypted or intercepted under any circumstances
        private static readonly HashSet<string> _criticalBypassDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            // Windows System & Security Update
            "windowsupdate.microsoft.com",
            "update.microsoft.com",
            "delivery.mp.microsoft.com",
            "wdcp.microsoft.com",
            "definitionupdates.microsoft.com",
            "smartscreen.microsoft.com",
            "login.live.com",
            "login.microsoftonline.com",

            // Apple & Google Accounts
            "appleid.apple.com",
            "accounts.google.com",
            "oauth2.googleapis.com",

            // Major Banking & Financial Services
            "chase.com",
            "bankofamerica.com",
            "wellsfargo.com",
            "citigroup.com",
            "citi.com",
            "capitalone.com",
            "paypal.com",
            "stripe.com",
            "square.com",
            "usbank.com",
            "pnc.com",
            "fidelity.com",
            "schwab.com",
            "vanguard.com",
            "coinbase.com",
            "binance.com",

            // Government & Healthcare
            "irs.gov",
            "login.gov",
            "ssa.gov",
            "medicare.gov",
            "healthcare.gov",

            // Certificate-pinned applications
            "discord.gg",
            "discord.com",
            "gateway.discord.gg",
            "steamcommunity.com",
            "steampowered.com",
            "epicgames.com",
            "spotify.com",
            "whatsapp.net",
            "web.whatsapp.com",
            "telegram.org",
            "signal.org"
        };

        // Processes that must be bypassed entirely
        private static readonly HashSet<string> _criticalBypassProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "steam",
            "discord",
            "spotify",
            "epicgameslauncher",
            "whatsapp",
            "signal",
            "msedge", // Optional: allow user to select
            "explorer",
            "svchost",
            "msmpeng" // Windows Defender Antivirus
        };

        public static bool ShouldBypassDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain)) return true;
            domain = domain.ToLowerInvariant().Trim();

            if (_criticalBypassDomains.Contains(domain)) return true;

            // Check subdomain matches for banking/gov
            if (domain.EndsWith(".gov") || domain.EndsWith(".mil")) return true;

            foreach (var bp in _criticalBypassDomains)
            {
                if (domain.EndsWith("." + bp)) return true;
            }

            return false;
        }

        public static bool ShouldBypassProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;
            string cleanName = System.IO.Path.GetFileNameWithoutExtension(processName).ToLowerInvariant();
            return _criticalBypassProcesses.Contains(cleanName);
        }
    }
}
