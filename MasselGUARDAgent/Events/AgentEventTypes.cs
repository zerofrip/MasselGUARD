namespace MasselGUARD.Agent.Events
{
    /// <summary>Canonical agent event type strings (NDJSON stream + Tauri mg/event).</summary>
    public static class AgentEventTypes
    {
        public const string TunnelStateChanged    = "tunnel.state_changed";
        public const string TunnelStatsUpdated    = "tunnel.stats_updated";
        public const string TunnelHandshakeUpdated = "tunnel.handshake_updated";
        public const string TunnelCreated         = "tunnel.created";
        public const string TunnelUpdated         = "tunnel.updated";
        public const string TunnelDeleted         = "tunnel.deleted";
        public const string TunnelImported        = "tunnel.imported";
        public const string TunnelCloned          = "tunnel.cloned";
        public const string WifiSsidChanged       = "wifi.ssid_changed";
        public const string WifiRuleApplied       = "wifi.rule_applied";
        public const string NetworkChanged        = "network.changed";
        public const string KillSwitchChanged     = "killswitch.changed";
        public const string NetworkLockEnabled    = "networklock.enabled";
        public const string NetworkLockDisabled   = "networklock.disabled";
        public const string NetworkLockPolicyChanged = "networklock.policy_changed";
        public const string NetworkLockRecovered  = "networklock.recovered";
        public const string RouteGuardRoutingChanged = "routeguard.routing_changed";
        public const string RouteGuardNetworkLockChanged = "routeguard.network_lock_changed";
        public const string RouteGuardAvailabilityChanged = "routeguard.availability_changed";
        public const string RouteGuardSyncCompleted = "routeguard.sync_completed";
        public const string ConnectionFailed      = "connection.failed";
        public const string LogEntry              = "log.entry";
        public const string Notification          = "notification";
        public const string AgentHeartbeat        = "agent.heartbeat";
        public const string AgentSnapshot         = "agent.snapshot";
        public const string AgentProtocolError    = "agent.protocol_error";
        public const string AgentStatus           = "agent.status";
    }
}
