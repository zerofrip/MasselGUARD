using System;
using System.Diagnostics;
using System.Threading;

namespace MasselGUARD.Agent.Events
{
    public sealed class EventStreamMetrics
    {
        private long _published;
        private long _subscriberCount;
        private long _latencySumUs;
        private long _latencySamples;
        private long _replayRequests;
        private readonly object _replayLock = new();
        private DateTime _replayWindowStart = DateTime.UtcNow;
        private int _replayInWindow;

        public void RecordPublished(long latencyUs)
        {
            Interlocked.Increment(ref _published);
            Interlocked.Add(ref _latencySumUs, latencyUs);
            Interlocked.Increment(ref _latencySamples);
        }

        public void SetSubscriberCount(int count) =>
            Interlocked.Exchange(ref _subscriberCount, count);

        public bool TryRecordReplayRequest(int maxPerSecond = 10)
        {
            lock (_replayLock)
            {
                if ((DateTime.UtcNow - _replayWindowStart).TotalSeconds >= 1)
                {
                    _replayWindowStart = DateTime.UtcNow;
                    _replayInWindow = 0;
                }
                if (_replayInWindow >= maxPerSecond) return false;
                _replayInWindow++;
                Interlocked.Increment(ref _replayRequests);
                return true;
            }
        }

        public object Snapshot(ulong lastSeq, EventRingBuffer ring) => new
        {
            published = Interlocked.Read(ref _published),
            dropped = ring.Dropped,
            lastSeq,
            subscribers = Interlocked.Read(ref _subscriberCount),
            avgPublishLatencyUs = Interlocked.Read(ref _latencySamples) > 0
                ? Interlocked.Read(ref _latencySumUs) / Interlocked.Read(ref _latencySamples)
                : 0,
            replayRequests = Interlocked.Read(ref _replayRequests),
            ring = new { size = ring.Count, capacity = ring.Capacity },
        };

        public static long MeasureUs(Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;
        }
    }
}
