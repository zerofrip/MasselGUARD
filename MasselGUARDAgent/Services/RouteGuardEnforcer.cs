using System.Collections.Generic;
using MasselGUARD.Models;
using MasselGUARD.Services;

namespace MasselGUARD.Agent.Services
{
    /// <summary>
    /// Delegates network lock WFP enforcement to RouteGuard when enabled.
    /// </summary>
    public sealed class RouteGuardEnforcer : INetworkLockEnforcer
    {
        private readonly Agent.Events.AgentEventBus _eventBus;
        private readonly Ipc.RouteGuard.RouteGuardBridgeService _bridge;
        private TelemetryRollupService? _telemetry;
        private bool _active;
        private string? _lastError;

        public RouteGuardEnforcer(
            Agent.Events.AgentEventBus eventBus,
            Ipc.RouteGuard.RouteGuardBridgeService bridge)
        {
            _eventBus = eventBus;
            _bridge   = bridge;
        }

        public void SetTelemetry(TelemetryRollupService telemetry) => _telemetry = telemetry;

        public void Apply(NetworkLockPolicy policy, IReadOnlyList<TunnelAllowRule> tunnels)
        {
            if (!NetworkLockModeExtensions.RequiresEnforcementWhenActive(policy.Mode))
            {
                RemoveAll();
                return;
            }

            try
            {
                var client = new Ipc.RouteGuard.RouteGuardPipeClient();
                client.Call("network_lock.enable", new { });
                _active = true;
                _lastError = null;
                _eventBus.Publish(Agent.Events.AgentEventTypes.RouteGuardNetworkLockChanged, new
                {
                    active = true,
                    backend = "routeguard_wfp",
                    tunnelCount = tunnels.Count,
                });
            }
            catch (Exception ex)
            {
                _active = false;
                _lastError = ex.Message;
                _telemetry?.RecordNetworkLockFailure("wfp_delegation_ipc");
                _eventBus.Publish(Agent.Events.AgentEventTypes.RouteGuardNetworkLockChanged, new
                {
                    active = false,
                    backend = "routeguard_wfp",
                    error = ex.Message,
                    delegationFailed = true,
                });
            }
        }

        public void RemoveAll()
        {
            if (!_active) return;
            try
            {
                var client = new Ipc.RouteGuard.RouteGuardPipeClient();
                client.Call("network_lock.disable", new { });
            }
            catch { /* best effort */ }

            _active = false;
            _eventBus.Publish(Agent.Events.AgentEventTypes.RouteGuardNetworkLockChanged, new
            {
                active = false,
                backend = "routeguard_wfp",
            });
        }

        public NetworkLockFilterSnapshot GetActiveFilters() => new()
        {
            LeakProtection = _active ? "active" : "inactive",
            ActiveRules = _active ? new List<string> { "RouteGuard WFP" } : new List<string>(),
            GlobalBlockActive = _active,
        };

        public void CleanupLegacyRules() { }
    }
}
