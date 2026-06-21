using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MasselGUARD;
using MasselGUARD.Agent.Events;
using MasselGUARD.Agent.Ipc;
using MasselGUARD.Agent.Services;
using MasselGUARD.Agent.Release;
using MasselGUARD.Cli;
using MasselGUARD.Models;
using MasselGUARD.Services;

namespace MasselGUARD.Agent
{
    public sealed class RpcHandler
    {
        private readonly ConfigService _config;
        private readonly TunnelService _tunnels;
        private readonly HistoryService _history;
        private readonly NetworkLockService _networkLock;
        private readonly Orchestrator _orch;
        private readonly WiFiService _wifi;
        private readonly RuleEngine _rules;
        private readonly TunnelProfileService _profiles;
        private readonly Ipc.RouteGuard.RouteGuardBridgeService _routeGuardBridge;
        private readonly CrashReportService? _crashReports;
        private readonly LogService _log;
        private SupportBundleBuilder? _supportBundle;
        private TelemetryRollupService? _telemetry;
        private AgentEventPublisher? _publisher;
        private readonly DateTime _started = DateTime.UtcNow;

        public RpcHandler(
            ConfigService config,
            TunnelService tunnels,
            HistoryService history,
            NetworkLockService networkLock,
            Orchestrator orch,
            WiFiService wifi,
            RuleEngine rules,
            AgentEventBus eventBus,
            Ipc.RouteGuard.RouteGuardBridgeService routeGuardBridge,
            LogService log,
            CrashReportService? crashReports = null,
            AgentEventPublisher? publisher = null)
        {
            _config     = config;
            _tunnels    = tunnels;
            _history     = history;
            _networkLock = networkLock;
            _orch        = orch;
            _wifi       = wifi;
            _rules      = rules;
            _profiles   = new TunnelProfileService(config, tunnels, orch, eventBus);
            _routeGuardBridge = routeGuardBridge;
            _log = log;
            _crashReports = crashReports;
            _publisher  = publisher;
        }

