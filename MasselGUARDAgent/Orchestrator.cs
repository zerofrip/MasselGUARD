using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MasselGUARD;
using MasselGUARD.Agent.Events;
using MasselGUARD.Cli;
using MasselGUARD.Models;
using MasselGUARD.Services;

namespace MasselGUARD.Agent
{
    /// <summary>
    /// WPF-free orchestration extracted from MainViewModel.
    /// Owns Wi-Fi rule evaluation, status polling, and auto-reconnect.
    /// </summary>
    public sealed class Orchestrator : IDisposable
    {
        private readonly ConfigService _config;
        private readonly TunnelService _tunnels;
        private readonly LogService _log;
        private readonly WiFiService _wifi;
        private readonly RuleEngine _rules;
        private readonly HistoryService _history;
        private readonly AgentEventBus _eventBus;
        private readonly TunnelSnapshotCache _snapshotCache;
        private readonly NetworkLockService? _networkLock;
        private readonly Ipc.RouteGuard.RouteGuardBridgeService? _routeGuardBridge;

        private readonly HashSet<string> _reconnecting = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _connectedAt = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _lastActive = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (long rx, long tx, DateTime at)> _rateBaseline =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _userDisconnected = new(StringComparer.OrdinalIgnoreCase);

        private Timer? _pollTimer;
        private Timer? _disconnectDebounce;
        private string? _currentSsid;
        private bool _currentIsOpen;
        private int _statusTick;
        private string? _cachedPublicIp;
        private DateTime _publicIpFetched = DateTime.MinValue;

        private const int StatsPollIdleEveryTicks = 5;
        private const int AutoReconnectMaxAttempts = 3;

