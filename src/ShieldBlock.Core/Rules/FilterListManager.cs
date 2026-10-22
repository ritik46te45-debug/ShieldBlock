using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ShieldBlock.Core.Common;

namespace ShieldBlock.Core.Rules
{
    public class FilterListManager
    {
        private readonly string _listsDirectory;
        private readonly string _manifestPath;
        private readonly HttpClient _httpClient;
        private readonly List<FilterListSubscription> _subscriptions = new();

        public IReadOnlyList<FilterListSubscription> Subscriptions => _subscriptions.AsReadOnly();

        public FilterListManager(string? listsDirectory = null, HttpClient? httpClient = null)
        {
            _listsDirectory = listsDirectory ?? Path.Combine(Constants.ProgramDataDir, "data", "lists");
            Directory.CreateDirectory(_listsDirectory);
            _manifestPath = Path.Combine(_listsDirectory, "subscriptions.json");

            _httpClient = httpClient ?? new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            LoadSubscriptions();
        }

        private void LoadSubscriptions()
        {
            _subscriptions.Clear();

            if (File.Exists(_manifestPath))
            {
                try
                {
                    string json = File.ReadAllText(_manifestPath);
                    var list = JsonSerializer.Deserialize<List<FilterListSubscription>>(json);
                    if (list != null && list.Count > 0)
                    {
                        _subscriptions.AddRange(list);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FilterListManager] Warning: Failed to read subscriptions manifest: {ex.Message}");
                }
            }

            // Seed default curated filter lists
            SeedDefaultSubscriptions();
            SaveSubscriptions();
        }