        public Task<IpcResponse> HandleAsync(IpcRequest req)
        {
            try
            {
                var result = req.Method switch
                {
                    IpcMethods.AgentPing => AgentPing(),
                    IpcMethods.AgentStatus => AgentStatus(),
                    IpcMethods.AgentSubscribeInfo => AgentSubscribeInfo(),
                    IpcMethods.AgentEventReplay => AgentEventReplay(req.Params),
                    IpcMethods.AgentRequestSnapshot => AgentRequestSnapshot(),
                    IpcMethods.TunnelList => TunnelList(req.Params),
                    IpcMethods.TunnelGet => TunnelGet(req.Params),
                    IpcMethods.TunnelStatus => TunnelStatus(req.Params),
                    IpcMethods.TunnelConnect => TunnelConnect(req.Params),
                    IpcMethods.TunnelDisconnect => TunnelDisconnect(req.Params),
                    IpcMethods.TunnelReconnect => TunnelReconnect(req.Params),
                    IpcMethods.TunnelImport => TunnelImport(req.Params),
                    IpcMethods.TunnelExport => TunnelExport(req.Params),
                    IpcMethods.TunnelUpdate => TunnelUpdate(req.Params),
                    IpcMethods.TunnelDelete => TunnelDelete(req.Params),
                    IpcMethods.TunnelCreate => TunnelCreate(req.Params),
                    IpcMethods.TunnelsCreate => TunnelCreate(req.Params),
                    IpcMethods.TunnelClone => TunnelClone(req.Params),
                    IpcMethods.TunnelsClone => TunnelClone(req.Params),
                    IpcMethods.TunnelValidate => TunnelValidate(req.Params),
                    IpcMethods.TunnelsValidate => TunnelValidate(req.Params),
                    IpcMethods.TunnelsList => TunnelList(req.Params),
                    IpcMethods.TunnelsGet => TunnelGet(req.Params),
                    IpcMethods.TunnelsStatus => TunnelStatus(req.Params),
                    IpcMethods.TunnelsConnect => TunnelConnect(req.Params),
                    IpcMethods.TunnelsDisconnect => TunnelDisconnect(req.Params),
                    IpcMethods.TunnelsReconnect => TunnelReconnect(req.Params),
                    IpcMethods.TunnelsImport => TunnelImport(req.Params),
                    IpcMethods.TunnelsExport => TunnelExport(req.Params),
                    IpcMethods.TunnelsUpdate => TunnelUpdate(req.Params),
                    IpcMethods.TunnelsDelete => TunnelDelete(req.Params),
                    IpcMethods.WifiCurrent => WifiCurrent(),
                    IpcMethods.WifiRulesList => WifiRulesList(),
                    IpcMethods.WifiRulesSet => WifiRulesSet(req.Params),
                    IpcMethods.WifiRulesTest => WifiRulesTest(req.Params),
                    IpcMethods.HistoryTunnel => HistoryTunnel(req.Params),
                    IpcMethods.HistoryWifi => HistoryWifi(req.Params),
                    IpcMethods.HistoryTunnelClear => HistoryTunnelClear(req.Params),
                    IpcMethods.HistoryTunnelExport => HistoryTunnelExport(req.Params),
                    IpcMethods.KillSwitchStatus => KillSwitchStatus(),
                    IpcMethods.KillSwitchSet => KillSwitchSet(req.Params),
                    IpcMethods.NetworkLockStatus => NetworkLockStatus(),
                    IpcMethods.NetworkLockEnable => NetworkLockEnable(),
                    IpcMethods.NetworkLockDisable => NetworkLockDisable(),
                    IpcMethods.NetworkLockSetMode => NetworkLockSetMode(req.Params),
                    IpcMethods.NetworkLockSetLanAccess => NetworkLockSetLanAccess(req.Params),
                    IpcMethods.NetworkLockSetDnsPolicy => NetworkLockSetDnsPolicy(req.Params),
                    IpcMethods.NetworkLockSetCanonical => NetworkLockSet(req.Params),
                    IpcMethods.ConfigGet => ConfigGet(),
                    IpcMethods.ConfigSet => ConfigSet(req.Params),
                    IpcMethods.SplitTunnelGet => SplitTunnelGet(),
                    IpcMethods.SplitTunnelSet => SplitTunnelSet(req.Params),
                    IpcMethods.NetworkLockGet => NetworkLockGet(),
                    IpcMethods.NetworkLockSet => NetworkLockSet(req.Params),
                    IpcMethods.SystemPublicIp => SystemPublicIp(),
                    IpcMethods.RouteGuardStatus => RouteGuardStatus(),
                    IpcMethods.RouteGuardCapabilities => RouteGuardCapabilities(),
                    IpcMethods.RouteGuardSync => RouteGuardSync(req.Params),
                    IpcMethods.RouteGuardRoutingTest => RouteGuardRoutingTest(req.Params),
                    IpcMethods.RouteGuardStart => RouteGuardStart(req.Params),
                    IpcMethods.RouteGuardObservabilitySnapshot => RouteGuardObservabilitySnapshot(),
                    IpcMethods.RouteGuardDiagnosticsExport => RouteGuardDiagnosticsExport(req.Params),
                    IpcMethods.UpdateCheck => UpdateCheck(),
                    IpcMethods.UpdateApply => UpdateApply(req.Params),
                    IpcMethods.SupportExport => SupportExport(req.Params),
                    IpcMethods.SupportExportStatus => SupportExportStatus(req.Params),
                    IpcMethods.TelemetrySummary => TelemetrySummary(),
                    IpcMethods.AgentDiagnosticsResources => AgentDiagnosticsResources(),
                    _ => throw new InvalidOperationException($"Unknown method: {req.Method}"),
                };
                return Task.FromResult(IpcResponse.Ok(req.Id, result));
            }
            catch (Exception ex)
            {
                return Task.FromResult(IpcResponse.Err(req.Id, -32603, ex.Message));
            }
        }

        private object AgentSubscribeInfo() => new
        {
            eventsPipe = IpcConstants.EventsPipeName,
            schemaVersion = EventEnvelope.CurrentEventSchemaVersion,
        };

        private object AgentStatus()
        {
            var ping = AgentPing();
            if (_publisher == null)
            {
                return new
                {
                    version = UpdateChecker.CurrentVersionString,
                    codename = UpdateChecker.Codename,
                    uptimeSecs = (long)(DateTime.UtcNow - _started).TotalSeconds,
                    pid = Environment.ProcessId,
                    networkLock = _networkLock.AgentStatusSlice(),
                };
            }

