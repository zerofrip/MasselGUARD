<script lang="ts">
  import { createQuery } from '@tanstack/svelte-query';
  import { dndzone } from 'svelte-dnd-action';
  import Button from '@mg-ui-core/components/Button.svelte';
  import { api } from '$lib/api';
  import type { WifiRule, WifiRulesResponse } from '@mg-ui-core/types';

  const rulesQ = createQuery<WifiRulesResponse>(() => ({
    queryKey: ['wifi', 'rules'],
    queryFn: () => api.wifiRulesGet(),
  }));

  let items: WifiRule[] = $state([]);
  let defaultAction = $state('none');
  let defaultTunnel = $state('');
  let openWifiTunnel = $state('');
  let manualMode = $state(false);
  let preview = $state('');

  $effect(() => {
    if (rulesQ.data) {
      items = [...rulesQ.data.rules];
      defaultAction = rulesQ.data.defaultAction;
      defaultTunnel = rulesQ.data.defaultTunnel;
      openWifiTunnel = rulesQ.data.openWifiTunnel;
      manualMode = rulesQ.data.manualMode;
    }
  });

  async function save() {
    await api.wifiRulesSet({
      rules: items,
      defaultAction,
      defaultTunnel,
      openWifiTunnel,
      manualMode,
    });
    rulesQ.refetch();
  }

  function addRule() {
    items = [...items, { name: '', ssid: '', tunnel: '', networkType: 'wifi', executionCount: 0 }];
  }

  function removeRule(idx: number) {
    items = items.filter((_, i) => i !== idx);
  }

  async function testPreview(ssid: string) {
    const r = await api.wifiRulesTest(ssid, false);
    preview = r.reason;
  }

  function handleDnd(e: CustomEvent<{ items: WifiRule[] }>) {
    items = e.detail.items;
  }
</script>

<svelte:head><title>Wi-Fi Automation · MasselGUARD</title></svelte:head>

<h2>Wi-Fi Automation</h2>

<section class="card defaults">
  <label>
    Default action
    <select bind:value={defaultAction} onchange={save}>
      <option value="none">No action</option>
      <option value="disconnect">Disconnect VPN</option>
      <option value="activate">Connect default tunnel</option>
    </select>
  </label>
  <label>
    Default tunnel
    <input bind:value={defaultTunnel} onchange={save} />
  </label>
  <label>
    Open Wi-Fi tunnel
    <input bind:value={openWifiTunnel} onchange={save} />
  </label>
  <label class="check">
    <input type="checkbox" bind:checked={manualMode} onchange={save} />
    Manual mode (disable automation)
  </label>
</section>

<section class="card" style="margin-top: 1rem">
  <div class="toolbar">
    <h3>SSID rules (priority top → bottom)</h3>
    <Button onclick={addRule}>Add rule</Button>
  </div>

  <div use:dndzone={{ items, flipDurationMs: 150 }} onconsider={handleDnd} onfinalize={handleDnd}>
    {#each items as rule, idx (rule.ssid + idx)}
      <div class="rule-row">
        <span class="handle">⋮⋮</span>
        <input placeholder="SSID" bind:value={rule.ssid} />
        <input placeholder="Tunnel (empty = disconnect)" bind:value={rule.tunnel} />
        <Button variant="ghost" onclick={() => testPreview(rule.ssid)}>Preview</Button>
        <Button variant="danger" onclick={() => removeRule(idx)}>Remove</Button>
      </div>
    {/each}
  </div>

  <div class="save-row">
    <Button onclick={save}>Save order & rules</Button>
    {#if preview}<p class="preview">{preview}</p>{/if}
  </div>
</section>

<style>
  h2 {
    margin: 0 0 1rem;
  }

  .defaults {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
    gap: 0.75rem;
  }

  label {
    display: flex;
    flex-direction: column;
    gap: 0.35rem;
    font-size: 0.85rem;
    color: var(--mg-text-muted);
  }

  .check {
    flex-direction: row;
    align-items: center;
    gap: 0.5rem;
  }

  .toolbar {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 0.75rem;
  }

  .toolbar h3 {
    margin: 0;
  }

  .rule-row {
    display: grid;
    grid-template-columns: auto 1fr 1fr auto auto;
    gap: 0.5rem;
    align-items: center;
    padding: 0.5rem 0;
    border-bottom: 1px solid var(--mg-border);
  }

  .handle {
    cursor: grab;
    color: var(--mg-text-muted);
    user-select: none;
  }

  .save-row {
    margin-top: 1rem;
    display: flex;
    align-items: center;
    gap: 1rem;
  }

  .preview {
    margin: 0;
    color: var(--mg-text-muted);
    font-size: 0.9rem;
  }
</style>
