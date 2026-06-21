<script lang="ts">
  import { createQuery } from '@tanstack/svelte-query';
  import ConnectionHero from '@mg-ui-core/components/ConnectionHero.svelte';
  import Badge from '@mg-ui-core/components/Badge.svelte';
  import Button from '@mg-ui-core/components/Button.svelte';
  import Sparkline from '@mg-ui-core/components/Sparkline.svelte';
  import ProfileSourceBadge from '@mg-ui-core/components/ProfileSourceBadge.svelte';
  import { api, formatBytes, formatConnectedSince, formatRateBps } from '$lib/api';
  import { agentLiveState, observabilityLiveState } from '$lib/events/agentEventStore.svelte';
  import { favorites, sortedRecent, tunnelProfileState } from '$lib/stores/tunnelProfiles.svelte';

  const statusQ = createQuery(() => ({
    queryKey: ['tunnel', 'status'],
    queryFn: () => api.tunnelStatus(),
    enabled: !agentLiveState.hydrated,
  }));

  const listQ = createQuery(() => ({
    queryKey: ['tunnel', 'list', 'dashboard'],
    queryFn: async () => {
      const res = await api.tunnelList({ sort: 'lastUsed' });
      const { setTunnelList } = await import('$lib/stores/tunnelProfiles.svelte');
      setTunnelList(res.tunnels);
      return res;
    },
  }));

  const ipQ = createQuery(() => ({
    queryKey: ['public-ip'],
    queryFn: () => api.publicIpRefresh(),
    refetchInterval: 300_000,
  }));

  const primary = $derived(agentLiveState.primaryTunnel);
  const obs = $derived(observabilityLiveState);
  const rxRate = $derived(obs.rxRateBps || primary?.rxRateBps || 0);
  const txRate = $derived(obs.txRateBps || primary?.txRateBps || 0);
  const tunnelName = $derived(primary?.name ?? '');
  const connected = $derived(!!primary?.active);
  const wifi = $derived(agentLiveState.wifi);
  const recent = $derived(sortedRecent(5));
  const favs = $derived(favorites());
  const lastConnected = $derived(
    [...tunnelProfileState.tunnels]
      .filter((t) => t.lastUsedAt)
      .sort((a, b) => (b.lastUsedAt ?? '').localeCompare(a.lastUsedAt ?? ''))[0] ?? null,
  );
  const showStreamBanner = $derived(
    agentLiveState.hydrated &&
      (!agentLiveState.streamConnected || agentLiveState.streamGap),
  );
  const bannerMessage = $derived(
    agentLiveState.streamGap
      ? `Event gap detected (seq ${agentLiveState.lastSeq ?? '?'}…) — resyncing…`
      : agentLiveState.streamDegraded
        ? 'Event stream degraded — refreshing every 5s…'
        : 'Reconnecting to service…',
  );

  async function connect() {
    if (!tunnelName) return;
    await api.tunnelConnect(tunnelName);
  }

  async function disconnect() {
    await api.tunnelDisconnect(tunnelName || undefined);
  }

  async function reconnect() {
    if (!tunnelName) return;
    await api.tunnelReconnect(tunnelName);
  }

  async function quickConnect(name: string) {
    await api.tunnelConnect(name);
  }

  async function quickDisconnect(name: string) {
    await api.tunnelDisconnect(name);
  }
</script>

<svelte:head><title>Dashboard · MasselGUARD</title></svelte:head>

<h2 class="page-title">Dashboard</h2>

