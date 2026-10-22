using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ShieldBlock.Core.Rules;

namespace ShieldBlock.Core.Network
{
    public class ProxyRequestEventArgs : EventArgs
    {
        public string Domain { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public bool IsBlocked { get; set; }
        public string RuleMatched { get; set; } = string.Empty;
        public TimeSpan Latency { get; set; }
        public string Protocol { get; set; } = "HTTPS";
    }

    public class HttpFilteringProxy : IDisposable
    {
        private readonly RuleEngine _ruleEngine;
        private readonly int _port;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptTask;
        private bool _isRunning;

        private int _actualPort;

        public bool IsRunning => _isRunning;
        public int Port => _port;
        public int BoundPort => _actualPort > 0 ? _actualPort : _port;
        public event EventHandler<ProxyRequestEventArgs>? OnRequestProcessed;

        public HttpFilteringProxy(RuleEngine ruleEngine, int port = 8888)
        {
            _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
            _port = port;
            _actualPort = port;
        }

        public bool Start()
        {
            if (_isRunning) return true;

            int portToTry = _port;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    _cts = new CancellationTokenSource();
                    _listener = new TcpListener(IPAddress.Loopback, portToTry);
                    _listener.Start(200);
                    _actualPort = portToTry;
                    _isRunning = true;
                    _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
                    Console.WriteLine($"[HttpFilteringProxy] Active on 127.0.0.1:{_actualPort}");
                    return true;
                }
                catch (SocketException)
                {
                    try { _listener?.Stop(); } catch { }
                    _listener = null;
                    portToTry++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HttpFilteringProxy] Failed to start on port {portToTry}: {ex.Message}");
                    Stop();
                    return false;
                }
            }
            return false;
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;

            try
            {
                _cts?.Cancel();
                _listener?.Stop();
            }
            catch { }

