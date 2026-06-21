using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MasselGUARD.Models
{
    /// <summary>Routing target for split-tunnel rules (UI + future backend).</summary>
    public enum SplitRouteTarget
    {
        Vpn,
        Direct
    }

    public class AppSplitRule
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
        public string AppPath { get; set; } = "";
        /// <summary>"vpn" | "direct"</summary>
        public string Route { get; set; } = "vpn";
        public bool Enabled { get; set; } = true;
    }

    public class IpSplitRule
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
        public string Cidr { get; set; } = "";
        public string Route { get; set; } = "direct";
        public bool Enabled { get; set; } = true;
    }

    public class DomainSplitRule
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
        public string Pattern { get; set; } = "";
        public string Route { get; set; } = "vpn";
        public bool Enabled { get; set; } = true;
    }

    public class SplitTunnelRules
    {
        public List<AppSplitRule> AppRules { get; set; } = new();
        public List<IpSplitRule> IpRules { get; set; } = new();
        public List<DomainSplitRule> DomainRules { get; set; } = new();
        /// <summary>When true, UI may attempt RouteGuard bridge for enforcement.</summary>
        public bool UseRouteGuardBridge { get; set; } = false;
    }

    public class NetworkLockConfig
    {
        /// <summary>Legacy toggle — migrated to Mode on load; omitted from JSON once Mode is set.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Enabled { get; set; }

        public NetworkLockMode Mode { get; set; } = NetworkLockMode.Disabled;
        public bool LanAccessEnabled { get; set; }
        public List<string> LanExceptions { get; set; } = new();
        /// <summary>strict | allow_exceptions | allow_dhcp</summary>
        public string DnsPolicy { get; set; } = "strict";
        public List<string> DnsExceptions { get; set; } = new();
        public bool AllowDhcp { get; set; } = true;
    }

}