        public Orchestrator(
            ConfigService config,
            TunnelService tunnels,
            LogService log,
            WiFiService wifi,
            RuleEngine rules,
            HistoryService history,
            AgentEventBus eventBus,
            TunnelSnapshotCache snapshotCache,
            NetworkLockService? networkLock = null,
            Ipc.RouteGuard.RouteGuardBridgeService? routeGuardBridge = null)
        {
            _config        = config;
            _tunnels       = tunnels;
            _log           = log;
            _wifi          = wifi;
            _rules         = rules;
            _history       = history;
            _eventBus      = eventBus;
            _snapshotCache = snapshotCache;
            _networkLock   = networkLock;
            _routeGuardBridge = routeGuardBridge;

            _log.EntryAdded += OnLogEntry;
            _wifi.SsidChanged += (ssid, isOpen) => ApplyWifiState(ssid, isOpen);

            _pollTimer = new Timer(_ => PollTick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public void Start()
        {
            _wifi.Start();
            InitialWifiCheck();
            RefreshTunnelStatus();
        }

        public string? CurrentSsid => _currentSsid;
        public bool CurrentIsOpen => _currentIsOpen;

        public void InitialWifiCheck()
        {
            var (ssid, isOpen) = _wifi.QueryCurrentSsid();
            ApplyWifiState(ssid, isOpen);
        }

        public void ApplyWifiState(string? ssid, bool isOpen)
        {
            if (ssid == null && _currentSsid == null) return;

            if (ssid == null)
            {
                _disconnectDebounce?.Dispose();
                _disconnectDebounce = new Timer(_ =>
                {
                    var (live, liveOpen) = _wifi.QueryCurrentSsid();
                    if (!string.IsNullOrEmpty(live))
                    {
                        if (live != _currentSsid)
                            ApplyWifiState(live, liveOpen);
                        return;
                    }

                    _currentSsid = null;
                    _currentIsOpen = false;
                    if (_config.Config.StoreWifiHistory)
                        _history.RecordSsidDisconnect();
                    _log.Info("WiFi disconnected");
                    PublishWifiChanged(null, false);
                    var result = _rules.EvaluateWifiDisconnected(_config.Config);
                    ApplyRuleResult(result);
                }, null, 2000, Timeout.Infinite);
                return;
            }

            _disconnectDebounce?.Dispose();
            _disconnectDebounce = null;

            if (ssid == _currentSsid) return;

            _currentSsid = ssid;
            _currentIsOpen = isOpen;
            if (_config.Config.StoreWifiHistory)
                _history.RecordSsidConnect(ssid, isOpen);
            _log.Info($"WiFi: {ssid}{(isOpen ? " (open)" : "")}");
            PublishWifiChanged(ssid, isOpen);

            if (_config.Config.ManualMode) return;

            var r = _rules.EvaluateWifi(_config.Config, ssid, isOpen);
            ApplyRuleResult(r);
        }

        private void PublishWifiChanged(string? ssid, bool isOpen) =>
            _eventBus.Publish(AgentEventTypes.WifiSsidChanged, new { ssid, isOpen });

        public void ApplyRuleResult(RuleEngine.RuleResult result)
        {
            if (result.Action == RuleEngine.ActionKind.None) return;

            _log.Info($"Automation: {result.Reason}");
            _eventBus.Publish(AgentEventTypes.WifiRuleApplied, new
            {
                action = result.Action.ToString().ToLowerInvariant(),
                tunnel = result.TunnelName,
                reason = result.Reason,
            });

            if (result.Action == RuleEngine.ActionKind.Disconnect)
            {
                foreach (var t in ActiveTunnels())
                    DisconnectTunnel(t, "WiFi rule");
                MaybeNotify("Disconnected", result.Reason);
                return;
            }

            var target = FindTunnel(result.TunnelName);
            if (target == null)
            {
                _log.Warn($"Tunnel not found: {result.TunnelName}");
                _eventBus.Publish(AgentEventTypes.ConnectionFailed, new { tunnel = result.TunnelName, reason = "not found" });
                return;
            }

            foreach (var t in ActiveTunnels().Where(t => t.Name != target.Name))
                DisconnectTunnel(t, "WiFi rule switch");

            if (!_tunnels.IsActive(target))
                ConnectTunnel(target, result.Reason);

            MaybeNotify(target.Name, result.Reason);
        }

        private void MaybeNotify(string primary, string secondary)
        {
            if (!_config.Config.ShowTrayPopupOnSwitch) return;
            _eventBus.Publish(AgentEventTypes.Notification, new
            {
                category = "Automation",
                primary,
                secondary,
                durationMs = _config.Config.NotificationDurationSeconds * 1000,
            });
        }

        public bool ConnectTunnel(StoredTunnel tunnel, string source = "Manual")
        {
            _userDisconnected.Remove(tunnel.Name);
            string? endpoint = null;
            try
            {
                var plain = TunnelService.DecryptConfig(tunnel);
                endpoint = Cli.WireGuardConf.ExtractPrimaryEndpoint(plain ?? "");
            }
            catch { /* best effort */ }

            var ok = _tunnels.Connect(tunnel, _config.Config, source);
            if (ok)
            {
                tunnel.LastUsedAt = DateTime.UtcNow;
                tunnel.ConnectionCount++;
                if (!string.IsNullOrEmpty(endpoint))
                    tunnel.EndpointSummary = endpoint;
                _config.Save();

                _connectedAt[tunnel.Name] = DateTime.UtcNow;
                _snapshotCache.SetActive(tunnel.Name, true);
                _networkLock?.OnTunnelConnected(tunnel, endpoint);
                PushRouteGuardTunnelContext(tunnel, endpoint, connected: true);
                _eventBus.Publish(AgentEventTypes.TunnelStateChanged, new { name = tunnel.Name, state = "connected", source });
                _eventBus.Publish(AgentEventTypes.Notification, new
                {
                    category = "Tunnel",
                    primary = "Connected",
                    secondary = tunnel.Name,
                    durationMs = _config.Config.NotificationDurationSeconds * 1000,
                });
            }
            else
            {
                _history.RecordConnectFailure(tunnel.Name, "connect failed", endpoint);
                _eventBus.Publish(AgentEventTypes.ConnectionFailed, new { tunnel = tunnel.Name, reason = "connect failed" });
            }
            RefreshTunnelStatus();
            return ok;
        }

        public bool DisconnectTunnel(StoredTunnel tunnel, string source = "Manual")
        {
            _userDisconnected.Add(tunnel.Name);
            var ok = _tunnels.Disconnect(tunnel, _config.Config);
            _connectedAt.Remove(tunnel.Name);
            _rateBaseline.Remove(tunnel.Name);
            _snapshotCache.SetActive(tunnel.Name, false);
            _networkLock?.OnTunnelDisconnected(tunnel.Name);
            PushRouteGuardTunnelContext(tunnel, null, connected: false);
            _eventBus.Publish(AgentEventTypes.TunnelStateChanged, new { name = tunnel.Name, state = "disconnected", source });
            _eventBus.Publish(AgentEventTypes.Notification, new
            {
                category = "Tunnel",
                primary = "Disconnected",
                secondary = tunnel.Name,
                durationMs = _config.Config.NotificationDurationSeconds * 1000,
            });
            RefreshTunnelStatus();
            return ok;
        }

        public bool ReconnectTunnel(StoredTunnel tunnel, string source = "Manual")
        {
            if (_tunnels.IsActive(tunnel))
                DisconnectTunnel(tunnel, source);
            return ConnectTunnel(tunnel, source);
        }

        private void PollTick() => RefreshTunnelStatus();

        public void RefreshTunnelStatus()
        {
            _statusTick++;
            bool anyActive = VisibleTunnels().Any(t => _tunnels.IsActive(t));
            bool doStats = anyActive || (_statusTick % StatsPollIdleEveryTicks == 0);

            foreach (var stored in VisibleTunnels())
            {
                bool wasActive = _lastActive.TryGetValue(stored.Name, out var prev) && prev;
                bool nowActive = _tunnels.IsActive(stored);
                _lastActive[stored.Name] = nowActive;

                if (doStats && nowActive)
                {
                    var stats = TunnelDll.GetTrafficStats(stored.Name);
                    var runtime = stored.Source == "local"
                        ? TunnelDll.GetRuntimeStats(stored.Name)
                        : default;

                    if (stored.Source == "local")
                    {
                        bool localDrop = !stats.AdapterFound
                            && _connectedAt.TryGetValue(stored.Name, out var t0)
                            && (DateTime.UtcNow - t0).TotalSeconds > 30;

                        if (localDrop && !IsIntentionalDrop(stored.Name)
                            && TunnelService.ShouldAutoReconnect(stored, _config.Config))
                        {
                            TunnelDll.ForceMarkDisconnected(stored.Name);
                            nowActive = false;
                            _lastActive[stored.Name] = false;
                            _ = AutoReconnectAsync(stored);
                        }
                    }

                    if (nowActive)
                    {
                        bool statsDelta = _snapshotCache.ShouldPublishStats(stored.Name, stats, runtime);
                        bool handshakeDelta = _snapshotCache.ShouldPublishHandshake(stored.Name, runtime);

                        if (statsDelta || handshakeDelta)
                            _snapshotCache.UpdateStats(stored.Name, stats, runtime, true);

                        if (statsDelta)
                        {
                            var (rxRate, txRate) = ComputeRates(stored.Name, stats.RxBytes, stats.TxBytes);
                            _eventBus.Publish(AgentEventTypes.TunnelStatsUpdated, new
                            {
                                name = stored.Name,
                                rxBytes = stats.RxBytes,
                                txBytes = stats.TxBytes,
                                rxRate,
                                txRate,
                                adapterUp = stats.AdapterUp,
                            });
                        }

                        if (handshakeDelta)
                        {
                            _eventBus.Publish(AgentEventTypes.TunnelHandshakeUpdated, new
                            {
                                name = stored.Name,
                                peerCount = runtime.PeerCount,
                                lastHandshakeSecsAgo = runtime.LastHandshakeSecsAgo,
                            });
                        }
                    }
                }

                if (stored.Source != "local" && !wasActive && nowActive)
                {
                    _userDisconnected.Remove(stored.Name);
                    _tunnels.RecordExternalConnect(stored.Name, "WireGuard app");
                    _connectedAt[stored.Name] = DateTime.UtcNow;
                    _snapshotCache.SetActive(stored.Name, true);
                    string? endpoint = null;
                    try
                    {
                        var plain = TunnelService.DecryptConfig(stored);
                        endpoint = plain != null ? Cli.WireGuardConf.ExtractPrimaryEndpoint(plain) : null;
                    }
                    catch { /* best effort */ }
                    _networkLock?.OnTunnelConnected(stored, endpoint);
                    PushRouteGuardTunnelContext(stored, endpoint, connected: true);
                    _eventBus.Publish(AgentEventTypes.TunnelStateChanged, new { name = stored.Name, state = "connected", source = "external" });
                }

                if (stored.Source != "local" && wasActive && !nowActive && !IsIntentionalDrop(stored.Name))
                {
                    _tunnels.RecordExternalDisconnect(stored.Name);
                    _connectedAt.Remove(stored.Name);
                    _rateBaseline.Remove(stored.Name);
                    _snapshotCache.SetActive(stored.Name, false);
                    _networkLock?.OnTunnelDisconnected(stored.Name);
                    PushRouteGuardTunnelContext(stored, null, connected: false);
                    _eventBus.Publish(AgentEventTypes.TunnelStateChanged, new { name = stored.Name, state = "disconnected", source = "external" });

                    var svcName = "WireGuardTunnel$" + stored.Name;
                    if (TunnelService.WireGuardServiceExists(svcName)
                        && TunnelService.ShouldAutoReconnect(stored, _config.Config))
                        _ = AutoReconnectAsync(stored);
                }
            }
        }

        private (double rxRate, double txRate) ComputeRates(string name, long rx, long tx)
        {
            if (!_rateBaseline.TryGetValue(name, out var prev))
            {
                _rateBaseline[name] = (rx, tx, DateTime.UtcNow);
                return (0, 0);
            }

            var dt = (DateTime.UtcNow - prev.at).TotalSeconds;
            if (dt <= 0) return (0, 0);

            var rxRate = (rx - prev.rx) / dt;
            var txRate = (tx - prev.tx) / dt;
            _rateBaseline[name] = (rx, tx, DateTime.UtcNow);
            return (rxRate, txRate);
        }

        private bool IsIntentionalDrop(string name) =>
            _tunnels.ConsumeIntentionalDisconnect(name) || _userDisconnected.Contains(name);

        private async Task AutoReconnectAsync(StoredTunnel stored)
        {
            lock (_reconnecting)
            {
                if (_reconnecting.Contains(stored.Name)) return;
                _reconnecting.Add(stored.Name);
            }

            try
            {
                if (stored.Source != "local")
                {
                    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    if (_tunnels.IsActive(stored)) return;
                    if (!TunnelService.WireGuardServiceExists("WireGuardTunnel$" + stored.Name))
                    {
                        _log.Info($"[AutoReconnect] '{stored.Name}' was deactivated via the WireGuard app — not reconnecting.");
                        return;
                    }
                }

                for (int attempt = 1; attempt <= AutoReconnectMaxAttempts; attempt++)
                {
                    int delaySec = attempt * 5;
                    _log.Info($"[AutoReconnect] '{stored.Name}' dropped — reconnecting in {delaySec}s (attempt {attempt}/{AutoReconnectMaxAttempts})…");
                    await Task.Delay(TimeSpan.FromSeconds(delaySec)).ConfigureAwait(false);

                    if (_tunnels.IsActive(stored) || IsIntentionalDrop(stored.Name)) return;
                    if (!TunnelService.ShouldAutoReconnect(stored, _config.Config)) return;
                    if (stored.Source != "local" && !TunnelService.WireGuardServiceExists("WireGuardTunnel$" + stored.Name))
                    {
                        _log.Info($"[AutoReconnect] '{stored.Name}' was deactivated via the WireGuard app — not reconnecting.");
                        return;
                    }

                    var ok = ConnectTunnel(stored, "Auto-reconnect");
                    if (ok)
                    {
                        _log.Ok($"[AutoReconnect] '{stored.Name}' reconnected successfully.");
                        return;
                    }
                    _log.Warn($"[AutoReconnect] '{stored.Name}' — attempt {attempt} failed.");
                }

                _log.Warn($"[AutoReconnect] '{stored.Name}' — giving up after {AutoReconnectMaxAttempts} attempts.");
            }
            finally
            {
                lock (_reconnecting) { _reconnecting.Remove(stored.Name); }
            }
        }

        private void OnLogEntry(Models.LogEntry entry) =>
            _eventBus.Publish(AgentEventTypes.LogEntry, new
            {
                time = entry.Timestamp,
                level = entry.Level.ToString().ToLowerInvariant(),
                message = entry.Message,
            });

        public IEnumerable<StoredTunnel> VisibleTunnels() =>
            _config.Config.Tunnels.Where(t => IsSourceAllowed(t.Source));

        private bool IsSourceAllowed(string source) => _config.Config.Mode switch
        {
            AppMode.Standalone => source == "local",
            AppMode.Companion  => source != "local",
            _                  => true,
        };

        public StoredTunnel? FindTunnel(string name) =>
            _config.Config.Tunnels.FirstOrDefault(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<StoredTunnel> ActiveTunnels() =>
            VisibleTunnels().Where(t => _tunnels.IsActive(t));

        public string? GetConnectedSince(string name) =>
            _connectedAt.TryGetValue(name, out var t) ? t.ToString("o") : null;

        public async Task<string?> FetchPublicIpAsync()
        {
            if (_cachedPublicIp != null && (DateTime.UtcNow - _publicIpFetched).TotalMinutes < 5)
                return _cachedPublicIp;

            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                _cachedPublicIp = (await http.GetStringAsync("https://api.ipify.org").ConfigureAwait(false)).Trim();
                _publicIpFetched = DateTime.UtcNow;
            }
            catch { /* best effort */ }

            return _cachedPublicIp;
        }

        public void InvalidatePublicIpCache()
        {
            _cachedPublicIp = null;
            _publicIpFetched = DateTime.MinValue;
        }

        private void PushRouteGuardTunnelContext(StoredTunnel tunnel, string? endpoint, bool connected)
        {
            if (_routeGuardBridge == null) return;
            if (connected)
            {
                _routeGuardBridge.PushTunnelContext(new Ipc.RouteGuard.TunnelContextDto
                {
                    Name         = tunnel.Name,
                    AdapterName  = tunnel.Name,
                    EndpointIp   = endpoint,
                    Connected    = true,
                });
            }
            else
            {
                _routeGuardBridge.RemoveTunnelContext(tunnel.Name);
            }
        }

        public void Dispose()
        {
            _pollTimer?.Dispose();
            _disconnectDebounce?.Dispose();
            _log.EntryAdded -= OnLogEntry;
            _wifi.Dispose();
        }
    }
}
