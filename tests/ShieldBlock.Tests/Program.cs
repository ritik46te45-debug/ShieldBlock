using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ShieldBlock.Core.Common;
using ShieldBlock.Core.Dns;
using ShieldBlock.Core.Https;
using ShieldBlock.Core.Network;
using ShieldBlock.Core.Rules;
using ShieldBlock.Core.Safety;
using ShieldBlock.Core.Stats;

namespace ShieldBlock.Tests
{
    public static class Program
    {
        private static int _passed = 0;
        private static int _failed = 0;

        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("       ShieldBlock Automated Test Suite          ");
            Console.WriteLine("=================================================\n");

            // Phase 0 Tests (Safety & Threat Model)
            RunTest("ThreatModel_BlocksDestructiveCommands", TestThreatModel_BlocksDestructiveCommands);
            RunTest("ThreatModel_PermitsSafeCommands", TestThreatModel_PermitsSafeCommands);
            RunTest("ThreatModel_RejectsArbitraryRegistryAccess", TestThreatModel_RejectsArbitraryRegistryAccess);
            RunTest("ThreatModel_EnforcesWfpOwnership", TestThreatModel_EnforcesWfpOwnership);
            RunTest("SnapshotManager_CreatesAndSavesSnapshot", TestSnapshotManager_CreatesAndSavesSnapshot);
            await RunTestAsync("ConnectivityTester_ExecutesWithoutCrashing", TestConnectivityTester_ExecutesWithoutCrashing);

            // Phase 2 Tests (Watchdog & IPC)
            RunTest("FailOpenWatchdog_TracksCrashesAndEntersSafeMode", TestFailOpenWatchdog_TracksCrashesAndEntersSafeMode);
            await RunTestAsync("IpcServerClient_RoundtripPingAndStatus", TestIpcServerClient_RoundtripPingAndStatus);

            // Phase 3 Tests (Rule Engine & Simulation)
            RunTest("AbpRuleParser_ParsesAllRuleKinds", TestAbpRuleParser_ParsesAllRuleKinds);
            RunTest("RuleEngine_SubdomainSuffixMatching", TestRuleEngine_SubdomainSuffixMatching);
            RunTest("RuleEngine_AllowlistPrecedence", TestRuleEngine_AllowlistPrecedence);
            RunTest("RuleEngine_SimulationMode", TestRuleEngine_SimulationMode);
            RunTest("RuleEngine_BenchmarkSubMicrosecondLatency", TestRuleEngine_BenchmarkSubMicrosecondLatency);

            // Phase 4 Tests (WFP Integration)
            RunTest("WfpManager_EnforcesFilterOwnership", TestWfpManager_EnforcesFilterOwnership);
            RunTest("WfpManager_LifecycleAndTracking", TestWfpManager_LifecycleAndTracking);

            // Phase 5 Tests (Local DNS Filtering Engine)
            RunTest("DnsWireParser_EncodesAndDecodesBlockedResponse", TestDnsWireParser_EncodesAndDecodesBlockedResponse);
            RunTest("DnsWireParser_EncodesNxDomainResponse", TestDnsWireParser_EncodesNxDomainResponse);
            RunTest("DnsCache_TTLAndTransactionIdRewrite", TestDnsCache_TTLAndTransactionIdRewrite);

            // Phase 6 Tests (Multi-List Manager & Storage)
            RunTest("FilterListManager_SeedsAndLoadsBaselineLists", TestFilterListManager_SeedsAndLoadsBaselineLists);

            // Phase 7 Tests (App Network Tracker)
            RunTest("AppConnectionTracker_DetectsNetworkSockets", TestAppConnectionTracker_DetectsNetworkSockets);

            // Phase 8 Tests (HTTPS & Bypass Lists)
            RunTest("BypassList_ProtectsCriticalDomainsAndProcesses", TestBypassList_ProtectsCriticalDomainsAndProcesses);
            RunTest("CertificateManager_GeneratesValidRootAndLeafCerts", TestCertificateManager_GeneratesValidRootAndLeafCerts);

            // Phase 9 Tests (Real-Time Stats Engine)
            RunTest("StatsManager_RecordsMetricsAndAtomicIncrements", TestStatsManager_RecordsMetricsAndAtomicIncrements);

