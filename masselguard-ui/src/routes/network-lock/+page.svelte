<script lang="ts">
  import { createQuery, useQueryClient } from '@tanstack/svelte-query';
  import Button from '@mg-ui-core/components/Button.svelte';
  import Badge from '@mg-ui-core/components/Badge.svelte';
  import ConfirmDialog from '@mg-ui-core/components/ConfirmDialog.svelte';
  import { api } from '$lib/api';
  import { showToast } from '$lib/stores/toast.svelte';
  import type { DnsPolicy, NetworkLockMode, NetworkLockStatus } from '@mg-ui-core/types';

  const queryClient = useQueryClient();

  const configQ = createQuery(() => ({
    queryKey: ['config'],
    queryFn: () => api.configGet(),
  }));

  let wfpDelegation = $state(false);

  const lockQ = createQuery<NetworkLockStatus>(() => ({
    queryKey: ['network-lock'],
    queryFn: () => api.networkLockStatus(),
  }));

  let mode: NetworkLockMode = $state('disabled');
  let lanEnabled = $state(false);
  let lanExceptions: string[] = $state([]);
  let dnsPolicy: DnsPolicy = $state('strict');
  let dnsExceptions: string[] = $state([]);
  let allowDhcp = $state(true);
  let showAlwaysOnConfirm = $state(false);
  let pendingMode: NetworkLockMode | null = $state(null);
  let showDiagnostics = $state(false);
  let busy = $state(false);

  $effect(() => {
    if (lockQ.data) {
      mode = lockQ.data.mode ?? lockQ.data.config?.mode ?? 'disabled';
      lanEnabled = lockQ.data.lanAccess?.enabled ?? lockQ.data.config?.lanAccessEnabled ?? false;
      lanExceptions = [...(lockQ.data.lanAccess?.exceptions ?? lockQ.data.config?.lanExceptions ?? [])];
      dnsPolicy = (lockQ.data.dnsPolicy?.policy ?? lockQ.data.config?.dnsPolicy ?? 'strict') as DnsPolicy;
      dnsExceptions = [...(lockQ.data.dnsPolicy?.exceptions ?? lockQ.data.config?.dnsExceptions ?? [])];
      allowDhcp = lockQ.data.dnsPolicy?.allowDhcp ?? lockQ.data.config?.allowDhcp ?? true;
    }
  });

  $effect(() => {
    if (configQ.data) wfpDelegation = configQ.data.networkLockWfpDelegation ?? false;
  });

  async function refreshStatus(data?: NetworkLockStatus) {
    if (data) queryClient.setQueryData(['network-lock'], data);
    else await lockQ.refetch();
  }

  async function applyMode(next: NetworkLockMode) {
    if (next === 'alwaysOn' && mode !== 'alwaysOn') {
      pendingMode = next;
      showAlwaysOnConfirm = true;
      return;
    }
    busy = true;
    try {
      const res = await api.networkLockSetMode(next);
      await refreshStatus(res);
      showToast('success', `Network Lock mode: ${next}`);
    } catch (e) {
      showToast('error', String(e));
    } finally {
      busy = false;
    }
  }

  async function confirmAlwaysOn() {
    if (!pendingMode) return;
    busy = true;
    try {
      const res = await api.networkLockSetMode(pendingMode);
      await refreshStatus(res);
      showToast('success', 'Network Lock set to Always On');
    } catch (e) {
      showToast('error', String(e));
    } finally {
      busy = false;
      pendingMode = null;
    }
  }

  async function enableNow() {
    busy = true;
    try {
      const res = await api.networkLockEnable();
      await refreshStatus(res);
      showToast('success', 'Network Lock enabled');
    } catch (e) {
      showToast('error', String(e));
    } finally {
      busy = false;
    }
  }

  async function disableNow() {
    busy = true;
    try {
      const res = await api.networkLockDisable();
      await refreshStatus(res);
      showToast('info', 'Network Lock disabled');
    } catch (e) {
      showToast('error', String(e));
    } finally {
      busy = false;
    }
  }

  async function saveWfpDelegation() {
    busy = true;
    try {
      await api.configSet({ networkLockWfpDelegation: wfpDelegation });
      await configQ.refetch();
      await lockQ.refetch();
      showToast('success', wfpDelegation ? 'WFP delegation enabled' : 'Using Windows Firewall backend');
    } catch (e) {
      showToast('error', String(e));
    } finally {
      busy = false;
    }
  }

  async function savePolicy() {
    busy = true;
    try {
      const res = await api.networkLockSet({
        mode,
        lanAccessEnabled: lanEnabled,
        lanExceptions,
        dnsPolicy,
        dnsExceptions,
        allowDhcp,
      });
      await refreshStatus(res);
      showToast('success', 'Network Lock policy saved');
    } catch (e) {
      showToast('error', String(e));
    } finally {
      busy = false;
    }
  }

  function addLan() {
    lanExceptions = [...lanExceptions, '192.168.0.0/16'];
  }

  function addDns() {
    dnsExceptions = [...dnsExceptions, '1.1.1.1'];
  }

  const status = $derived(lockQ.data);
  const enforcementActive = $derived(status?.enforcementActive ?? false);
  const leakProtection = $derived(status?.diagnostics?.leakProtection ?? 'inactive');
