using System.Threading;

namespace MasselGUARD.Agent.Events
{
    /// <summary>Thread-safe monotonic sequence allocator with optional persistence.</summary>
    public sealed class EventSequenceAllocator
    {
        private long _current;
        private readonly EventSequenceStore? _store;

        public EventSequenceAllocator(EventSequenceStore store)
        {
            _store = store;
            _current = (long)store.Load();
        }

        public ulong Current => (ulong)Interlocked.Read(ref _current);

        /// <summary>Assign next sequence number (never returns 0).</summary>
        public ulong Next()
        {
            var next = Interlocked.Increment(ref _current);
            if (next < 0)
            {
                Interlocked.Exchange(ref _current, 1);
                next = 1;
            }
            var seq = (ulong)next;
            _store?.MaybeFlush(seq);
            return seq;
        }

        public void Flush() => _store?.Flush(Current);
    }
}