            var metrics = _publisher.Metrics.Snapshot(
                _publisher.Sequencer.Current,
                _publisher.Ring);

            return new
            {
                version = UpdateChecker.CurrentVersionString,
                codename = UpdateChecker.Codename,
                uptimeSecs = (long)(DateTime.UtcNow - _started).TotalSeconds,
                pid = Environment.ProcessId,
                events = metrics,
                networkLock = _networkLock.AgentStatusSlice(),
            };
        }

        private object AgentEventReplay(JsonElement p)
        {
            if (_publisher == null)
                throw new InvalidOperationException("Event replay unavailable");

            if (!_publisher.Metrics.TryRecordReplayRequest())
                throw new InvalidOperationException("Replay rate limit exceeded");

            ulong sinceSeq = p.TryGetProperty("sinceSeq", out var s) && s.TryGetUInt64(out var v) ? v : 0;
            int limit = p.TryGetProperty("limit", out var l) && l.TryGetInt32(out var lim) ? Math.Clamp(lim, 1, 512) : 128;

            var lines = _publisher.Ring.ReplaySince(sinceSeq, limit);
            var events = new List<object>();
            ulong latest = sinceSeq;
            foreach (var line in lines)
            {
                var env = EventEnvelope.TryParseLine(line);
                if (env == null) continue;
                events.Add(new
                {
                    version = env.Version,
                    seq = env.Seq,
                    type = env.Type,
                    ts = env.Ts,
                    payload = env.Payload,
                });
                if (env.Seq.HasValue && env.Seq.Value > latest)
                    latest = env.Seq.Value;
            }

            return new { events, latestSeq = latest };
        }

        private object AgentRequestSnapshot() => BuildAgentSnapshot(_publisher?.Sequencer.Current);

        /// <summary>Full live state pushed to new event-stream subscribers.</summary>
        public object BuildAgentSnapshot(ulong? seq = null)
        {
            var active = _orch.ActiveTunnels().ToList();
            var primary = active.FirstOrDefault();
            return new
            {
                activeCount = active.Count,
                primary = primary != null ? BuildStatus(primary) : null,
                tunnels = _orch.VisibleTunnels().Select(BuildStatus).ToList(),
                wifi = new
                {
                    ssid = _orch.CurrentSsid,
                    isOpen = _orch.CurrentIsOpen,
                    manualMode = _config.Config.ManualMode,
                },
                networkAvailable = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(),
                meta = seq.HasValue ? new
                {
                    seq = seq.Value,
                    eventCount = _publisher?.Ring.Count ?? 0,
                    ringCapacity = _publisher?.Ring.Capacity ?? 0,
                } : null,
            };
        }

        public void SetTelemetry(TelemetryRollupService telemetry) => _telemetry = telemetry;

        public void SetPublisher(AgentEventPublisher publisher)
        {
            _publisher = publisher;
            _supportBundle = new SupportBundleBuilder(
                _config,
                _log,
                _routeGuardBridge,
                _history,
                _crashReports,
                AgentStatus,
                BuildEventHistoryForExport);
        }

        private object AgentPing() => new
        {
            version = UpdateChecker.CurrentVersionString,
            codename = UpdateChecker.Codename,
            uptimeSecs = (long)(DateTime.UtcNow - _started).TotalSeconds,
            pid = Environment.ProcessId,
        };

        private object TunnelList(JsonElement p)
        {
            var filter = new TunnelListFilter
            {
                Group = p.TryGetProperty("group", out var g) ? g.GetString() : null,
                ActiveOnly = p.TryGetProperty("activeOnly", out var a) && a.GetBoolean(),
                Search = p.TryGetProperty("search", out var s) ? s.GetString() : null,
                IncludeArchived = p.TryGetProperty("includeArchived", out var ia) && ia.GetBoolean(),
                Sort = p.TryGetProperty("sort", out var so) ? so.GetString() ?? "name" : "name",
            };
            return new { tunnels = _profiles.List(filter) };
        }

        private object TunnelGet(JsonElement p)
        {
            string name = p.GetProperty("name").GetString() ?? throw new ArgumentException("name required");
            bool includeConfig = !p.TryGetProperty("includeConfig", out var ic) || ic.GetBoolean();
            return _profiles.Get(name, includeConfig);
        }

