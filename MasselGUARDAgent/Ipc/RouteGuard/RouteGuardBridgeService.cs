using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MasselGUARD.Agent.Events;
using MasselGUARD.Models;
using MasselGUARD.Services;
using Microsoft.Win32;

namespace MasselGUARD.Agent.Ipc.RouteGuard
{
    /// <summary>
    /// RouteGuard integration: availability FSM, rule reconcile, event relay.
    /// </summary>
    public sealed class RouteGuardBridgeService : IDisposable
    {
        private readonly ConfigService _config;
        private readonly LogService _log;
        private readonly AgentEventBus _eventBus;
        private readonly RouteGuardPipeClient _client = new();
        private readonly object _lock = new();

        private RouteGuardAvailability _availability = RouteGuardAvailability.Absent;
        private JsonElement? _remoteCapabilities;
        private string? _lastPolicyHash;
        private DateTime? _lastSyncAt;
        private string? _lastSyncError;
        private ulong _lastEventId;
        private int _lastDomainRules;
        private int _lastDomainRoutes;
        private int _lastResolvedIps;
        private bool _lastDomainEffective;
        private bool _lastKernelRedirect;
        private bool _lastDriverPresent;
        private ulong _lastEventId;
        private int? _lastHealthScore;
        private JsonElement? _lastRedirectStats;
        private JsonElement? _lastObservability;
        private Timer? _healthTimer;
        private Timer? _eventTimer;
        private readonly List<TunnelContextDto> _tunnelContexts = new();

        public RouteGuardBridgeService(
            ConfigService config,
            LogService log,
            AgentEventBus eventBus)
        {
            _config   = config;
            _log      = log;
            _eventBus = eventBus;
        }

