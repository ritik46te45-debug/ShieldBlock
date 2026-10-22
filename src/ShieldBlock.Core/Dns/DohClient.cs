using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ShieldBlock.Core.Dns
{
    public class DohClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string[] _dohEndpoints = new[]
        {
            "https://1.1.1.1/dns-query",
            "https://1.0.0.1/dns-query",
            "https://9.9.9.9/dns-query"
        };

        private readonly IPEndPoint[] _fallbackUdpServers = new[]
        {
            new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53),
            new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53),
            new IPEndPoint(IPAddress.Parse("9.9.9.9"), 53)
        };

        public DohClient(HttpClient? httpClient = null)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 20,
                EnableMultipleHttp2Connections = true
            };

            _httpClient = httpClient ?? new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(1500)
            };
        }

        public async Task<byte[]?> ResolveAsync(byte[] queryBytes, ushort transactionId, CancellationToken cancellationToken = default)
        {
            // 1. Try DoH endpoints
            foreach (var endpoint in _dohEndpoints)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(1200); // 1.2s timeout per DoH try

                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));
                    request.Content = new ByteArrayContent(queryBytes);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");

                    var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        var responseBytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                        if (responseBytes.Length >= 12)
                        {
                            // Patch original transaction ID back
                            responseBytes[0] = (byte)(transactionId >> 8);
                            responseBytes[1] = (byte)(transactionId & 0xFF);
                            return responseBytes;
                        }
                    }
                }
                catch
                {
                    // Fallthrough to next DoH provider or fallback
                }
            }

            // 2. Fallback to direct UDP query
            return await QueryUdpFallbackAsync(queryBytes, transactionId, cancellationToken);
        }

        private async Task<byte[]?> QueryUdpFallbackAsync(byte[] queryBytes, ushort transactionId, CancellationToken cancellationToken)
        {
            foreach (var server in _fallbackUdpServers)
            {
                try
                {
                    using var udp = new UdpClient();
                    udp.Client.SendTimeout = 1000;
                    udp.Client.ReceiveTimeout = 1000;

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(1000);

                    await udp.SendAsync(queryBytes, queryBytes.Length, server);
                    var result = await udp.ReceiveAsync(cts.Token);

                    if (result.Buffer.Length >= 12)
                    {
                        var buf = result.Buffer;
                        buf[0] = (byte)(transactionId >> 8);
                        buf[1] = (byte)(transactionId & 0xFF);
                        return buf;
                    }
                }
                catch
                {
                    // Fallthrough to next UDP server
                }
            }

            return null;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
