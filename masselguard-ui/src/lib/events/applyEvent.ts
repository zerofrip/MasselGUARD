import type { TunnelStatus } from '@mg-ui-core/types';
import type {
  AgentEventEnvelope,
  AgentLiveState,
  AgentSnapshotPayload,
  NetworkChangedPayload,
  TunnelHandshakeUpdatedPayload,
  TunnelStateChangedPayload,
  TunnelStatsUpdatedPayload,
  WifiSsidChangedPayload,
} from './types';

function patchTunnel(state: AgentLiveState, name: string, patch: Partial<TunnelStatus>) {
  const existing = state.tunnels.get(name);
  if (!existing) return;
  const updated = { ...existing, ...patch };
  state.tunnels.set(name, updated);
  if (state.primaryTunnel?.name === name) {
    state.primaryTunnel = updated;
  }
}

function mergeSnapshot(state: AgentLiveState, snap: AgentSnapshotPayload) {
  state.tunnels = new Map(snap.tunnels.map((t) => [t.name, t]));
  state.primaryTunnel = snap.primary;
  state.wifi = snap.wifi;
  state.networkAvailable = snap.networkAvailable;
  state.hydrated = true;
}

export function applyEvent(state: AgentLiveState, event: AgentEventEnvelope): AgentLiveState {
  const next: AgentLiveState = {
    ...state,
    tunnels: new Map(state.tunnels),
    wifi: { ...state.wifi },
  };

  const payload = event.payload;

  switch (event.type) {
    case 'agent.snapshot': {
      mergeSnapshot(next, payload as AgentSnapshotPayload);
      break;
    }
    case 'tunnel.state_changed': {
      const p = payload as TunnelStateChangedPayload;
      const active = p.state === 'connected';
      const connectedSince =
        active && !next.tunnels.get(p.name)?.connectedSince
          ? new Date().toISOString()
          : active
            ? next.tunnels.get(p.name)?.connectedSince ?? new Date().toISOString()
            : null;
      patchTunnel(next, p.name, { active, connectedSince: connectedSince ?? null });
      if (active) {
        const t = next.tunnels.get(p.name);
        if (t) next.primaryTunnel = t;
      } else if (next.primaryTunnel?.name === p.name) {
        next.primaryTunnel =
          [...next.tunnels.values()].find((t) => t.active) ?? null;
      }
      break;
    }
    case 'tunnel.stats_updated': {
      const p = payload as TunnelStatsUpdatedPayload;
      patchTunnel(next, p.name, {
        rxBytes: p.rxBytes,
        txBytes: p.txBytes,
        rxRateBps: p.rxRate ?? next.tunnels.get(p.name)?.rxRateBps,
        txRateBps: p.txRate ?? next.tunnels.get(p.name)?.txRateBps,
        adapterUp: p.adapterUp ?? next.tunnels.get(p.name)?.adapterUp ?? false,
      });
      break;
    }
    case 'tunnel.handshake_updated': {
      const p = payload as TunnelHandshakeUpdatedPayload;
      patchTunnel(next, p.name, {
        peerCount: p.peerCount,
        lastHandshakeSecsAgo: p.lastHandshakeSecsAgo,
      });
      break;
    }
    case 'wifi.ssid_changed': {
      const p = payload as WifiSsidChangedPayload;
      next.wifi = { ...next.wifi, ssid: p.ssid, isOpen: p.isOpen };
      break;
    }
    case 'network.changed': {
      const p = payload as NetworkChangedPayload;
      next.networkAvailable = p.available;
      break;
    }
    default:
      break;
  }

  return next;
}

export function hydrateFromStatus(
  state: AgentLiveState,
  status: { primary: TunnelStatus | null; tunnels: TunnelStatus[] },
  wifi: { ssid: string | null; isOpen: boolean; manualMode: boolean },
) {
  state.tunnels = new Map(status.tunnels.map((t) => [t.name, t]));
  state.primaryTunnel = status.primary;
  state.wifi = wifi;
  state.hydrated = true;
}
