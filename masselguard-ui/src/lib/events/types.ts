import type { TunnelStatus, WifiSnapshot } from '@mg-ui-core/types';

export type AgentEventType =
  | 'tunnel.state_changed'
  | 'tunnel.stats_updated'
  | 'tunnel.handshake_updated'
  | 'wifi.ssid_changed'
  | 'wifi.rule_applied'
  | 'network.changed'
  | 'agent.snapshot'
  | 'agent.heartbeat'
  | 'notification'
  | 'log.entry'
  | 'connection.failed';

export type AgentEventEnvelope = {
  type: AgentEventType | string;
  payload: unknown;
  ts?: number;
  version?: number;
  seq?: number;
};

export type StreamStatus = 'connected' | 'disconnected' | 'degraded' | 'gap';

export type StreamMeta = {
  status: StreamStatus;
  error?: string;
  from?: number;
  to?: number;
};

export type TunnelStateChangedPayload = {
  name: string;
  state: 'connected' | 'disconnected';
  source?: string;
};

export type TunnelStatsUpdatedPayload = {
  name: string;
  rxBytes: number;
  txBytes: number;
  rxRate?: number;
  txRate?: number;
  adapterUp?: boolean;
};

export type TunnelHandshakeUpdatedPayload = {
  name: string;
  peerCount: number;
  lastHandshakeSecsAgo: number | null;
};

export type WifiSsidChangedPayload = {
  ssid: string | null;
  isOpen: boolean;
};

export type NetworkChangedPayload = {
  available: boolean;
  changeKind: string;
};

export type AgentSnapshotPayload = {
  activeCount: number;
  primary: TunnelStatus | null;
  tunnels: TunnelStatus[];
  wifi: WifiSnapshot;
  networkAvailable: boolean;
  meta?: { seq: number; eventCount: number; ringCapacity: number };
};

export type AgentLiveState = {
  streamConnected: boolean;
  streamDegraded: boolean;
  primaryTunnel: TunnelStatus | null;
  tunnels: Map<string, TunnelStatus>;
  wifi: WifiSnapshot;
  networkAvailable: boolean;
  publicIp: string | null;
  hydrated: boolean;
};

export type StreamHealthResponse = {
  connected: boolean;
  degraded: boolean;
  lastEventMs: number;
  lastHeartbeatMs: number;
  lastSeq?: number;
  gapsDetected?: number;
  replayRequests?: number;
};

export const emptyWifi = (): WifiSnapshot => ({
  ssid: null,
  isOpen: false,
  manualMode: false,
});

export const initialLiveState = (): AgentLiveState => ({
  streamConnected: false,
  streamDegraded: false,
  primaryTunnel: null,
  tunnels: new Map(),
  wifi: emptyWifi(),
  networkAvailable: true,
  publicIp: null,
  hydrated: false,
});