        public void Start()
        {
            DetectAvailability(publish: false);
            _healthTimer = new Timer(_ => DetectAvailability(publish: true), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            _eventTimer  = new Timer(_ => PollEvents(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));
        }

        public void Dispose()
        {
            _healthTimer?.Dispose();
            _eventTimer?.Dispose();
        }

        public RouteGuardAvailability Availability
        {
            get { lock (_lock) return _availability; }
        }

        public object GetStatus()
        {
            lock (_lock)
            {
                return new
                {
                    availability = _availability.ToString().ToLowerInvariant(),
                    pipe = $@"\\.\pipe\{RouteGuardPipeClient.PipeName}",
                    installPath = ResolveInstallPath(),
                    remote = _remoteCapabilities.HasValue
                        ? JsonSerializer.Deserialize<object>(_remoteCapabilities.Value.GetRawText())
                        : null,
                    bridge = new
                    {
                        schemaVersion = 1,
                        lastSyncAt = _lastSyncAt,
                        lastSyncError = _lastSyncError,
                        lastPolicyHash = _lastPolicyHash,
                        eventBridgeReady = true,
                        lastEventId = _lastEventId,
                        lastDomainSync = new
                        {
                            rules = _lastDomainRules,
                            resolvedIps = _lastResolvedIps,
                            routes = _lastDomainRoutes,
                            effective = _lastDomainEffective,
                        },
                    },
                    domain = new
                    {
                        rules = _lastDomainRules,
                        resolvedIps = _lastResolvedIps,
                        effective = _lastDomainEffective,
                        kernelRedirect = _lastKernelRedirect,
                        driverPresent = _lastDriverPresent,
                        redirectStats = _lastRedirectStats.HasValue
                            ? JsonSerializer.Deserialize<object>(_lastRedirectStats.Value.GetRawText())
                            : null,
                    },
                    observability = _lastObservability.HasValue
                        ? JsonSerializer.Deserialize<object>(_lastObservability.Value.GetRawText())
                        : null,
                    health = _lastHealthScore.HasValue
                        ? new { score = _lastHealthScore.Value }
                        : null,
                    negotiated = BuildNegotiated(),
                };
            }
        }

        public object GetCapabilities() => new { negotiated = BuildNegotiated(), remote = GetStatus() };

        public object GetObservabilitySnapshot()
        {
            if (_availability != RouteGuardAvailability.Running)
                throw new InvalidOperationException("RouteGuard not running");
            var result = _client.Call("observability.snapshot", new { });
            lock (_lock)
            {
                if (result.HasValue) _lastObservability = result;
            }
            return result.HasValue
                ? JsonSerializer.Deserialize<object>(result.Value.GetRawText()) ?? new { }
                : new { };
        }

        public object ExportDiagnostics(string tier = "sanitized", string? crashReportsDir = null)
        {
            if (_availability != RouteGuardAvailability.Running)
                throw new InvalidOperationException("RouteGuard not running");
            var rg = ExportDiagnosticsRaw(tier);

            if (string.IsNullOrEmpty(crashReportsDir) || !Directory.Exists(crashReportsDir))
                return rg;

            var crashes = Directory.GetFiles(crashReportsDir, "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(20)
                .Select(f => new { name = Path.GetFileName(f), content = File.ReadAllText(f) })
                .ToList();

            return new { routeGuard = rg, agentCrashes = crashes, exportTs = DateTime.UtcNow.ToString("o") };
        }

        /// <summary>RouteGuard diagnostics.export result only (path at top level).</summary>
        public object ExportDiagnosticsRaw(string tier = "sanitized")
        {
            if (_availability != RouteGuardAvailability.Running)
                throw new InvalidOperationException("RouteGuard not running");
            var result = _client.Call("diagnostics.export", new
            {
                tier,
                includeEvents = true,
                eventLimit = 500,
                includeHistory = true,
                historyWindow = "1h",
            });
            return result.HasValue
                ? JsonSerializer.Deserialize<object>(result.Value.GetRawText()) ?? new { }
                : new { };
        }

        public object Sync(bool force = false)
        {
            if (!_config.Config.SplitTunnel.UseRouteGuardBridge)
                return new { ok = true, skipped = true, reason = "bridge_disabled" };

            DetectAvailability(publish: true);
            if (_availability != RouteGuardAvailability.Running)
                return new { ok = false, errors = new[] { "RouteGuard not running" } };

            lock (_lock)
            {
                var rules = _config.Config.SplitTunnel;
                var hash = RouteGuardRuleProjector.ComputeHash(rules, _tunnelContexts);
                if (!force && hash == _lastPolicyHash)
                    return new { ok = true, skipped = true, rulesApplied = 0 };

                try
                {
                    var payload = RouteGuardRuleProjector.BuildImportPayload(rules, _tunnelContexts);
                    var result = _client.Call("routing.import_rules", payload);
                    _lastPolicyHash = hash;
                    _lastSyncAt = DateTime.UtcNow;
                    _lastSyncError = null;

                    var counts = ParseImportCounts(result);
                    _lastDomainRules = counts.domain;
                    _lastDomainRoutes = counts.domainRoutes;
                    _lastResolvedIps = counts.domainRoutes;
                    RefreshDomainStatus();
                    _eventBus.Publish("routeguard.sync_completed", new { ok = true, rulesApplied = counts.total, counts });
                    _eventBus.Publish(AgentEventTypes.RouteGuardRoutingChanged, new { reason = "sync", ruleCount = counts.total });

                    return new { ok = true, rulesApplied = counts.total, counts };
                }
                catch (Exception ex)
                {
                    _lastSyncError = ex.Message;
                    _log.Warn($"[RouteGuard] Sync failed: {ex.Message}");
                    _eventBus.Publish("routeguard.sync_completed", new { ok = false, errors = new[] { ex.Message } });
                    return new { ok = false, errors = new[] { ex.Message } };
                }
            }
        }

        public object RoutingTest(string? appPath, string remoteIp, string? domain)
        {
            if (_availability != RouteGuardAvailability.Running)
                throw new InvalidOperationException("RouteGuard not running");

            var result = _client.Call("routing.test", new
            {
                appPath,
                remoteIp,
                domain,
            });
            return result.HasValue
                ? JsonSerializer.Deserialize<object>(result.Value.GetRawText()) ?? new { }
                : new { };
        }

        public bool TryStart(int waitSecs = 10)
        {
            var exe = Path.Combine(ResolveInstallPath(), "routeguard-service.exe");
            if (!File.Exists(exe))
                throw new FileNotFoundException("routeguard-service.exe not found", exe);

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--console",
                UseShellExecute = true,
                Verb = "runas",
            });

            var deadline = DateTime.UtcNow.AddSeconds(waitSecs);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(500);
                DetectAvailability(publish: true);
                if (_availability == RouteGuardAvailability.Running)
                    return true;
            }
            return false;
        }

        public void ReconcileOnStartup() => Sync(force: true);

        public void SyncIfEnabled()
        {
            if (_config.Config.SplitTunnel.UseRouteGuardBridge)
                Sync(force: false);
        }

