using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MasselGUARD.Agent.Events;
using MasselGUARD.Models;
using MasselGUARD.Services;

namespace MasselGUARD.Agent.Services
{
    public sealed class NetworkLockService
    {
        private readonly ConfigService _config;
        private readonly TunnelService _tunnels;
        private readonly LogService _log;
        private readonly INetworkLockEnforcer _firewall;
        private readonly RouteGuardEnforcer? _routeGuard;
        private readonly NetworkLockStateStore _stateStore;
        private readonly AgentEventBus _eventBus;

        private readonly HashSet<string> _connectedTunnels = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TunnelAllowRule> _tunnelRules = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private bool _enforcementActive;

        public NetworkLockService(
            ConfigService config,
            TunnelService tunnels,
            LogService log,
            INetworkLockEnforcer firewall,
            NetworkLockStateStore stateStore,
            AgentEventBus eventBus,
            RouteGuardEnforcer? routeGuard = null)
        {
            _config     = config;
            _tunnels    = tunnels;
            _log        = log;
            _firewall   = firewall;
            _stateStore = stateStore;
            _eventBus   = eventBus;
            _routeGuard = routeGuard;
        }

        private bool UseWfpDelegation() =>
            _config.Config.NetworkLockWfpDelegation && _routeGuard != null;

        private NetworkLockFilterSnapshot GetFilterSnapshot()
        {
            if (UseWfpDelegation())
                return _routeGuard!.GetActiveFilters();
            return _firewall.GetActiveFilters();
        }

        private void ApplyEnforcers(NetworkLockPolicy policy, IReadOnlyList<TunnelAllowRule> tunnels)
        {
            if (UseWfpDelegation())
            {
                _firewall.RemoveAll();
                _routeGuard!.Apply(policy, tunnels);
            }
            else
            {
                _routeGuard?.RemoveAll();
                _firewall.Apply(policy, tunnels);
            }
        }

        private void RemoveAllEnforcers()
        {
            _firewall.RemoveAll();
            _routeGuard?.RemoveAll();
        }

        public void RecoverOnStartup()
        {
            _stateStore.Load();
            _firewall.CleanupLegacyRules();

            lock (_lock)
            {
                foreach (var t in _config.Config.Tunnels)
                {
                    if (!_tunnels.IsActive(t)) continue;
                    RegisterTunnelLocked(t);
                }
            }

            var state = _stateStore.Snapshot();
            var reason = "agent_restart";
            if (state.EnforcementActive || ShouldEnforce())
            {
                ApplyEnforcement(recovered: true, recoveryReason: reason);
            }
            else
            {
                RemoveAllEnforcers();
                PersistState(false);
            }
        }

        public object GetStatus()
        {
            var cfg = _config.Config.NetworkLock;
            var filters = GetFilterSnapshot();
            var state = _stateStore.Snapshot();
            lock (_lock)
            {
                return new
                {
                    mode = cfg.Mode.ToApiString(),
                    enforcementActive = _enforcementActive,
                    lanAccess = new
                    {
                        enabled = cfg.LanAccessEnabled,
                        exceptions = cfg.LanExceptions ?? new List<string>(),
                    },
                    dnsPolicy = new
                    {
                        policy = cfg.DnsPolicy ?? "strict",
                        exceptions = cfg.DnsExceptions ?? new List<string>(),
                        allowDhcp = cfg.AllowDhcp,
                    },
                    activeTunnels = _connectedTunnels.Where(n => QualifiesForAuto(n)).ToList(),
                    diagnostics = GetDiagnosticsInternal(filters, state),
                    lastRecovery = state.LastRecoveryAt.HasValue
                        ? new { at = state.LastRecoveryAt, reason = state.LastRecoveryReason }
                        : null,
                    config = cfg,
                };
            }
        }

        public object GetDiagnostics()
        {
            var filters = GetFilterSnapshot();
            var state = _stateStore.Snapshot();
            return GetDiagnosticsInternal(filters, state);
        }

        public void Enable()
        {
            _config.Config.NetworkLock.Mode = NetworkLockMode.AlwaysOn;
            _config.Save();
            ApplyEnforcement();
            PublishPolicyChanged();
        }

