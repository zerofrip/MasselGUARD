import type { RouteGuardStatus } from '@mg-ui-core/types';

const HISTORY_LEN = 60;

export type RedirectStats = {
  redirectedV4?: number;
  redirectedV6?: number;
  redirectedTcpV4?: number;
  redirectedTcpV6?: number;
};

export type ObservabilityLiveState = {
  healthScore: number | null;
  healthStatus: string | null;
  rxRateBps: number;
  txRateBps: number;
  rxHistory: number[];
  txHistory: number[];
  transportHealth: string | null;
  transportKind: string | null;
  recoveryAttempts: number;
  redirectStats: RedirectStats | null;
  routeGuardAvailability: string | null;
  snapshot: Record<string, unknown> | null;
  lastUpdated: string | null;
};

export const initialObservabilityState = (): ObservabilityLiveState => ({
  healthScore: null,
  healthStatus: null,
  rxRateBps: 0,
  txRateBps: 0,
  rxHistory: [],
  txHistory: [],
  transportHealth: null,
  transportKind: null,
  recoveryAttempts: 0,
  redirectStats: null,
  routeGuardAvailability: null,
  snapshot: null,
  lastUpdated: null,
});

function pushHistory(values: number[], value: number): number[] {
  const next = [...values, value];
  if (next.length > HISTORY_LEN) next.shift();
  return next;
}

export function applyObservabilityEvent(
  state: ObservabilityLiveState,
  type: string,
  payload: unknown,
): ObservabilityLiveState {
  const next = { ...state };
  const p = payload as Record<string, unknown>;
  const now = new Date().toISOString();

  switch (type) {
    case 'observability.health_changed': {
      if (typeof p.score === 'number') next.healthScore = p.score;
      if (typeof p.status === 'string') next.healthStatus = p.status;
      next.lastUpdated = now;
      break;
    }
    case 'routeguard.metrics_updated':
    case 'tunnel.stats_updated': {
      const rx = (p.rxRateBps ?? p.rxRate ?? 0) as number;
      const tx = (p.txRateBps ?? p.txRate ?? 0) as number;
      next.rxRateBps = rx;
      next.txRateBps = tx;
      next.rxHistory = pushHistory(state.rxHistory, rx);
      next.txHistory = pushHistory(state.txHistory, tx);
      next.lastUpdated = now;
      break;
    }
    case 'routeguard.transport_health': {
      if (typeof p.health === 'string') next.transportHealth = p.health;
      if (typeof p.kind === 'string') next.transportKind = p.kind;
      next.lastUpdated = now;
      break;
    }
    case 'routeguard.transport_recovery': {
      next.recoveryAttempts = state.recoveryAttempts + 1;
      if (typeof p.health === 'string') next.transportHealth = p.health;
      next.lastUpdated = now;
      break;
    }
    case 'routeguard.dns_redirect_stats': {
      next.redirectStats = p as RedirectStats;
      next.lastUpdated = now;
      break;
    }
    case 'routeguard.availability_changed': {
      if (typeof p.availability === 'string') next.routeGuardAvailability = p.availability;
      next.lastUpdated = now;
      break;
    }
    default:
      break;
  }

  return next;
}

export function mergeObservabilitySnapshot(
  state: ObservabilityLiveState,
  snap: Record<string, unknown>,
  rgStatus?: RouteGuardStatus | null,
): ObservabilityLiveState {
  const next = { ...state, snapshot: snap, lastUpdated: new Date().toISOString() };

  const health = snap.health as { score?: number; status?: string } | undefined;
  if (health?.score != null) next.healthScore = health.score;
  if (health?.status) next.healthStatus = health.status;

  const tunnel = snap.tunnel as { stats?: { rxRateBps?: number; txRateBps?: number } } | undefined;
  if (tunnel?.stats) {
    if (tunnel.stats.rxRateBps != null) {
      next.rxRateBps = tunnel.stats.rxRateBps;
      next.rxHistory = pushHistory(state.rxHistory, tunnel.stats.rxRateBps);
    }
    if (tunnel.stats.txRateBps != null) {
      next.txRateBps = tunnel.stats.txRateBps;
      next.txHistory = pushHistory(state.txHistory, tunnel.stats.txRateBps);
    }
  }

  const transport = snap.transport as { health?: string; kind?: string; recovery?: { attempts?: number } } | undefined;
  if (transport?.health) next.transportHealth = transport.health;
  if (transport?.kind) next.transportKind = transport.kind;
  if (transport?.recovery?.attempts != null) next.recoveryAttempts = transport.recovery.attempts;

  if (rgStatus) {
    next.routeGuardAvailability = rgStatus.availability;
    next.redirectStats = rgStatus.domain?.redirectStats ?? next.redirectStats;
    if (rgStatus.health?.score != null) next.healthScore = rgStatus.health.score;
  }

  return next;
}

export function formatRateBps(bps: number): string {
  if (bps < 1024) return `${bps} B/s`;
  if (bps < 1024 * 1024) return `${(bps / 1024).toFixed(1)} KB/s`;
  return `${(bps / (1024 * 1024)).toFixed(2)} MB/s`;
}

export function healthBand(score: number | null): 'healthy' | 'degraded' | 'critical' | 'unknown' {
  if (score == null) return 'unknown';
  if (score >= 80) return 'healthy';
  if (score >= 50) return 'degraded';
  return 'critical';
}
