import { invoke } from '@tauri-apps/api/core';
import { listen, type UnlistenFn } from '@tauri-apps/api/event';
import type { QueryClient } from '@tanstack/svelte-query';
import type { ExtendedStreamHealth, NormalizedEventEnvelope } from '@mg-ui-core/types/events';
import { api } from '$lib/api';
import { applyEvent, hydrateFromStatus } from './applyEvent';
import { EventStreamManager } from './EventStreamManager';
import type { AgentLiveState, StreamMeta } from './types';
import { initialLiveState } from './types';
import {
  applyObservabilityEvent,
  initialObservabilityState,
  mergeObservabilitySnapshot,
  type ObservabilityLiveState,
} from './observabilityState';
import { patchTunnelSummary, removeTunnel, setTunnelList } from '$lib/stores/tunnelProfiles.svelte';
import { showToast } from '$lib/stores/toast.svelte';
import type { TunnelSummary } from '@mg-ui-core/types';

const FALLBACK_INTERVAL_MS = 30_000;
const DEGRADED_INTERVAL_MS = 5_000;
const HEARTBEAT_STALE_MS = 20_000;

export const agentLiveState = $state<AgentLiveState & {
  lastSeq: number | null;
  gapsDetected: number;
  streamGap: boolean;
}>({
  ...initialLiveState(),
  lastSeq: null,
  gapsDetected: 0,
  streamGap: false,
});

export const observabilityLiveState = $state<ObservabilityLiveState>(initialObservabilityState());

let fallbackTimer: ReturnType<typeof setInterval> | null = null;
let healthTimer: ReturnType<typeof setInterval> | null = null;
let streamManager: EventStreamManager | null = null;

async function hydrateInitial(queryClient: QueryClient) {
  try {
    const [status, wifi, list, rgStatus] = await Promise.all([
      api.tunnelStatus(),
      api.wifiCurrent(),
      api.tunnelList(),
      api.routeguardStatus().catch(() => null),
    ]);
    hydrateFromStatus(agentLiveState, status, wifi);
    setTunnelList(list.tunnels);
    queryClient.setQueryData(['tunnel', 'status'], status);
    queryClient.setQueryData(['tunnel', 'list'], list);
    queryClient.setQueryData(['wifi', 'current'], wifi);
    if (rgStatus) {
      queryClient.setQueryData(['routeguard'], rgStatus);
      Object.assign(
        observabilityLiveState,
        mergeObservabilitySnapshot(observabilityLiveState, (rgStatus.observability as Record<string, unknown>) ?? {}, rgStatus),
      );
    }
  } catch {
    /* RPC may fail before agent starts */
  }
}

async function fallbackRefetch(queryClient: QueryClient) {
  try {
    const [status, wifi] = await Promise.all([api.tunnelStatus(), api.wifiCurrent()]);
    hydrateFromStatus(agentLiveState, status, wifi);
    queryClient.setQueryData(['tunnel', 'status'], status);
    queryClient.setQueryData(['wifi', 'current'], wifi);
    agentLiveState.streamGap = false;
  } catch {
    /* best effort */
  }
}

function clearFallback() {
  if (fallbackTimer) {
    clearInterval(fallbackTimer);
    fallbackTimer = null;
  }
}

function scheduleFallback(queryClient: QueryClient) {
  clearFallback();
  const degraded = agentLiveState.streamDegraded;
  const disconnected = !agentLiveState.streamConnected;
  if (!disconnected && !degraded && !agentLiveState.streamGap) return;

  const interval = degraded || agentLiveState.streamGap ? DEGRADED_INTERVAL_MS : FALLBACK_INTERVAL_MS;
  fallbackTimer = setInterval(() => fallbackRefetch(queryClient), interval);
}

