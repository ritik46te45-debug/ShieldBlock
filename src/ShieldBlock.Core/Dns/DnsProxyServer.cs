using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ShieldBlock.Core.Rules;

namespace ShieldBlock.Core.Dns
{
    public class DnsQueryEventArgs : EventArgs
    {
        public string Domain { get; set; } = string.Empty;
        public DnsRecordType Type { get; set; }
        public bool IsBlocked { get; set; }
        public string RuleMatched { get; set; } = string.Empty;
        public TimeSpan Latency { get; set; }
        public string ClientIp { get; set; } = string.Empty;
    }

    public class DnsProxyServer : IDisposable
    {
        private readonly IPAddress _listenIp;
        private readonly int _port;
        private readonly RuleEngine _ruleEngine;
        private readonly DnsCache _dnsCache;
        private readonly DohClient _dohClient;

        private UdpClient? _udpListener;
        private TcpListener? _tcpListener;
        private CancellationTokenSource? _cts;
        private Task? _udpTask;
        private Task? _tcpTask;
        private bool _isRunning;

        public event EventHandler<DnsQueryEventArgs>? OnQueryProcessed;

        public bool IsRunning => _isRunning;
        public DnsCache Cache => _dnsCache;

        public DnsProxyServer(
            RuleEngine ruleEngine,
            string listenIp = "127.0.0.1",
            int port = 53,
            DnsCache? dnsCache = null,
            DohClient? dohClient = null)
        {
            _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
            _listenIp = IPAddress.Parse(listenIp);
            _port = port;
            _dnsCache = dnsCache ?? new DnsCache();
            _dohClient = dohClient ?? new DohClient();
        }

        public bool Start()
        {
            if (_isRunning) return true;

            try
            {
                _cts = new CancellationTokenSource();
                var endpoint = new IPEndPoint(IPAddress.Any, _port);

                // Start UDP listener
                try
                {
                    _udpListener = new UdpClient(endpoint);
                    _udpListener.Client.ReceiveBufferSize = 1024 * 1024; // 1 MB buffer
                    _udpListener.Client.SendBufferSize = 1024 * 1024;
                }
                catch (SocketException)
                {
                    _udpListener = new UdpClient(new IPEndPoint(IPAddress.Loopback, _port));
                }

                // Start TCP listener
                try
                {
                    _tcpListener = new TcpListener(endpoint);
                    _tcpListener.Start(100);
                }
                catch (SocketException)
                {
                    _tcpListener = new TcpListener(new IPEndPoint(IPAddress.Loopback, _port));
                    _tcpListener.Start(100);
                }

                _isRunning = true;
                _udpTask = Task.Run(() => ListenUdpAsync(_cts.Token));
                _tcpTask = Task.Run(() => ListenTcpAsync(_cts.Token));

                Console.WriteLine($"[DnsProxyServer] Active on {_listenIp}:{_port}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DnsProxyServer] Failed to start: {ex.Message}");
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;

            try
            {
                _cts?.Cancel();
                _udpListener?.Close();
                _tcpListener?.Stop();
            }
            catch { }

            _udpListener = null;
            _tcpListener = null;
            _cts?.Dispose();
            _cts = null;
            Console.WriteLine("[DnsProxyServer] Stopped.");
        }

        private async Task ListenUdpAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _udpListener != null)
            {
                try
                {
                    var result = await _udpListener.ReceiveAsync(token);
                    _ = Task.Run(async () =>
                    {
                        var start = DateTime.UtcNow;
                        var response = await ProcessDnsPacketAsync(result.Buffer, result.RemoteEndPoint.Address.ToString());
                        if (response != null && _udpListener != null)
                        {
                            try
                            {
                                await _udpListener.SendAsync(response, response.Length, result.RemoteEndPoint);
                            }
                            catch { }
                        }
                    }, token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DnsProxyServer] UDP receive error: {ex.Message}");
                }
            }
        }

