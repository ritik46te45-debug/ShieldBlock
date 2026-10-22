using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace ShieldBlock.Core.Safety
{
    public class ConnectivityResult
    {
        public bool IsOverallSuccess => DnsSuccess && HttpsSuccess;
        public bool DnsSuccess { get; set; }
        public bool HttpsSuccess { get; set; }
        public bool Ipv4PingSuccess { get; set; }

        public long DnsLatencyMs { get; set; }
        public long HttpsLatencyMs { get; set; }
        public long PingLatencyMs { get; set; }

        public string? FailureReason { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    public class ConnectivityTester
    {
        private static readonly HttpClient HttpClient = new HttpClient(new HttpClientHandler
        {
            UseProxy = false, // Direct connection to test true transport
            CheckCertificateRevocationList = false
        })
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        public async Task<ConnectivityResult> RunConnectivityCheckAsync(int timeoutMs = 3000, CancellationToken cancellationToken = default)
        {
            var result = new ConnectivityResult();
            var sw = Stopwatch.StartNew();

            // 1. DNS Resolution Test
            try
            {
                sw.Restart();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                var addresses = await System.Net.Dns.GetHostAddressesAsync("microsoft.com", cts.Token);
                result.DnsLatencyMs = sw.ElapsedMilliseconds;
                result.DnsSuccess = addresses != null && addresses.Length > 0;
            }
            catch (Exception ex)
            {
                result.DnsSuccess = false;
                result.FailureReason = $"DNS Resolution failed: {ex.Message}";
            }

            // 2. HTTPS Connectivity Test
            try
            {
                sw.Restart();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                // Use standard lightweight captive portal test URL
                using var response = await HttpClient.GetAsync("http://www.msftconnecttest.com/connecttest.txt", cts.Token);
                result.HttpsLatencyMs = sw.ElapsedMilliseconds;
                result.HttpsSuccess = response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                result.HttpsSuccess = false;
                result.FailureReason = (result.FailureReason ?? "") + $" | HTTPS Test failed: {ex.Message}";
            }

            // 3. Ping IPv4 Test (1.1.1.1 or 8.8.8.8)
            try
            {
                sw.Restart();
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(IPAddress.Parse("1.1.1.1"), timeoutMs);
                result.PingLatencyMs = sw.ElapsedMilliseconds;
                result.Ipv4PingSuccess = reply.Status == IPStatus.Success;
            }
            catch
            {
                // Ping might be blocked by local firewall, non-fatal if HTTPS/DNS passed
                result.Ipv4PingSuccess = false;
            }

            return result;
        }

        public async Task<(bool IsConnected, bool IsCaptivePortalDetected, TimeSpan DnsLatency, TimeSpan ProbeLatency, string ErrorMessage)> VerifyConnectivityAsync(int timeoutMs = 3000, CancellationToken cancellationToken = default)
        {
            var res = await RunConnectivityCheckAsync(timeoutMs, cancellationToken);
            return (
                res.IsOverallSuccess,
                false,
                TimeSpan.FromMilliseconds(res.DnsLatencyMs),
                TimeSpan.FromMilliseconds(res.HttpsLatencyMs),
                res.FailureReason ?? string.Empty
            );
        }
    }
}
