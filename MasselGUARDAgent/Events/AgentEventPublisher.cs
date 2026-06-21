using System;
using System.Diagnostics;
using System.Threading;
using MasselGUARD.Agent.Ipc;

namespace MasselGUARD.Agent.Events
{
    /// <summary>Subscribes to AgentEventBus, assigns seq, records ring buffer, broadcasts v1 NDJSON.</summary>
    public sealed class AgentEventPublisher : IDisposable
    {
        private readonly AgentEventBus _bus;
        private readonly EventStreamHost _stream;
        private readonly EventSequenceAllocator _seq;
        private readonly EventRingBuffer _ring;
        private readonly EventStreamMetrics _metrics;
        private readonly Func<ulong, object?> _snapshotFactory;
        private readonly DateTime _started = DateTime.UtcNow;
        private readonly Timer _heartbeatTimer;

        public AgentEventPublisher(
            AgentEventBus bus,
            EventStreamHost stream,
            EventSequenceAllocator seq,
            EventRingBuffer ring,
            EventStreamMetrics metrics,
            Func<ulong, object?> snapshotFactory)
        {
            _bus             = bus;
            _stream          = stream;
            _seq             = seq;
            _ring            = ring;
            _metrics         = metrics;
            _snapshotFactory = snapshotFactory;

            _bus.EventPublished += OnEvent;
            _stream.SnapshotRequested += OnSnapshotRequested;

            _heartbeatTimer = new Timer(_ => PublishHeartbeat(), null,
                TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        }

        public EventRingBuffer Ring => _ring;
        public EventStreamMetrics Metrics => _metrics;
        public EventSequenceAllocator Sequencer => _seq;

        private void OnSnapshotRequested(EventStreamHost.SnapshotRequest req)
        {
            var seq = _seq.Next();
            var body = _snapshotFactory(seq);
            if (body == null) return;

            var env = EventEnvelope.CreateV1(seq, AgentEventTypes.AgentSnapshot, body);
            var line = env.ToJsonLine();
            _ring.Add(seq, line);
            req.OnSeqAssigned?.Invoke(seq);

            try
            {
                lock (typeof(EventStreamHost))
                {
                    req.Writer.WriteLine(line);
                    req.Writer.Flush();
                }
            }
            catch { /* session gone */ }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            sw.Stop();
            _metrics.RecordPublished(0);
        }

        private void OnEvent(AgentEvent evt)
        {
            if (evt.Type == AgentEventTypes.AgentSnapshot) return;
            PublishEnvelope(evt.Type, evt.Payload);
        }

        private void PublishHeartbeat()
        {
            PublishEnvelope(AgentEventTypes.AgentHeartbeat, new
            {
                uptimeSecs = (long)(DateTime.UtcNow - _started).TotalSeconds,
            });
        }

        internal string PublishEnvelope(string type, object? payload, ulong? forcedSeq = null)
        {
            var sw = Stopwatch.StartNew();
            var seq = forcedSeq ?? _seq.Next();
            var env = EventEnvelope.CreateV1(seq, type, payload);
            var line = env.ToJsonLine();
            _ring.Add(seq, line);
            _stream.Broadcast(line, type);
            sw.Stop();
            _metrics.RecordPublished(sw.ElapsedTicks * 1_000_000 / Stopwatch.Frequency);
            return line;
        }

        public void Dispose()
        {
            _heartbeatTimer.Dispose();
            _bus.EventPublished -= OnEvent;
            _stream.SnapshotRequested -= OnSnapshotRequested;
            _seq.Flush();
        }
    }
}
