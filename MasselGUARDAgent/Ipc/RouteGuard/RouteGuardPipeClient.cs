using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MasselGUARD.Agent.Ipc.RouteGuard
{
    /// <summary>
    /// Length-prefixed JSON-RPC 2.0 client for \\.\pipe\RouteGuard.
    /// </summary>
    public sealed class RouteGuardPipeClient
    {
        public const string PipeName = "RouteGuard";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        private ulong _nextId;

        public bool TryConnect(int timeoutMs = 500)
        {
            try
            {
                using var pipe = CreatePipe();
                pipe.Connect(timeoutMs);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public JsonElement? Call(string method, object? parameters = null, int timeoutMs = 5000)
        {
            using var pipe = CreatePipe();
            pipe.Connect(timeoutMs);

            var id = Interlocked.Increment(ref _nextId);
            var req = JsonSerializer.SerializeToUtf8Bytes(new
            {
                jsonrpc = "2.0",
                id,
                method,
                parameters = parameters ?? new { },
            }, JsonOpts);

            var len = BitConverter.GetBytes(req.Length);
            pipe.Write(len, 0, 4);
            pipe.Write(req, 0, req.Length);
            pipe.Flush();

            var lenBuf = new byte[4];
            ReadExact(pipe, lenBuf, timeoutMs);
            var respLen = BitConverter.ToInt32(lenBuf, 0);
            if (respLen <= 0 || respLen > 16 * 1024 * 1024)
                throw new InvalidOperationException($"Invalid response length: {respLen}");

            var respBuf = new byte[respLen];
            ReadExact(pipe, respBuf, timeoutMs);

            using var doc = JsonDocument.Parse(respBuf);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "RPC error";
                throw new InvalidOperationException(msg ?? "RPC error");
            }

            return root.TryGetProperty("result", out var result) ? result.Clone() : null;
        }

        private static NamedPipeClientStream CreatePipe() =>
            new(".", PipeName, PipeDirection.InOut, PipeOptions.None);

        private static void ReadExact(Stream stream, byte[] buffer, int timeoutMs)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            var offset = 0;
            while (offset < buffer.Length)
            {
                if (Environment.TickCount64 > deadline)
                    throw new TimeoutException("RouteGuard pipe read timeout");
                var read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException("RouteGuard pipe closed");
                offset += read;
            }
        }
    }
}