        private object TunnelStatus(JsonElement p)
        {
            string? name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
            var active = _orch.ActiveTunnels().ToList();

            if (!string.IsNullOrEmpty(name))
            {
                var t = _orch.FindTunnel(name!);
                if (t == null) throw new InvalidOperationException($"Tunnel not found: {name}");
                return BuildStatus(t);
            }

            var primary = active.FirstOrDefault();
            return new
            {
                activeCount = active.Count,
                primary = primary != null ? BuildStatus(primary) : null,
                tunnels = _orch.VisibleTunnels().Select(BuildStatus).ToList(),
            };
        }

        private object BuildStatus(StoredTunnel t)
        {
            bool active = _tunnels.IsActive(t);
            long rx = 0, tx = 0;
            bool adapterUp = false;
            int? peerCount = null;
            long? lastHandshakeSecsAgo = null;

            if (active)
            {
                var stats = TunnelDll.GetTrafficStats(t.Name);
                rx = stats.RxBytes;
                tx = stats.TxBytes;
                adapterUp = stats.AdapterUp;

                if (t.Source == "local")
                {
                    var runtime = TunnelDll.GetRuntimeStats(t.Name);
                    peerCount = runtime.PeerCount;
                    lastHandshakeSecsAgo = runtime.LastHandshakeSecsAgo;
                }
            }

            return new
            {
                name = t.Name,
                group = t.Group,
                source = t.Source,
                active,
                adapterUp,
                rxBytes = rx,
                txBytes = tx,
                connectedSince = _orch.GetConnectedSince(t.Name),
                killSwitch = t.KillSwitch,
                autoReconnect = t.AutoReconnect,
                peerCount,
                lastHandshakeSecsAgo,
            };
        }

        private object ToTunnelSummary(StoredTunnel t) => _profiles.ToSummary(t);

        private object TunnelConnect(JsonElement p)
        {
            string? name = p.GetProperty("name").GetString();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required");
            var t = _orch.FindTunnel(name!) ?? throw new InvalidOperationException($"Tunnel not found: {name}");
            bool ok = _orch.ConnectTunnel(t, "Manual");
            if (!ok) throw new InvalidOperationException($"Connect failed: {name}");
            return new { ok = true, name };
        }

        private object TunnelDisconnect(JsonElement p)
        {
            string? name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(name))
            {
                foreach (var t in _orch.ActiveTunnels().ToList())
                    _orch.DisconnectTunnel(t, "Manual");
                return new { ok = true };
            }
            var tunnel = _orch.FindTunnel(name!) ?? throw new InvalidOperationException($"Tunnel not found: {name}");
            _orch.DisconnectTunnel(tunnel, "Manual");
            return new { ok = true, name };
        }

        private object TunnelReconnect(JsonElement p)
        {
            string name = p.GetProperty("name").GetString() ?? throw new ArgumentException("name required");
            var t = _orch.FindTunnel(name) ?? throw new InvalidOperationException($"Tunnel not found: {name}");
            bool ok = _orch.ReconnectTunnel(t, "Manual");
            if (!ok) throw new InvalidOperationException($"Reconnect failed: {name}");
            return new { ok = true, name };
        }

        private object TunnelImport(JsonElement p)
        {
            var opts = new TunnelImportOptions
            {
                Path = p.TryGetProperty("path", out var fp) ? fp.GetString() : null,
                Config = p.TryGetProperty("config", out var c) ? c.GetString() : null,
                Name = p.TryGetProperty("name", out var n) ? n.GetString() : null,
                Group = p.TryGetProperty("group", out var gr) ? gr.GetString() : "",
                OnConflict = p.TryGetProperty("onConflict", out var oc) ? oc.GetString() ?? "fail" : "fail",
            };
            return _profiles.Import(opts);
        }

        private object TunnelExport(JsonElement p)
        {
            string name = p.GetProperty("name").GetString() ?? throw new ArgumentException("name required");
            string? dest = p.TryGetProperty("dest", out var d) ? d.GetString() : null;
            string mode = p.TryGetProperty("mode", out var m) ? m.GetString() ?? "full" : "full";
            return _profiles.Export(name, mode, dest);
        }

