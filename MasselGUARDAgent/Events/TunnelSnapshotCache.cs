using System;
using System.Collections.Generic;
using MasselGUARD;

namespace MasselGUARD.Agent.Events
{
    internal sealed class TunnelSnapshotEntry
    {
        public long RxBytes;
        public long TxBytes;
        public int PeerCount;
        public long? LastHandshakeSecsAgo;
        public bool Active;
        public DateTime LastStatsPublishUtc = DateTime.MinValue;
    }

    /// <summary>Delta detection for stats/handshake publishing (coalesce high-frequency updates).</summary>
    public sealed class TunnelSnapshotCache
    {
        private readonly Dictionary<string, TunnelSnapshotEntry> _entries =
            new(StringComparer.OrdinalIgnoreCase);

        private const long StatsByteDeltaThreshold = 4096;
        private static readonly TimeSpan MinStatsInterval = TimeSpan.FromMilliseconds(500);

        public bool ShouldPublishStats(string name, TunnelDll.TunnelStats stats, TunnelDll.RuntimeStats runtime)
        {
            if (!_entries.TryGetValue(name, out var e))
                return stats.AdapterFound || runtime.PeerCount > 0;

            if (Math.Abs(stats.RxBytes - e.RxBytes) >= StatsByteDeltaThreshold
                || Math.Abs(stats.TxBytes - e.TxBytes) >= StatsByteDeltaThreshold)
                return true;

            if (DateTime.UtcNow - e.LastStatsPublishUtc >= MinStatsInterval)
                return stats.RxBytes != e.RxBytes || stats.TxBytes != e.TxBytes;

            return false;
        }

        public bool ShouldPublishHandshake(string name, TunnelDll.RuntimeStats runtime)
        {
            if (!_entries.TryGetValue(name, out var e))
                return runtime.PeerCount > 0 || runtime.LastHandshakeSecsAgo.HasValue;

            return e.PeerCount != runtime.PeerCount
                || e.LastHandshakeSecsAgo != runtime.LastHandshakeSecsAgo;
        }

        public void UpdateStats(string name, TunnelDll.TunnelStats stats, TunnelDll.RuntimeStats runtime, bool active)
        {
            if (!_entries.TryGetValue(name, out var e))
            {
                e = new TunnelSnapshotEntry();
                _entries[name] = e;
            }
            e.RxBytes = stats.RxBytes;
            e.TxBytes = stats.TxBytes;
            e.PeerCount = runtime.PeerCount;
            e.LastHandshakeSecsAgo = runtime.LastHandshakeSecsAgo;
            e.Active = active;
            e.LastStatsPublishUtc = DateTime.UtcNow;
        }

        public void SetActive(string name, bool active)
        {
            if (!_entries.TryGetValue(name, out var e))
            {
                e = new TunnelSnapshotEntry();
                _entries[name] = e;
            }
            e.Active = active;
        }
    }
}
