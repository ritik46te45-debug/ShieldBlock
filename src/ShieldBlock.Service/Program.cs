using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ShieldBlock.Core.Common;
using ShieldBlock.Core.Config;
using ShieldBlock.Core.Dns;
using ShieldBlock.Core.Ipc;
using ShieldBlock.Core.Rules;
using ShieldBlock.Core.Safety;
using ShieldBlock.Core.Stats;
using ShieldBlock.Core.Wfp;

namespace ShieldBlock.Service
{
    public class Program
    {
        private static ConfigManager _configManager = null!;
        private static ServiceSettings _settings = null!;
        private static FailOpenWatchdog _watchdog = null!;
        private static IpcServer _ipcServer = null!;
        private static RollbackEngine _rollbackEngine = null!;
        private static RuleEngine _ruleEngine = null!;
        private static FilterListManager _filterManager = null!;
        private static StatsManager _statsManager = null!;
        private static DnsProxyServer _dnsProxy = null!;
        private static WfpManager _wfpManager = null!;
        private static AdapterDnsConfigurator _dnsConfigurator = null!;

        private static readonly ManualResetEvent _shutdownEvent = new ManualResetEvent(false);
        private static readonly DateTime _startTimeUtc = DateTime.UtcNow;

        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine($"   {Constants.ServiceDisplayName} v{Constants.Version}   ");
            Console.WriteLine("   Fail-Open Network Protection & Privacy Guard  ");
            Console.WriteLine("=================================================\n");

            // 1. Initialize Configuration & Core Safety
            _configManager = new ConfigManager();
            _settings = _configManager.LoadServiceSettings();
            _rollbackEngine = new RollbackEngine();
            _watchdog = new FailOpenWatchdog(_rollbackEngine);
            _watchdog.RecordServiceStart();

            _statsManager = new StatsManager();
            _ruleEngine = new RuleEngine();
            _filterManager = new FilterListManager();

            // 2. Load filtering rules into memory
            Console.WriteLine("[Service] Compiling curated filter lists...");
            _filterManager.LoadAllIntoEngine(_ruleEngine);

            // 3. Initialize DNS Proxy Server
            _dnsProxy = new DnsProxyServer(_ruleEngine, "127.0.0.53", 53);
            _dnsProxy.OnQueryProcessed += (s, e) =>
            {
                _statsManager.RecordQuery(
                    e.Domain,
                    e.Type.ToString(),
                    e.IsBlocked,
                    e.RuleMatched,
                    e.Latency,
                    e.ClientIp);
            };

            // 4. Initialize WFP Manager & Adapter Configurator
            _wfpManager = new WfpManager();
            _wfpManager.Initialize();
            _dnsConfigurator = new AdapterDnsConfigurator(new SnapshotManager(), _rollbackEngine);

            // 5. Watchdog Safe-Mode event handler
            _watchdog.OnSafeModeEntered += reason =>
            {
                Console.WriteLine($"[Service] ENTERING SAFE MODE: {reason}");
                _settings.SafeModeActive = true;
                _settings.ProtectionEnabled = false;
                _configManager.SaveServiceSettings(_settings);

                // Revert all filtering immediately
                _dnsProxy.Stop();
                _ = _dnsConfigurator.RevertDnsAsync();
                _wfpManager.RemoveAllFilters();
            };

            // Start Watchdog background health checks
            _watchdog.StartHealthMonitoring(intervalSeconds: 15);

            // 6. Start DNS Proxy and Protection if enabled
            if (_settings.ProtectionEnabled && !_watchdog.IsSafeMode)
            {
                StartProtection();
            }

            // 7. Start IPC Server
            _ipcServer = new IpcServer(HandleIpcRequestAsync);
            _ipcServer.Start();
            Console.WriteLine($"[Service] Local IPC Server listening on {Constants.IpcFullPipeName}");

