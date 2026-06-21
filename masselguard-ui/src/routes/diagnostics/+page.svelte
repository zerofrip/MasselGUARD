<script lang="ts">
  import { createQuery } from '@tanstack/svelte-query';
  import { save } from '@tauri-apps/plugin-dialog';
  import Badge from '@mg-ui-core/components/Badge.svelte';
  import Button from '@mg-ui-core/components/Button.svelte';
  import Sparkline from '@mg-ui-core/components/Sparkline.svelte';
  import StatusPill from '@mg-ui-core/components/StatusPill.svelte';
  import { api, formatBytes, formatConnectedSince, formatRateBps } from '$lib/api';
  import { agentLiveState, observabilityLiveState } from '$lib/events/agentEventStore.svelte';
  import { healthBand, mergeObservabilitySnapshot } from '$lib/events/observabilityState';
  import { showToast } from '$lib/stores/toast.svelte';
  import type { RouteGuardStatus } from '@mg-ui-core/types';

  let exportBusy = $state(false);
  let exportProgress = $state('');
  let exportTruncated = $state(false);
  let showConsent = $state(false);
  let consentIncludeCrashes = $state(false);
  let exportTier = $state<'sanitized' | 'support'>('sanitized');
  let consentChecked = $state(false);

  const rgQ = createQuery(() => ({
    queryKey: ['routeguard'],
    queryFn: () => api.routeguardStatus(),
    refetchInterval: 30_000,
  }));

  const configQ = createQuery(() => ({
    queryKey: ['config'],
    queryFn: () => api.configGet(),
  }));

  const obsQ = createQuery(() => ({
    queryKey: ['observability'],
    queryFn: async () => {
      try {
        return await api.routeguardObservability();
      } catch {
        return null;
      }
    },
    refetchInterval: 30_000,
    enabled: (rgQ.data?.availability ?? '') === 'running',
  }));

  $effect(() => {
    const cfg = configQ.data;
    if (cfg?.supportExportConsentAt) {
      consentChecked = true;
      consentIncludeCrashes = cfg.supportExportIncludeCrashes ?? false;
    }
  });

  $effect(() => {
    const snap = obsQ.data ?? (rgQ.data?.observability as Record<string, unknown> | null);
    if (snap && typeof snap === 'object') {
      Object.assign(
        observabilityLiveState,
        mergeObservabilitySnapshot(observabilityLiveState, snap, rgQ.data ?? null),
      );
    } else if (rgQ.data) {
      Object.assign(
        observabilityLiveState,
        mergeObservabilitySnapshot(observabilityLiveState, {}, rgQ.data),
      );
    }
  });

  const obs = $derived(observabilityLiveState);
  const rg = $derived(rgQ.data as RouteGuardStatus | undefined);
  const primary = $derived(agentLiveState.primaryTunnel);
  const snap = $derived((obs.snapshot ?? obsQ.data ?? rg?.observability ?? {}) as Record<string, unknown>);
  const tunnel = $derived(snap.tunnel as Record<string, unknown> | undefined);
  const transport = $derived(snap.transport as Record<string, unknown> | undefined);
  const routing = $derived(snap.routing as Record<string, unknown> | undefined);
  const networkLock = $derived(snap.networkLock as Record<string, unknown> | undefined);
  const capabilities = $derived(snap.capabilities as Record<string, unknown> | undefined);
  const band = $derived(healthBand(obs.healthScore));
  const healthLabel = $derived(
    band === 'healthy' ? 'Healthy' : band === 'degraded' ? 'Degraded' : band === 'critical' ? 'Critical' : 'Unknown',
  );

  const isDev = import.meta.env.DEV;

  function beginExport() {
    if (!consentChecked) {
      showConsent = true;
      return;
    }
    void runExport();
  }

  async function acceptConsent() {
    await api.configSet({
      supportExportConsentAt: new Date().toISOString(),
      supportExportIncludeCrashes: consentIncludeCrashes,
    });
    consentChecked = true;
    showConsent = false;
    void runExport();
  }

  async function runExport() {
    exportBusy = true;
    exportProgress = 'Starting…';
    exportTruncated = false;
    let pollTimer: ReturnType<typeof setInterval> | undefined;
    try {
      const dest = await save({
        filters: [{ name: 'ZIP', extensions: ['zip'] }],
        defaultPath: `support_bundle-${new Date().toISOString().slice(0, 10)}.zip`,
      });
      if (!dest) return;

      pollTimer = setInterval(async () => {
        try {
          const st = await api.supportExportStatus();
          if (st.phase && st.phase !== 'idle' && st.phase !== 'done') {
            exportProgress = st.phase.replace(/_/g, ' ');
          }
        } catch {
          /* ignore poll errors */
        }
      }, 500);

      const result = await api.supportExport({
        tier: exportTier,
        includeCrashReports: consentIncludeCrashes,
        dest: String(dest),
      });
      exportTruncated = !!result.truncated;
      const kb = result.sizeBytes ? Math.round(result.sizeBytes / 1024) : undefined;
      showToast(
        'success',
        `Support bundle exported${kb ? ` (${kb} KB)` : ''}${result.truncated ? ' — truncated' : ''}`,
      );
    } catch (e) {
      showToast('error', String(e));
    } finally {
      if (pollTimer) clearInterval(pollTimer);
      exportBusy = false;
      exportProgress = '';
    }
  }

  function degradedHandshake(): boolean {
    const secs = (tunnel?.stats as { lastHandshakeSecsAgo?: number } | undefined)?.lastHandshakeSecsAgo
      ?? primary?.lastHandshakeSecsAgo;
    return secs != null && secs > 120;
  }

  function driverMissing(): boolean {
    return !!(rg?.domain?.kernelRedirect && !rg?.domain?.driverPresent);
  }