        public void SaveSubscriptions()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_subscriptions, options);
                string tmpPath = _manifestPath + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, _manifestPath, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FilterListManager] Error saving subscriptions manifest: {ex.Message}");
            }
        }

        private void SeedDefaultSubscriptions()
        {
            _subscriptions.AddRange(new[]
            {
                new FilterListSubscription
                {
                    Id = "adblock-core",
                    Name = "ShieldBlock Core Ad Protection",
                    Category = "Ads",
                    Url = "https://raw.githubusercontent.com/AdguardTeam/AdguardFilters/master/BaseFilter/sections/adservers.txt",
                    Enabled = true,
                    LocalFilePath = Path.Combine(_listsDirectory, "adblock-core.txt")
                },
                new FilterListSubscription
                {
                    Id = "privacy-tracking",
                    Name = "ShieldBlock Privacy & Anti-Tracker",
                    Category = "Privacy",
                    Url = "https://raw.githubusercontent.com/AdguardTeam/AdguardFilters/master/SpywareFilter/sections/tracking_servers.txt",
                    Enabled = true,
                    LocalFilePath = Path.Combine(_listsDirectory, "privacy-tracking.txt")
                },
                new FilterListSubscription
                {
                    Id = "malware-security",
                    Name = "ShieldBlock Malware & Phishing Defense",
                    Category = "Security",
                    Url = "https://urlhaus.abuse.ch/downloads/hostfile/",
                    Enabled = true,
                    LocalFilePath = Path.Combine(_listsDirectory, "malware-security.txt")
                },
                new FilterListSubscription
                {
                    Id = "youtube-annoyances",
                    Name = "ShieldBlock YouTube & Media Ad Rules",
                    Category = "Annoyances",
                    Url = "https://raw.githubusercontent.com/AdguardTeam/AdguardFilters/master/AnnoyancesFilter/sections/popups.txt",
                    Enabled = true,
                    LocalFilePath = Path.Combine(_listsDirectory, "youtube-annoyances.txt")
                }
            });

            // Ensure baseline offline rules exist immediately
            EnsureOfflineDefaults();
        }

        private void EnsureOfflineDefaults()
        {
            foreach (var sub in _subscriptions)
            {
                if (!File.Exists(sub.LocalFilePath))
                {
                    string baselineContent = GenerateBaselineRules(sub.Id);
                    File.WriteAllText(sub.LocalFilePath, baselineContent);
                    sub.RuleCount = CountRulesInFile(sub.LocalFilePath);
                    sub.LastUpdated = DateTime.UtcNow;
                }
            }
        }

        private string GenerateBaselineRules(string listId)
        {
            var sb = new StringBuilder();
            sb.AppendLine("! Title: ShieldBlock Offline Baseline");
            sb.AppendLine("! Version: 1.0.0");
            sb.AppendLine("! Description: Curated baseline rules for instant offline protection");
            sb.AppendLine();

            switch (listId)
            {
                case "adblock-core":
                    sb.AppendLine("||doubleclick.net^");
                    sb.AppendLine("||googlesyndication.com^");
                    sb.AppendLine("||googleadservices.com^");
                    sb.AppendLine("||adnxs.com^");
                    sb.AppendLine("||adroll.com^");
                    sb.AppendLine("||criteo.com^");
                    sb.AppendLine("||taboola.com^");
                    sb.AppendLine("||outbrain.com^");
                    sb.AppendLine("||rubiconproject.com^");
                    sb.AppendLine("||pubmatic.com^");
                    sb.AppendLine("||scorecardresearch.com^");
                    sb.AppendLine("||casalemedia.com^");
                    sb.AppendLine("||openx.net^");
                    sb.AppendLine("||smartadserver.com^");
                    sb.AppendLine("||zedo.com^");
                    break;

                case "privacy-tracking":
                    sb.AppendLine("||google-analytics.com^");
                    sb.AppendLine("||analytics.google.com^");
                    sb.AppendLine("||hotjar.com^");
                    sb.AppendLine("||segment.io^");
                    sb.AppendLine("||segment.com^");
                    sb.AppendLine("||quantserve.com^");
                    sb.AppendLine("||telemetry.microsoft.com^");
                    sb.AppendLine("||vortex.data.microsoft.com^");
                    sb.AppendLine("||metrics.data.microsoft.com^");
                    sb.AppendLine("||mixpanel.com^");
                    sb.AppendLine("||clarity.ms^");
                    sb.AppendLine("||branch.io^");
                    break;

                case "malware-security":
                    sb.AppendLine("||malware-check.invalid^");
                    sb.AppendLine("||phishing-alert.invalid^");
                    sb.AppendLine("||ransomware-tracker.invalid^");
                    sb.AppendLine("||cryptominer-pool.invalid^");
                    break;

                case "youtube-annoyances":
                    sb.AppendLine("||youtube.com/api/stats/ads^");
                    sb.AppendLine("||youtube.com/pagead/*^");
                    sb.AppendLine("||googlevideo.com/videoplayback?*&oad=*^");
                    sb.AppendLine("||youtube.com/get_video_info?*&adformat=*^");
                    sb.AppendLine("||youtube.com/ptracking^*");
                    sb.AppendLine("||s.youtube.com/api/stats^*");
                    break;
            }

            return sb.ToString();
        }

        public async Task<List<FilterListUpdateResult>> UpdateAllListsAsync(CancellationToken cancellationToken = default)
        {
            var results = new List<FilterListUpdateResult>();
            foreach (var sub in _subscriptions)
            {
                if (!sub.Enabled) continue;
                var res = await UpdateListAsync(sub, cancellationToken);
                results.Add(res);
            }
            SaveSubscriptions();
            return results;
        }

        public async Task<FilterListUpdateResult> UpdateListAsync(FilterListSubscription sub, CancellationToken cancellationToken = default)
        {
            var result = new FilterListUpdateResult { ListId = sub.Id };

            if (string.IsNullOrWhiteSpace(sub.Url))
            {
                result.Success = false;
                result.ErrorMessage = "No URL specified.";
                return result;
            }

            string tmpFile = sub.LocalFilePath + ".download.tmp";

            try
            {
                using var response = await _httpClient.GetAsync(sub.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    result.Success = false;
                    result.ErrorMessage = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                    return result;
                }

                // Download to temporary file
                using (var fs = new FileStream(tmpFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await response.Content.CopyToAsync(fs, cancellationToken);
                }

                var fileInfo = new FileInfo(tmpFile);
                if (fileInfo.Length < 100)
                {
                    File.Delete(tmpFile);
                    result.Success = false;
                    result.ErrorMessage = "Downloaded list is too small or empty (< 100 bytes). Retaining previous version.";
                    return result;
                }

                // Compute SHA-256 to check for changes
                string newHash = ComputeSha256(tmpFile);
                if (newHash == sub.Sha256Hash && File.Exists(sub.LocalFilePath))
                {
                    File.Delete(tmpFile);
                    result.Success = true;
                    result.Changed = false;
                    result.RulesLoaded = sub.RuleCount;
                    return result;
                }

                // Count rules and verify parseability
                int rulesCount = CountRulesInFile(tmpFile);
                if (rulesCount == 0 && File.Exists(sub.LocalFilePath))
                {
                    File.Delete(tmpFile);
                    result.Success = false;
                    result.ErrorMessage = "Parsed rule count is 0. Safeguard kept previous valid list.";
                    return result;
                }

                // Atomic replace
                File.Move(tmpFile, sub.LocalFilePath, true);
                sub.Sha256Hash = newHash;
                sub.RuleCount = rulesCount;
                sub.LastUpdated = DateTime.UtcNow;

                result.Success = true;
                result.Changed = true;
                result.RulesLoaded = rulesCount;
            }
            catch (Exception ex)
            {
                if (File.Exists(tmpFile))
                {
                    try { File.Delete(tmpFile); } catch { }
                }
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        public void LoadAllIntoEngine(RuleEngine engine)
        {
            int total = 0;
            foreach (var sub in _subscriptions)
            {
                if (!sub.Enabled || !File.Exists(sub.LocalFilePath)) continue;

                try
                {
                    var lines = File.ReadAllLines(sub.LocalFilePath);
                    int count = engine.LoadRules(lines, sub.Name);
                    sub.RuleCount = count;
                    total += count;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FilterListManager] Error loading list '{sub.Name}': {ex.Message}");
                }
            }
            Console.WriteLine($"[FilterListManager] Successfully compiled and loaded {total} active filtering rules into RuleEngine.");
        }

        private static int CountRulesInFile(string filePath)
        {
            if (!File.Exists(filePath)) return 0;
            int count = 0;
            foreach (var line in File.ReadLines(filePath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("!") || trimmed.StartsWith("[")) continue;
                count++;
            }
            return count;
        }

        private static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