            // 8. Register termination handlers
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\n[Service] Termination signal received. Commencing safe shutdown...");
                _shutdownEvent.Set();
            };

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                PerformSafeShutdown();
            };

            Console.WriteLine("[Service] ShieldBlock Engine active and guarding network.");
            Console.WriteLine("[Service] Press Ctrl+C to stop service safely.\n");

            _shutdownEvent.WaitOne();

            PerformSafeShutdown();
            return 0;
        }

        private static void StartProtection()
        {
            try
            {
                Console.WriteLine("[Service] Activating DNS Proxy Server...");
                _dnsProxy.Start();

                // Configure adapter DNS asynchronously
                Task.Run(async () =>
                {
                    await _dnsConfigurator.ConfigureDnsProxyAsync("127.0.0.53");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] Protection activation error: {ex.Message}");
            }
        }

        private static void StopProtection()
        {
            try
            {
                Console.WriteLine("[Service] Deactivating DNS Proxy Server & Reverting DNS...");
                _dnsProxy.Stop();
                Task.Run(async () =>
                {
                    await _dnsConfigurator.RevertDnsAsync();
                });
                _wfpManager.RemoveAllFilters();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] Protection deactivation error: {ex.Message}");
            }
        }

        private static async Task<IpcResponse> HandleIpcRequestAsync(IpcRequest request)
        {
            try
            {
                switch (request.Action)
                {
                    case IpcActions.Ping:
                        return IpcResponse.Ok(request.RequestId, "PONG");

                    case IpcActions.GetStatus:
                        var status = new ServiceStatusDto
                        {
                            ProtectionEnabled = _settings.ProtectionEnabled && !_watchdog.IsSafeMode,
                            SafeModeActive = _watchdog.IsSafeMode,
                            FailOpenMode = _settings.FailOpenMode,
                            ConsecutiveCrashes = _watchdog.CrashCount,
                            DnsEngineRunning = _dnsProxy.IsRunning,
                            WfpFiltersActive = _settings.ProtectionEnabled,
                            HttpsFilteringActive = _settings.HttpsFilteringEnabled,
                            RulesLoadedCount = _ruleEngine.LoadedRuleCount,
                            TotalQueriesFiltered = _statsManager.TotalQueries,
                            TotalAdsBlocked = _statsManager.BlockedQueries,
                            TrackersBlocked = _statsManager.TrackersBlocked,
                            MalwareBlocked = _statsManager.MalwareBlocked,
                            BandwidthSavedBytes = _statsManager.BandwidthSavedBytes,
                            AverageLatencyMs = _statsManager.AverageLatencyMs,
                            UptimeStartUtc = _startTimeUtc
                        };
                        return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(status));

                    case IpcActions.SetProtection:
                        if (bool.TryParse(request.PayloadJson, out bool enable))
                        {
                            _settings.ProtectionEnabled = enable;
                            _configManager.SaveServiceSettings(_settings);

                            if (enable)
                            {
                                StartProtection();
                            }
                            else
                            {
                                StopProtection();
                            }

                            Console.WriteLine($"[Service] Protection state updated: {enable}");
                            return IpcResponse.Ok(request.RequestId, enable.ToString());
                        }
                        return IpcResponse.Fail(request.RequestId, "Invalid boolean payload.");

                    case IpcActions.EmergencyDisable:
                        Console.WriteLine("[Service] EMERGENCY DISABLE requested via IPC!");
                        _settings.ProtectionEnabled = false;
                        _configManager.SaveServiceSettings(_settings);
                        StopProtection();
                        await _rollbackEngine.ExecuteEmergencyRollbackAsync();
                        return IpcResponse.Ok(request.RequestId, "Emergency disable executed. Network restored.");

                    case IpcActions.TriggerRollback:
                        Console.WriteLine("[Service] TriggerRollback requested via IPC.");
                        StopProtection();
                        var rollRes = await _rollbackEngine.ExecuteEmergencyRollbackAsync();
                        return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(rollRes));

                    case IpcActions.ReloadRules:
                        Console.WriteLine("[Service] Reloading rule databases...");
                        _ruleEngine.Clear();
                        _filterManager.LoadAllIntoEngine(_ruleEngine);
                        return IpcResponse.Ok(request.RequestId, $"Reloaded {_ruleEngine.LoadedRuleCount} rules.");

                    case IpcActions.GetMetrics:
                        var stats = _statsManager.GetSnapshot();
                        return IpcResponse.Ok(request.RequestId, JsonSerializer.Serialize(stats));

                    default:
                        return IpcResponse.Fail(request.RequestId, $"Unknown action: {request.Action}");
                }
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail(request.RequestId, $"Execution error: {ex.Message}");
            }
        }

        private static void PerformSafeShutdown()
        {
            try
            {
                Console.WriteLine("[Service] Performing safe teardown and recording clean shutdown...");
                _dnsProxy?.Stop();
                _dnsProxy?.Dispose();
                _wfpManager?.RemoveAllFilters();
                _wfpManager?.Dispose();
                _ipcServer?.Dispose();
                _statsManager?.SaveStats();
                _watchdog?.RecordCleanShutdown();
                _watchdog?.Dispose();
                Console.WriteLine("[Service] Clean shutdown completed. Goodbye.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] Shutdown warning: {ex.Message}");
            }
        }
    }
}
