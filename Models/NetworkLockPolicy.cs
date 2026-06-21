using System.Collections.Generic;

namespace MasselGUARD.Models
{
    public sealed class TunnelAllowRule
    {
        public string Name { get; set; } = "";
        public string? EndpointIp { get; set; }
        public string? AdapterName { get; set; }
        public bool KillSwitchTrigger { get; set; }
    }

    public sealed class NetworkLockFilterSnapshot
    {
        public List<string> ActiveRules { get; set; } = new();
        public bool GlobalBlockActive { get; set; }
        public string LeakProtection { get; set; } = "inactive";
    }

    public sealed class NetworkLockPolicy
    {
        public NetworkLockMode Mode { get; set; } = NetworkLockMode.Disabled;
        public bool LanAccessEnabled { get; set; }
        public List<string> LanExceptions { get; set; } = new();
        public string DnsPolicy { get; set; } = "strict";
        public List<string> DnsExceptions { get; set; } = new();
        public bool AllowDhcp { get; set; } = true;

        public static NetworkLockPolicy FromConfig(NetworkLockConfig cfg) => new()
        {
            Mode              = cfg.Mode,
            LanAccessEnabled  = cfg.LanAccessEnabled,
            LanExceptions     = cfg.LanExceptions ?? new List<string>(),
            DnsPolicy         = string.IsNullOrWhiteSpace(cfg.DnsPolicy) ? "strict" : cfg.DnsPolicy,
            DnsExceptions     = cfg.DnsExceptions ?? new List<string>(),
            AllowDhcp         = cfg.AllowDhcp,
        };
    }
}
