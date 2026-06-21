using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MasselGUARD.Agent.Ipc
{
    public static class IpcConstants
    {
        public const string PipeName = @"\\.\pipe\MasselGUARD";
        public const string EventsPipeName = @"\\.\pipe\MasselGUARDAgent-events";
        public const string JsonRpcVersion = "2.0";
    }

    public class IpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = IpcConstants.JsonRpcVersion;

        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; } = "";

        [JsonPropertyName("params")]
        public JsonElement Params { get; set; }
    }

    public class IpcResponse
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = IpcConstants.JsonRpcVersion;

        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("result")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Result { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IpcError? Error { get; set; }

        public static IpcResponse Ok(long id, object? result) =>
            new() { Id = id, Result = result };

        public static IpcResponse Err(long id, int code, string message) =>
            new() { Id = id, Error = new IpcError { Code = code, Message = message } };
    }

    public class IpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }

    /// <summary>Server-initiated notification (no id).</summary>
    public class IpcEvent
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = IpcConstants.JsonRpcVersion;

        [JsonPropertyName("method")]
        public string Method { get; set; } = "notify";

        [JsonPropertyName("params")]
        public IpcEventParams Params { get; set; } = new();
    }

    public class IpcEventParams
    {
        [JsonPropertyName("event")]
        public string Event { get; set; } = "";

        [JsonPropertyName("payload")]
        public object? Payload { get; set; }
    }

    public static class IpcMethods
    {
        public const string AgentPing = "agent.ping";
        public const string AgentStatus = "agent.status";
        public const string AgentSubscribeInfo = "agent.subscribe_info";
        public const string AgentEventReplay = "agent.event_replay";
        public const string AgentRequestSnapshot = "agent.request_snapshot";
        public const string TunnelList = "tunnel.list";
        public const string TunnelGet = "tunnel.get";
        public const string TunnelStatus = "tunnel.status";
        public const string TunnelConnect = "tunnel.connect";
        public const string TunnelDisconnect = "tunnel.disconnect";
        public const string TunnelReconnect = "tunnel.reconnect";
        public const string TunnelImport = "tunnel.import";
        public const string TunnelExport = "tunnel.export";
        public const string TunnelUpdate = "tunnel.update";
        public const string TunnelDelete = "tunnel.delete";
        public const string TunnelCreate = "tunnel.create";
        public const string TunnelClone = "tunnel.clone";
        public const string TunnelValidate = "tunnel.validate";
        public const string TunnelsList = "tunnels.list";
        public const string TunnelsGet = "tunnels.get";
        public const string TunnelsStatus = "tunnels.status";
        public const string TunnelsConnect = "tunnels.connect";
        public const string TunnelsDisconnect = "tunnels.disconnect";
        public const string TunnelsReconnect = "tunnels.reconnect";
        public const string TunnelsImport = "tunnels.import";
        public const string TunnelsExport = "tunnels.export";
        public const string TunnelsUpdate = "tunnels.update";
        public const string TunnelsDelete = "tunnels.delete";
        public const string TunnelsCreate = "tunnels.create";
        public const string TunnelsClone = "tunnels.clone";
        public const string TunnelsValidate = "tunnels.validate";
        public const string HistoryTunnelClear = "history.tunnel_clear";
        public const string HistoryTunnelExport = "history.tunnel_export";
        public const string WifiCurrent = "wifi.current";
        public const string WifiRulesList = "wifi.rules.list";
        public const string WifiRulesSet = "wifi.rules.set";
        public const string WifiRulesTest = "wifi.rules.test";
        public const string HistoryTunnel = "history.tunnel";
        public const string HistoryWifi = "history.wifi";
        public const string KillSwitchStatus = "killswitch.status";
        public const string KillSwitchSet = "killswitch.set";
        public const string ConfigGet = "config.get";
        public const string ConfigSet = "config.set";
        public const string SplitTunnelGet = "split_tunnel.get";
        public const string SplitTunnelSet = "split_tunnel.set";
        public const string NetworkLockGet = "network_lock.get";
        public const string NetworkLockSet = "network_lock.set";
        public const string NetworkLockStatus = "networklock.status";
        public const string NetworkLockEnable = "networklock.enable";
        public const string NetworkLockDisable = "networklock.disable";
        public const string NetworkLockSetMode = "networklock.set_mode";
        public const string NetworkLockSetLanAccess = "networklock.set_lan_access";
        public const string NetworkLockSetDnsPolicy = "networklock.set_dns_policy";
        public const string NetworkLockSetCanonical = "networklock.set";
        public const string SystemPublicIp = "system.public_ip";
        public const string RouteGuardStatus = "routeguard.status";
        public const string RouteGuardCapabilities = "routeguard.capabilities";
        public const string RouteGuardSync = "routeguard.sync";
        public const string RouteGuardRoutingTest = "routeguard.routing.test";
        public const string RouteGuardStart = "routeguard.start";
        public const string RouteGuardObservabilitySnapshot = "routeguard.observability.snapshot";
        public const string RouteGuardDiagnosticsExport = "routeguard.diagnostics.export";
        public const string UpdateCheck = "update.check";
        public const string UpdateApply = "update.apply";
        public const string SupportExport = "support.export";
        public const string SupportExportStatus = "support.export.status";
        public const string TelemetrySummary = "telemetry.summary";
        public const string AgentDiagnosticsResources = "agent.diagnostics.resources";
    }

    public static class IpcEvents
    {
        public const string TunnelStateChanged = "tunnel.state_changed";
        public const string TunnelStatsUpdated = "tunnel.stats_updated";
        public const string WifiSsidChanged = "wifi.ssid_changed";
        public const string WifiRuleApplied = "wifi.rule_applied";
        public const string KillSwitchChanged = "killswitch.changed";
        public const string ConnectionFailed = "connection.failed";
        public const string LogEntry = "log.entry";
        public const string Notification = "notification";
        public const string TunnelHandshakeUpdated = "tunnel.handshake_updated";
        public const string NetworkChanged = "network.changed";
        public const string AgentHeartbeat = "agent.heartbeat";
        public const string AgentSnapshot = "agent.snapshot";
    }
}