</script>

<svelte:head><title>Network Lock · MasselGUARD</title></svelte:head>

<h2>Network Lock</h2>
<p class="lead">Blocks direct Internet traffic via Windows Firewall when active. VPN adapters and configured exceptions remain allowed.</p>

<section class="card status">
  <div>
    <strong>Enforcement</strong>
    <p>Mode: {status?.mode ?? '—'}</p>
    <p>Active tunnels: {(status?.activeTunnels ?? []).join(', ') || 'None'}</p>
    {#if status?.lastRecovery}
      <p class="muted">Last recovery: {new Date(status.lastRecovery.at).toLocaleString()} ({status.lastRecovery.reason ?? 'unknown'})</p>
    {/if}
  </div>
  <div class="badges">
    <Badge variant={enforcementActive ? 'success' : 'muted'}>
      {enforcementActive ? 'Active' : 'Inactive'}
    </Badge>
    <Badge variant={leakProtection === 'active' ? 'success' : 'warning'}>
      Leak protection: {leakProtection}
    </Badge>
  </div>
</section>

<section class="card" style="margin-top: 1rem">
  <h3>Mode</h3>
  <div class="mode-group" role="radiogroup" aria-label="Network Lock mode">
    {#each [
      { id: 'disabled', label: 'Disabled', hint: 'No firewall enforcement' },
      { id: 'auto', label: 'Auto', hint: 'Enforce when a kill-switch tunnel connects' },
      { id: 'alwaysOn', label: 'Always On', hint: 'Block direct traffic even without VPN' },
    ] as opt (opt.id)}
      <label class="mode-option">
        <input
          type="radio"
          name="nl-mode"
          value={opt.id}
          checked={mode === opt.id}
          disabled={busy}
          onchange={() => applyMode(opt.id as NetworkLockMode)}
        />
        <span>
          <strong>{opt.label}</strong>
          <small>{opt.hint}</small>
        </span>
      </label>
    {/each}
  </div>

  <div class="actions-row">
    <Button onclick={enableNow} disabled={busy || mode === 'alwaysOn'}>Enable now</Button>
    <Button variant="danger" onclick={disableNow} disabled={busy}>Disable</Button>
  </div>
</section>

<section class="card" style="margin-top: 1rem">
  <h3>Advanced: RouteGuard WFP</h3>
  <p class="muted">When enabled and RouteGuard is running, Network Lock uses RouteGuard WFP instead of Windows Firewall. Only one backend is active at a time.</p>
  <label class="toggle">
    <input type="checkbox" bind:checked={wfpDelegation} disabled={busy} />
    Delegate to RouteGuard WFP
  </label>
  <div style="margin-top: 0.75rem"><Button onclick={saveWfpDelegation} disabled={busy}>Save backend</Button></div>
</section>

