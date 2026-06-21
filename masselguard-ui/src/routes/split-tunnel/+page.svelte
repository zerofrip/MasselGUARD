<script lang="ts">
  import { createQuery } from '@tanstack/svelte-query';
  import Badge from '@mg-ui-core/components/Badge.svelte';
  import Button from '@mg-ui-core/components/Button.svelte';
  import { api } from '$lib/api';
  import { showToast } from '$lib/stores/toast.svelte';
  import type { RouteGuardStatus, SplitTunnelRules } from '@mg-ui-core/types';

  type Tab = 'app' | 'ip' | 'domain';
  let tab: Tab = $state('app');
  let busy = $state(false);

  const rulesQ = createQuery<SplitTunnelRules>(() => ({
    queryKey: ['split-tunnel'],
    queryFn: () => api.splitTunnelGet(),
  }));

  const rgQ = createQuery<RouteGuardStatus>(() => ({
    queryKey: ['routeguard'],
    queryFn: () => api.routeguardStatus(),
    refetchInterval: 10_000,
  }));

  let rules: SplitTunnelRules = $state({
    appRules: [],
    ipRules: [],
    domainRules: [],
    useRouteGuardBridge: false,
  });

  $effect(() => {
    if (rulesQ.data) rules = structuredClone(rulesQ.data);
  });

  const rg = $derived(rgQ.data);
  const availability = $derived(rg?.availability ?? 'absent');
  const domainEffective = $derived(
    rg?.negotiated?.domainRouting ?? rg?.domain?.effective ?? false
  );
  const domainResolved = $derived(rg?.domain?.resolvedIps ?? rg?.bridge?.lastDomainSync?.resolvedIps ?? 0);
  const kernelRedirect = $derived(rg?.domain?.kernelRedirect ?? false);
  const driverPresent = $derived(rg?.domain?.driverPresent ?? false);

  let testDomain = $state('*.netflix.com');
  let testResult = $state<{ target?: string; reason?: string } | null>(null);

  async function testDomainRouting() {
    busy = true;
    testResult = null;
    try {
      const res = await api.routeguardRoutingTest({ remoteIp: '0.0.0.0', domain: testDomain });
      testResult = res as { target?: string; reason?: string };
    } catch (e) {
      showToast('error', String(e));
    } finally {
      busy = false;
    }
  }

  async function save() {
    busy = true;
    try {
      await api.splitTunnelSet(rules);
      await rulesQ.refetch();
      await rgQ.refetch();
      showToast('success', 'Split tunnel rules saved');
    } catch (e) {
      showToast('error', String(e));
    } finally {
      busy = false;
    }
  }

  async function syncNow() {
    busy = true;
    try {
      const res = await api.routeguardSync(true);
      await rgQ.refetch();
      showToast(res.ok ? 'success' : 'error', res.ok ? 'RouteGuard synced' : (res.errors?.[0] ?? 'Sync failed'));
    } catch (e) {
      showToast('error', String(e));
    } finally {
      busy = false;
    }
  }

  async function startRouteGuard() {
    busy = true;
    try {
      const res = await api.routeguardStart(10);
      await rgQ.refetch();
      showToast(res.started ? 'success' : 'error', res.started ? 'RouteGuard started' : 'Failed to start RouteGuard');
    } catch (e) {
      showToast('error', String(e));
    } finally {
      busy = false;
    }
  }

  function addApp() {
    rules.appRules = [...rules.appRules, { id: crypto.randomUUID(), appPath: '', route: 'vpn', enabled: true }];
  }

  function addIp() {
    rules.ipRules = [...rules.ipRules, { id: crypto.randomUUID(), cidr: '', route: 'direct', enabled: true }];
  }

  function addDomain() {
    rules.domainRules = [...rules.domainRules, { id: crypto.randomUUID(), pattern: '', route: 'vpn', enabled: true }];
  }

  function badgeVariant(): 'success' | 'warning' | 'muted' {
    if (availability === 'running') return 'success';
    if (availability === 'installed') return 'warning';
    return 'muted';
  }
</script>

<svelte:head><title>Split Tunnel · MasselGUARD</title></svelte:head>

<h2>Split Tunnel</h2>

