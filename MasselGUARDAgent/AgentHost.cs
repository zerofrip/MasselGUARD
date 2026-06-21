using System.Collections.Generic;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MasselGUARD.Agent.Events;
using MasselGUARD.Agent.Ipc;

namespace MasselGUARD.Agent
{
    /// <summary>Dedicated NDJSON event stream with subscribe handshake, filtering, and replay.</summary>
    public sealed class EventStreamHost : IDisposable
    {
        public const string PipeName = "MasselGUARDAgent-events";

        public sealed class SnapshotRequest
        {
            public Guid SessionId { get; init; }
            public StreamWriter Writer { get; init; } = null!;
            public ulong SinceSeq { get; init; }
            public SubscriptionFilter Filter { get; init; } = null!;
            public Action<ulong>? OnSeqAssigned { get; init; }
        }

        private sealed class SubscriberSession
        {
            public Guid Id { get; init; }
            public StreamWriter Writer { get; init; } = null!;
            public SubscriptionFilter Filter { get; } = new();
            public ulong SinceSeq { get; set; }
        }

        private readonly ConcurrentDictionary<Guid, SubscriberSession> _sessions = new();
        private readonly object _writeLock = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly EventRingBuffer _ring;
        private readonly EventStreamMetrics _metrics;
        private Task? _acceptLoop;

        public event Action<SnapshotRequest>? SnapshotRequested;

        public EventStreamHost(EventRingBuffer ring, EventStreamMetrics metrics)
        {
            _ring    = ring;
            _metrics = metrics;
        }

        public void Start() => _acceptLoop = Task.Run(AcceptLoopAsync);

        public void Broadcast(string ndjsonLine, string eventType)
        {
            lock (_writeLock)
            {
                var dead = new List<Guid>();
                foreach (var kv in _sessions)
                {
                    if (!kv.Value.Filter.Matches(eventType)) continue;
                    try
                    {
                        kv.Value.Writer.WriteLine(ndjsonLine);
                        kv.Value.Writer.Flush();
                    }
                    catch
                    {
                        dead.Add(kv.Key);
                    }
                }
                foreach (var id in dead)
                    _sessions.TryRemove(id, out _);
                _metrics.SetSubscriberCount(_sessions.Count);
            }
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var server = PipeSecurityHelper.CreateSecureServer(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                    _ = Task.Run(() => HandleSubscriberAsync(server), _cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(500, _cts.Token).ConfigureAwait(false); }
            }
        }

        private async Task HandleSubscriberAsync(NamedPipeServerStream stream)
        {
            var id = Guid.NewGuid();
            using (stream)
            using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
            {
                var session = new SubscriberSession { Id = id, Writer = writer };

                var subscribeLine = await ReadLineWithTimeoutAsync(reader, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                ulong sinceSeq = 0;
                if (!string.IsNullOrWhiteSpace(subscribeLine))
                {
                    var msg = SubscribeMessage.TryParse(subscribeLine);
                    if (msg != null && msg.Op == "subscribe")
                    {
                        sinceSeq = msg.SinceSeq;
                        session.SinceSeq = sinceSeq;
                        session.Filter.SetPatterns(msg.Filters);
                    }
                }

                _sessions[id] = session;
                _metrics.SetSubscriberCount(_sessions.Count);

                var (snapshotSeq, replayCount) = SendSnapshotAndReplay(session, sinceSeq);

                await writer.WriteLineAsync(new SubscribeMessage
                {
                    Op = "subscribe",
                    SinceSeq = sinceSeq,
                }.ToAckJson(snapshotSeq, replayCount, replayCount > 0 ? sinceSeq + 1 : null)).ConfigureAwait(false);

                try
                {
                    while (stream.IsConnected && !_cts.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) break;
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var msg = SubscribeMessage.TryParse(line);
                        if (msg == null) continue;

                        if (msg.Op == "subscribe")
                        {
                            session.Filter.SetPatterns(msg.Filters);
                            session.SinceSeq = msg.SinceSeq;
                            await writer.WriteLineAsync(msg.ToAckJson(0, 0, null)).ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    _sessions.TryRemove(id, out _);
                    _metrics.SetSubscriberCount(_sessions.Count);
                }
            }
        }

        private (ulong snapshotSeq, int replayCount) SendSnapshotAndReplay(SubscriberSession session, ulong sinceSeq)
        {
            ulong snapshotSeq = 0;
            SnapshotRequested?.Invoke(new SnapshotRequest
            {
                SessionId = session.Id,
                Writer    = session.Writer,
                SinceSeq  = sinceSeq,
                Filter    = session.Filter,
                OnSeqAssigned = s => snapshotSeq = s,
            });

            var replay = _ring.ReplaySince(sinceSeq);
            var replayCount = 0;
            lock (_writeLock)
            {
                foreach (var line in replay)
                {
                    var env = EventEnvelope.TryParseLine(line);
                    if (env == null) continue;
                    if (env.Type == AgentEventTypes.AgentSnapshot) continue;
                    if (!session.Filter.Matches(env.Type)) continue;
                    try
                    {
                        session.Writer.WriteLine(line);
                        replayCount++;
                        if (env.Seq.HasValue && env.Seq.Value > snapshotSeq)
                            snapshotSeq = env.Seq.Value;
                    }
                    catch { break; }
                }
                try { session.Writer.Flush(); } catch { }
            }
            return (snapshotSeq, replayCount);
        }

        private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader, TimeSpan timeout)
        {
            var readTask = reader.ReadLineAsync();
            var completed = await Task.WhenAny(readTask, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != readTask) return null;
            return await readTask.ConfigureAwait(false);
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts.Dispose();
        }
    }

    /// <summary>JSON-RPC server — request/response only; no event registration.</summary>
    public sealed class RpcHost : IDisposable
    {
        private readonly RpcHandler _handler;
        private readonly CancellationTokenSource _cts = new();
        private Task? _acceptLoop;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        public RpcHost(RpcHandler handler) => _handler = handler;

        public void Start() => _acceptLoop = Task.Run(AcceptLoopAsync);

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var server = PipeSecurityHelper.CreateSecureServer(
                        "MasselGUARD",
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(server), _cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(500, _cts.Token).ConfigureAwait(false); }
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream stream)
        {
            using (stream)
            using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
            {
                while (stream.IsConnected && !_cts.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    IpcResponse response;
                    try
                    {
                        var req = JsonSerializer.Deserialize<IpcRequest>(line, JsonOpts);
                        if (req == null || string.IsNullOrEmpty(req.Method))
                            response = IpcResponse.Err(0, -32600, "Invalid Request");
                        else
                            response = await _handler.HandleAsync(req).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        response = IpcResponse.Err(0, -32603, ex.Message);
                    }

                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOpts)).ConfigureAwait(false);
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts.Dispose();
        }
    }

    /// <summary>Hosts both RPC and event stream pipes.</summary>
    public sealed class AgentHost : IDisposable
    {
        private readonly RpcHost _rpc;
        private readonly EventStreamHost _events;

        public AgentHost(RpcHandler handler, EventRingBuffer ring, EventStreamMetrics metrics)
        {
            _rpc    = new RpcHost(handler);
            _events = new EventStreamHost(ring, metrics);
        }

        public EventStreamHost EventStream => _events;

        public void Start()
        {
            _rpc.Start();
            _events.Start();
        }

        public void Dispose()
        {
            _rpc.Dispose();
            _events.Dispose();
        }
    }
}