        public void Disable()
        {
            _config.Config.NetworkLock.Mode = NetworkLockMode.Disabled;
            _config.Save();
            RemoveEnforcement();
            PublishPolicyChanged();
        }

        public void SetMode(NetworkLockMode mode)
        {
            _config.Config.NetworkLock.Mode = mode;
            _config.Save();
            Reevaluate();
            PublishPolicyChanged();
        }

        public void SetLanAccess(bool enabled, IReadOnlyList<string>? exceptions = null)
        {
            var nl = _config.Config.NetworkLock;
            nl.LanAccessEnabled = enabled;
            if (exceptions != null)
                nl.LanExceptions = exceptions.ToList();
            _config.Save();
            Reevaluate();
            PublishPolicyChanged();
        }

        public void SetDnsPolicy(string policy, IReadOnlyList<string>? exceptions = null)
        {
            var nl = _config.Config.NetworkLock;
            nl.DnsPolicy = string.IsNullOrWhiteSpace(policy) ? "strict" : policy;
            if (exceptions != null)
                nl.DnsExceptions = exceptions.ToList();
            _config.Save();
            Reevaluate();
            PublishPolicyChanged();
        }

        public void ApplyConfigPatch(NetworkLockConfig patch)
        {
            if (patch.Enabled == true && patch.Mode == NetworkLockMode.Disabled)
                patch.Mode = NetworkLockMode.Auto;
            patch.Enabled = null;
            _config.Config.NetworkLock = patch;
            _config.Save();
            Reevaluate();
            PublishPolicyChanged();
        }

        public void OnTunnelConnected(StoredTunnel tunnel, string? endpointIp)
        {
            lock (_lock) { RegisterTunnelLocked(tunnel, endpointIp); }
            Reevaluate();
        }

        public void OnTunnelDisconnected(string tunnelName)
        {
            lock (_lock)
            {
                _connectedTunnels.Remove(tunnelName);
                _tunnelRules.Remove(tunnelName);
            }
            Reevaluate();
        }

        private void RegisterTunnelLocked(StoredTunnel tunnel, string? endpointIp = null)
        {
            _connectedTunnels.Add(tunnel.Name);
            if (string.IsNullOrEmpty(endpointIp))
            {
                try
                {
                    var plain = TunnelService.DecryptConfig(tunnel);
                    endpointIp = plain != null ? FirewallEnforcer.ParseEndpointIp(plain) : null;
                }
                catch { /* best effort */ }
            }

            _tunnelRules[tunnel.Name] = new TunnelAllowRule
            {
                Name              = tunnel.Name,
                AdapterName       = tunnel.Name,
                EndpointIp        = endpointIp,
                KillSwitchTrigger = tunnel.KillSwitch || _config.Config.KillSwitchMode == "always",
            };
        }

        private void Reevaluate()
        {
            if (ShouldEnforce())
                ApplyEnforcement();
            else
                RemoveEnforcement();
        }

        /// <summary>Re-apply enforcement after config changes (e.g. WFP delegation toggle).</summary>
        public void ReevaluateFromConfig() => Reevaluate();

        private bool ShouldEnforce()
        {
            var mode = _config.Config.NetworkLock.Mode;
            if (mode == NetworkLockMode.Disabled) return false;
            if (mode == NetworkLockMode.AlwaysOn) return true;

            lock (_lock)
            {
                return _connectedTunnels.Any(QualifiesForAuto);
            }
        }

        private bool QualifiesForAuto(string tunnelName)
        {
            if (!_tunnelRules.TryGetValue(tunnelName, out var rule)) return false;
            if (_config.Config.KillSwitchMode == "always") return true;
            return rule.KillSwitchTrigger;
        }

        private IReadOnlyList<TunnelAllowRule> BuildAllowRules()
        {
            lock (_lock)
            {
                var mode = _config.Config.NetworkLock.Mode;
                if (mode == NetworkLockMode.AlwaysOn)
                    return _tunnelRules.Values.ToList();

                return _tunnelRules.Values.Where(r => r.KillSwitchTrigger).ToList();
            }
        }

