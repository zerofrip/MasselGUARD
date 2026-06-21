<script lang="ts">
  import { createQuery } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { getCurrentWebviewWindow } from '@tauri-apps/api/webviewWindow';
  import { onMount } from 'svelte';
  import Button from '@mg-ui-core/components/Button.svelte';
  import Badge from '@mg-ui-core/components/Badge.svelte';
  import StatusPill from '@mg-ui-core/components/StatusPill.svelte';
  import ProfileSourceBadge from '@mg-ui-core/components/ProfileSourceBadge.svelte';
  import ConfirmDialog from '@mg-ui-core/components/ConfirmDialog.svelte';
  import { api } from '$lib/api';
  import { showToast } from '$lib/stores/toast.svelte';
  import { setTunnelList } from '$lib/stores/tunnelProfiles.svelte';
  import type { TunnelSummary } from '@mg-ui-core/types';

  let search = $state('');
  let sort = $state('name');
  let showArchived = $state(false);
  let dragOver = $state(false);
  let deleteTarget = $state<TunnelSummary | null>(null);
  let confirmOpen = $state(false);

  const listQ = createQuery(() => ({
    queryKey: ['tunnel', 'list', search, sort, showArchived],
    queryFn: async () => {
      const res = await api.tunnelList({ search: search || undefined, sort, includeArchived: showArchived });
      setTunnelList(res.tunnels);
      return res;
    },
  }));

  onMount(() => {
    let unlisten: (() => void) | undefined;
    void getCurrentWebviewWindow()
      .onDragDropEvent((event) => {
        if (event.payload.type === 'over') dragOver = true;
        else if (event.payload.type === 'leave') dragOver = false;
        else if (event.payload.type === 'drop') {
          dragOver = false;
          const paths = event.payload.paths;
          if (paths[0]) void importPath(paths[0]);
        }
      })
      .then((fn) => {
        unlisten = fn;
      });
    return () => unlisten?.();
  });

  async function importPath(path: string) {
    try {
      await api.tunnelImport({ path, onConflict: 'rename' });
      showToast('success', 'Tunnel imported');
      listQ.refetch();
    } catch (e) {
      showToast('error', e instanceof Error ? e.message : 'Import failed');
    }
  }

  async function importConf() {
    const path = await api.pickConfFile();
    if (!path) return;
    await importPath(path);
  }

  async function toggleFavorite(t: TunnelSummary) {
    await api.tunnelUpdate({ name: t.name, favorite: !t.favorite });
  }

  async function toggleConnect(t: TunnelSummary) {
    if (t.active) await api.tunnelDisconnect(t.name);
    else await api.tunnelConnect(t.name);
  }

  async function cloneTunnel(t: TunnelSummary) {
    await api.tunnelClone(t.name);
    listQ.refetch();
  }

  function askDelete(t: TunnelSummary) {
    deleteTarget = t;
    confirmOpen = true;
  }

  async function doDelete() {
    if (!deleteTarget) return;
    await api.tunnelDelete(deleteTarget.name);
    deleteTarget = null;
    listQ.refetch();
  }

  async function toggleArchive(t: TunnelSummary) {
    await api.tunnelUpdate({ name: t.name, archived: !t.archived });
    listQ.refetch();
  }
</script>

<svelte:head><title>Tunnels · MasselGUARD</title></svelte:head>

<div class="toolbar">
  <h2>Profile Library</h2>
  <div class="toolbar-actions">
    <Button onclick={() => goto('/tunnels/new')}>New tunnel</Button>
    <Button variant="ghost" onclick={importConf}>Import .conf</Button>
  </div>
</div>

<div class="filters card">
  <input type="search" placeholder="Search name, endpoint, tags…" bind:value={search} />
  <select bind:value={sort}>
    <option value="name">Name</option>
    <option value="lastUsed">Last used</option>
    <option value="connectionCount">Connections</option>
    <option value="favorite">Favorites first</option>
  </select>
  <label class="check"><input type="checkbox" bind:checked={showArchived} /> Show archived</label>
