/** MasselGUARD event schema v1 — shared between agent, Tauri, and Svelte. */

export const EVENT_SCHEMA_VERSION = 1;

export type EventEnvelopeV1 = {
  version: 1;
  seq: number;
  type: string;
  ts: string;
  payload: unknown;
};

export type EventEnvelopeV0 = {
  type: string;
  payload: unknown;
  ts?: number;
};

export type NormalizedEventEnvelope = {
  version: number;
  seq: number | null;
  type: string;
  ts: string | null;
  payload: unknown;
};

export type SubscribeMessage = {
  op: 'subscribe';
  version?: number;
  sinceSeq?: number;
  filters?: string[];
};

export type SubscribedAck = {
  op: 'subscribed' | 'filters_updated';
  version: number;
  snapshotSeq: number;
  replayFrom?: number | null;
  replayCount: number;
};

export type StreamGapMeta = {
  status: 'gap';
  from: number;
  to: number;
};

export const EventTypes = {
  agent: {
    heartbeat: 'agent.heartbeat',
    snapshot: 'agent.snapshot',
    protocolError: 'agent.protocol_error',
  },
  tunnel: {
    stateChanged: 'tunnel.state_changed',
    statsUpdated: 'tunnel.stats_updated',
    handshakeUpdated: 'tunnel.handshake_updated',
    created: 'tunnel.created',
    updated: 'tunnel.updated',
    deleted: 'tunnel.deleted',
    imported: 'tunnel.imported',
    cloned: 'tunnel.cloned',
  },
  wifi: {
    ssidChanged: 'wifi.ssid_changed',
    ruleApplied: 'wifi.rule_applied',
  },
  network: {
    changed: 'network.changed',
  },
  routeguard: {
    routingChanged: 'routeguard.routing_changed',
    networkLockChanged: 'routeguard.network_lock_changed',
    availabilityChanged: 'routeguard.availability_changed',
    syncCompleted: 'routeguard.sync_completed',
    awgConnected: 'routeguard.awg_connected',
    phantunConnected: 'routeguard.phantun_connected',
    lwoConnected: 'routeguard.lwo_connected',
    lwoFallback: 'routeguard.lwo_fallback',
    lwoFailed: 'routeguard.lwo_failed',
    lwoDisconnected: 'routeguard.lwo_disconnected',
    lwoStarting: 'routeguard.lwo_starting',
    lwoRecovering: 'routeguard.lwo_recovering',
    metricsUpdated: 'routeguard.metrics_updated',
    transportHealth: 'routeguard.transport_health',
    transportRecovery: 'routeguard.transport_recovery',
    dnsRedirectStats: 'routeguard.dns_redirect_stats',
  },
  observability: {
    healthChanged: 'observability.health_changed',
  },
  networkLock: {
    enabled: 'networklock.enabled',
    disabled: 'networklock.disabled',
    policyChanged: 'networklock.policy_changed',
    recovered: 'networklock.recovered',
  },
} as const;

export type AgentStatusResponse = {
  version: string;
  codename: string;
  uptimeSecs: number;
  pid: number;
  events?: {
    published: number;
    dropped: number;
    lastSeq: number;
    subscribers: number;
    avgPublishLatencyUs: number;
    replayRequests: number;
    ring: { size: number; capacity: number };
  };
  networkLock?: {
    mode: string;
    enforcementActive: boolean;
    leakProtection: string;
    activeFilterCount: number;
    lastRecoveryAt?: string | null;
    recoveryState: string;
  };
};

export type EventReplayResponse = {
  events: NormalizedEventEnvelope[];
  latestSeq: number;
};

export type ExtendedStreamHealth = {
  connected: boolean;
  degraded: boolean;
  lastEventMs: number;
  lastHeartbeatMs: number;
  lastSeq: number;
  gapsDetected: number;
  replayRequests: number;
};
