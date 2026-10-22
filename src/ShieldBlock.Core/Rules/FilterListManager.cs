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
                        bool hasModernLists = false;
                        foreach (var sub in list)
                        {
                            if (sub.Id == "adblock-core" || sub.Id == "adguard-dns")
                            {
                                hasModernLists = true;
                                break;
                            }
                        }

                        if (hasModernLists)
                        {
                            _subscriptions.AddRange(list);
                            EnsureOfflineDefaults();
                            return;
                        }
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
            _subscriptions.Clear();
            _subscriptions.AddRange(new[]
            {
                new FilterListSubscription
                {
                    Id = "adblock-core",
                    Name = "ShieldBlock Core Ad Protection",
                    Category = "Ads",
                    // Steven Black unified hosts — the gold standard for DNS-level ad blocking
                    Url = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts",
                    Enabled = true,
                    LocalFilePath = Path.Combine(_listsDirectory, "adblock-core.txt")
                },
                new FilterListSubscription
                {
                    Id = "privacy-tracking",
                    Name = "ShieldBlock Privacy & Anti-Tracker",
                    Category = "Privacy",
                    // Peter Lowe's ad and tracking server list (domain format)
                    Url = "https://pgl.yoyo.org/adservers/serverlist.php?hostformat=hosts&showintro=0&mimetype=plaintext",
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
                    Id = "adguard-dns",
                    Name = "AdGuard DNS Filter",
                    Category = "Ads",
                    // AdGuard DNS filter — specifically for DNS-level blocking
                    Url = "https://adguardteam.github.io/HostlistsRegistry/assets/filter_1.txt",
                    Enabled = true,
                    LocalFilePath = Path.Combine(_listsDirectory, "adguard-dns.txt")
                }
            });

            // Ensure baseline offline rules exist immediately
            EnsureOfflineDefaults();
        }

        private void EnsureOfflineDefaults()
        {
            foreach (var sub in _subscriptions)
            {
                bool needsWrite = !File.Exists(sub.LocalFilePath);
                if (!needsWrite)
                {
                    try
                    {
                        var fi = new FileInfo(sub.LocalFilePath);
                        if (fi.Length < 1000) needsWrite = true;
                    }
                    catch { needsWrite = true; }
                }

                if (needsWrite)
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
            sb.AppendLine("# ShieldBlock Offline Baseline");
            sb.AppendLine("# Version: 1.0.0");
            sb.AppendLine("# Description: Curated baseline rules for instant offline protection");
            sb.AppendLine();

            switch (listId)
            {
                case "adblock-core":
                    // Top ad-serving domains in hosts format
                    var adDomains = new[] {
                        // YouTube & Google Ad Networks
                        "doubleclick.net", "ad.doubleclick.net", "pagead2.googlesyndication.com",
                        "googlesyndication.com", "googleadservices.com", "adservice.google.com",
                        "adservice.google.co.in", "adservice.google.co.uk", "adservice.google.de",
                        "pagead-googlehosted.l.google.com", "tpc.googlesyndication.com",
                        "www.googletagservices.com", "securepubads.g.doubleclick.net",
                        "pubads.g.doubleclick.net", "static.doubleclick.net",
                        "googleads.g.doubleclick.net", "stats.g.doubleclick.net",
                        "adclick.g.doubleclick.net", "ad.youtube.com", "ads.youtube.com",
                        "s.youtube.com", "video-stats.l.google.com",
                        // Programmatic Ad Exchanges & SSPs
                        "adnxs.com", "ib.adnxs.com", "adroll.com", "d.adroll.com",
                        "criteo.com", "dis.criteo.com", "cas.criteo.com", "bidder.criteo.com",
                        "taboola.com", "cdn.taboola.com", "trc.taboola.com",
                        "outbrain.com", "amplify.outbrain.com", "widgets.outbrain.com",
                        "rubiconproject.com", "fastlane.rubiconproject.com",
                        "pubmatic.com", "ads.pubmatic.com", "image2.pubmatic.com",
                        "openx.net", "us-u.openx.net", "ox-d.servedbyopenx.com",
                        "casalemedia.com", "www.casalemedia.com", "htl.bid",
                        "smartadserver.com", "zedo.com", "ad.atdmt.com", "adform.net",
                        "advertising.com", "yieldmanager.com", "adbrite.com",
                        "buysellads.com", "mediavine.com", "media.net",
                        "amazon-adsystem.com", "c.amazon-adsystem.com", "aax.amazon-adsystem.com",
                        "popads.net", "propellerads.com", "exoclick.com", "adcash.com",
                        "trafficjunky.com", "juicyads.com", "exponential.com",
                        "revcontent.com", "mgid.com", "sharethrough.com",
                        "ads-twitter.com", "ads.linkedin.com", "an.facebook.com",
                        "adskeeper.com", "ad-delivery.net", "bidswitch.net",
                        "contextweb.com", "districtm.io", "indexexchange.com",
                        "sovrn.com", "lijit.com", "adtech.de", "yieldmo.com",
                        "33across.com", "tribalfusion.com", "conversantmedia.com",
                        "undertone.com", "gumgum.com", "appnexus.com",
                        "spotxchange.com", "springserve.com", "brightcom.com",
                        "ads.yahoo.com", "gemini.yahoo.com",
                        "unityads.unity3d.com", "adcolony.com", "vungle.com",
                        "applovin.com", "mopub.com", "inmobi.com",
                        "supersonicads.com", "chartboost.com", "fyber.com",
                        "ad.turn.com", "mathtag.com", "rlcdn.com",
                        "smaato.net", "startapp.com", "admob.com",
                        "adpushup.com", "adsterra.com", "colossusssp.com"
                    };
                    foreach (var d in adDomains)
                        sb.AppendLine($"0.0.0.0 {d}");
                    break;

                case "privacy-tracking":
                    var trackingDomains = new[] {
                        "google-analytics.com", "analytics.google.com",
                        "www.google-analytics.com", "ssl.google-analytics.com",
                        "hotjar.com", "static.hotjar.com", "script.hotjar.com",
                        "segment.io", "segment.com", "cdn.segment.com", "api.segment.io",
                        "quantserve.com", "pixel.quantserve.com",
                        "telemetry.microsoft.com", "vortex.data.microsoft.com",
                        "metrics.data.microsoft.com", "settings-win.data.microsoft.com",
                        "watson.telemetry.microsoft.com", "watson.microsoft.com",
                        "mixpanel.com", "api.mixpanel.com", "cdn.mxpnl.com",
                        "clarity.ms", "www.clarity.ms",
                        "branch.io", "api.branch.io", "cdn.branch.io",
                        "scorecardresearch.com", "sb.scorecardresearch.com",
                        "appsflyer.com", "t.appsflyer.com",
                        "adjust.com", "app.adjust.com",
                        "amplitude.com", "api.amplitude.com",
                        "mouseflow.com", "fullstory.com", "rs.fullstory.com",
                        "newrelic.com", "bam.nr-data.net", "js-agent.newrelic.com",
                        "bugsnag.com", "sessions.bugsnag.com",
                        "sentry.io", "o0.ingest.sentry.io",
                        "crazyegg.com", "script.crazyegg.com",
                        "inspectlet.com", "cdn.inspectlet.com",
                        "heapanalytics.com", "cdn.heapanalytics.com",
                        "kissmetrics.com", "i.kissmetrics.com",
                        "optimizely.com", "cdn.optimizely.com",
                        "pingdom.net", "freshmarketer.com",
                        "an.yandex.ru", "mc.yandex.ru", "metrika.yandex.ru",
                        "pixel.facebook.com", "connect.facebook.net",
                        "bat.bing.com", "c.bing.com",
                        "ads.linkedin.com", "analytics.twitter.com",
                        "demdex.net", "omtrdc.net", "2o7.net",
                        "bluekai.com", "krxd.net", "exelator.com",
                        "agkn.com", "eyeota.net", "adsrvr.org",
                        "bounceexchange.com", "bouncex.net",
                        "tapad.com", "pippio.com", "adsymptotic.com"
                    };
                    foreach (var d in trackingDomains)
                        sb.AppendLine($"0.0.0.0 {d}");
                    break;

                case "malware-security":
                    sb.AppendLine("0.0.0.0 malware-check.invalid");
                    sb.AppendLine("0.0.0.0 phishing-alert.invalid");
                    sb.AppendLine("0.0.0.0 ransomware-tracker.invalid");
                    sb.AppendLine("0.0.0.0 cryptominer-pool.invalid");
                    sb.AppendLine("0.0.0.0 coin-hive.com");
                    sb.AppendLine("0.0.0.0 coinhive.com");
                    sb.AppendLine("0.0.0.0 crypto-loot.com");
                    sb.AppendLine("0.0.0.0 cryptaloot.pro");
                    break;

                case "adguard-dns":
                    // Additional ad domains not covered above
                    var extraAdDomains = new[] {
                        "video-stats.l.google.com", "r.turn.com",
                        "advertising-api-eu.amazon.com",
                        "aax-us-east.amazon-adsystem.com", "aax-eu.amazon-adsystem.com",
                        "fls-na.amazon-adsystem.com", "mads.amazon-adsystem.com",
                        "adtago.s3.amazonaws.com", "analyticsengine.s3.amazonaws.com",
                        "advice-ads.s3.amazonaws.com",
                        "affiliationjs.s3.amazonaws.com",
                        "widgets.pinterest.com", "log.pinterest.com",
                        "ads.pinterest.com",
                        "static.ads-twitter.com", "syndication.twitter.com",
                        "ib.insightexpressai.com", "mfadsrvr.com",
                        "cdn.fraudlogix.com"
                    };
                    foreach (var d in extraAdDomains)
                        sb.AppendLine($"0.0.0.0 {d}");
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