</div>

<div class="drop-zone card" class:drag-over={dragOver}>
  <p class="muted">Drop a <code>.conf</code> file here or use Import</p>
</div>

{#if listQ.isLoading}
  <p class="muted">Loading tunnels…</p>
{:else}
  <div class="list">
    {#each listQ.data?.tunnels ?? [] as t (t.name)}
      <article class="row card">
        <button type="button" class="star" class:active={t.favorite} onclick={() => toggleFavorite(t)} aria-label="Favorite">★</button>
        <div class="meta">
          <a class="title-link" href="/tunnels/{encodeURIComponent(t.name)}"><strong>{t.name}</strong></a>
          <span class="muted">{t.endpointSummary || t.group || 'No endpoint'} · {t.connectionCount} connects</span>
          {#if t.tags?.length}
            <span class="tags">{#each t.tags as tag}<Badge>{tag}</Badge>{/each}</span>
          {/if}
        </div>
        <div class="badges">
          <ProfileSourceBadge source={t.profileSource} />
          <StatusPill connected={t.active} label={t.active ? 'Active' : 'Inactive'} />
          {#if t.archived}<Badge variant="accent">Archived</Badge>{/if}
        </div>
        <div class="actions">
          <Button variant="ghost" onclick={() => toggleConnect(t)}>{t.active ? 'Disconnect' : 'Connect'}</Button>
          <Button variant="ghost" onclick={() => cloneTunnel(t)}>Duplicate</Button>
          <Button variant="ghost" onclick={() => toggleArchive(t)}>{t.archived ? 'Unarchive' : 'Archive'}</Button>
          <Button variant="danger" onclick={() => askDelete(t)}>Delete</Button>
        </div>
      </article>
    {:else}
      <p class="muted">No tunnels yet. Create one or import a .conf file.</p>
    {/each}
  </div>
{/if}

<ConfirmDialog
  bind:open={confirmOpen}
  title="Delete tunnel"
  message={deleteTarget ? `Delete "${deleteTarget.name}"? This cannot be undone.` : ''}
  confirmLabel="Delete"
  danger
  onConfirm={doDelete}
/>

<style>
  .toolbar { display: flex; align-items: center; justify-content: space-between; gap: 1rem; margin-bottom: 1rem; }
  .toolbar-actions { display: flex; gap: 0.5rem; }
  .filters { display: flex; flex-wrap: wrap; gap: 0.75rem; padding: 0.75rem; margin-bottom: 0.75rem; align-items: center; }
  .filters input, .filters select { flex: 1; min-width: 160px; }
  .check { display: flex; align-items: center; gap: 0.35rem; font-size: 0.9rem; color: var(--mg-text-muted); }
  .drop-zone { padding: 1rem; text-align: center; margin-bottom: 1rem; border: 1px dashed var(--mg-border); }
  .drop-zone.drag-over { border-color: var(--mg-accent); background: var(--mg-surface-hover); }
  .list { display: flex; flex-direction: column; gap: 0.65rem; }
  .row { display: grid; grid-template-columns: auto 1fr auto auto; gap: 0.75rem; align-items: center; padding: 0.85rem 1rem; }
  .title-link { color: inherit; text-decoration: none; }
  .title-link:hover { color: var(--mg-accent); }
  .star { background: none; border: none; font-size: 1.2rem; color: var(--mg-text-muted); cursor: pointer; }
  .star.active { color: #fcc419; }
  .meta strong { display: block; }
  .tags { display: flex; flex-wrap: wrap; gap: 0.25rem; margin-top: 0.35rem; }
  .badges, .actions { display: flex; flex-wrap: wrap; gap: 0.35rem; align-items: center; }
  .muted { color: var(--mg-text-muted); }
</style>
