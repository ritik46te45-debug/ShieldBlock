using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ShieldBlock.Core.Common;

namespace ShieldBlock.Core.Ipc
{
    public class IpcServer : IDisposable
    {
        private readonly Func<IpcRequest, Task<IpcResponse>> _handler;
        private readonly string _pipeName;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _listenerTask;
        private NamedPipeServerStream? _currentServerStream;
        private bool _isDisposed;

        public IpcServer(Func<IpcRequest, Task<IpcResponse>> handler, string? pipeName = null)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _pipeName = pipeName ?? Constants.IpcPipeName;
        }

        public void Start()
        {
            _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && !_isDisposed)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    _currentServerStream = server;
                    await server.WaitForConnectionAsync(token);
                    _ = HandleClientAsync(server, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    server?.Dispose();
                    if (token.IsCancellationRequested || _isDisposed) break;
                    await Task.Delay(200, token);
                }
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken token)
        {
            using (pipe)
            {
                try
                {
                    // Read 4-byte length prefix
                    byte[] lenBuf = new byte[4];
                    await pipe.ReadExactlyAsync(lenBuf, token);
                    int reqLen = BitConverter.ToInt32(lenBuf, 0);

                    if (reqLen <= 0 || reqLen > 10 * 1024 * 1024) // 10MB safety cap
                        return;

                    byte[] reqBuf = new byte[reqLen];
                    await pipe.ReadExactlyAsync(reqBuf, token);

                    var request = JsonSerializer.Deserialize<IpcRequest>(reqBuf);
                    IpcResponse response;

                    if (request == null || string.IsNullOrWhiteSpace(request.Action))
                    {
                        response = IpcResponse.Fail(request?.RequestId ?? "unknown", "Malformed request.");
                    }
                    else
                    {
                        response = await _handler(request);
                    }

                    // Write 4-byte length prefix and response bytes
                    byte[] respBytes = JsonSerializer.SerializeToUtf8Bytes(response);
                    byte[] respLenBuf = BitConverter.GetBytes(respBytes.Length);

                    await pipe.WriteAsync(respLenBuf, token);
                    await pipe.WriteAsync(respBytes, token);
                    await pipe.FlushAsync(token);
                }
                catch (Exception ex)
                {
                    // Client disconnected or pipe closed
                }
            }
        }

        public void Stop()
        {
            _cts.Cancel();
            try { _currentServerStream?.Dispose(); } catch { }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _cts.Cancel();
                try { _currentServerStream?.Dispose(); } catch { }
                _cts.Dispose();
            }
        }
    }
}
