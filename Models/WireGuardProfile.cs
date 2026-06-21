using System.Collections.Generic;

namespace MasselGUARD.Models
{
    public sealed class WireGuardInterfaceSection
    {
        public string PrivateKey { get; set; } = "";
        public string Address    { get; set; } = "";
        public string Dns        { get; set; } = "";
        public string ListenPort { get; set; } = "";
        public string Mtu        { get; set; } = "";
        public Dictionary<string, string> Extra { get; set; } = new();
    }

    public sealed class WireGuardPeerSection
    {
        public string PublicKey           { get; set; } = "";
        public string PresharedKey        { get; set; } = "";
        public string Endpoint            { get; set; } = "";
        public string AllowedIPs          { get; set; } = "";
        public string PersistentKeepalive { get; set; } = "";
        public Dictionary<string, string> Extra { get; set; } = new();
    }

    public sealed class WireGuardProfile
    {
        public WireGuardInterfaceSection Interface { get; set; } = new();
        public List<WireGuardPeerSection> Peers   { get; set; } = new();
    }
}
