import type { TunnelSummary } from '@mg-ui-core/types';

export const tunnelProfileState = $state<{ tunnels: TunnelSummary[] }>({ tunnels: [] });

export function setTunnelList(tunnels: TunnelSummary[]) {
  tunnelProfileState.tunnels = tunnels;
}

export function patchTunnelSummary(summary: TunnelSummary) {
  const idx = tunnelProfileState.tunnels.findIndex((t) => t.name === summary.name);
  if (idx >= 0) {
    const next = [...tunnelProfileState.tunnels];
    next[idx] = { ...next[idx], ...summary };
    tunnelProfileState.tunnels = next;
  } else {
    tunnelProfileState.tunnels = [...tunnelProfileState.tunnels, summary];
  }
}

export function removeTunnel(name: string) {
  tunnelProfileState.tunnels = tunnelProfileState.tunnels.filter((t) => t.name !== name);
}

export function sortedRecent(limit = 5): TunnelSummary[] {
  return [...tunnelProfileState.tunnels]
    .filter((t) => !t.archived && t.lastUsedAt)
    .sort((a, b) => (b.lastUsedAt ?? '').localeCompare(a.lastUsedAt ?? ''))
    .slice(0, limit);
}

export function favorites(): TunnelSummary[] {
  return tunnelProfileState.tunnels.filter((t) => t.favorite && !t.archived);
}
