using System;
using System.IO;
using System.Text.Json;

namespace MasselGUARD.Agent.Events
{
    /// <summary>Persists last assigned sequence to %APPDATA%\MasselGUARD\event_seq.json.</summary>
    public sealed class EventSequenceStore : IDisposable
    {
        private readonly string _path;
        private readonly object _lock = new();
        private DateTime _lastFlush = DateTime.UtcNow;
        private int _eventsSinceFlush;

        private sealed class SeqFile
        {
            public ulong LastSeq { get; set; }
        }

        public EventSequenceStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MasselGUARD");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "event_seq.json");
        }

        public ulong Load()
        {
            lock (_lock)
            {
                if (!File.Exists(_path)) return 0;
                try
                {
                    var data = JsonSerializer.Deserialize<SeqFile>(File.ReadAllText(_path));
                    return data?.LastSeq ?? 0;
                }
                catch
                {
                    return 0;
                }
            }
        }

        public void MaybeFlush(ulong lastSeq)
        {
            lock (_lock)
            {
                _eventsSinceFlush++;
                var elapsed = DateTime.UtcNow - _lastFlush;
                if (_eventsSinceFlush < 50 && elapsed.TotalSeconds < 5) return;

                FlushInternal(lastSeq);
            }
        }

        public void Flush(ulong lastSeq)
        {
            lock (_lock) => FlushInternal(lastSeq);
        }

        private void FlushInternal(ulong lastSeq)
        {
            try
            {
                var json = JsonSerializer.Serialize(new SeqFile { LastSeq = lastSeq });
                File.WriteAllText(_path, json);
                _eventsSinceFlush = 0;
                _lastFlush = DateTime.UtcNow;
            }
            catch { /* best effort */ }
        }

        public void Dispose() { }
    }
}