{#if showStreamBanner}
  <p class="stream-banner">{bannerMessage}</p>
{/if}

{#if !agentLiveState.hydrated && statusQ.isLoading}
  <p class="muted">Loading…</p>
{:else if statusQ.error && !agentLiveState.hydrated}
  <p class="error">{statusQ.error.message}</p>
{:else}
  <ConnectionHero
    state={connected ? 'connected' : 'disconnected'}
    tunnel={tunnelName || 'No active tunnel'}
    onConnect={connect}
    onDisconnect={disconnect}
    onReconnect={reconnect}
  />

  <section class="stat-grid card" style="margin-top: 1rem">
    <div class="stat-item">
      <label>Peers</label>
      <strong>{primary?.peerCount ?? '—'}</strong>
    </div>
    <div class="stat-item">
      <label>Last handshake</label>
      <strong>{primary?.lastHandshakeSecsAgo != null ? `${primary.lastHandshakeSecsAgo}s ago` : '—'}</strong>
    </div>
    <div class="stat-item">
      <label>Duration</label>
      <strong>{formatConnectedSince(primary?.connectedSince)}</strong>
    </div>
    <div class="stat-item">
      <label>Public IP</label>
      <strong>{ipQ.data?.ip ?? agentLiveState.publicIp ?? '—'}</strong>
    </div>
    <div class="stat-item">
      <label>Download</label>
      <strong>{formatBytes(primary?.rxBytes ?? 0)}</strong>
      {#if connected}
        <span class="rate">{formatRateBps(rxRate)}</span>
        <Sparkline values={obs.rxHistory} width={100} height={24} />
      {/if}
    </div>
    <div class="stat-item">
      <label>Upload</label>
      <strong>{formatBytes(primary?.txBytes ?? 0)}</strong>
      {#if connected}
        <span class="rate">{formatRateBps(txRate)}</span>
        <Sparkline values={obs.txHistory} width={100} height={24} stroke="var(--mg-success)" fill="color-mix(in srgb, var(--mg-success) 20%, transparent)" />
      {/if}
    </div>
  </section>

  {#if lastConnected}
    <section class="card quick-section" style="margin-top: 1rem">
      <h3>Last connected</h3>
      <div class="quick-row">
        <div>
          <strong>{lastConnected.name}</strong>
          <span class="muted">{lastConnected.endpointSummary ?? lastConnected.group}</span>
        </div>
        <ProfileSourceBadge source={lastConnected.profileSource} />
        <div class="quick-actions">
          {#if lastConnected.active}
            <Button variant="ghost" onclick={() => quickDisconnect(lastConnected.name)}>Disconnect</Button>
            <Button variant="ghost" onclick={() => api.tunnelReconnect(lastConnected.name)}>Reconnect</Button>
          {:else}
            <Button variant="ghost" onclick={() => quickConnect(lastConnected.name)}>Connect</Button>
          {/if}
        </div>
      </div>
    </section>
  {/if}

  {#if favs.length}
    <section class="card quick-section" style="margin-top: 1rem">
      <h3>Favorites</h3>
      {#each favs as t (t.name)}
        <div class="quick-row">
          <a href="/tunnels/{encodeURIComponent(t.name)}"><strong>{t.name}</strong></a>
          <Button variant="ghost" onclick={() => (t.active ? quickDisconnect(t.name) : quickConnect(t.name))}>
            {t.active ? 'Disconnect' : 'Connect'}
          </Button>
        </div>
      {/each}
    </section>
  {/if}

  {#if recent.length}
    <section class="card quick-section" style="margin-top: 1rem">
      <h3>Recent</h3>
      {#each recent as t (t.name)}
        <div class="quick-row">
          <a href="/tunnels/{encodeURIComponent(t.name)}"><strong>{t.name}</strong></a>
          <span class="muted">{t.endpointSummary ?? '—'}</span>
          <Button variant="ghost" onclick={() => (t.active ? quickDisconnect(t.name) : quickConnect(t.name))}>
            {t.active ? 'Disconnect' : 'Connect'}
          </Button>
        </div>
      {/each}
    </section>
  {/if}

  <section class="card network" style="margin-top: 1rem">
    <h3>Active network</h3>
    {#if wifi.ssid}
      <p>
        {wifi.ssid}
        {#if wifi.isOpen}
          <Badge variant="warning">Open</Badge>
        {:else}
          <Badge variant="success">Secured</Badge>
        {/if}
      </p>
    {:else}
      <p class="muted">Not connected to Wi-Fi</p>
    {/if}
  </section>
{/if}

<style>
  .page-title {
    margin: 0 0 1rem;
    font-size: 1.35rem;
  }

  .stream-banner {
    margin: 0 0 1rem;
    padding: 0.5rem 0.75rem;
    border-radius: 8px;
    background: color-mix(in srgb, var(--mg-warning) 15%, transparent);
    color: var(--mg-text-muted);
    font-size: 0.9rem;
  }

  .stat-item {
    display: flex;
    flex-direction: column;
    gap: 0.2rem;
  }

  .rate {
    font-size: 0.8rem;
    color: var(--mg-text-muted);
    font-weight: 500;
  }

  .muted {
    color: var(--mg-text-muted);
  }

  .error {
    color: var(--mg-danger);
  }

  .network h3 {
    margin: 0 0 0.5rem;
    font-size: 1rem;
  }

  .network p {
    margin: 0;
    display: flex;
    align-items: center;
    gap: 0.5rem;
  }

  .quick-section h3 {
    margin: 0 0 0.65rem;
    font-size: 1rem;
  }

  .quick-row {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    padding: 0.45rem 0;
    border-top: 1px solid var(--mg-border);
    flex-wrap: wrap;
  }

  .quick-row:first-of-type {
    border-top: none;
  }

  .quick-actions {
    margin-left: auto;
    display: flex;
    gap: 0.35rem;
  }
</style>