<section class="card status">
  <div>
    <strong>RouteGuard</strong>
    <p class="muted">Install: {rg?.installPath ?? '—'}</p>
    {#if rg?.bridge?.lastSyncAt}
      <p class="muted">Last sync: {new Date(rg.bridge.lastSyncAt).toLocaleString()}</p>
    {/if}
    {#if rg?.bridge?.lastSyncError}
      <p class="error">Sync error: {rg.bridge.lastSyncError}</p>
    {/if}
  </div>
  <div class="badges">
    <Badge variant={badgeVariant()}>{availability}</Badge>
    {#if rules.useRouteGuardBridge && rg?.negotiated?.appSplitTunnel}
      <Badge variant="success">Enforcement active</Badge>
    {:else if rules.useRouteGuardBridge}
      <Badge variant="warning">Config only</Badge>
    {/if}
    {#if rules.useRouteGuardBridge && rg?.negotiated?.awg}
      <Badge variant="success">AWG backend</Badge>
    {/if}
    {#if rules.useRouteGuardBridge && kernelRedirect}
      <Badge variant="success">Kernel DNS redirect</Badge>
    {:else if rules.useRouteGuardBridge && driverPresent}
      <Badge variant="warning">Callout driver ready</Badge>
    {:else if rules.useRouteGuardBridge && domainEffective}
      <Badge variant="success">Domain routing active</Badge>
    {:else if rules.useRouteGuardBridge && rules.domainRules.length > 0}
      <Badge variant="warning">Domain rules pending</Badge>
    {/if}
  </div>
</section>

<div class="actions-row">
  {#if availability === 'installed'}
    <Button onclick={startRouteGuard} disabled={busy}>Start RouteGuard</Button>
  {/if}
  {#if availability === 'running'}
    <Button variant="ghost" onclick={syncNow} disabled={busy}>Sync now</Button>
  {/if}
</div>

<label class="check card">
  <input type="checkbox" bind:checked={rules.useRouteGuardBridge} disabled={busy} />
  Use RouteGuard bridge when available
</label>

{#if tab === 'domain' || rules.domainRules.length > 0}
  {#if domainEffective}
    <p class="muted">
      Domain routing effective — {domainResolved} resolved IP(s) cached.
      {#if kernelRedirect}
        Kernel DNS redirect active.
      {:else if driverPresent}
        Callout driver loaded; enable <code>redirect_port_53</code> in RouteGuard config.
      {/if}
    </p>
  {:else}
    <p class="warn">Domain rules are synced but not fully effective until RouteGuard DNS redirect is active (install routeguard-callout.sys or set explicit_proxy).</p>
  {/if}
{/if}

<div class="tabs">
  <button class:active={tab === 'app'} onclick={() => (tab = 'app')}>App Rules</button>
  <button class:active={tab === 'ip'} onclick={() => (tab = 'ip')}>IP Rules</button>
  <button class:active={tab === 'domain'} onclick={() => (tab = 'domain')}>Domain Rules</button>
</div>

<section class="card">
  {#if tab === 'app'}
    {#each rules.appRules as rule, i (rule.id)}
      <div class="row">
        <input placeholder="C:\\Path\\chrome.exe" bind:value={rule.appPath} disabled={busy} />
        <select bind:value={rule.route} disabled={busy}>
          <option value="vpn">VPN</option>
          <option value="direct">Direct</option>
        </select>
        <label><input type="checkbox" bind:checked={rule.enabled} disabled={busy} /> Enabled</label>
        <Button variant="danger" disabled={busy} onclick={() => (rules.appRules = rules.appRules.filter((_, j) => j !== i))}>Remove</Button>
      </div>
    {/each}
    <Button onclick={addApp} disabled={busy}>Add app rule</Button>
  {:else if tab === 'ip'}
    {#each rules.ipRules as rule, i (rule.id)}
      <div class="row">
        <input placeholder="192.168.0.0/16" bind:value={rule.cidr} disabled={busy} />
        <select bind:value={rule.route} disabled={busy}>
          <option value="vpn">VPN</option>
          <option value="direct">Direct</option>
        </select>
        <label><input type="checkbox" bind:checked={rule.enabled} disabled={busy} /> Enabled</label>
        <Button variant="danger" disabled={busy} onclick={() => (rules.ipRules = rules.ipRules.filter((_, j) => j !== i))}>Remove</Button>
      </div>
    {/each}
    <Button onclick={addIp} disabled={busy}>Add IP rule</Button>
  {:else}
    {#each rules.domainRules as rule, i (rule.id)}
      <div class="row">
        <input placeholder="*.netflix.com" bind:value={rule.pattern} disabled={busy} />
        <select bind:value={rule.route} disabled={busy}>
          <option value="vpn">VPN</option>
          <option value="direct">Direct</option>
        </select>
        <label><input type="checkbox" bind:checked={rule.enabled} disabled={busy} /> Enabled</label>
        <Button variant="danger" disabled={busy} onclick={() => (rules.domainRules = rules.domainRules.filter((_, j) => j !== i))}>Remove</Button>
      </div>
    {/each}
    <Button onclick={addDomain} disabled={busy}>Add domain rule</Button>

    {#if availability === 'running'}
      <section class="domain-test card">
        <h3>Routing test</h3>
        <div class="row">
          <input placeholder="*.netflix.com" bind:value={testDomain} disabled={busy} />
          <Button variant="ghost" onclick={testDomainRouting} disabled={busy}>Test</Button>
        </div>
        {#if testResult}
          <p class="muted">Target: <strong>{testResult.target ?? '—'}</strong> — {testResult.reason ?? ''}</p>
        {/if}
      </section>
    {/if}
  {/if}

  <div style="margin-top: 1rem">
    <Button onclick={save} disabled={busy}>Save rules</Button>
  </div>
</section>

<style>
  h2 { margin: 0 0 1rem; }
  .status { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; margin-bottom: 0.75rem; }
  .status p { margin: 0.2rem 0; font-size: 0.9rem; }
  .muted { color: var(--mg-text-muted); }
  .error { color: var(--mg-danger, #c44); font-size: 0.88rem; }
  .warn { color: var(--mg-warning, #b8860b); font-size: 0.88rem; margin: 0 0 0.75rem; }
  .badges { display: flex; flex-direction: column; gap: 0.35rem; align-items: flex-end; }
  .actions-row { display: flex; gap: 0.5rem; margin-bottom: 0.75rem; }
  .check { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 1rem; }
  .tabs { display: flex; gap: 0.5rem; margin-bottom: 0.75rem; }
  .tabs button {
    background: var(--mg-surface);
    border: 1px solid var(--mg-border);
    color: var(--mg-text-muted);
    padding: 0.45rem 0.8rem;
    border-radius: 8px;
  }
  .tabs button.active { color: var(--mg-accent); border-color: var(--mg-accent); }
  .row {
    display: grid;
    grid-template-columns: 1fr auto auto auto;
    gap: 0.5rem;
    align-items: center;
    margin-bottom: 0.5rem;
  }
  .domain-test { margin-top: 1rem; padding-top: 0.75rem; border-top: 1px solid var(--mg-border); }
  .domain-test h3 { margin: 0 0 0.5rem; font-size: 0.95rem; }
</style>
