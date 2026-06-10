using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MasselGUARD.Models;

namespace MasselGUARD.Services
{
    /// <summary>
    /// Manages the in-app activity log.
    /// ViewModels subscribe to EntryAdded to render entries.
    /// No UI references — produces pure LogEntry objects.
    /// </summary>
    public class LogService
    {
        private readonly List<LogEntry> _entries = new();
        private readonly object _lock = new();
        private LogLevel _minLevel = LogLevel.Ok;

        /// <summary>
        /// Raised after each new entry is added.
        /// May be invoked on a background thread — subscribers must marshal to the UI
        /// thread themselves (e.g. via Dispatcher.BeginInvoke).
        /// </summary>
        public event Action<LogEntry>? EntryAdded;

        /// <summary>
        /// Returns a point-in-time snapshot of all entries.
        /// Safe to call from any thread.
        /// </summary>
        public IReadOnlyList<LogEntry> Entries
        {
            get { lock (_lock) { return _entries.ToList(); } }
        }

        /// <summary>Thread-safe entry count (no snapshot allocation).</summary>
        public int Count { get { lock (_lock) { return _entries.Count; } } }

        public LogLevel MinLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

        public bool IsExtended
        {
            get => _minLevel <= LogLevel.Info;
            set => _minLevel = value ? LogLevel.Debug : LogLevel.Ok;
        }

        // ── Write ─────────────────────────────────────────────────────────────
        public void Write(LogLevel level, string message, bool isContinuation = false)
        {
            if (level < _minLevel) return;
            var entry = new LogEntry(DateTime.Now, level, message, isContinuation);
            lock (_lock) { _entries.Add(entry); }
            // Fire outside the lock — handlers may call back into LogService.
            EntryAdded?.Invoke(entry);
        }

        public void Ok   (string msg) => Write(LogLevel.Ok,   msg);
        public void Warn  (string msg) => Write(LogLevel.Warn,  msg);
        public void Info  (string msg) => Write(LogLevel.Info,  msg);
        public void Debug (string msg) => Write(LogLevel.Debug, msg);

        // ── Export ────────────────────────────────────────────────────────────
        public void ExportToFile(string path)
        {
            IReadOnlyList<LogEntry> snapshot;
            lock (_lock) { snapshot = _entries.ToList(); }
            using var sw = new StreamWriter(path, append: false,
                encoding: System.Text.Encoding.UTF8);
            foreach (var e in snapshot)
                sw.WriteLine($"{e.Timestamp:HH:mm:ss}  [{e.Level,-5}]  {e.Message}");
        }

        public void Clear() { lock (_lock) { _entries.Clear(); } }
    }
}
