using System;

namespace MasselGUARD.Models
{
    /// <summary>
    /// One connect/disconnect event persisted to history.json.
    /// </summary>
    public class ConnectionHistoryEntry
    {
        /// <summary>Display name of the tunnel.</summary>
        public string    TunnelName     { get; set; } = "";

        /// <summary>UTC moment the tunnel connected.</summary>
        public DateTime  ConnectedAt    { get; set; }

        /// <summary>UTC moment the tunnel disconnected. Null if the session was never
        /// cleanly closed (e.g. app was killed or the machine lost power).</summary>
        public DateTime? DisconnectedAt { get; set; }

        /// <summary>
        /// Human-readable trigger description.
        /// Examples: "Manual", "Auto-reconnect", "Rule: HomeNet → Work VPN",
        /// "Open network protection", "Default action".
        /// </summary>
        public string    Source         { get; set; } = "Manual";

        /// <summary>Bytes received during this session. Zero when not recorded or HideAndNoStore.</summary>
        public long SessionRxBytes { get; set; }

        /// <summary>Bytes sent during this session. Zero when not recorded or HideAndNoStore.</summary>
        public long SessionTxBytes { get; set; }

        /// <summary>Primary peer endpoint at connect time (if known).</summary>
        public string? Endpoint { get; set; }

        /// <summary>Non-null when this entry records a failed connect attempt.</summary>
        public string? FailureReason { get; set; }
    }
}