        private object TunnelUpdate(JsonElement p)
        {
            string name = p.GetProperty("name").GetString() ?? throw new ArgumentException("name required");
            return _profiles.Update(name, p);
        }

        private object TunnelDelete(JsonElement p)
        {
            string name = p.GetProperty("name").GetString() ?? throw new ArgumentException("name required");
            return _profiles.Delete(name);
        }

        private object TunnelCreate(JsonElement p)
        {
            string name = p.GetProperty("name").GetString() ?? throw new ArgumentException("name required");
            string config = p.GetProperty("config").GetString() ?? throw new ArgumentException("config required");
            string? group = p.TryGetProperty("group", out var g) ? g.GetString() : null;
            string? notes = p.TryGetProperty("notes", out var n) ? n.GetString() : null;
            List<string>? tags = p.TryGetProperty("tags", out var t)
                ? JsonSerializer.Deserialize<List<string>>(t.GetRawText()) : null;
            return _profiles.Create(name, config, group, notes, tags);
        }

        private object TunnelClone(JsonElement p)
        {
            string name = p.GetProperty("name").GetString() ?? throw new ArgumentException("name required");
            string? newName = p.TryGetProperty("newName", out var nn) ? nn.GetString() : null;
            return _profiles.Clone(name, newName);
        }

        private object TunnelValidate(JsonElement p)
        {
            string? name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? config = p.TryGetProperty("config", out var c) ? c.GetString() : null;
            string? exclude = p.TryGetProperty("excludeName", out var e) ? e.GetString() : null;
            var result = _profiles.Validate(name, config, exclude);
            return new { valid = result.Valid, errors = result.Errors };
        }

        private object WifiCurrent() => new
        {
            ssid = _orch.CurrentSsid,
            isOpen = _orch.CurrentIsOpen,
            manualMode = _config.Config.ManualMode,
        };

        private object WifiRulesList() => new
        {
            rules = _config.Config.Rules,
            defaultAction = _config.Config.DefaultAction,
            defaultTunnel = _config.Config.DefaultTunnel,
            openWifiTunnel = _config.Config.OpenWifiTunnel,
            manualMode = _config.Config.ManualMode,
        };

        private object WifiRulesSet(JsonElement p)
        {
            if (p.TryGetProperty("rules", out var rulesEl))
                _config.Config.Rules = JsonSerializer.Deserialize<List<TunnelRule>>(rulesEl.GetRawText()) ?? new();
            if (p.TryGetProperty("defaultAction", out var da)) _config.Config.DefaultAction = da.GetString() ?? "none";
            if (p.TryGetProperty("defaultTunnel", out var dt)) _config.Config.DefaultTunnel = dt.GetString() ?? "";
            if (p.TryGetProperty("openWifiTunnel", out var ow)) _config.Config.OpenWifiTunnel = ow.GetString() ?? "";
            if (p.TryGetProperty("manualMode", out var mm)) _config.Config.ManualMode = mm.GetBoolean();
            _config.Save();
            return WifiRulesList();
        }

        private object WifiRulesTest(JsonElement p)
        {
            string? ssid = p.TryGetProperty("ssid", out var s) ? s.GetString() : null;
            bool isOpen = p.TryGetProperty("isOpen", out var o) && o.GetBoolean();
            var result = _rules.EvaluateWifi(_config.Config, ssid, isOpen);
            return new
            {
                action = result.Action.ToString().ToLowerInvariant(),
                tunnel = result.TunnelName,
                reason = result.Reason,
            };
        }

        private object HistoryTunnel(JsonElement p)
        {
            int limit = p.TryGetProperty("limit", out var l) ? l.GetInt32() : 100;
            string? tunnelName = p.TryGetProperty("tunnelName", out var tn) ? tn.GetString() : null;
            bool includeFailures = !p.TryGetProperty("includeFailures", out var inc) || inc.GetBoolean();
            var entries = _history.QueryTunnelHistory(limit, tunnelName, includeFailures);
            return new { entries };
        }

        private object HistoryTunnelClear(JsonElement p)
        {
            string? tunnelName = p.TryGetProperty("tunnelName", out var tn) ? tn.GetString() : null;
            _history.ClearTunnelHistory(tunnelName);
            return new { ok = true };
        }