</script>

<svelte:head><title>Diagnostics · MasselGUARD</title></svelte:head>

<h2 class="page-title">Diagnostics</h2>

{#if showConsent}
  <dialog open class="card consent">
    <h3>Export support bundle</h3>
    <p>
      Creates a ZIP with agent status, RouteGuard diagnostics, event history, and logs.
      Tier <strong>{exportTier}</strong> controls how much identifying data is included.
    </p>
    <label class="check">
      <input type="checkbox" bind:checked={consentIncludeCrashes} />
      Include crash reports (local only; not uploaded unless crash reporting is enabled in Settings)
    </label>
    <div class="consent-actions">
      <Button variant="ghost" onclick={() => (showConsent = false)}>Cancel</Button>
      <Button variant="primary" onclick={acceptConsent}>Export</Button>
    </div>
  </dialog>
{/if}

<header class="card header-bar">
  <div class="health-block">
    <span class="score" class:healthy={band === 'healthy'} class:degraded={band === 'degraded'} class:critical={band === 'critical'}>
      {obs.healthScore ?? '—'}
    </span>
    <div>
      <strong>{healthLabel}</strong>
      <p class="muted">Composite health score</p>
    </div>
  </div>
  <div class="rg-block">
    <StatusPill connected={rg?.availability === 'running'} label={rg?.availability ?? 'unknown'} />
    <p class="muted">RouteGuard</p>
  </div>
  <div class="export-controls">
    {#if isDev}
      <label class="tier-select">
        Tier
        <select bind:value={exportTier}>
          <option value="sanitized">sanitized</option>
          <option value="support">support</option>
        </select>
      </label>
    {/if}
    <Button variant="primary" onclick={beginExport} disabled={exportBusy}>
      {exportBusy ? (exportProgress || 'Exporting…') : 'Export support bundle'}
    </Button>
    {#if exportTruncated}
      <Badge variant="warning">Truncated</Badge>
    {/if}
  </div>
</header>

{#if rgQ.isLoading}
  <p class="muted">Loading…</p>
{:else if rg?.availability !== 'running'}
  <p class="card muted">RouteGuard is not running. Start RouteGuard to view live diagnostics and export a support bundle.</p>
{:else}
  <div class="grid two-col">
    <section class="card" class:warn={degradedHandshake()}>
      <h3>Tunnel</h3>
      <dl>
        <dt>Name</dt><dd>{(tunnel?.name as string) ?? primary?.name ?? '—'}</dd>
        <dt>Backend</dt><dd>{(tunnel?.backend as { kind?: string })?.kind ?? '—'}</dd>
        <dt>Peers</dt><dd>{(tunnel?.stats as { peerCount?: number })?.peerCount ?? primary?.peerCount ?? '—'}</dd>
        <dt>Handshake</dt>
        <dd class:warn-text={degradedHandshake()}>
          {(tunnel?.stats as { lastHandshakeSecsAgo?: number })?.lastHandshakeSecsAgo ?? primary?.lastHandshakeSecsAgo ?? '—'}s ago
        </dd>
        <dt>RX rate</dt><dd>{formatRateBps(obs.rxRateBps)}</dd>
        <dt>TX rate</dt><dd>{formatRateBps(obs.txRateBps)}</dd>
        <dt>Session</dt><dd>{formatConnectedSince(primary?.connectedSince)}</dd>
      </dl>
      <a href="/tunnels">View tunnels →</a>
    </section>

    <section class="card" class:warn={obs.transportHealth === 'degraded' || obs.transportHealth === 'failed'}>
      <h3>Transport</h3>
      <dl>
        <dt>Kind</dt><dd>{obs.transportKind ?? (transport?.kind as string) ?? '—'}</dd>
        <dt>Health</dt>
        <dd>
          {#if obs.transportHealth}
            <Badge variant={obs.transportHealth === 'healthy' ? 'success' : 'warning'}>{obs.transportHealth}</Badge>
          {:else}—{/if}
        </dd>
        <dt>Recovery attempts</dt><dd>{obs.recoveryAttempts}</dd>
        <dt>Remote</dt><dd class="mono">{(transport?.remoteTransport as string) ?? '—'}</dd>
      </dl>
    </section>

    <section class="card">
      <h3>Routing</h3>
      <dl>
        <dt>App rules</dt><dd>{(routing?.appRules as { count?: number })?.count ?? '—'}</dd>
        <dt>IP rules</dt><dd>{(routing?.ipRules as { count?: number })?.count ?? '—'}</dd>
        <dt>Domain rules</dt><dd>{rg?.domain?.rules ?? '—'}</dd>
        <dt>Mode</dt><dd>{(routing?.mode as string) ?? '—'}</dd>
      </dl>
      <a href="/split-tunnel">Split tunnel →</a>
    </section>

    <section class="card" class:warn={driverMissing()}>
      <h3>Domain</h3>
      <dl>
        <dt>Effective</dt><dd>{rg?.domain?.effective ? 'Yes' : 'No'}</dd>
        <dt>Resolved IPs</dt><dd>{rg?.domain?.resolvedIps ?? '—'}</dd>
        <dt>Kernel redirect</dt><dd>{rg?.domain?.kernelRedirect ? 'On' : 'Off'}</dd>
        <dt>Driver</dt><dd class:warn-text={driverMissing()}>{rg?.domain?.driverPresent ? 'Present' : 'Absent'}</dd>
        <dt>Redirected (v4)</dt><dd>{obs.redirectStats?.redirectedV4 ?? '—'}</dd>
      </dl>
    </section>

    <section class="card">
      <h3>Network Lock</h3>
      <dl>
        <dt>Active</dt><dd>{(networkLock?.active as boolean) ? 'Yes' : 'No'}</dd>
        <dt>Filters</dt><dd>{(networkLock?.filterCount as number) ?? '—'}</dd>
        <dt>Violations/min</dt><dd>{(networkLock?.violationsPerMin as number) ?? '—'}</dd>
      </dl>
      <a href="/network-lock">Network Lock →</a>
    </section>

    <section class="card">
      <h3>Capabilities</h3>
      <dl>
        <dt>Observability</dt><dd>{rg?.negotiated?.observability ? 'Yes' : 'No'}</dd>
        <dt>Diagnostics export</dt><dd>{rg?.negotiated?.diagnosticsExport ? 'Yes' : 'No'}</dd>
        <dt>Domain routing</dt><dd>{rg?.negotiated?.domainRouting ? 'Yes' : 'No'}</dd>
        <dt>Schema</dt><dd>{(capabilities?.schemaVersion as number) ?? '—'}</dd>
      </dl>
    </section>
  </div>

  <section class="card trends" style="margin-top: 1rem">
    <h3>Throughput (live)</h3>
    <div class="spark-row">
      <div>
        <label>RX</label>
        <strong>{formatRateBps(obs.rxRateBps)}</strong>
        <Sparkline values={obs.rxHistory} width={200} height={40} />
      </div>
      <div>
        <label>TX</label>
        <strong>{formatRateBps(obs.txRateBps)}</strong>
        <Sparkline values={obs.txHistory} width={200} height={40} stroke="var(--mg-success)" fill="color-mix(in srgb, var(--mg-success) 20%, transparent)" />
      </div>
      <div>
        <label>Total RX</label>
        <strong>{formatBytes((tunnel?.stats as { rxBytes?: number })?.rxBytes ?? primary?.rxBytes ?? 0)}</strong>
      </div>
      <div>
        <label>Total TX</label>
        <strong>{formatBytes((tunnel?.stats as { txBytes?: number })?.txBytes ?? primary?.txBytes ?? 0)}</strong>
      </div>
    </div>
  </section>

  <footer class="card footer" style="margin-top: 1rem">
    <div>
      <strong>Event stream</strong>
      <p class="muted">
        {agentLiveState.streamConnected ? 'Connected' : 'Disconnected'}
        {#if agentLiveState.streamDegraded} · degraded{/if}
        {#if agentLiveState.streamGap} · gap detected{/if}
        · gaps {agentLiveState.gapsDetected}
      </p>
    </div>
    <div>
      <strong>Bridge</strong>
      <p class="muted">Last event #{rg?.bridge?.lastEventId ?? '—'}</p>
    </div>
    <div>
      <strong>Updated</strong>
      <p class="muted">{obs.lastUpdated ? new Date(obs.lastUpdated).toLocaleTimeString() : '—'}</p>
    </div>
  </footer>
{/if}

<style>
  .page-title {
    margin: 0 0 1rem;
    font-size: 1.35rem;
  }

  .header-bar {
    display: flex;
    align-items: center;
    gap: 1.5rem;
    flex-wrap: wrap;
    margin-bottom: 1rem;
  }

  .health-block {
    display: flex;
    align-items: center;
    gap: 0.75rem;
  }

  .score {
    font-size: 2rem;
    font-weight: 800;
    min-width: 3ch;
    text-align: center;
  }

  .score.healthy { color: var(--mg-success); }
  .score.degraded { color: var(--mg-warning); }
  .score.critical { color: var(--mg-danger); }

  .rg-block {
    flex: 1;
  }

  .export-controls {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    flex-wrap: wrap;
  }

  .tier-select {
    display: flex;
    align-items: center;
    gap: 0.35rem;
    font-size: 0.85rem;
    color: var(--mg-text-muted);
  }

  .consent {
    max-width: 28rem;
    margin-bottom: 1rem;
  }

  .consent h3 {
    margin-top: 0;
  }

  .check {
    display: flex;
    gap: 0.5rem;
    align-items: flex-start;
    margin: 1rem 0;
    font-size: 0.9rem;
  }

  .consent-actions {
    display: flex;
    justify-content: flex-end;
    gap: 0.5rem;
  }

  .muted {
    margin: 0.15rem 0 0;
    color: var(--mg-text-muted);
    font-size: 0.85rem;
  }

  .grid.two-col {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
    gap: 1rem;
  }

  section h3 {
    margin: 0 0 0.75rem;
    font-size: 1rem;
  }

  dl {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 0.35rem 1rem;
    margin: 0 0 0.75rem;
  }

  dt {
    color: var(--mg-text-muted);
    font-size: 0.85rem;
  }

  dd {
    margin: 0;
    font-weight: 600;
  }

  section.warn {
    border-color: color-mix(in srgb, var(--mg-warning) 50%, var(--mg-border));
  }

  .warn-text {
    color: var(--mg-warning);
  }

  .mono {
    font-family: ui-monospace, monospace;
    font-size: 0.85rem;
    word-break: break-all;
  }

  a {
    color: var(--mg-accent);
    font-size: 0.9rem;
  }

  .spark-row {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
    gap: 1rem;
  }

  .spark-row label {
    display: block;
    color: var(--mg-text-muted);
    font-size: 0.85rem;
  }

  .footer {
    display: flex;
    gap: 2rem;
    flex-wrap: wrap;
  }

  .footer p {
    margin: 0.25rem 0 0;
  }
</style>