function onNormalizedEvent(evt: NormalizedEventEnvelope) {
  const next = applyEvent(agentLiveState, {
    type: evt.type,
    payload: evt.payload,
    ts: evt.ts ? Date.parse(evt.ts) : undefined,
    version: evt.version,
    seq: evt.seq ?? undefined,
  });
  Object.assign(agentLiveState, next);
  if (isObservabilityEvent(evt.type)) {
    Object.assign(observabilityLiveState, applyObservabilityEvent(observabilityLiveState, evt.type, evt.payload));
  }
  if (evt.seq != null) agentLiveState.lastSeq = evt.seq;
}

function isObservabilityEvent(type: string): boolean {
  return (
    type === 'observability.health_changed' ||
    type === 'routeguard.metrics_updated' ||
    type === 'tunnel.stats_updated' ||
    type === 'routeguard.transport_health' ||
    type === 'routeguard.transport_recovery' ||
    type === 'routeguard.dns_redirect_stats' ||
    type === 'routeguard.availability_changed'
  );
}

async function pollStreamHealth(queryClient: QueryClient) {
  try {
    const health = await invoke<ExtendedStreamHealth>('event_stream_status');
    applyHealth(health, queryClient);
  } catch {
    agentLiveState.streamConnected = false;
    scheduleFallback(queryClient);
  }
}

function applyHealth(health: ExtendedStreamHealth, queryClient: QueryClient) {
  const now = Date.now();
  const heartbeatFresh =
    health.connected &&
    health.lastHeartbeatMs > 0 &&
    now - health.lastHeartbeatMs < HEARTBEAT_STALE_MS;

  agentLiveState.streamConnected = health.connected && heartbeatFresh;
  agentLiveState.streamDegraded =
    health.degraded || (health.connected && !heartbeatFresh);
  agentLiveState.lastSeq = health.lastSeq > 0 ? health.lastSeq : agentLiveState.lastSeq;
  agentLiveState.gapsDetected = health.gapsDetected;
  scheduleFallback(queryClient);
}

function onStreamMeta(meta: StreamMeta & { from?: number; to?: number }, queryClient: QueryClient) {
  if (meta.status === 'connected') {
    agentLiveState.streamConnected = true;
    agentLiveState.streamDegraded = false;
    agentLiveState.streamGap = false;
    streamManager?.resetGapAttempts();
    clearFallback();
  } else if (meta.status === 'gap') {
    agentLiveState.streamGap = true;
    agentLiveState.gapsDetected += 1;
    scheduleFallback(queryClient);
  } else if (meta.status === 'degraded') {
    agentLiveState.streamConnected = false;
    agentLiveState.streamDegraded = true;
    scheduleFallback(queryClient);
  } else {
    agentLiveState.streamConnected = false;
    scheduleFallback(queryClient);
  }
}

export async function initAgentEventSubscription(queryClient: QueryClient): Promise<() => void> {
  await hydrateInitial(queryClient);

  streamManager = new EventStreamManager(onNormalizedEvent, () => {
    agentLiveState.streamGap = true;
  });
  await streamManager.initCursor();

  const unsubs: UnlistenFn[] = [];

  unsubs.push(
    await listen<{ type: string; payload: unknown; ts?: unknown; version?: number; seq?: number }>(
      'mg/event',
      (e) => {
        streamManager?.handleRawEvent({
          type: e.payload.type,
          payload: e.payload.payload,
          ts: e.payload.ts as number | string | null | undefined,
          version: e.payload.version,
          seq: e.payload.seq ?? null,
        });
        if (e.payload.type === 'tunnel.state_changed') {
          queryClient.invalidateQueries({ queryKey: ['tunnel', 'list'] });
        }
        handleProfileEvent(e.payload.type, e.payload.payload, queryClient);
        handleRouteGuardEvent(e.payload.type, e.payload.payload, queryClient);
        handleObservabilityEvent(e.payload.type, e.payload.payload, queryClient);
        handleNetworkLockEvent(e.payload.type, queryClient);
        if (e.payload.type === 'notification') {
          const p = e.payload.payload as { primary?: string; secondary?: string };
          if (p?.primary) showToast('info', [p.primary, p.secondary].filter(Boolean).join(' — '));
        }
        if (e.payload.type === 'network.changed') {
          void api.publicIpRefresh().then((r) => {
            agentLiveState.publicIp = r.ip;
          });
        }
      },
    ),
  );

  unsubs.push(
    await listen<StreamMeta & { from?: number; to?: number }>('mg/stream', (e) => {
      onStreamMeta(e.payload, queryClient);
    }),
  );

  healthTimer = setInterval(() => pollStreamHealth(queryClient), 5_000);
  void pollStreamHealth(queryClient);

  return () => {
    unsubs.forEach((u) => u());
    clearFallback();
    if (healthTimer) {
      clearInterval(healthTimer);
      healthTimer = null;
    }
    streamManager = null;
  };
}

