using System;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ShieldBlock.Core.Common;

namespace ShieldBlock.Core.Ipc
{
    public class IpcClient
    {
        private readonly string _pipeName;

        public IpcClient(string? pipeName = null)
        {
            _pipeName = pipeName ?? Constants.IpcPipeName;
        }

        public Task<IpcResponse> SendAsync(string action, string? payloadJson = null, int timeoutMs = 2500) => SendRequestAsync(action, payloadJson, timeoutMs);

        public async Task<IpcResponse> SendRequestAsync(string action, string? payloadJson = null, int timeoutMs = 2500)
        {
            var req = new IpcRequest
            {
                Action = action,
                PayloadJson = payloadJson
            };

            using var cts = new CancellationTokenSource(timeoutMs);

            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            client.Connect(timeoutMs);

            // Write 4-byte length prefix + request bytes
            byte[] reqBytes = JsonSerializer.SerializeToUtf8Bytes(req);
            byte[] lenBuf = BitConverter.GetBytes(reqBytes.Length);

            await client.WriteAsync(lenBuf, cts.Token);
            await client.WriteAsync(reqBytes, cts.Token);
            await client.FlushAsync(cts.Token);

            // Read 4-byte response length
            byte[] respLenBuf = new byte[4];
            await client.ReadExactlyAsync(respLenBuf, cts.Token);
            int respLen = BitConverter.ToInt32(respLenBuf, 0);

            if (respLen <= 0 || respLen > 10 * 1024 * 1024)
            {
                return IpcResponse.Fail(req.RequestId, "Invalid response frame size from ShieldBlockService.");
            }

            byte[] respBuf = new byte[respLen];
            await client.ReadExactlyAsync(respBuf, cts.Token);

            var resp = JsonSerializer.Deserialize<IpcResponse>(respBuf);
            return resp ?? IpcResponse.Fail(req.RequestId, "Failed to deserialize response.");
        }

        public async Task<bool> IsServiceAvailableAsync(int timeoutMs = 1000)
        {
            try
            {
                var resp = await SendRequestAsync(IpcActions.Ping, timeoutMs: timeoutMs);
                return resp.Success;
            }
            catch
            {
                return false;
            }
        }
    }
}
