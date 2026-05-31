using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MasselGUARD.Models;

namespace MasselGUARD.Services
{
    /// <summary>
    /// Persists a rolling log of tunnel connect/disconnect events.
    /// Thread-safe for reads; writes are serialised via lock.
    /// </summary>
    public class HistoryService
    {
        private const int MaxEntries = 500;

        private static readonly string HistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MasselGUARD", "history.json");

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        private readonly object _lock = new();
        private List<ConnectionHistoryEntry> _entries = new();

        // ── Public read access ────────────────────────────────────────────────

        /// <summary>Snapshot of all history entries, newest first.</summary>
        public IReadOnlyList<ConnectionHistoryEntry> Entries
        {
            get { lock (_lock) { return _entries.AsReadOnly(); } }
        }

        // ── Load / Save ───────────────────────────────────────────────────────

        public void Load()
        {
            if (!File.Exists(HistoryPath)) return;
            try
            {
                var list = JsonSerializer.Deserialize<List<ConnectionHistoryEntry>>(
                    File.ReadAllText(HistoryPath), JsonOpts);
                if (list != null)
                    lock (_lock) { _entries = list; }
            }
            catch { /* corrupt file — start fresh */ }
        }

        public void Save()
        {
            List<ConnectionHistoryEntry> snapshot;
            lock (_lock) { snapshot = new List<ConnectionHistoryEntry>(_entries); }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
                File.WriteAllText(HistoryPath,
                    JsonSerializer.Serialize(snapshot, JsonOpts));
            }
            catch { /* non-critical */ }
        }

        public void Clear()
        {
            lock (_lock) { _entries.Clear(); }
            Save();
        }

        // ── Record events ─────────────────────────────────────────────────────

        /// <summary>Called when a tunnel successfully connects.</summary>
        public void RecordConnect(string tunnelName, string source)
        {
            var entry = new ConnectionHistoryEntry
            {
                TunnelName  = tunnelName,
                ConnectedAt = DateTime.UtcNow,
                Source      = source,
            };

            lock (_lock)
            {
                // Remove any dangling open session for this tunnel
                _entries.RemoveAll(e =>
                    e.TunnelName.Equals(tunnelName, StringComparison.OrdinalIgnoreCase)
                    && e.DisconnectedAt == null);

                _entries.Insert(0, entry);

                // Prune oldest entries beyond the cap
                if (_entries.Count > MaxEntries)
                    _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
            }
            // Save asynchronously on a threadpool thread — don't block the UI thread.
            System.Threading.ThreadPool.QueueUserWorkItem(_ => Save());
        }

        /// <summary>Called when a tunnel disconnects (clean disconnect).</summary>
        public void RecordDisconnect(string tunnelName)
        {
            lock (_lock)
            {
                var entry = _entries.FirstOrDefault(e =>
                    e.TunnelName.Equals(tunnelName, StringComparison.OrdinalIgnoreCase)
                    && e.DisconnectedAt == null);
                if (entry != null)
                    entry.DisconnectedAt = DateTime.UtcNow;
            }
            System.Threading.ThreadPool.QueueUserWorkItem(_ => Save());
        }
    }
}
