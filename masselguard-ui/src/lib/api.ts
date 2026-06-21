import { invoke } from '@tauri-apps/api/core';
import type {
  AgentInfo,
  AppConfig,
  ExportMode,
  HistoryEntry,
  NetworkLockConfig,
  NetworkLockMode,
  NetworkLockStatus,
  DnsPolicy,
  RouteGuardStatus,
  SplitTunnelRules,
  TunnelDetailResponse,
  TunnelStatusResponse,
  TunnelSummary,
  ValidationResult,
  WifiRulesResponse,
  WifiSnapshot,
} from '@mg-ui-core/types';

async function call<T>(cmd: string, args: Record<string, unknown> = {}): Promise<T> {
  return invoke<T>(cmd, args);
}

export const api = {
  agentPing: () => call<AgentInfo>('agent_ping'),
  tunnelList: (opts?: {
    group?: string;
    activeOnly?: boolean;
    search?: string;
    includeArchived?: boolean;
    sort?: string;
  }) =>
    call<{ tunnels: TunnelSummary[] }>('tunnel_list', {
      group: opts?.group,
      activeOnly: opts?.activeOnly,
      search: opts?.search,
      includeArchived: opts?.includeArchived,
      sort: opts?.sort,
    }),
  tunnelGet: (name: string, includeConfig = true) =>
    call<TunnelDetailResponse>('tunnel_get', { name, includeConfig }),
  tunnelStatus: (name?: string) => call<TunnelStatusResponse>('tunnel_status', { name }),
  tunnelConnect: (name: string) => call<{ ok: boolean }>('tunnel_connect', { name }),
  tunnelDisconnect: (name?: string) => call<{ ok: boolean }>('tunnel_disconnect', { name }),
  tunnelReconnect: (name: string) => call<{ ok: boolean }>('tunnel_reconnect', { name }),
  tunnelImport: (opts: {
    path?: string;
    config?: string;
    name?: string;
    group?: string;
    onConflict?: 'fail' | 'rename';
  }) => call<TunnelSummary>('tunnel_import', opts),
  tunnelExport: (name: string, mode: ExportMode = 'full', dest?: string) =>
    call<{ config: string; mode: string }>('tunnel_export', { name, mode, dest }),
  tunnelCreate: (opts: {
    name: string;
    config: string;
    group?: string;
    notes?: string;
    tags?: string[];
  }) => call<TunnelSummary>('tunnel_create', opts),
  tunnelClone: (name: string, newName?: string) =>
    call<TunnelSummary>('tunnel_clone', { name, newName }),
  tunnelValidate: (opts: { name?: string; config?: string; excludeName?: string }) =>
    call<ValidationResult>('tunnel_validate', opts),
  tunnelUpdate: (tunnel: Record<string, unknown>) => call<TunnelSummary>('tunnel_update', tunnel),
  tunnelDelete: (name: string) => call<{ ok: boolean }>('tunnel_delete', { name }),
  wifiCurrent: () => call<WifiSnapshot>('wifi_current'),
  wifiRulesGet: () => call<WifiRulesResponse>('wifi_rules_get'),
  wifiRulesSet: (payload: Record<string, unknown>) => call<WifiRulesResponse>('wifi_rules_set', payload),
  wifiRulesTest: (ssid?: string, isOpen?: boolean) =>
    call<{ action: string; tunnel?: string; reason: string }>('wifi_rules_test', {
      ssid,
      isOpen: isOpen ?? false,
    }),
  killswitchGet: () => call<NetworkLockStatus>('killswitch_get'),
  killswitchSet: (payload: Record<string, unknown>) => call<NetworkLockStatus>('killswitch_set', payload),
  configGet: () => call<AppConfig>('config_get'),
  configSet: (patch: Record<string, unknown>) => call<AppConfig>('config_set', { patch }),
  splitTunnelGet: () => call<SplitTunnelRules>('split_tunnel_get'),
  splitTunnelSet: (rules: SplitTunnelRules) => call<SplitTunnelRules>('split_tunnel_set', { rules }),
  networkLockGet: () => call<NetworkLockStatus>('network_lock_get'),
  networkLockSet: (config: NetworkLockConfig) => call<NetworkLockStatus>('network_lock_set', { config }),
  networkLockStatus: () => call<NetworkLockStatus>('networklock_status'),
  networkLockEnable: () => call<NetworkLockStatus>('networklock_enable'),
  networkLockDisable: () => call<NetworkLockStatus>('networklock_disable'),
  networkLockSetMode: (mode: NetworkLockMode) => call<NetworkLockStatus>('networklock_set_mode', { mode }),
  networkLockSetLanAccess: (enabled: boolean, exceptions?: string[]) =>
    call<NetworkLockStatus>('networklock_set_lan_access', { enabled, exceptions }),
  networkLockSetDnsPolicy: (policy: DnsPolicy, exceptions?: string[]) =>
    call<NetworkLockStatus>('networklock_set_dns_policy', { policy, exceptions }),
  routeguardStatus: () => call<RouteGuardStatus>('routeguard_status'),
  routeguardCapabilities: () => call<{ negotiated: RouteGuardStatus['negotiated'] }>('routeguard_capabilities'),
  routeguardSync: (force = false) => call<{ ok: boolean; rulesApplied?: number; errors?: string[] }>('routeguard_sync', { force }),
  routeguardRoutingTest: (opts: { appPath?: string; remoteIp?: string; domain?: string }) =>
    call<{ target: string; reason: string }>('routeguard_routing_test', opts),
  routeguardStart: (waitSecs = 10) => call<{ started: boolean }>('routeguard_start', { waitSecs }),
  routeguardObservability: () =>
    call<Record<string, unknown>>('routeguard_observability_snapshot'),
  routeguardDiagnosticsExport: (opts?: { tier?: string; dest?: string }) =>
    call<{ bundleId?: string; path?: string; tier?: string; sizeBytes?: number; savedPath?: string }>(
      'routeguard_diagnostics_export',
      { tier: opts?.tier ?? 'sanitized', dest: opts?.dest },
    ),
  supportExport: (opts?: {
    tier?: string;
    includeCrashReports?: boolean;
    includeEventHistory?: boolean;
    includeTunnelHistory?: boolean;
    dest?: string;
  }) =>
    call<{
      bundleId?: string;
      path?: string;
      tier?: string;
      sizeBytes?: number;
      truncated?: boolean;
      savedPath?: string;
      sections?: unknown[];
    }>('support_export', {
      tier: opts?.tier ?? 'sanitized',
      includeCrashReports: opts?.includeCrashReports ?? false,
      includeEventHistory: opts?.includeEventHistory ?? true,
      includeTunnelHistory: opts?.includeTunnelHistory ?? false,
      dest: opts?.dest,
    }),
  supportExportStatus: (exportId?: string) =>
    call<{ exportId?: string; phase?: string; updatedAt?: string }>('support_export_status', {
      exportId,
    }),
  telemetrySummary: () =>
    call<Record<string, unknown>>('telemetry_summary'),
  agentDiagnosticsResources: () =>
    call<Record<string, unknown>>('agent_diagnostics_resources'),
  historyTunnel: (opts?: {
    limit?: number;
    tunnelName?: string;
    includeFailures?: boolean;
  }) =>
    call<{ entries: HistoryEntry[] }>('history_tunnel', {
      limit: opts?.limit ?? 100,
      tunnelName: opts?.tunnelName,
      includeFailures: opts?.includeFailures ?? true,
    }),
  historyTunnelClear: (tunnelName?: string) =>
    call<{ ok: boolean }>('history_tunnel_clear', { tunnelName }),
  historyTunnelExport: (dest: string, format: 'json' | 'csv' = 'json', tunnelName?: string) =>
    call<{ count: number }>('history_tunnel_export', { dest, format, tunnelName }),
  historyWifi: (limit = 50) => call<{ entries: HistoryEntry[] }>('history_wifi', { limit }),
  publicIpRefresh: () => call<{ ip: string | null }>('public_ip_refresh'),
  agentStatus: () => call<import('@mg-ui-core/types/events').AgentStatusResponse>('agent_status'),
  agentEventReplay: (sinceSeq: number, limit = 128) =>
    call<import('@mg-ui-core/types/events').EventReplayResponse>('agent_event_replay', {
      sinceSeq,
      limit,
    }),
  pickConfFile: () => call<string | null>('pick_conf_file'),
  readTextFile: (path: string) => call<string>('read_text_file', { path }),
  saveConfFile: (contents: string, suggestedName?: string) =>
    call<string | null>('save_conf_file', { contents, suggestedName }),
};

export function formatRateBps(bps: number): string {
  if (bps < 1024) return `${bps} B/s`;
  if (bps < 1024 * 1024) return `${(bps / 1024).toFixed(1)} KB/s`;
  return `${(bps / (1024 * 1024)).toFixed(2)} MB/s`;
}

export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  if (n < 1024 * 1024 * 1024) return `${(n / (1024 * 1024)).toFixed(1)} MB`;
  return `${(n / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

export function formatDuration(secs: number): string {
  if (secs < 60) return `${secs}s`;
  if (secs < 3600) return `${Math.floor(secs / 60)}m ${secs % 60}s`;
  const h = Math.floor(secs / 3600);
  const m = Math.floor((secs % 3600) / 60);
  return `${h}h ${m}m`;
}

export function formatConnectedSince(iso: string | null | undefined): string {
  if (!iso) return '—';
  const secs = Math.max(0, Math.floor((Date.now() - Date.parse(iso)) / 1000));
  return formatDuration(secs);
}
