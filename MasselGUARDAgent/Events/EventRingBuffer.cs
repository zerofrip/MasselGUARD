using System;
using System.Collections.Generic;

namespace MasselGUARD.Agent.Events
{
    /// <summary>In-memory ring buffer of serialized v1 envelope lines for replay.</summary>
    public sealed class EventRingBuffer
    {
        private readonly object _lock = new();
        private readonly (ulong seq, string line)[] _slots;
        private int _head;
        private int _count;
        private ulong _dropped;

        public EventRingBuffer(int capacity = 512)
        {
            capacity = Math.Clamp(capacity, 64, 4096);
            _slots = new (ulong, string)[capacity];
            Capacity = capacity;
        }

        public int Capacity { get; }
        public int Count { get { lock (_lock) return _count; } }
        public ulong Dropped { get { lock (_lock) return _dropped; } }

        public void Add(ulong seq, string jsonLine)
        {
            lock (_lock)
            {
                if (_count == Capacity)
                {
                    _dropped++;
                    _head = (_head + 1) % Capacity;
                    _count--;
                }

                var idx = (_head + _count) % Capacity;
                _slots[idx] = (seq, jsonLine);
                _count++;
            }
        }

        /// <summary>Replay events with seq strictly greater than sinceSeq, up to limit.</summary>
        public IReadOnlyList<string> ReplaySince(ulong sinceSeq, int limit = 512)
        {
            var result = new List<string>();
            lock (_lock)
            {
                var items = SnapshotLocked();
                foreach (var (seq, line) in items)
                {
                    if (seq <= sinceSeq) continue;
                    result.Add(line);
                    if (result.Count >= limit) break;
                }
            }
            return result;
        }

        public IReadOnlyList<(ulong seq, string line)> Snapshot()
        {
            lock (_lock) return SnapshotLocked();
        }

        private List<(ulong seq, string line)> SnapshotLocked()
        {
            var list = new List<(ulong, string)>(_count);
            for (int i = 0; i < _count; i++)
            {
                var idx = (_head + i) % Capacity;
                list.Add(_slots[idx]);
            }
            return list;
        }

        public ulong? OldestSeq
        {
            get
            {
                lock (_lock)
                {
                    if (_count == 0) return null;
                    return _slots[_head].seq;
                }
            }
        }
    }
}
