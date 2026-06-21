<script lang="ts">
  import { createQuery } from '@tanstack/svelte-query';
  import { save } from '@tauri-apps/plugin-dialog';
  import Button from '@mg-ui-core/components/Button.svelte';
  import ConfirmDialog from '@mg-ui-core/components/ConfirmDialog.svelte';
  import { api, formatDuration } from '$lib/api';
  import { showToast } from '$lib/stores/toast.svelte';

  let filterTunnel = $state('');
  let clearOpen = $state(false);

  const historyQ = createQuery(() => ({
    queryKey: ['history', 'tunnel', filterTunnel],
    queryFn: () =>
      api.historyTunnel({
        limit: 200,
        tunnelName: filterTunnel || undefined,
        includeFailures: true,
      }),
  }));

  const listQ = createQuery(() => ({
    queryKey: ['tunnel', 'list', 'names'],
    queryFn: () => api.tunnelList({ includeArchived: true }),
  }));

  function duration(entry: { connectedAt: string; disconnectedAt: string | null; failureReason?: string | null }) {
    if (entry.failureReason) return 'Failed';
    if (!entry.disconnectedAt) return 'Active';
    const secs = (Date.parse(entry.disconnectedAt) - Date.parse(entry.connectedAt)) / 1000;
    return formatDuration(Math.max(0, Math.floor(secs)));
  }

  async function exportHistory(format: 'json' | 'csv') {
    const path = await save({
      filters: [{ name: format.toUpperCase(), extensions: [format] }],
      defaultPath: `tunnel-history.${format}`,
    });
    if (!path) return;
    await api.historyTunnelExport(String(path), format, filterTunnel || undefined);
    showToast('success', 'History exported');
  }

  async function clearHistory() {
    await api.historyTunnelClear(filterTunnel || undefined);
    showToast('info', 'History cleared');
    historyQ.refetch();
  }
</script>

<svelte:head><title>History · MasselGUARD</title></svelte:head>

<h2>Connection history</h2>

<div class="filters card">
  <label>
    Filter by tunnel
    <select bind:value={filterTunnel} onchange={() => historyQ.refetch()}>
      <option value="">All tunnels</option>
      {#each listQ.data?.tunnels ?? [] as t}
        <option value={t.name}>{t.name}</option>
      {/each}
    </select>
  </label>
  <Button variant="ghost" onclick={() => exportHistory('csv')}>Export CSV</Button>
  <Button variant="ghost" onclick={() => exportHistory('json')}>Export JSON</Button>
  <Button variant="danger" onclick={() => (clearOpen = true)}>Clear</Button>
</div>

{#if historyQ.isLoading}
  <p class="muted">Loading…</p>
{:else}
  <div class="table card">
    <div class="head row">
      <span>Tunnel</span><span>Connected</span><span>Duration</span><span>Endpoint</span><span>Source</span><span>Failure</span>
    </div>
    {#each historyQ.data?.entries ?? [] as e (e.connectedAt + (e.tunnelName ?? ''))}
      <div class="row" class:failed={!!e.failureReason}>
        <span>{e.tunnelName}</span>
        <span>{new Date(e.connectedAt).toLocaleString()}</span>
        <span>{duration(e)}</span>
        <span class="muted">{e.endpoint ?? '—'}</span>
        <span>{e.source ?? 'Manual'}</span>
        <span class="fail">{e.failureReason ?? ''}</span>
      </div>
    {:else}
      <p class="muted pad">No history recorded yet.</p>
    {/each}
  </div>
{/if}

<ConfirmDialog
  bind:open={clearOpen}
  title="Clear history"
  message={filterTunnel ? `Clear history for "${filterTunnel}"?` : 'Clear all tunnel connection history?'}
  confirmLabel="Clear"
  danger
  onConfirm={clearHistory}
/>

<style>
  .filters { display: flex; flex-wrap: wrap; gap: 0.75rem; padding: 0.75rem; margin: 1rem 0; align-items: end; }
  .filters label { display: grid; gap: 0.25rem; font-size: 0.85rem; font-weight: 600; }
  select { padding: 0.4rem 0.55rem; border-radius: 6px; border: 1px solid var(--mg-border); background: var(--mg-bg); color: var(--mg-text); }
  .table { overflow: auto; }
  .row { display: grid; grid-template-columns: 1.2fr 1.4fr 0.8fr 1fr 0.8fr 1fr; gap: 0.5rem; padding: 0.55rem 0.75rem; border-top: 1px solid var(--mg-border); font-size: 0.88rem; }
  .head { font-weight: 700; border-top: none; background: var(--mg-surface-hover); }
  .failed { background: rgb(155 34 38 / 0.08); }
  .fail { color: #ff6b6b; }
  .muted { color: var(--mg-text-muted); }
  .pad { padding: 1rem; }
</style>
