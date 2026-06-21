using System;
using System.Collections.Generic;

namespace MasselGUARD.Agent.Events
{
    /// <summary>Registry of known event types and reserved namespaces.</summary>
    public static class EventCatalog
    {
        public static readonly IReadOnlySet<string> KnownTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            AgentEventTypes.TunnelStateChanged,
            AgentEventTypes.TunnelStatsUpdated,
            AgentEventTypes.TunnelHandshakeUpdated,
            AgentEventTypes.TunnelCreated,
            AgentEventTypes.TunnelUpdated,
            AgentEventTypes.TunnelDeleted,
            AgentEventTypes.TunnelImported,
            AgentEventTypes.TunnelCloned,
            AgentEventTypes.WifiSsidChanged,
            AgentEventTypes.WifiRuleApplied,
            AgentEventTypes.NetworkChanged,
            AgentEventTypes.KillSwitchChanged,
            AgentEventTypes.NetworkLockEnabled,
            AgentEventTypes.NetworkLockDisabled,
            AgentEventTypes.NetworkLockPolicyChanged,
            AgentEventTypes.NetworkLockRecovered,
            AgentEventTypes.RouteGuardRoutingChanged,
            AgentEventTypes.RouteGuardNetworkLockChanged,
            AgentEventTypes.RouteGuardAvailabilityChanged,
            AgentEventTypes.RouteGuardSyncCompleted,
            AgentEventTypes.ConnectionFailed,
            AgentEventTypes.LogEntry,
            AgentEventTypes.Notification,
            AgentEventTypes.AgentHeartbeat,
            AgentEventTypes.AgentSnapshot,
            AgentEventTypes.AgentProtocolError,
            AgentEventTypes.AgentStatus,
            // Observability (Phase 10)
            "observability.health_changed",
            "routeguard.metrics_updated",
            "routeguard.transport_health",
            "routeguard.transport_recovery",
            "routeguard.dns_redirect_stats",
            // Reserved RouteGuard (not emitted in Phase 2)
            "routeguard.awg_connected",
            "routeguard.phantun_connected",
            "routeguard.lwo_connected",
            "routeguard.lwo_fallback",
            "routeguard.lwo_failed",
            // Reserved future
            "splittunnel.rule_added",
            "splittunnel.rule_removed",
            "awg.connected",
            "awg.disconnected",
        };

        public static bool IsReservedRouteGuard(string type) =>
            type.StartsWith("routeguard.", StringComparison.Ordinal);

        public static bool IsKnownOrReserved(string type) =>
            KnownTypes.Contains(type) || IsReservedRouteGuard(type);
    }
}