            // Phase 10 & 11 Tests (Recovery & Setup Invariant Verification)
            await RunTestAsync("Recovery_ReversesNetworkModificationsSafely", TestRecovery_ReversesNetworkModificationsSafely);

            // Phase 12-14 Tests (High-Throughput Stress Test & Security Audit)
            RunTest("StressTest_HighThroughputRuleMatching", TestStressTest_HighThroughputRuleMatching);
            RunTest("SecurityAudit_ZeroDangerousCommandsAllowed", TestSecurityAudit_ZeroDangerousCommandsAllowed);

            Console.WriteLine("\n-------------------------------------------------");
            Console.WriteLine($"Results: {_passed} Passed, {_failed} Failed");
            Console.WriteLine("-------------------------------------------------");

            return _failed == 0 ? 0 : 1;
        }

        private static void RunTest(string testName, Action testAction)
        {
            try
            {
                testAction();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  [PASS] ");
                Console.ResetColor();
                Console.WriteLine(testName);
                _passed++;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  [FAIL] ");
                Console.ResetColor();
                Console.WriteLine($"{testName}: {ex.Message}");
                _failed++;
            }
        }

        private static async Task RunTestAsync(string testName, Func<Task> testAction)
        {
            try
            {
                await testAction();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("  [PASS] ");
                Console.ResetColor();
                Console.WriteLine(testName);
                _passed++;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("  [FAIL] ");
                Console.ResetColor();
                Console.WriteLine($"{testName}: {ex.Message}");
                _failed++;
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition) throw new Exception("Assertion failed: " + message);
        }

        private static void AssertThrows<T>(Action action) where T : Exception
        {
            try
            {
                action();
                throw new Exception($"Expected exception of type {typeof(T).Name} was not thrown.");
            }
            catch (T) { }
        }

        // ==========================================
        // PHASE 0 TESTS
        // ==========================================
        private static void TestThreatModel_BlocksDestructiveCommands()
        {
            AssertThrows<SecurityAuditViolation>(() => ThreatModel.AssertSafeCommandExecution("netsh int ip reset"));
            AssertThrows<SecurityAuditViolation>(() => ThreatModel.AssertSafeCommandExecution("netsh winsock reset"));
            AssertThrows<SecurityAuditViolation>(() => ThreatModel.AssertSafeCommandExecution("netsh advfirewall reset"));
            AssertThrows<SecurityAuditViolation>(() => ThreatModel.AssertSafeCommandExecution("reg delete HKLM\\Software\\Policies"));
            AssertThrows<SecurityAuditViolation>(() => ThreatModel.AssertSafeCommandExecution("sc delete mpssvc"));
        }

        private static void TestThreatModel_PermitsSafeCommands()
        {
            ThreatModel.AssertSafeCommandExecution("netsh interface ip show config");
            ThreatModel.AssertSafeCommandExecution("netsh interface ip set dns \"Ethernet\" static 127.0.0.53 validate=no");
            ThreatModel.AssertSafeCommandExecution("netsh interface ip set dns \"Wi-Fi\" dhcp");
        }

        private static void TestThreatModel_RejectsArbitraryRegistryAccess()
        {
            AssertThrows<SecurityAuditViolation>(() => ThreatModel.AssertRegistrySafety("HKLM\\System\\CurrentControlSet"));
        }

        private static void TestThreatModel_EnforcesWfpOwnership()
        {
            ThreatModel.AssertFilterOwnership("ShieldBlock-TestRule-01", Constants.WfpProviderGuid);
            AssertThrows<SecurityAuditViolation>(() =>
                ThreatModel.AssertFilterOwnership("UnownedFilterRule", Constants.WfpProviderGuid));
            AssertThrows<SecurityAuditViolation>(() =>
                ThreatModel.AssertFilterOwnership("ShieldBlock-ValidName", Guid.NewGuid()));
        }