function handleObservabilityEvent(type: string, payload: unknown, queryClient: QueryClient) {
  if (!isObservabilityEvent(type)) return;
  Object.assign(observabilityLiveState, applyObservabilityEvent(observabilityLiveState, type, payload));
  queryClient.invalidateQueries({ queryKey: ['observability'] });
  if (type.startsWith('routeguard.') || type.startsWith('observability.')) {
    queryClient.invalidateQueries({ queryKey: ['routeguard'] });
  }
}

function handleRouteGuardEvent(type: string, payload: unknown, queryClient: QueryClient) {
  if (
    type.startsWith('routeguard.') ||
    type === 'killswitch.changed'
  ) {
    queryClient.invalidateQueries({ queryKey: ['routeguard'] });
    queryClient.invalidateQueries({ queryKey: ['split-tunnel'] });
    if (type === 'routeguard.availability_changed') {
      showToast('info', 'RouteGuard availability changed');
    } else if (type === 'routeguard.sync_completed') {
      const p = queryClient.getQueryData(['routeguard']) as { ok?: boolean } | undefined;
      showToast('info', 'RouteGuard sync completed');
    } else if (type === 'routeguard.routing_changed') {
      showToast('info', 'RouteGuard routing updated');
    } else if (type.startsWith('routeguard.domain_')) {
      showToast('info', 'Domain routing updated');
    }
  }
}

function handleNetworkLockEvent(type: string, queryClient: QueryClient) {
  if (
    type === 'networklock.enabled' ||
    type === 'networklock.disabled' ||
    type === 'networklock.policy_changed' ||
    type === 'networklock.recovered' ||
    type === 'killswitch.changed'
  ) {
    queryClient.invalidateQueries({ queryKey: ['network-lock'] });
    if (type === 'networklock.policy_changed') {
      showToast('info', 'Network Lock policy updated');
    } else if (type === 'networklock.recovered') {
      showToast('info', 'Network Lock recovered after restart');
    }
  }
}

function handleProfileEvent(type: string, payload: unknown, queryClient: QueryClient) {
  const summary = payload as TunnelSummary;
  switch (type) {
    case 'tunnel.created':
    case 'tunnel.imported':
    case 'tunnel.cloned':
    case 'tunnel.updated':
      if (summary?.name) {
        patchTunnelSummary(summary);
        queryClient.setQueryData(['tunnel', 'list'], (old: { tunnels: TunnelSummary[] } | undefined) => {
          const tunnels = old?.tunnels ?? [];
          const idx = tunnels.findIndex((t) => t.name === summary.name);
          const next = idx >= 0 ? tunnels.map((t, i) => (i === idx ? { ...t, ...summary } : t)) : [...tunnels, summary];
          return { tunnels: next };
        });
        if (type !== 'tunnel.updated') showToast('success', `Tunnel ${summary.name} saved`);
      }
      break;
    case 'tunnel.deleted': {
      const p = payload as { name?: string };
      if (p?.name) {
        removeTunnel(p.name);
        queryClient.setQueryData(['tunnel', 'list'], (old: { tunnels: TunnelSummary[] } | undefined) => ({
          tunnels: (old?.tunnels ?? []).filter((t) => t.name !== p.name),
        }));
        showToast('info', `Tunnel ${p.name} deleted`);
      }
      break;
    }
  }
}