            _listener = null;
            _cts?.Dispose();
            _cts = null;
            Console.WriteLine("[HttpFilteringProxy] Stopped.");
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => ProcessClientAsync(client, token), token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HttpFilteringProxy] Accept error: {ex.Message}");
                }
            }
        }

        private async Task ProcessClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    var clientStream = client.GetStream();
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    // Read HTTP request line
                    byte[] buffer = new byte[8192];
                    int bytesRead = await clientStream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (bytesRead <= 0) return;

                    string headerStr = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    int firstLineEnd = headerStr.IndexOf("\r\n", StringComparison.Ordinal);
                    if (firstLineEnd < 0) return;

                    string firstLine = headerStr.Substring(0, firstLineEnd);
                    var parts = firstLine.Split(' ');
                    if (parts.Length < 3) return;

                    string method = parts[0].ToUpperInvariant();
                    string target = parts[1];

                    if (method == "CONNECT")
                    {
                        // HTTPS CONNECT tunneling
                        await HandleHttpsConnectAsync(client, clientStream, target, sw, token);
                    }
                    else
                    {
                        // Standard HTTP request
                        await HandlePlainHttpAsync(client, clientStream, method, target, headerStr, buffer, bytesRead, sw, token);
                    }
                }
                catch { }
            }
        }

        private async Task HandleHttpsConnectAsync(
            TcpClient client,
            NetworkStream clientStream,
            string target,
            System.Diagnostics.Stopwatch sw,
            CancellationToken token)
        {
            int colonIndex = target.IndexOf(':');
            string host = colonIndex >= 0 ? target.Substring(0, colonIndex).Trim().Trim('[', ']') : target.Trim();
            int port = 443;
            if (colonIndex >= 0 && int.TryParse(target.Substring(colonIndex + 1), out int parsedPort))
            {
                port = parsedPort;
            }

            // Evaluate against RuleEngine
            var match = _ruleEngine.Evaluate(host);
            sw.Stop();

            if (match.IsBlocked)
            {
                // Instant 403 Forbidden
                byte[] forbiddenResponse = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 403 Forbidden\r\n" +
                    "Content-Type: text/plain\r\n" +
                    "Connection: close\r\n" +
                    "X-ShieldBlock: Blocked\r\n\r\n" +
                    "Blocked by ShieldBlock Ad Protection\r\n");

                try
                {
                    await clientStream.WriteAsync(forbiddenResponse, 0, forbiddenResponse.Length, token);
                }
                catch { }

                OnRequestProcessed?.Invoke(this, new ProxyRequestEventArgs
                {
                    Domain = host,
                    Method = "CONNECT",
                    IsBlocked = true,
                    RuleMatched = match.MatchedRule?.RawRule ?? "Ad Blocked",
                    Latency = sw.Elapsed,
                    Protocol = "HTTPS"
                });
                return;
            }

            // Target is allowed -> connect to remote server and establish transparent tunnel
            TcpClient? remoteClient = null;
            try
            {
                remoteClient = new TcpClient();
                remoteClient.NoDelay = true;

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                connectCts.CancelAfter(5000); // 5 second connection timeout

                await remoteClient.ConnectAsync(host, port, connectCts.Token);
                var remoteStream = remoteClient.GetStream();

                // Send 200 Connection Established back to browser
                byte[] okResponse = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
                await clientStream.WriteAsync(okResponse, 0, okResponse.Length, token);

                OnRequestProcessed?.Invoke(this, new ProxyRequestEventArgs
                {
                    Domain = host,
                    Method = "CONNECT",
                    IsBlocked = false,
                    RuleMatched = "Allowed",
                    Latency = sw.Elapsed,
                    Protocol = "HTTPS"
                });

                // Bidirectionally relay data until either stream closes
                var t1 = RelayStreamAsync(clientStream, remoteStream, token);
                var t2 = RelayStreamAsync(remoteStream, clientStream, token);
                await Task.WhenAny(t1, t2);
            }
            catch
            {
                try
                {
                    byte[] errResponse = Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\n\r\n");
                    await clientStream.WriteAsync(errResponse, 0, errResponse.Length, token);
                }
                catch { }
            }
            finally
            {
                remoteClient?.Dispose();
            }
        }

        private async Task HandlePlainHttpAsync(
            TcpClient client,
            NetworkStream clientStream,
            string method,
            string target,
            string fullHeaders,
            byte[] rawBuffer,
            int rawLength,
            System.Diagnostics.Stopwatch sw,
            CancellationToken token)
        {
            string host = string.Empty;
            int port = 80;

            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri(target);
                    host = uri.Host;
                    port = uri.Port;
                }
                catch { }
            }

            if (string.IsNullOrEmpty(host))
            {
                // Parse Host header
                using var sr = new StringReader(fullHeaders);
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                    {
                        string hostVal = line.Substring(5).Trim();
                        int c = hostVal.IndexOf(':');
                        if (c >= 0)
                        {
                            host = hostVal.Substring(0, c);
                            int.TryParse(hostVal.Substring(c + 1), out port);
                        }
                        else
                        {
                            host = hostVal;
                        }
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(host)) return;

            // Evaluate against RuleEngine
            var match = _ruleEngine.Evaluate(host);
            sw.Stop();

            if (match.IsBlocked)
            {
                byte[] forbidden = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 403 Forbidden\r\n" +
                    "Content-Type: text/plain\r\n" +
                    "Connection: close\r\n" +
                    "X-ShieldBlock: Blocked\r\n\r\n" +
                    "Blocked by ShieldBlock Ad Protection\r\n");

                try
                {
                    await clientStream.WriteAsync(forbidden, 0, forbidden.Length, token);
                }
                catch { }

                OnRequestProcessed?.Invoke(this, new ProxyRequestEventArgs
                {
                    Domain = host,
                    Method = method,
                    IsBlocked = true,
                    RuleMatched = match.MatchedRule?.RawRule ?? "Ad Blocked",
                    Latency = sw.Elapsed,
                    Protocol = "HTTP"
                });
                return;
            }

            // Forward plain HTTP request
            TcpClient? remote = null;
            try
            {
                remote = new TcpClient();
                remote.NoDelay = true;

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                connectCts.CancelAfter(5000);

                await remote.ConnectAsync(host, port, connectCts.Token);
                var remoteStream = remote.GetStream();

                await remoteStream.WriteAsync(rawBuffer, 0, rawLength, token);

                OnRequestProcessed?.Invoke(this, new ProxyRequestEventArgs
                {
                    Domain = host,
                    Method = method,
                    IsBlocked = false,
                    RuleMatched = "Allowed",
                    Latency = sw.Elapsed,
                    Protocol = "HTTP"
                });

                var t1 = RelayStreamAsync(clientStream, remoteStream, token);
                var t2 = RelayStreamAsync(remoteStream, clientStream, token);
                await Task.WhenAny(t1, t2);
            }
            catch { }
            finally
            {
                remote?.Dispose();
            }
        }

        private static async Task RelayStreamAsync(Stream source, Stream destination, CancellationToken token)
        {
            byte[] relayBuffer = new byte[65536]; // 64 KB buffer for high throughput
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int bytesRead = await source.ReadAsync(relayBuffer, 0, relayBuffer.Length, token);
                    if (bytesRead <= 0) break;
                    await destination.WriteAsync(relayBuffer, 0, bytesRead, token);
                }
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
