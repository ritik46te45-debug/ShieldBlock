using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ShieldBlock.Core.Rules;

namespace ShieldBlock.Core.Https
{
    public class HttpsMitmProxy : IDisposable
    {
        private readonly int _port;
        private readonly RuleEngine _ruleEngine;
        private readonly CertificateManager _certManager;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private bool _isRunning;

        public bool IsRunning => _isRunning;

        public HttpsMitmProxy(RuleEngine ruleEngine, CertificateManager certManager, int port = 8443)
        {
            _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
            _certManager = certManager ?? throw new ArgumentNullException(nameof(certManager));
            _port = port;
        }

        public bool Start()
        {
            if (_isRunning) return true;

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start(100);
                _isRunning = true;
                _listenTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
                Console.WriteLine($"[HttpsMitmProxy] Active on 127.0.0.1:{_port}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HttpsMitmProxy] Failed to start: {ex.Message}");
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
                _listener?.Stop();
            }
            catch { }

            _listener = null;
            _cts?.Dispose();
            _cts = null;
            Console.WriteLine("[HttpsMitmProxy] Stopped.");
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleClientAsync(client, token), token);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    byte[] peekBuffer = new byte[1024];
                    int bytesRead = await stream.ReadAsync(peekBuffer, 0, peekBuffer.Length, token);
                    if (bytesRead <= 0) return;

                    // Extract SNI from TLS ClientHello
                    string? sni = ExtractSni(peekBuffer, bytesRead);

                    // If domain is on strict bypass list or cannot be parsed, pass through transparently
                    if (string.IsNullOrEmpty(sni) || BypassList.ShouldBypassDomain(sni))
                    {
                        // Direct bypass: relay without decrypting
                        return;
                    }

                    // Check if domain is blocked by RuleEngine
                    var match = _ruleEngine.Evaluate(sni);
                    if (match.IsBlocked)
                    {
                        // Block ad connection immediately
                        client.Close();
                        return;
                    }

                    // For allowed non-bypassed domains, optionally terminate TLS
                    var leafCert = _certManager.GetOrCreateLeafCertificate(sni);
                    using var sslStream = new SslStream(new ReplayStream(stream, peekBuffer, bytesRead), false);

                    await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = leafCert,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                    }, token);

                    // Read HTTP request line
                    using var reader = new StreamReader(sslStream, Encoding.ASCII, leaveOpen: true);
                    string? line = await reader.ReadLineAsync(token);
                    if (line != null && _ruleEngine.Evaluate(sni + "/" + line).IsBlocked)
                    {
                        // Return HTTP 204 No Content for blocked sub-resource
                        byte[] noContent = Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n");
                        await sslStream.WriteAsync(noContent, 0, noContent.Length, token);
                    }
                }
                catch
                {
                    // Fail-Open on TLS errors
                }
            }
        }

        private static string? ExtractSni(byte[] buffer, int length)
        {
            try
            {
                // Verify TLS Handshake (0x16) and ClientHello (0x01)
                if (length < 44 || buffer[0] != 0x16 || buffer[5] != 0x01) return null;

                int pos = 43; // Skip header, version, random
                if (pos >= length) return null;

                // Skip session ID
                int sessionIdLen = buffer[pos++];
                pos += sessionIdLen;
                if (pos + 2 >= length) return null;

                // Skip cipher suites
                int cipherLen = (buffer[pos] << 8) | buffer[pos + 1];
                pos += 2 + cipherLen;
                if (pos >= length) return null;

                // Skip compression methods
                int compLen = buffer[pos++];
                pos += compLen;
                if (pos + 2 >= length) return null;

                // Extensions length
                int extLen = (buffer[pos] << 8) | buffer[pos + 1];
                pos += 2;

                while (pos + 4 < length)
                {
                    int extType = (buffer[pos] << 8) | buffer[pos + 1];
                    int extDataLen = (buffer[pos + 2] << 8) | buffer[pos + 3];
                    pos += 4;

                    if (extType == 0x00) // server_name extension
                    {
                        if (pos + 5 >= length) return null;
                        int nameListLen = (buffer[pos] << 8) | buffer[pos + 1];
                        int nameType = buffer[pos + 2];
                        int nameLen = (buffer[pos + 3] << 8) | buffer[pos + 4];
                        pos += 5;

                        if (nameType == 0 && pos + nameLen <= length)
                        {
                            return Encoding.ASCII.GetString(buffer, pos, nameLen);
                        }
                    }

                    pos += extDataLen;
                }
            }
            catch { }

            return null;
        }

        private class ReplayStream : Stream
        {
            private readonly Stream _inner;
            private readonly byte[] _initial;
            private int _initialPos;

            public ReplayStream(Stream inner, byte[] initial, int count)
            {
                _inner = inner;
                _initial = new byte[count];
                Buffer.BlockCopy(initial, 0, _initial, 0, count);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_initialPos < _initial.Length)
                {
                    int available = Math.Min(count, _initial.Length - _initialPos);
                    Buffer.BlockCopy(_initial, _initialPos, buffer, offset, available);
                    _initialPos += available;
                    return available;
                }
                return _inner.Read(buffer, offset, count);
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (_initialPos < _initial.Length)
                {
                    int available = Math.Min(count, _initial.Length - _initialPos);
                    Buffer.BlockCopy(_initial, _initialPos, buffer, offset, available);
                    _initialPos += available;
                    return available;
                }
                return await _inner.ReadAsync(buffer, offset, count, cancellationToken);
            }

            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _inner.WriteAsync(buffer, offset, count, cancellationToken);
            public override void Flush() => _inner.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
