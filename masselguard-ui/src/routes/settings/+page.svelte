<script lang="ts">
  import { createQuery } from '@tanstack/svelte-query';
  import Button from '@mg-ui-core/components/Button.svelte';
  import { api } from '$lib/api';
  import { saveTheme, type ThemeMode } from '$lib/theme';
  import type { AppConfig } from '@mg-ui-core/types';

  const configQ = createQuery<AppConfig>(() => ({
    queryKey: ['config'],
    queryFn: () => api.configGet(),
  }));

  const agentQ = createQuery(() => ({
    queryKey: ['agent'],
    queryFn: () => api.agentPing(),
  }));

  const historyQ = createQuery(() => ({
    queryKey: ['history', 'tunnel'],
    queryFn: () => api.historyTunnel({ limit: 20 }),
    enabled: false,
  }));

  let themeMode: ThemeMode = $state('system');

  async function patch(p: Partial<AppConfig>) {
    await api.configSet(p as Record<string, unknown>);
    configQ.refetch();
  }

  $effect(() => {
    if (configQ.data?.systemThemeMode) {
      themeMode = (configQ.data.systemThemeMode as ThemeMode) || 'system';
    }
  });
</script>

<svelte:head><title>Settings · MasselGUARD</title></svelte:head>

<h2>Settings</h2>

<section class="card grid">
  <label>
    WireGuard install path
    <input
      value={configQ.data?.wireGuardInstallDirectory ?? ''}
      onchange={(e) => patch({ wireGuardInstallDirectory: (e.currentTarget as HTMLInputElement).value })}
    />
  </label>

  <label>
    App mode
    <select
      value={configQ.data?.mode ?? 'Standalone'}
      onchange={(e) => patch({ mode: (e.currentTarget as HTMLSelectElement).value })}
    >
      <option value="Standalone">Standalone (local tunnels)</option>
      <option value="Companion">Companion (WireGuard app)</option>
      <option value="Mixed">Mixed</option>
    </select>
  </label>

  <label class="check">
    <input
      type="checkbox"
      checked={configQ.data?.startWithWindows ?? false}
      onchange={(e) => patch({ startWithWindows: (e.currentTarget as HTMLInputElement).checked })}
    />
    Start with Windows
  </label>

  <label>
    Auto-reconnect mode
    <select
      value={configQ.data?.autoReconnectMode ?? 'always'}
      onchange={(e) => patch({ autoReconnectMode: (e.currentTarget as HTMLSelectElement).value })}
    >
      <option value="off">Off</option>
      <option value="per-tunnel">Per tunnel</option>
      <option value="always">Always</option>
    </select>
  </label>

  <label>
    Log level
    <select
      value={configQ.data?.logLevelSetting ?? 'normal'}
      onchange={(e) => patch({ logLevelSetting: (e.currentTarget as HTMLSelectElement).value })}
    >
      <option value="normal">Normal</option>
      <option value="extended">Extended</option>
    </select>
  </label>

  <label>
    Theme
    <select
      bind:value={themeMode}
      onchange={() => {
        patch({ systemThemeMode: themeMode });
        saveTheme(themeMode);
      }}
    >
      <option value="system">System</option>
      <option value="dark">Dark</option>
      <option value="light">Light</option>
    </select>
  </label>

  <label>
    Language
    <select
      value={configQ.data?.language ?? 'en'}
      onchange={(e) => patch({ language: (e.currentTarget as HTMLSelectElement).value })}
    >
      <option value="en">English</option>
      <option value="de">Deutsch</option>
      <option value="nl">Nederlands</option>
      <option value="fr">Français</option>
      <option value="es">Español</option>
    </select>
  </label>
</section>

<section class="card" style="margin-top: 1rem">
  <h3>History</h3>
  <label class="check">
    <input
      type="checkbox"
      checked={configQ.data?.storeConnectionHistory ?? true}
      onchange={(e) => patch({ storeConnectionHistory: (e.currentTarget as HTMLInputElement).checked })}
    />
    Capture connection history
  </label>
  <label class="check">
    <input
      type="checkbox"
      checked={configQ.data?.storeWifiHistory ?? true}
      onchange={(e) => patch({ storeWifiHistory: (e.currentTarget as HTMLInputElement).checked })}
    />
    Capture Wi-Fi history
  </label>
  <label class="check">
    <input
      type="checkbox"
      checked={configQ.data?.showTimeline ?? true}
      onchange={(e) => patch({ showTimeline: (e.currentTarget as HTMLInputElement).checked })}
    />
    Show timeline
  </label>
  <Button variant="ghost" onclick={() => historyQ.refetch()}>Refresh timeline preview</Button>

  {#if historyQ.data?.entries?.length}
    <ul class="timeline">
      {#each historyQ.data.entries.slice(0, 8) as e (e.connectedAt + (e.tunnelName ?? e.ssid ?? ''))}
        <li>{e.tunnelName ?? e.ssid} · {new Date(e.connectedAt).toLocaleString()}</li>
      {/each}
    </ul>
  {/if}
</section>

<section class="card about" style="margin-top: 1rem">
  <h3>About</h3>
  <p>Version {agentQ.data?.version ?? '—'}{agentQ.data?.codename ? ` — ${agentQ.data.codename}` : ''}</p>
  <p class="muted">Agent PID {agentQ.data?.pid ?? '—'} · uptime {agentQ.data?.uptimeSecs ?? 0}s</p>
</section>

<style>
  h2 { margin: 0 0 1rem; }
  h3 { margin: 0 0 0.75rem; font-size: 1rem; }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
    gap: 0.75rem;
  }
  label {
    display: flex;
    flex-direction: column;
    gap: 0.35rem;
    font-size: 0.85rem;
    color: var(--mg-text-muted);
  }
  .check { flex-direction: row; align-items: center; }
  .timeline { margin: 0.75rem 0 0; padding-left: 1.2rem; color: var(--mg-text-muted); font-size: 0.9rem; }
  .about p { margin: 0.25rem 0; }
  .muted { color: var(--mg-text-muted); font-size: 0.85rem; }
</style>