        private void ApplyEnforcement(bool recovered = false, string? recoveryReason = null)
        {
            var policy = NetworkLockPolicy.FromConfig(_config.Config.NetworkLock);
            if (policy.Mode == NetworkLockMode.Disabled)
            {
                RemoveEnforcement();
                return;
            }

            var tunnels = BuildAllowRules();
            ApplyEnforcers(policy, tunnels);

            lock (_lock) { _enforcementActive = true; }

            var hash = ComputePolicyHash(policy, tunnels);
            PersistState(true, hash, recovered, recoveryReason);

            if (recovered)
            {
                _eventBus.Publish(AgentEventTypes.NetworkLockRecovered, new
                {
                    mode = policy.Mode.ToApiString(),
                    tunnelCount = tunnels.Count,
                    reason = recoveryReason,
                });
                _log.Ok("[NetworkLock] Recovered enforcement on startup");
            }
            else
            {
                _eventBus.Publish(AgentEventTypes.NetworkLockEnabled, new
                {
                    mode = policy.Mode.ToApiString(),
                    tunnelCount = tunnels.Count,
                });
            }

            _eventBus.Publish(AgentEventTypes.KillSwitchChanged, BuildKillSwitchPayload());
        }

        private void RemoveEnforcement()
        {
            RemoveAllEnforcers();
            lock (_lock) { _enforcementActive = false; }
            PersistState(false);
            _eventBus.Publish(AgentEventTypes.NetworkLockDisabled, new { });
            _eventBus.Publish(AgentEventTypes.KillSwitchChanged, BuildKillSwitchPayload());
        }

        private void PersistState(bool active, string? policyHash = null, bool recovery = false, string? recoveryReason = null)
        {
            var mode = _config.Config.NetworkLock.Mode.ToApiString();
            _stateStore.Save(s =>
            {
                s.Mode = mode;
                s.EnforcementActive = active;
                s.ActiveTunnels = _connectedTunnels.ToList();
                s.LastPolicyHash = policyHash ?? s.LastPolicyHash;
                if (recovery)
                {
                    s.LastRecoveryAt = DateTime.UtcNow;
                    s.LastRecoveryReason = recoveryReason;
                }
            });
        }

        private void PublishPolicyChanged()
        {
            var cfg = _config.Config.NetworkLock;
            _eventBus.Publish(AgentEventTypes.NetworkLockPolicyChanged, new
            {
                mode = cfg.Mode.ToApiString(),
                lanAccessEnabled = cfg.LanAccessEnabled,
                dnsPolicy = cfg.DnsPolicy,
            });
            _eventBus.Publish(AgentEventTypes.KillSwitchChanged, BuildKillSwitchPayload());
        }

        private object BuildKillSwitchPayload()
        {
            lock (_lock)
            {
                return new
                {
                    mode = _config.Config.KillSwitchMode,
                    activeTunnels = _connectedTunnels.Where(QualifiesForAuto).ToList(),
                    networkLock = _config.Config.NetworkLock,
                    enforcementActive = _enforcementActive,
                };
            }
        }

        public object AgentStatusSlice()
        {
            var filters = GetFilterSnapshot();
            var state = _stateStore.Snapshot();
            lock (_lock)
            {
                return new
                {
                    mode = _config.Config.NetworkLock.Mode.ToApiString(),
                    enforcementActive = _enforcementActive,
                    leakProtection = filters.LeakProtection,
                    activeFilterCount = filters.ActiveRules.Count,
                    lastRecoveryAt = state.LastRecoveryAt,
                    recoveryState = state.EnforcementActive == _enforcementActive ? "ok" : "pending",
                };
            }
        }

        private static object GetDiagnosticsInternal(NetworkLockFilterSnapshot filters, NetworkLockRuntimeState state)
        {
            return new
            {
                activeFilterCount = filters.ActiveRules.Count,
                ruleNames = filters.ActiveRules,
                globalBlockActive = filters.GlobalBlockActive,
                leakProtection = filters.LeakProtection,
                lastPolicyHash = state.LastPolicyHash,
                recoveryState = state.EnforcementActive ? "active" : "inactive",
            };
        }

        private static string ComputePolicyHash(NetworkLockPolicy policy, IReadOnlyList<TunnelAllowRule> tunnels)
        {
            var json = JsonSerializer.Serialize(new { policy, tunnels });
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(hash)[..12].ToLowerInvariant();
        }
    }
}