<section class="card" style="margin-top: 1rem">
  <h3>LAN access</h3>
  <label class="toggle">
    <input type="checkbox" bind:checked={lanEnabled} disabled={busy} />
    Allow configured LAN CIDR ranges
  </label>

  {#if lanEnabled}
    {#each lanExceptions as _, i (i)}
      <div class="row">
        <input bind:value={lanExceptions[i]} disabled={busy} placeholder="192.168.0.0/16" />
        <Button variant="danger" disabled={busy} onclick={() => (lanExceptions = lanExceptions.filter((_, j) => j !== i))}>Remove</Button>
      </div>
    {/each}
    <Button variant="ghost" disabled={busy} onclick={addLan}>Add LAN range</Button>
  {/if}

  <h3>DNS policy</h3>
  <select bind:value={dnsPolicy} disabled={busy}>
    <option value="strict">Strict — DNS only via VPN when enforced</option>
    <option value="allow_exceptions">Allow exceptions — permit listed DNS servers</option>
    <option value="allow_dhcp">Allow DHCP — permit DHCP-assigned DNS</option>
  </select>

  {#if dnsPolicy === 'allow_exceptions'}
    {#each dnsExceptions as _, i (i)}
      <div class="row">
        <input bind:value={dnsExceptions[i]} disabled={busy} placeholder="1.1.1.1" />
        <Button variant="danger" disabled={busy} onclick={() => (dnsExceptions = dnsExceptions.filter((_, j) => j !== i))}>Remove</Button>
      </div>
    {/each}
    <Button variant="ghost" disabled={busy} onclick={addDns}>Add DNS server</Button>
  {/if}

  <label class="toggle" style="margin-top: 0.75rem">
    <input type="checkbox" bind:checked={allowDhcp} disabled={busy} />
    Allow DHCP (UDP 67/68)
  </label>

  <div style="margin-top: 1rem"><Button onclick={savePolicy} disabled={busy}>Save policy</Button></div>
</section>

<section class="card" style="margin-top: 1rem">
  <button type="button" class="diag-toggle" onclick={() => (showDiagnostics = !showDiagnostics)}>
    Diagnostics {showDiagnostics ? '▾' : '▸'}
  </button>
  {#if showDiagnostics && status?.diagnostics}
    <ul class="diag-list">
      <li>Active filters: {status.diagnostics.activeFilterCount}</li>
      <li>Global block: {status.diagnostics.globalBlockActive ? 'yes' : 'no'}</li>
      <li>Recovery state: {status.diagnostics.recoveryState}</li>
      {#if status.diagnostics.lastPolicyHash}
        <li>Policy hash: {status.diagnostics.lastPolicyHash}</li>
      {/if}
    </ul>
    {#if status.diagnostics.ruleNames?.length}
      <details>
        <summary>Rule names ({status.diagnostics.ruleNames.length})</summary>
        <ul class="rule-names">
          {#each status.diagnostics.ruleNames as name (name)}
            <li><code>{name}</code></li>
          {/each}
        </ul>
      </details>
    {/if}
  {/if}
</section>

<ConfirmDialog
  bind:open={showAlwaysOnConfirm}
  title="Enable Always On?"
  message="All traffic will be blocked except VPN and configured exceptions. Misconfiguration can lock you out of the network."
  confirmLabel="Enable Always On"
  danger={true}
  onConfirm={confirmAlwaysOn}
/>

<style>
  h2 { margin: 0 0 0.35rem; }
  .lead { margin: 0 0 1rem; color: var(--mg-text-muted); font-size: 0.92rem; }
  h3 { margin: 0 0 0.75rem; font-size: 0.95rem; }
  .status { display: flex; justify-content: space-between; align-items: flex-start; gap: 1rem; }
  .status p { margin: 0.2rem 0; font-size: 0.9rem; }
  .status .muted { color: var(--mg-text-muted); }
  .badges { display: flex; flex-direction: column; gap: 0.35rem; align-items: flex-end; }
  .mode-group { display: flex; flex-direction: column; gap: 0.5rem; }
  .mode-option { display: flex; gap: 0.6rem; align-items: flex-start; cursor: pointer; }
  .mode-option small { display: block; color: var(--mg-text-muted); font-size: 0.82rem; }
  .actions-row { display: flex; gap: 0.5rem; margin-top: 1rem; }
  .toggle { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.5rem; }
  .row { display: flex; gap: 0.5rem; margin-bottom: 0.5rem; }
  .row input { flex: 1; }
  select { width: 100%; margin-bottom: 0.75rem; }
  .diag-toggle {
    background: none;
    border: none;
    color: inherit;
    font: inherit;
    font-weight: 600;
    cursor: pointer;
    padding: 0;
  }
  .diag-list { margin: 0.5rem 0 0; padding-left: 1.2rem; font-size: 0.88rem; }
  .rule-names { font-size: 0.82rem; max-height: 12rem; overflow: auto; }
</style>