        public void PushTunnelContext(TunnelContextDto ctx)
        {
            lock (_lock)
            {
                var idx = _tunnelContexts.FindIndex(c =>
                    string.Equals(c.Name, ctx.Name, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) _tunnelContexts[idx] = ctx;
                else _tunnelContexts.Add(ctx);
            }

            if (!_config.Config.SplitTunnel.UseRouteGuardBridge) return;
            if (_availability != RouteGuardAvailability.Running) return;

            try
            {
                _client.Call("routing.set_tunnel_context", new
                {
                    name = ctx.Name,
                    adapterName = ctx.AdapterName ?? ctx.Name,
                    ifIndex = ctx.IfIndex,
                    endpointIp = ctx.EndpointIp,
                    connected = ctx.Connected,
                    transportKind = ctx.TransportKind,
                    transportRemote = ctx.TransportRemote,
                });
                Sync(force: true);
            }
            catch (Exception ex)
            {
                _log.Debug($"[RouteGuard] PushTunnelContext: {ex.Message}");
            }
        }

        public void RemoveTunnelContext(string name)
        {
            lock (_lock)
            {
                _tunnelContexts.RemoveAll(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            }

            PushTunnelContext(new TunnelContextDto { Name = name, Connected = false });
        }

        public bool IsFeatureAvailable(string feature) =>
            BuildNegotiated() is Dictionary<string, bool> n && n.TryGetValue(feature, out var v) && v;

        private void DetectAvailability(bool publish)
        {
            RouteGuardAvailability prev;
            RouteGuardAvailability next;

            lock (_lock) prev = _availability;

            if (File.Exists(Path.Combine(ResolveInstallPath(), "routeguard-service.exe"))
                || File.Exists(@"C:\Program Files\RouteGuard\routeguard-service.exe"))
            {
                next = _client.TryConnect(500)
                    ? RouteGuardAvailability.Running
                    : RouteGuardAvailability.Installed;
            }
            else if (_client.TryConnect(500))
            {
                next = RouteGuardAvailability.Running;
            }
            else
            {
                next = RouteGuardAvailability.Absent;
            }

            if (next == RouteGuardAvailability.Running)
            {
                try
                {
                    var caps = _client.Call("service.capabilities", new { });
                    lock (_lock) _remoteCapabilities = caps;
                    RefreshDomainStatus();
                }
                catch
                {
                    lock (_lock) _remoteCapabilities = null;
                }
                RefreshObservabilityCache();
            }
            else
            {
                lock (_lock) _remoteCapabilities = null;
            }

            lock (_lock) _availability = next;

            if (publish && prev != next)
            {
                _eventBus.Publish("routeguard.availability_changed", new
                {
                    availability = next.ToString().ToLowerInvariant(),
                    previous = prev.ToString().ToLowerInvariant(),
                });
            }
        }

        private void PollEvents()
        {
            if (_availability != RouteGuardAvailability.Running) return;

            try
            {
                var result = _client.Call("events.poll", new { sinceId = _lastEventId, limit = 64 }, 2000);
                if (!result.HasValue) return;

                if (!result.Value.TryGetProperty("events", out var events) ||
                    events.ValueKind != JsonValueKind.Array)
                    return;

                foreach (var evt in events.EnumerateArray())
                {
                    if (evt.TryGetProperty("id", out var idEl) && idEl.TryGetUInt64(out var id))
                    {
                        if (id > _lastEventId) _lastEventId = id;
                    }

                    var type = evt.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (string.IsNullOrEmpty(type)) continue;

                    object? payload = null;
                    JsonElement payloadEl = default;
                    if (evt.TryGetProperty("payload", out var p))
                    {
                        payloadEl = p;
                        payload = JsonSerializer.Deserialize<object>(p.GetRawText());
                    }

                    var mgType = MapEventType(type, payloadEl.ValueKind == JsonValueKind.Undefined ? null : payloadEl);
                    if (mgType != null)
                        _eventBus.Publish(mgType, payload ?? new { });

                    UpdateObservabilityFromEvent(type, payloadEl.ValueKind == JsonValueKind.Undefined ? null : payloadEl);
                    UpdateTunnelTransportFromEvent(type, payloadEl.ValueKind == JsonValueKind.Undefined ? null : payloadEl);
                }

                if (result.Value.TryGetProperty("latestId", out var latest) && latest.TryGetUInt64(out var lid))
                    _lastEventId = Math.Max(_lastEventId, lid);
            }
            catch
            {
                /* best effort poll */
            }
        }

        public object DomainStatus()
        {
            if (_availability != RouteGuardAvailability.Running)
                throw new InvalidOperationException("RouteGuard not running");
            var result = _client.Call("domain.status", new { });
            RefreshDomainStatusFrom(result);
            return result.HasValue
                ? JsonSerializer.Deserialize<object>(result.Value.GetRawText()) ?? new { }
                : new { };
        }

        private void RefreshDomainStatus()
        {
            if (_availability != RouteGuardAvailability.Running) return;
            try
            {
                var result = _client.Call("domain.status", new { }, 1500);
                RefreshDomainStatusFrom(result);
            }
            catch
            {
                /* best effort */
            }
        }

        private void RefreshDomainStatusFrom(JsonElement? result)
        {
            if (!result.HasValue) return;
            if (result.Value.TryGetProperty("effective", out var eff) &&
                (eff.ValueKind == JsonValueKind.True || eff.ValueKind == JsonValueKind.False))
                _lastDomainEffective = eff.GetBoolean();

            if (result.Value.TryGetProperty("domain", out var domain))
            {
                if (domain.TryGetProperty("rules", out var rules)) _lastDomainRules = rules.GetInt32();
                if (domain.TryGetProperty("resolvedIps", out var ips)) _lastResolvedIps = ips.GetInt32();
                if (domain.TryGetProperty("routes", out var routes)) _lastDomainRoutes = routes.GetInt32();
                else _lastDomainRoutes = _lastResolvedIps;
                if (domain.TryGetProperty("kernelRedirect", out var kr) &&
                    (kr.ValueKind == JsonValueKind.True || kr.ValueKind == JsonValueKind.False))
                    _lastKernelRedirect = kr.GetBoolean();
                if (domain.TryGetProperty("driverPresent", out var dp) &&
                    (dp.ValueKind == JsonValueKind.True || dp.ValueKind == JsonValueKind.False))
                    _lastDriverPresent = dp.GetBoolean();
                if (domain.TryGetProperty("redirectStats", out var rs) && rs.ValueKind == JsonValueKind.Object)
                    _lastRedirectStats = rs;
            }
        }

        private void RefreshObservabilityCache()
        {
            if (_availability != RouteGuardAvailability.Running) return;
            try
            {
                var health = _client.Call("service.health", new { }, 1500);
                if (health.HasValue && health.Value.TryGetProperty("score", out var score))
                    _lastHealthScore = score.GetInt32();
                var snap = _client.Call("observability.snapshot", new { sections = new[] { "health", "transport", "tunnel" } }, 2000);
                if (snap.HasValue) _lastObservability = snap;
            }
            catch
            {
                /* best effort */
            }
        }

        private void UpdateObservabilityFromEvent(string rgType, JsonElement? payload)
        {
            if (!payload.HasValue) return;
            if (string.Equals(rgType, "observability.health_changed", StringComparison.Ordinal) &&
                payload.Value.TryGetProperty("score", out var score))
            {
                _lastHealthScore = score.GetInt32();
            }
        }

        private void UpdateTunnelTransportFromEvent(string rgType, JsonElement? payload)
        {
            if (!string.Equals(rgType, "transport.connected", StringComparison.Ordinal) || !payload.HasValue)
                return;

            var p = payload.Value;
            var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(name)) return;

            var kind = p.TryGetProperty("kind", out var k) ? k.GetString() : null;
            var remote = p.TryGetProperty("remoteTransport", out var r) ? r.GetString() : null;

            TunnelContextDto? ctx;
            lock (_lock)
            {
                var idx = _tunnelContexts.FindIndex(c =>
                    string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) return;
                ctx = _tunnelContexts[idx];
                ctx.TransportKind = kind;
                ctx.TransportRemote = remote;
                _tunnelContexts[idx] = ctx;
            }

            if (ctx.Connected)
                PushTunnelContext(ctx);
        }

        private static string? MapEventType(string rgType, JsonElement? payload)
        {
            var kind = payload.HasValue && payload.Value.TryGetProperty("kind", out var k)
                ? k.GetString()
                : null;
            var requested = payload.HasValue && payload.Value.TryGetProperty("requested", out var r)
                ? r.GetString()
                : null;
            var actual = payload.HasValue && payload.Value.TryGetProperty("actual", out var a)
                ? a.GetString()
                : null;

            if (string.Equals(kind, "lwo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(requested, "lwo", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actual, "lwo", StringComparison.OrdinalIgnoreCase))
            {
                return rgType switch
                {
                    "transport.connected" => "routeguard.lwo_connected",
                    "transport.fallback" => "routeguard.lwo_fallback",
                    "transport.failed" => "routeguard.lwo_failed",
                    "transport.disconnected" => "routeguard.lwo_disconnected",
                    "transport.starting" => "routeguard.lwo_starting",
                    "transport.recovering" => "routeguard.lwo_recovering",
                    _ => null,
                };
            }

            return rgType switch
            {
                "observability.health_changed" => "observability.health_changed",
                "tunnel.stats" => "routeguard.metrics_updated",
                "tunnel.handshake" => AgentEventTypes.TunnelHandshakeUpdated,
                "transport.health_changed" => "routeguard.transport_health",
                "transport.recovery" => "routeguard.transport_recovery",
                "dns.redirect_stats" => "routeguard.dns_redirect_stats",
                "routing.reloaded" => AgentEventTypes.RouteGuardRoutingChanged,
                "routing.dns.resolved" => "routeguard.domain_resolved",
                "routing.domain_route_added" => "routeguard.domain_route_added",
                "routing.domain_route_expired" => "routeguard.domain_route_expired",
                "routing.domain_recovered" => "routeguard.domain_recovered",
                "tunnel.awg.connected" => "routeguard.awg_connected",
                "tunnel.backend_fallback" => "routeguard.awg_fallback",
                "tunnel.profile.imported" => "routeguard.profile_imported",
                "transport.connected" => "routeguard.phantun_connected",
                "transport.fallback" => "routeguard.phantun_fallback",
                "transport.failed" => "routeguard.phantun_failed",
                "transport.disconnected" => "routeguard.phantun_disconnected",
                "transport.starting" => "routeguard.phantun_starting",
                "transport.recovering" => "routeguard.phantun_recovering",
                "network_lock.enabled" or "network_lock.disabled" => AgentEventTypes.RouteGuardNetworkLockChanged,
                _ when rgType.StartsWith("routing.", StringComparison.Ordinal) => AgentEventTypes.RouteGuardRoutingChanged,
                _ => null,
            };
        }

        private object BuildNegotiated()
        {
            var running = _availability == RouteGuardAvailability.Running;
            var bridge = _config.Config.SplitTunnel.UseRouteGuardBridge;
            var features = ParseFeatures(_remoteCapabilities);

            return new
            {
                appSplitTunnel = running && bridge && features.GetValueOrDefault("appSplitTunnel"),
                ipRouting = running && bridge && features.GetValueOrDefault("ipRouting"),
                domainRouting = running && bridge && features.GetValueOrDefault("domainRoutingEffective"),
                calloutDriver = running && features.GetValueOrDefault("calloutDriver"),
                awg = running && features.GetValueOrDefault("awg"),
                transport = running && features.GetValueOrDefault("transports"),
                phantun = running && features.GetValueOrDefault("phantun"),
                lwo = running && features.GetValueOrDefault("lwo"),
                observability = running && features.GetValueOrDefault("observability"),
                diagnosticsExport = running && features.GetValueOrDefault("diagnosticsExport"),
                metricsHistory = running && features.GetValueOrDefault("metricsHistory"),
            };
        }

        private static Dictionary<string, bool> ParseFeatures(JsonElement? caps)
        {
            var d = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (!caps.HasValue || !caps.Value.TryGetProperty("features", out var f)) return d;

            foreach (var prop in f.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False)
                    d[prop.Name] = prop.Value.GetBoolean();
            }
            return d;
        }

        private static (int total, int app, int ip, int domain, int domainRoutes) ParseImportCounts(JsonElement? result)
        {
            if (!result.HasValue) return (0, 0, 0, 0, 0);
            var app = result.Value.TryGetProperty("appRules", out var a) ? a.GetInt32() : 0;
            var ip = result.Value.TryGetProperty("ipRules", out var i) ? i.GetInt32() : 0;
            var domain = result.Value.TryGetProperty("domainRules", out var d) ? d.GetInt32() : 0;
            var routes = result.Value.TryGetProperty("domainRoutes", out var r) ? r.GetInt32() : 0;
            return (app + ip + domain, app, ip, domain, routes);
        }

        private string ResolveInstallPath()
        {
            var configured = _config.Config.RouteGuardInstallPath?.Trim();
            if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
                return configured.TrimEnd('\\');

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"Software\RouteGuard");
                var reg = key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(reg) && Directory.Exists(reg))
                    return reg.TrimEnd('\\');
            }
            catch { /* non-critical */ }

            var defaultPath = @"C:\Program Files\RouteGuard";
            return Directory.Exists(defaultPath) ? defaultPath : defaultPath;
        }
    }
}