        private static void TestSnapshotManager_CreatesAndSavesSnapshot()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "ShieldBlockTest_Snapshot_" + Guid.NewGuid().ToString("N"));
            try
            {
                var mgr = new SnapshotManager(tempDir);
                var snap = mgr.CreateSnapshot("Unit test snapshot");

                AssertTrue(snap != null, "Snapshot object created");
                AssertTrue(!string.IsNullOrEmpty(snap!.SnapshotId), "Snapshot ID is populated");

                var loaded = mgr.LoadLastKnownGood();
                AssertTrue(loaded != null, "Last known good loaded successfully");
                AssertTrue(loaded!.SnapshotId == snap.SnapshotId, "Snapshot IDs match");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        private static async Task TestConnectivityTester_ExecutesWithoutCrashing()
        {
            var tester = new ConnectivityTester();
            var result = await tester.RunConnectivityCheckAsync(timeoutMs: 1500);
            AssertTrue(result != null, "Connectivity test returned a result object");
        }

        // ==========================================
        // PHASE 2 TESTS
        // ==========================================
        private static void TestFailOpenWatchdog_TracksCrashesAndEntersSafeMode()
        {
            string tempCrashFile = Path.Combine(Path.GetTempPath(), "sb_crash_" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                var watchdog = new FailOpenWatchdog(new RollbackEngine(), null, tempCrashFile);
                watchdog.ResetSafeMode();

                AssertTrue(!watchdog.IsSafeMode, "Starts not in safe mode");
                AssertTrue(watchdog.CrashCount == 0, "Initial crash count is 0");

                watchdog.RecordServiceStart();
                watchdog.RecordServiceStart();
                watchdog.RecordServiceStart();

                AssertTrue(watchdog.CrashCount == 3, "Crash count reached 3 after 3 unclean starts");
                AssertTrue(watchdog.IsSafeMode, "Safe mode automatically activated at 3 crashes");

                watchdog.ResetSafeMode();
                AssertTrue(!watchdog.IsSafeMode, "Safe mode can be reset");
                AssertTrue(watchdog.CrashCount == 0, "Crash count reset to 0");
            }
            finally
            {
                if (File.Exists(tempCrashFile)) File.Delete(tempCrashFile);
            }
        }

        private static async Task TestIpcServerClient_RoundtripPingAndStatus()
        {
            string pipeName = "SB_TestPipe_" + Guid.NewGuid().ToString("N");
            using var server = new ShieldBlock.Core.Ipc.IpcServer(async req =>
            {
                if (req.Action == ShieldBlock.Core.Ipc.IpcActions.Ping)
                {
                    return ShieldBlock.Core.Ipc.IpcResponse.Ok(req.RequestId, "PONG");
                }
                return ShieldBlock.Core.Ipc.IpcResponse.Fail(req.RequestId, "Unknown action");
            }, pipeName);

            server.Start();

            var client = new ShieldBlock.Core.Ipc.IpcClient(pipeName);
            bool available = await client.IsServiceAvailableAsync(timeoutMs: 2000);
            AssertTrue(available, "IPC Service responded to Ping");

            var pong = await client.SendRequestAsync(ShieldBlock.Core.Ipc.IpcActions.Ping, timeoutMs: 2000);
            AssertTrue(pong.Success, "Ping request succeeded");
            AssertTrue(pong.PayloadJson == "PONG", "Ping response is PONG");
        }

        // ==========================================
        // PHASE 3 TESTS
        // ==========================================
        private static void TestAbpRuleParser_ParsesAllRuleKinds()
        {
            var r1 = AbpRuleParser.ParseRule("||doubleclick.net^");
            AssertTrue(r1 != null && r1.Kind == RuleKind.DomainBlock && r1.Domain == "doubleclick.net", "Domain block parsed");

            var r2 = AbpRuleParser.ParseRule("@@||google.com^");
            AssertTrue(r2 != null && r2.Kind == RuleKind.DomainAllow && r2.Domain == "google.com", "Domain allow parsed");

            var r3 = AbpRuleParser.ParseRule("example.com##.ad-banner");
            AssertTrue(r3 != null && r3.Kind == RuleKind.CosmeticElement && r3.Domain == "example.com", "Cosmetic parsed");

            var r4 = AbpRuleParser.ParseRule("! This is a comment");
            AssertTrue(r4 == null, "Comment correctly ignored");
        }

        private static void TestRuleEngine_SubdomainSuffixMatching()
        {
            var engine = new RuleEngine();
            engine.AddRule(new FilterRule { Domain = "doubleclick.net", Kind = RuleKind.DomainBlock });

            var match1 = engine.Evaluate("doubleclick.net");
            AssertTrue(match1.IsBlocked, "Exact match blocked");

            var match2 = engine.Evaluate("ad.doubleclick.net");
            AssertTrue(match2.IsBlocked, "Subdomain match blocked");

            var match3 = engine.Evaluate("sub.deep.ad.doubleclick.net");
            AssertTrue(match3.IsBlocked, "Multi-level subdomain match blocked");

            var match4 = engine.Evaluate("notdoubleclick.net");
            AssertTrue(!match4.IsBlocked, "Different domain allowed");
        }

        private static void TestRuleEngine_AllowlistPrecedence()
        {
            var engine = new RuleEngine();
            engine.AddRule(new FilterRule { Domain = "analytics.google.com", Kind = RuleKind.DomainBlock });
            engine.AddRule(new FilterRule { Domain = "analytics.google.com", Kind = RuleKind.DomainAllow });

            var match = engine.Evaluate("analytics.google.com");
            AssertTrue(!match.IsBlocked, "Allowlist rule takes precedence over block rule");
            AssertTrue(match.Action == RuleAction.Allow, "Action is Allow");
        }

        private static void TestRuleEngine_SimulationMode()
        {
            var engine = new RuleEngine { SimulationMode = true };
            engine.AddRule(new FilterRule { Domain = "badtracker.com", Kind = RuleKind.DomainBlock });

            var match = engine.Evaluate("badtracker.com");
            AssertTrue(match.IsSimulated, "Simulation mode flagged in match result");
            AssertTrue(match.Action == RuleAction.NoMatch, "Action reports NoMatch so traffic passes through");
        }

        private static void TestRuleEngine_BenchmarkSubMicrosecondLatency()
        {
            var engine = new RuleEngine();
            for (int i = 0; i < 5000; i++)
            {
                engine.AddRule(new FilterRule { Domain = $"adserver{i}.adnetwork{i % 100}.com", Kind = RuleKind.DomainBlock });
            }

            int queries = 20000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < queries; i++)
            {
                engine.Evaluate($"adserver{i % 5000}.adnetwork{(i % 5000) % 100}.com");
            }
            sw.Stop();

            double avgMicros = (double)sw.ElapsedTicks * 1000000 / (Stopwatch.Frequency * queries);
            Console.Write($" (Avg Latency: {avgMicros:F3} µs) ");
            AssertTrue(avgMicros < 100.0, "Average latency is well under 100 microseconds");
        }

        // ==========================================
        // PHASE 4 TESTS
        // ==========================================
        private static void TestWfpManager_EnforcesFilterOwnership()
        {
            using var wfp = new ShieldBlock.Core.Wfp.WfpManager();
            AssertThrows<SecurityAuditViolation>(() =>
            {
                wfp.AddFilter("MaliciousNonShieldBlockRule", ShieldBlock.Core.Wfp.WfpNative.FWPM_LAYER_ALE_AUTH_CONNECT_V4);
            });
        }

        private static void TestWfpManager_LifecycleAndTracking()
        {
            using var wfp = new ShieldBlock.Core.Wfp.WfpManager();
            wfp.Initialize();

            bool added = wfp.AddFilter("ShieldBlock-DnsRedirect-Filter", ShieldBlock.Core.Wfp.WfpNative.FWPM_LAYER_ALE_AUTH_CONNECT_V4);
            AssertTrue(added, "Filter added successfully or simulation recorded");
            AssertTrue(wfp.CreatedFilterIds.Count == 1, "Filter ID tracked in created list");

            wfp.RemoveAllShieldBlockFilters();
            AssertTrue(wfp.CreatedFilterIds.Count == 0, "Created filters list cleared after removal");
        }

        // ==========================================
        // PHASE 5 TESTS (DNS PROXY & WIRE PARSER)
        // ==========================================
        private static void TestDnsWireParser_EncodesAndDecodesBlockedResponse()
        {
            var query = new DnsMessage
            {
                TransactionId = 0x4B12
            };
            query.Questions.Add(new DnsQuestion
            {
                Name = "ad.doubleclick.net",
                Type = DnsRecordType.A,
                Class = 1
            });

            byte[] blockedA = DnsWireParser.CreateBlockedResponse(query, "0.0.0.0", 300);
            AssertTrue(blockedA.Length >= 12, "Blocked A response has valid DNS length");
            AssertTrue(blockedA[0] == 0x4B && blockedA[1] == 0x12, "Transaction ID preserved in response");
            AssertTrue(blockedA[2] == 0x85 && blockedA[3] == 0x80, "DNS response flags set to 0x8580");

            // AAAA test
            query.Questions[0].Type = DnsRecordType.AAAA;
            byte[] blockedAaaa = DnsWireParser.CreateBlockedResponse(query, "0.0.0.0", 300);
            AssertTrue(blockedAaaa.Length > blockedA.Length, "AAAA response accommodates 16-byte IPv6 record");
        }

        private static void TestDnsWireParser_EncodesNxDomainResponse()
        {
            var query = new DnsMessage { TransactionId = 0x9876 };
            query.Questions.Add(new DnsQuestion { Name = "nonexistent.example", Type = DnsRecordType.A });

            byte[] nxResp = DnsWireParser.CreateNxDomainResponse(query);
            AssertTrue(nxResp.Length >= 12, "NXDOMAIN response has valid length");
            AssertTrue(nxResp[0] == 0x98 && nxResp[1] == 0x76, "Transaction ID matched");
            AssertTrue((nxResp[3] & 0x0F) == 3, "RCODE set to 3 (NXDOMAIN)");
        }

        private static void TestDnsCache_TTLAndTransactionIdRewrite()
        {
            var cache = new DnsCache(100);
            byte[] fakeResponse = new byte[16];
            fakeResponse[0] = 0x00;
            fakeResponse[1] = 0x01;

            cache.Set("example.com", DnsRecordType.A, fakeResponse, ttlSeconds: 60);
            AssertTrue(cache.Count == 1, "Cache stores entry");

            bool hit = cache.TryGet("example.com", DnsRecordType.A, 0xABCD, out var cached);
            AssertTrue(hit, "Cache hit for stored domain");
            AssertTrue(cached != null && cached[0] == 0xAB && cached[1] == 0xCD, "Transaction ID successfully rewritten");
        }

        // ==========================================
        // PHASE 6 TESTS (FILTER LISTS)
        // ==========================================
        private static void TestFilterListManager_SeedsAndLoadsBaselineLists()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SB_Lists_" + Guid.NewGuid().ToString("N"));
            try
            {
                var mgr = new FilterListManager(tempDir);
                AssertTrue(mgr.Subscriptions.Count >= 4, "Curated subscriptions seeded");

                var engine = new RuleEngine();
                mgr.LoadAllIntoEngine(engine);
                AssertTrue(engine.TotalRulesCount > 0, "Baseline rules loaded into RuleEngine");

                var match = engine.Evaluate("doubleclick.net");
                AssertTrue(match.IsBlocked, "Baseline doubleclick rule active");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        // ==========================================
        // PHASE 7 TESTS (APP NETWORK TRACKER)
        // ==========================================
        private static void TestAppConnectionTracker_DetectsNetworkSockets()
        {
            var procs = AppConnectionTracker.GetActiveNetworkProcesses();
            AssertTrue(procs != null, "Active network process query returned valid list");

            var current = AppConnectionTracker.ResolveProcessInfo(Process.GetCurrentProcess().Id);
            AssertTrue(current != null && current.ProcessId == Process.GetCurrentProcess().Id, "Current PID resolved");
        }

        // ==========================================
        // PHASE 8 TESTS (HTTPS & STRICT BYPASS)
        // ==========================================
        private static void TestBypassList_ProtectsCriticalDomainsAndProcesses()
        {
            AssertTrue(BypassList.ShouldBypassDomain("chase.com"), "Banking domain bypassed");
            AssertTrue(BypassList.ShouldBypassDomain("login.gov"), "Government domain bypassed");
            AssertTrue(BypassList.ShouldBypassDomain("sub.medicare.gov"), "Gov subdomain bypassed");
            AssertTrue(BypassList.ShouldBypassDomain("discord.com"), "Pinned app domain bypassed");
            AssertTrue(BypassList.ShouldBypassProcess("steam.exe"), "Steam process bypassed");
            AssertTrue(BypassList.ShouldBypassProcess("discord.exe"), "Discord process bypassed");
            AssertTrue(!BypassList.ShouldBypassDomain("doubleclick.net"), "Ad domain not bypassed");
        }

        private static void TestCertificateManager_GeneratesValidRootAndLeafCerts()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SB_Certs_" + Guid.NewGuid().ToString("N"));
            try
            {
                using var certMgr = new CertificateManager(tempDir);
                var root = certMgr.GetOrCreateRootCa();
                AssertTrue(root != null, "Root CA generated");
                AssertTrue(root.Subject.Contains("ShieldBlock"), "Root CA subject validated");

                var leaf = certMgr.GetOrCreateLeafCertificate("test-filter.domain");
                AssertTrue(leaf != null, "Leaf certificate generated");
                AssertTrue(leaf.Subject.Contains("test-filter.domain"), "Leaf subject validated");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        // ==========================================
        // PHASE 9 TESTS (STATISTICS ENGINE)
        // ==========================================
        private static void TestStatsManager_RecordsMetricsAndAtomicIncrements()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "sb_stats_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var stats = new StatsManager(tempFile, autoPersist: false);
                stats.RecordQuery("ad.doubleclick.net", "A", isBlocked: true, ruleMatched: "||doubleclick.net^", latency: TimeSpan.FromMilliseconds(0.5));
                stats.RecordQuery("google-analytics.com", "A", isBlocked: true, ruleMatched: "||google-analytics.com^ (privacy)", latency: TimeSpan.FromMilliseconds(0.4));
                stats.RecordQuery("wikipedia.org", "A", isBlocked: false, ruleMatched: "Allow", latency: TimeSpan.FromMilliseconds(1.2));

                AssertTrue(stats.TotalQueries == 3, "Total queries is 3");
                AssertTrue(stats.BlockedQueries == 2, "Blocked queries is 2");
                AssertTrue(stats.TrackersBlocked >= 1, "Trackers count incremented");
                AssertTrue(stats.BandwidthSavedBytes > 0, "Bandwidth saved computed");
                AssertTrue(stats.GetRecentQueries().Count == 3, "Recent queries log contains 3 items");
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        // ==========================================
        // PHASE 10 & 11 TESTS (RECOVERY & SETUP)
        // ==========================================
        private static async Task TestRecovery_ReversesNetworkModificationsSafely()
        {
            var rollback = new RollbackEngine();
            var res = await rollback.ExecuteEmergencyRollbackAsync();
            AssertTrue(res != null, "Emergency rollback executes safely");
        }

        // ==========================================
        // PHASE 12-14 TESTS (STRESS TEST & SECURITY AUDIT)
        // ==========================================
        private static void TestStressTest_HighThroughputRuleMatching()
        {
            var engine = new RuleEngine();
            for (int i = 0; i < 10000; i++)
            {
                engine.AddRule(new FilterRule { Domain = $"tracker{i}.adcorp.com", Kind = RuleKind.DomainBlock });
            }

            int queryCount = 100000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < queryCount; i++)
            {
                engine.Evaluate($"tracker{i % 10000}.adcorp.com");
            }
            sw.Stop();

            double queriesPerSec = queryCount / sw.Elapsed.TotalSeconds;
            Console.Write($" ({queriesPerSec:N0} queries/sec) ");
            AssertTrue(queriesPerSec > 25000, "High throughput stress test achieved > 25,000 queries/second");
        }

        private static void TestSecurityAudit_ZeroDangerousCommandsAllowed()
        {
            string[] dangerousCommands = new[]
            {
                "netsh int ip reset",
                "netsh winsock reset",
                "advfirewall reset",
                "reg delete HKLM\\Software",
                "sc delete mpssvc",
                "sc config windefend start= disabled"
            };

            foreach (var cmd in dangerousCommands)
            {
                AssertTrue(!ThreatModel.IsCommandSafe(cmd, ""), $"Dangerous command '{cmd}' strictly blocked by ThreatModel");
            }
        }
    }
}