        private object HistoryTunnelExport(JsonElement p)
        {
            int limit = p.TryGetProperty("limit", out var l) ? l.GetInt32() : 1000;
            string? tunnelName = p.TryGetProperty("tunnelName", out var tn) ? tn.GetString() : null;
            string format = p.TryGetProperty("format", out var f) ? f.GetString() ?? "json" : "json";
            string? dest = p.TryGetProperty("dest", out var d) ? d.GetString() : null;
            var entries = _history.QueryTunnelHistory(limit, tunnelName, includeFailures: true);

            string output = format.ToLowerInvariant() switch
            {
                "csv" => ExportHistoryCsv(entries),
                _ => JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }),
            };

            if (!string.IsNullOrEmpty(dest))
                File.WriteAllText(dest!, output);

            return new { written = dest, format, count = entries.Count };
        }

        private static string ExportHistoryCsv(IReadOnlyList<ConnectionHistoryEntry> entries)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("TunnelName,ConnectedAt,DisconnectedAt,DurationSecs,Source,Endpoint,FailureReason,RxBytes,TxBytes");
            foreach (var e in entries)
            {
                var dur = e.DisconnectedAt.HasValue
                    ? (e.DisconnectedAt.Value - e.ConnectedAt).TotalSeconds.ToString("F0")
                    : "";
                sb.AppendLine(string.Join(",",
                    CsvEscape(e.TunnelName),
                    e.ConnectedAt.ToString("o"),
                    e.DisconnectedAt?.ToString("o") ?? "",
                    dur,
                    CsvEscape(e.Source),
                    CsvEscape(e.Endpoint ?? ""),
                    CsvEscape(e.FailureReason ?? ""),
                    e.SessionRxBytes,
                    e.SessionTxBytes));
            }
            return sb.ToString();
        }

        private static string CsvEscape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        private object HistoryWifi(JsonElement p)
        {
            int limit = p.TryGetProperty("limit", out var l) ? l.GetInt32() : 100;
            var entries = _history.SsidEntries.Take(limit).ToList();
            return new { entries };
        }

        private object KillSwitchStatus() => _networkLock.GetStatus();

        private object KillSwitchSet(JsonElement p)
        {
            if (p.TryGetProperty("mode", out var m))
                _config.Config.KillSwitchMode = m.GetString() ?? "per-tunnel";
            if (p.TryGetProperty("networkLock", out var nl))
            {
                var patch = JsonSerializer.Deserialize<NetworkLockConfig>(nl.GetRawText()) ?? new();
                _networkLock.ApplyConfigPatch(patch);
            }
            else
            {
                _config.Save();
            }
            return KillSwitchStatus();
        }

        private object NetworkLockStatus() => _networkLock.GetStatus();

        private object NetworkLockEnable()
        {
            _networkLock.Enable();
            return NetworkLockStatus();
        }

        private object NetworkLockDisable()
        {
            _networkLock.Disable();
            return NetworkLockStatus();
        }

        private object NetworkLockSetMode(JsonElement p)
        {
            var modeStr = p.TryGetProperty("mode", out var m) ? m.GetString() : null;
            _networkLock.SetMode(NetworkLockModeExtensions.FromApiString(modeStr));
            return NetworkLockStatus();
        }

        private object NetworkLockSetLanAccess(JsonElement p)
        {
            var enabled = p.TryGetProperty("enabled", out var e) && e.GetBoolean();
            List<string>? exceptions = null;
            if (p.TryGetProperty("exceptions", out var ex) && ex.ValueKind == JsonValueKind.Array)
                exceptions = ex.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList();
            _networkLock.SetLanAccess(enabled, exceptions);
            return NetworkLockStatus();
        }

        private object NetworkLockSetDnsPolicy(JsonElement p)
        {
            var policy = p.TryGetProperty("policy", out var pol) ? pol.GetString() : "strict";
            List<string>? exceptions = null;
            if (p.TryGetProperty("exceptions", out var ex) && ex.ValueKind == JsonValueKind.Array)
                exceptions = ex.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList();
            _networkLock.SetDnsPolicy(policy ?? "strict", exceptions);
            return NetworkLockStatus();
        }

        private object NetworkLockGet() => _networkLock.GetStatus();

        private object NetworkLockSet(JsonElement p)
        {
            var el = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("config", out var c) ? c : p;
            var patch = JsonSerializer.Deserialize<NetworkLockConfig>(el.GetRawText()) ?? new();
            _networkLock.ApplyConfigPatch(patch);
            return NetworkLockStatus();
        }

        private object ConfigGet() => _config.Config;

        private object ConfigSet(JsonElement p)
        {
            if (p.TryGetProperty("patch", out var patch))
            {
                var merged = JsonSerializer.Serialize(_config.Config);
                using var doc = JsonDocument.Parse(merged);
                var node = System.Text.Json.Nodes.JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
                foreach (var prop in patch.EnumerateObject())
                    node[prop.Name] = System.Text.Json.Nodes.JsonNode.Parse(prop.Value.GetRawText());
                _config.Config = node.Deserialize<AppConfig>() ?? _config.Config;
            }
            else
            {
                _config.Config = JsonSerializer.Deserialize<AppConfig>(p.GetRawText()) ?? _config.Config;
            }
            _config.Save();
            if (p.TryGetProperty("patch", out var patchEl) &&
                patchEl.TryGetProperty("networkLockWfpDelegation", out _))
            {
                _networkLock.ReevaluateFromConfig();
            }
            return _config.Config;
        }

        private object SplitTunnelGet() => _config.Config.SplitTunnel;

        private object SplitTunnelSet(JsonElement p)
        {
            var el = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("rules", out var r) ? r : p;
            _config.Config.SplitTunnel = JsonSerializer.Deserialize<SplitTunnelRules>(el.GetRawText()) ?? new();
            _config.Save();
            _routeGuardBridge.SyncIfEnabled();
            return _config.Config.SplitTunnel;
        }

        private object SystemPublicIp() => new { ip = _orch.FetchPublicIpAsync().GetAwaiter().GetResult() };

        private object RouteGuardStatus() => _routeGuardBridge.GetStatus();

        private object RouteGuardCapabilities() => _routeGuardBridge.GetCapabilities();

        private object RouteGuardSync(JsonElement p)
        {
            bool force = p.TryGetProperty("force", out var f) && f.GetBoolean();
            return _routeGuardBridge.Sync(force);
        }

        private object RouteGuardRoutingTest(JsonElement p)
        {
            var appPath = p.TryGetProperty("appPath", out var a) ? a.GetString() : null;
            var remoteIp = p.TryGetProperty("remoteIp", out var ip) ? ip.GetString() ?? "8.8.8.8" : "8.8.8.8";
            var domain = p.TryGetProperty("domain", out var d) ? d.GetString() : null;
            return _routeGuardBridge.RoutingTest(appPath, remoteIp, domain);
        }

        private object RouteGuardStart(JsonElement p)
        {
            int wait = p.TryGetProperty("waitSecs", out var w) ? w.GetInt32() : 10;
            var ok = _routeGuardBridge.TryStart(wait);
            return new { started = ok, status = _routeGuardBridge.GetStatus() };
        }

        private object RouteGuardObservabilitySnapshot()
        {
            return _routeGuardBridge.GetObservabilitySnapshot();
        }

        private object RouteGuardDiagnosticsExport(JsonElement p)
        {
            var tier = p.TryGetProperty("tier", out var t) ? t.GetString() ?? "sanitized" : "sanitized";
            if (_crashReports == null)
                return _routeGuardBridge.ExportDiagnosticsRaw(tier);
            return _routeGuardBridge.ExportDiagnostics(tier, _crashReports.CrashesDirectory);
        }

        private object UpdateCheck()
        {
            var updater = new UnifiedUpdateService(_config.Config);
            var manifest = updater.FetchManifestAsync().GetAwaiter().GetResult();
            if (manifest == null)
            {
                _telemetry?.RecordUpdateCheck(false);
                return new { available = false, reason = "manifest_unavailable" };
            }
            var current = UpdateChecker.CurrentVersionString;
            var available = !string.Equals(manifest.ProductVersion, current, StringComparison.OrdinalIgnoreCase)
                && UpdateChecker.IsNewerVersion(manifest.ProductVersion);
            _telemetry?.RecordUpdateCheck(available);
            return new { available, current, latest = manifest.ProductVersion, channel = manifest.Channel, mandatory = manifest.Mandatory };
        }

        private object UpdateApply(JsonElement p)
        {
            var updater = new UnifiedUpdateService(_config.Config);
            var manifest = updater.FetchManifestAsync().GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("Update manifest unavailable");
            if (!updater.VerifyManifestForChannel(manifest))
                throw new InvalidOperationException("Manifest signature verification failed");
            var result = updater.ApplyAsync(manifest).GetAwaiter().GetResult();
            UpdateHistoryStore.Record(result, _config.Config.UpdateChannel);
            _telemetry?.RecordUpdateApply(result.Ok ? "ok" : (result.Error?.Contains("rollback", StringComparison.OrdinalIgnoreCase) == true ? "rollback" : "fail"));
            return result;
        }

        private object TelemetrySummary()
        {
            if (_telemetry == null)
                return new { available = false };
            return _telemetry.Summary();
        }

        private object AgentDiagnosticsResources()
        {
            var baseSnap = AgentResourceDiagnostics.Collect();
            int? wfpFilters = null;
            try
            {
                if (_routeGuardBridge.Availability == Ipc.RouteGuard.RouteGuardAvailability.Running)
                {
                    var obs = _routeGuardBridge.GetObservabilitySnapshot();
                    var json = JsonSerializer.Serialize(obs);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("networkLock", out var nl) &&
                        nl.TryGetProperty("wfpFilters", out var wf) &&
                        wf.TryGetInt32(out var count))
                        wfpFilters = count;
                }
            }
            catch { /* RG optional */ }

            using var snapDoc = JsonDocument.Parse(JsonSerializer.Serialize(baseSnap));
            var root = snapDoc.RootElement;
            return new
            {
                agent = JsonSerializer.Deserialize<object>(root.GetProperty("agent").GetRawText()),
                routeguard = new
                {
                    process = root.TryGetProperty("routeguard", out var rg)
                        ? JsonSerializer.Deserialize<object?>(rg.GetRawText())
                        : null,
                    wfpFilters,
                },
                ts = root.GetProperty("ts").GetString(),
            };
        }

        private object SupportExport(JsonElement p)
        {
            if (_supportBundle == null)
                throw new InvalidOperationException("Support export unavailable");

            var tier = p.TryGetProperty("tier", out var t) ? t.GetString() ?? "sanitized" : "sanitized";
            var includeCrash = p.TryGetProperty("includeCrashReports", out var c) && c.GetBoolean();
            if (!includeCrash)
                includeCrash = _config.Config.SupportExportIncludeCrashes;
            var includeEvents = !p.TryGetProperty("includeEventHistory", out var e) || e.GetBoolean();
            var includeTunnel = p.TryGetProperty("includeTunnelHistory", out var th) && th.GetBoolean();
            long maxBytes = p.TryGetProperty("maxSizeBytes", out var m) && m.TryGetInt64(out var mb) ? mb : 52_428_800;

            var result = _supportBundle.Export(new SupportExportParams
            {
                Tier = tier,
                IncludeCrashReports = includeCrash,
                IncludeEventHistory = includeEvents,
                IncludeTunnelHistory = includeTunnel,
                MaxSizeBytes = maxBytes,
            });
            _telemetry?.RecordSupportExport();
            return result;
        }

        private object SupportExportStatus(JsonElement p)
        {
            if (_supportBundle == null)
                return new { phase = "idle" };
            var exportId = p.TryGetProperty("exportId", out var id) ? id.GetString() : null;
            return _supportBundle.ExportStatus(exportId) ?? new { phase = "idle" };
        }

        private IReadOnlyList<object> BuildEventHistoryForExport()
        {
            if (_publisher == null) return Array.Empty<object>();

            var lines = _publisher.Ring.Snapshot();
            var events = new List<object>();
            foreach (var (seq, line) in lines.TakeLast(500))
            {
                var env = EventEnvelope.TryParseLine(line);
                if (env == null) continue;
                events.Add(new
                {
                    version = env.Version,
                    seq = env.Seq ?? seq,
                    type = env.Type,
                    ts = env.Ts,
                    payload = env.Payload,
                });
            }
            return events;
        }

        private static string SanitizeName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
