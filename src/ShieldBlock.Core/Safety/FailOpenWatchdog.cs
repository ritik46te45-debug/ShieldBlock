using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ShieldBlock.Core.Common;

namespace ShieldBlock.Core.Safety
{
    public class WatchdogState
    {
        public int ConsecutiveCrashes { get; set; }
        public bool InSafeMode { get; set; }
        public DateTime LastStartUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastCleanShutdownUtc { get; set; }
    }

    public class FailOpenWatchdog : IDisposable
    {
        private readonly string _statePath;
        private readonly RollbackEngine _rollbackEngine;
        private readonly ConnectivityTester _connectivityTester;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private WatchdogState _state;
        private Task? _monitorTask;

        public bool IsSafeMode => _state.InSafeMode;
        public int CrashCount => _state.ConsecutiveCrashes;

        public event Action<string>? OnSafeModeEntered;

        public FailOpenWatchdog(RollbackEngine? rollback = null, ConnectivityTester? tester = null, string? statePath = null)
        {
            _rollbackEngine = rollback ?? new RollbackEngine();
            _connectivityTester = tester ?? new ConnectivityTester();
            _statePath = statePath ?? Path.Combine(Constants.ConfigDir, "watchdog_state.json");
            _state = LoadState();
        }

        private WatchdogState LoadState()
        {
            try
            {
                if (File.Exists(_statePath))
                {
                    var json = File.ReadAllText(_statePath);
                    var loaded = JsonSerializer.Deserialize<WatchdogState>(json);
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Watchdog] Warning loading state: {ex.Message}");
            }

            return new WatchdogState();
        }

        private void SaveState()
        {
            try
            {
                var dir = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_statePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Watchdog] Error saving state: {ex.Message}");
            }
        }

        public void RecordServiceStart()
        {
            if (_state.LastCleanShutdownUtc == null || _state.LastCleanShutdownUtc < _state.LastStartUtc)
            {
                _state.ConsecutiveCrashes++;
                Console.WriteLine($"[Watchdog] Detected unclean shutdown. Consecutive crash count: {_state.ConsecutiveCrashes}");
            }

            _state.LastStartUtc = DateTime.UtcNow;
            _state.LastCleanShutdownUtc = null;

            if (_state.ConsecutiveCrashes >= 3)
            {
                TriggerSafeMode("3 consecutive unclean restarts detected.");
            }

            SaveState();
        }

        public void RecordCleanShutdown()
        {
            _state.ConsecutiveCrashes = 0;
            _state.LastCleanShutdownUtc = DateTime.UtcNow;
            SaveState();
        }

        public void StartHealthMonitoring(int intervalSeconds = 10)
        {
            _monitorTask = Task.Run(() => MonitorLoopAsync(intervalSeconds, _cts.Token));
        }

        private async Task MonitorLoopAsync(int intervalSeconds, CancellationToken token)
        {
            await Task.Delay(15000, token);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var connectivity = await _connectivityTester.RunConnectivityCheckAsync(timeoutMs: 3000, token);
                    if (!connectivity.IsOverallSuccess)
                    {
                        Console.WriteLine($"[Watchdog] Network connectivity health check failed: {connectivity.FailureReason}");
                        Console.WriteLine("[Watchdog] Fail-Open Protocol: Rolling back network modifications to preserve user Internet.");

                        await _rollbackEngine.ExecuteEmergencyRollbackAsync();
                        TriggerSafeMode($"Connectivity failure detected: {connectivity.FailureReason}");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Watchdog] Monitoring loop error: {ex.Message}");
                }

                await Task.Delay(intervalSeconds * 1000, token);
            }
        }

        public void TriggerSafeMode(string reason)
        {
            _state.InSafeMode = true;
            SaveState();

            Console.WriteLine($"[Watchdog] SAFE MODE ACTIVATED: {reason}");
            OnSafeModeEntered?.Invoke(reason);
        }

        public void ResetSafeMode()
        {
            _state.InSafeMode = false;
            _state.ConsecutiveCrashes = 0;
            SaveState();
            Console.WriteLine("[Watchdog] Safe Mode reset. Normal filtering re-enabled.");
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
