using System;

namespace MasselGUARD.Models
{
    /// <summary>
    /// One WiFi SSID connection recorded for the activity chart.
    /// </summary>
    public class WifiHistoryEntry
    {
        /// <summary>SSID of the network.</summary>
        public string    Ssid           { get; set; } = "";

        /// <summary>UTC moment the device connected to this SSID.</summary>
        public DateTime  ConnectedAt    { get; set; }

        /// <summary>UTC moment the device left this SSID. Null = still connected.</summary>
        public DateTime? DisconnectedAt { get; set; }
    }
}
