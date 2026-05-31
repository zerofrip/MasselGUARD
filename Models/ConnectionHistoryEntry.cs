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
    }
}