        private async Task ListenTcpAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _tcpListener != null)
            {
                try
                {
                    var client = await _tcpListener.AcceptTcpClientAsync(token);
                    _ = Task.Run(async () =>
                    {
                        using (client)
                        using (var stream = client.GetStream())
                        {
                            byte[] lengthBuf = new byte[2];
                            if (await stream.ReadAsync(lengthBuf, 0, 2, token) < 2) return;
                            ushort queryLength = (ushort)((lengthBuf[0] << 8) | lengthBuf[1]);

                            byte[] queryBuf = new byte[queryLength];
                            int read = 0;
                            while (read < queryLength)
                            {
                                int r = await stream.ReadAsync(queryBuf, read, queryLength - read, token);
                                if (r == 0) break;
                                read += r;
                            }

                            if (read == queryLength)
                            {
                                var clientIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
                                var response = await ProcessDnsPacketAsync(queryBuf, clientIp);
                                if (response != null)
                                {
                                    byte[] respLen = new byte[2]
                                    {
                                        (byte)(response.Length >> 8),
                                        (byte)(response.Length & 0xFF)
                                    };
                                    await stream.WriteAsync(respLen, 0, 2, token);
                                    await stream.WriteAsync(response, 0, response.Length, token);
                                }
                            }
                        }
                    }, token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { }
            }
        }

        public async Task<byte[]?> ProcessDnsPacketAsync(byte[] buffer, string clientIp)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var query = DnsWireParser.Parse(buffer, 0, buffer.Length);
            if (query == null || query.Questions.Count == 0)
            {
                return null;
            }

            var question = query.Questions[0];
            string domain = question.Name;
            var type = question.Type;

            // Instant bootstrap for upstream resolver domains
            if (string.Equals(domain, "one.one.one.one", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(domain, "cloudflare-dns.com", StringComparison.OrdinalIgnoreCase))
            {
                sw.Stop();
                return DnsWireParser.CreateBlockedResponse(query, "1.1.1.1", 300);
            }
            if (string.Equals(domain, "dns.google", StringComparison.OrdinalIgnoreCase))
            {
                sw.Stop();
                return DnsWireParser.CreateBlockedResponse(query, "8.8.8.8", 300);
            }
            if (string.Equals(domain, "dns.quad9.net", StringComparison.OrdinalIgnoreCase))
            {
                sw.Stop();
                return DnsWireParser.CreateBlockedResponse(query, "9.9.9.9", 300);
            }

            // 1. Evaluate Rule Engine
            var match = _ruleEngine.Evaluate(domain);

            if (match.IsBlocked)
            {
                sw.Stop();
                var blockedResponse = DnsWireParser.CreateBlockedResponse(query, "0.0.0.0", 300);

                OnQueryProcessed?.Invoke(this, new DnsQueryEventArgs
                {
                    Domain = domain,
                    Type = type,
                    IsBlocked = true,
                    RuleMatched = match.MatchedRule?.RawRule ?? "Blocked",
                    Latency = sw.Elapsed,
                    ClientIp = clientIp
                });

                return blockedResponse;
            }

            // 2. Check Cache
            if (_dnsCache.TryGet(domain, type, query.TransactionId, out var cachedResponse) && cachedResponse != null)
            {
                sw.Stop();
                OnQueryProcessed?.Invoke(this, new DnsQueryEventArgs
                {
                    Domain = domain,
                    Type = type,
                    IsBlocked = false,
                    RuleMatched = "Cache-Hit",
                    Latency = sw.Elapsed,
                    ClientIp = clientIp
                });
                return cachedResponse;
            }

            // 3. Forward to Upstream DoH
            try
            {
                var upstreamResponse = await _dohClient.ResolveAsync(buffer, query.TransactionId);
                sw.Stop();

                if (upstreamResponse != null)
                {
                    // Cache successful response (TTL default 300)
                    _dnsCache.Set(domain, type, upstreamResponse, 300);

                    OnQueryProcessed?.Invoke(this, new DnsQueryEventArgs
                    {
                        Domain = domain,
                        Type = type,
                        IsBlocked = false,
                        RuleMatched = "Upstream-Resolved",
                        Latency = sw.Elapsed,
                        ClientIp = clientIp
                    });

                    return upstreamResponse;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DnsProxyServer] Upstream error resolving {domain}: {ex.Message}");
            }

            sw.Stop();
            // Fail-Open: Return NXDOMAIN or empty response if upstream unavailable
            return DnsWireParser.CreateNxDomainResponse(query);
        }

        public void Dispose()
        {
            Stop();
            _dohClient.Dispose();
        }
    }
}
