<script lang="ts">
  import { createQuery } from '@tanstack/svelte-query';
  import { api } from '$lib/api';

  const summaryQ = createQuery(() => ({
    queryKey: ['telemetry-summary'],
    queryFn: () => api.telemetrySummary(),
    refetchInterval: 60_000,
  }));

  const resourcesQ = createQuery(() => ({
    queryKey: ['agent-resources'],
    queryFn: () => api.agentDiagnosticsResources(),
    refetchInterval: 60_000,
  }));

  const s = $derived(summaryQ.data ?? {});
  const updateHist = $derived(s.updateHistory as Record<string, unknown> | undefined);
  const counters = $derived((s.counters as Array<{ name: string; count: number; dims?: Record<string, string> }>) ?? []);
  const res = $derived(resourcesQ.data ?? {});
  const agentProc = $derived(res.agent as Record<string, unknown> | undefined);
  const rgProc = $derived((res.routeguard as Record<string, unknown> | undefined)?.process as Record<string, unknown> | undefined);
  const wfpFilters = $derived((res.routeguard as Record<string, unknown> | undefined)?.wfpFilters as number | undefined);

  /** Client-side PRS estimate from local telemetry (full gate uses compute-prs.ps1 + soak/chaos artifacts). */
  const prsEstimate = $derived.by(() => {
    const crashFree = typeof s.crashFreeSessionRate === 'number' ? s.crashFreeSessionRate : 100;
    const updateRate = typeof updateHist?.successRate === 'number' ? (updateHist.successRate as number) : 100;
    const normCrash = crashFree >= 98 ? 100 : (crashFree / 98) * 100;
    const normUpdate = updateRate >= 95 ? 100 : (updateRate / 95) * 100;
    const partial = 0.25 * normCrash + 0.15 * normUpdate + 0.60 * 100;
    return Math.round(partial * 10) / 10;
  });
</script>

<svelte:head><title>Reliability · MasselGUARD</title></svelte:head>

<h2 class="page-title">Reliability</h2>
<p class="muted">Local release-quality metrics. Telemetry upload is opt-in in Settings → Privacy.</p>

{#if summaryQ.isLoading}
  <p class="muted">Loading…</p>
{:else if summaryQ.isError}
  <p class="card warn">Could not load reliability summary. Is MasselGUARDAgent running?</p>
{:else}
  <section class="card prs-card">
    <h3>Production Readiness Score (estimate)</h3>
    <div class="prs-gauge" aria-label="PRS estimate">
      <span class="prs-value">{prsEstimate}</span>
      <span class="prs-label">/ 100</span>
    </div>
    <p class="muted small">Partial estimate from session + update metrics. Beta gate ≥ 85, stable ≥ 92 — see <code>scripts/compute-prs.ps1</code> for full score with soak/chaos artifacts.</p>
  </section>

  <div class="grid">
    <section class="card">
      <h3>Session health</h3>
      <dl>
        <dt>Crash-free rate</dt>
        <dd>{s.crashFreeSessionRate != null ? `${s.crashFreeSessionRate}%` : '—'}</dd>
        <dt>Sessions (clean / total)</dt>
        <dd>{s.sessionsClean ?? '—'} / {s.sessionsStarted ?? '—'}</dd>
        <dt>Local crash files</dt>
        <dd>{s.localCrashFiles ?? 0}</dd>
      </dl>
    </section>

    <section class="card">
      <h3>Resource health</h3>
      {#if resourcesQ.isLoading}
        <p class="muted">Loading…</p>
      {:else if resourcesQ.isError}
        <p class="muted">Resource snapshot unavailable</p>
      {:else}
        <dl>
          <dt>Agent RSS</dt>
          <dd>{agentProc?.rssMb != null ? `${agentProc.rssMb} MB` : '—'}</dd>
          <dt>Agent handles</dt>
          <dd>{agentProc?.handles ?? '—'}</dd>
          <dt>RouteGuard RSS</dt>
          <dd>{rgProc?.rssMb != null ? `${rgProc.rssMb} MB` : '—'}</dd>
          <dt>WFP filters</dt>
          <dd>{wfpFilters ?? '—'}</dd>
        </dl>
      {/if}
    </section>

    <section class="card">
      <h3>Updates</h3>
      <dl>
        <dt>Apply attempts</dt>
        <dd>{updateHist?.attempts ?? '—'}</dd>
        <dt>Success rate</dt>
        <dd>{updateHist?.successRate != null ? `${updateHist.successRate}%` : '—'}</dd>
        <dt>Failed</dt>
        <dd>{updateHist?.failed ?? '—'}</dd>
      </dl>
    </section>

    <section class="card">
      <h3>Install</h3>
      {#if s.installOutcome}
        <pre class="mono">{JSON.stringify(s.installOutcome, null, 2)}</pre>
      {:else}
        <p class="muted">No installer outcome recorded yet.</p>
      {/if}
    </section>

    <section class="card wide">
      <h3>Metric counters (local)</h3>
      {#if counters.length === 0}
        <p class="muted">No counters yet this session.</p>
      {:else}
        <table>
          <thead><tr><th>Metric</th><th>Dims</th><th>Count</th></tr></thead>
          <tbody>
            {#each counters as c}
              <tr>
                <td>{c.name}</td>
                <td class="mono">{c.dims ? JSON.stringify(c.dims) : '—'}</td>
                <td>{c.count}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      {/if}
    </section>
  </div>
{/if}

<style>
  .page-title { margin: 0 0 0.5rem; font-size: 1.35rem; }
  .muted { color: var(--mg-text-muted); font-size: 0.9rem; }
  .small { font-size: 0.8rem; margin: 0.5rem 0 0; }
  .prs-card { margin-top: 1rem; margin-bottom: 0.5rem; }
  .prs-gauge {
    display: flex;
    align-items: baseline;
    gap: 0.25rem;
    margin: 0.5rem 0;
  }
  .prs-value {
    font-size: 2.5rem;
    font-weight: 700;
    color: var(--mg-accent, #4ade80);
  }
  .prs-label { color: var(--mg-text-muted); font-size: 1rem; }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
    gap: 1rem;
    margin-top: 1rem;
  }
  .wide { grid-column: 1 / -1; }
  section h3 { margin: 0 0 0.75rem; font-size: 1rem; }
  dl {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 0.35rem 1rem;
    margin: 0;
  }
  dt { color: var(--mg-text-muted); font-size: 0.85rem; }
  dd { margin: 0; font-weight: 600; }
  .mono { font-family: ui-monospace, monospace; font-size: 0.8rem; word-break: break-all; }
  table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
  th, td { text-align: left; padding: 0.35rem 0.5rem; border-bottom: 1px solid var(--mg-border); }
  .warn { border-color: var(--mg-warning); }
</style>
