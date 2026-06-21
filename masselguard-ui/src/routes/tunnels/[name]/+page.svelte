<script lang="ts">
  import { page } from '$app/stores';
  import { goto } from '$app/navigation';
  import { createQuery } from '@tanstack/svelte-query';
  import { invoke } from '@tauri-apps/api/core';
  import Button from '@mg-ui-core/components/Button.svelte';
  import ProfileSourceBadge from '@mg-ui-core/components/ProfileSourceBadge.svelte';
  import AwgBadge from '@mg-ui-core/components/AwgBadge.svelte';
  import ConfirmDialog from '@mg-ui-core/components/ConfirmDialog.svelte';
  import { api } from '$lib/api';
  import { showToast } from '$lib/stores/toast.svelte';
  import {
    buildConfigFromProfile,
    isAwgProfile,
    normalizeTunnelDetail,
    parseProfileFromConfig,
    validateClient,
  } from '$lib/validation/wireguard';
  import type { WireGuardProfile } from '@mg-ui-core/types';

  const tunnelName = $derived(decodeURIComponent($page.params.name ?? ''));

  let profile = $state<WireGuardProfile | null>(null);
  let meta = $state({ group: '', notes: '', tagsText: '', favorite: false, archived: false });
  let readOnly = $state(false);
  let dirty = $state(false);
  let errors = $state<Record<string, string>>({});
  let exportOpen = $state(false);
  let qrConfirmOpen = $state(false);

  const detailQ = createQuery(() => ({
    queryKey: ['tunnel', 'detail', tunnelName],
    enabled: !!tunnelName,
    queryFn: async () => {
      const raw = await invoke<Record<string, unknown>>('tunnel_get', { name: tunnelName, includeConfig: true });
      const detail = normalizeTunnelDetail(raw);
      profile = detail.profile ?? parseProfileFromConfig(detail.config ?? '');
      if (profile && !profile.interface.awg) profile.interface.awg = {};
      readOnly = !detail.summary.configEditable;
      meta = {
        group: detail.summary.group,
        notes: detail.summary.notes,
        tagsText: (detail.summary.tags ?? []).join(', '),
        favorite: detail.summary.favorite,
        archived: detail.summary.archived,
      };
      return detail;
    },
  }));

  function markDirty() {
    dirty = true;
  }

  async function save() {
    if (!profile) return;
    const config = buildConfigFromProfile(profile);
    const clientErrors = validateClient(tunnelName, config);
    errors = Object.fromEntries(clientErrors.map((e) => [e.field, e.message]));
    if (clientErrors.length) return;

    const server = await api.tunnelValidate({ name: tunnelName, config, excludeName: tunnelName });
    if (!server.valid) {
      errors = Object.fromEntries(server.errors.map((e) => [e.field, e.message]));
      return;
    }

    await api.tunnelUpdate({
      name: tunnelName,
      group: meta.group,
      notes: meta.notes,
      favorite: meta.favorite,
      archived: meta.archived,
      tags: meta.tagsText.split(',').map((t) => t.trim()).filter(Boolean),
      ...(readOnly ? {} : { config }),
    });
    showToast('success', 'Tunnel saved');
    dirty = false;
    detailQ.refetch();
  }

  async function exportConfig(mode: 'full' | 'sanitized' | 'qr') {
    if (mode === 'qr') {
      qrConfirmOpen = true;
      return;
    }
    const res = await api.tunnelExport(tunnelName, mode);
    await navigator.clipboard.writeText(res.config);
    showToast('success', `Copied ${mode} config to clipboard`);
  }

  async function exportQrConfirmed() {
    const res = await api.tunnelExport(tunnelName, 'qr');
    await navigator.clipboard.writeText(res.config);
    showToast('success', 'Full config copied — contains private key');
    qrConfirmOpen = false;
  }
</script>

<svelte:head><title>{tunnelName} · MasselGUARD</title></svelte:head>

{#if detailQ.isLoading}
  <p class="muted">Loading…</p>
{:else if detailQ.error}
  <p class="err">{detailQ.error.message}</p>
{:else if detailQ.data}
  {@const summary = detailQ.data.summary}
  <div class="toolbar">
    <div>
      <h2>{summary.name}</h2>
      <ProfileSourceBadge source={summary.profileSource} />
      {#if profile && isAwgProfile(profile)}<AwgBadge active />{/if}
      {#if readOnly}<span class="pill">Config read-only</span>{/if}
      {#if summary.active}<span class="pill warn">Active — disconnect to edit config</span>{/if}
    </div>
    <div class="toolbar-actions">
      <Button variant="ghost" onclick={() => goto('/tunnels')}>Back</Button>
      {#if !readOnly}
        <Button onclick={save} disabled={!dirty}>Save</Button>
      {:else}
        <Button onclick={save} disabled={!dirty}>Save metadata</Button>
      {/if}
      <Button variant="ghost" onclick={() => (exportOpen = !exportOpen)}>Export</Button>
    </div>
  </div>

  {#if exportOpen}
    <div class="export-menu card">
      <Button variant="ghost" onclick={() => exportConfig('full')}>Copy full config</Button>
      <Button variant="ghost" onclick={() => exportConfig('sanitized')}>Copy sanitized</Button>
      <Button variant="ghost" onclick={() => exportConfig('qr')}>Copy for QR</Button>
      <Button variant="ghost" onclick={async () => {
        const res = await api.tunnelExport(tunnelName, 'full');
        await api.saveConfFile(res.config, `${tunnelName}.conf`);
      }}>Save to file…</Button>
    </div>
  {/if}

  {#if profile}
    <form class="editor card" onsubmit={(e) => { e.preventDefault(); void save(); }}>
      <fieldset>
        <legend>Metadata</legend>
        <label>Group <input bind:value={meta.group} oninput={markDirty} /></label>
        <label>Notes <input bind:value={meta.notes} oninput={markDirty} /></label>
        <label>Tags <input bind:value={meta.tagsText} oninput={markDirty} /></label>
        <label class="check"><input type="checkbox" bind:checked={meta.favorite} onchange={markDirty} /> Favorite</label>
        <label class="check"><input type="checkbox" bind:checked={meta.archived} onchange={markDirty} /> Archived</label>
      </fieldset>

      <fieldset disabled={readOnly}>
        <legend>Interface</legend>
        <label>Private Key <input bind:value={profile.interface.privateKey} oninput={markDirty} /></label>
        <label>Address <input bind:value={profile.interface.address} oninput={markDirty} /></label>
        <label>DNS <input bind:value={profile.interface.dns} oninput={markDirty} /></label>
        <label>Listen Port <input bind:value={profile.interface.listenPort} oninput={markDirty} /></label>
        <label>MTU <input bind:value={profile.interface.mtu} oninput={markDirty} /></label>
      </fieldset>

      <fieldset disabled={readOnly}>
        <legend>AWG obfuscation</legend>
        {#if profile.interface.awg}
          <label>Jc <input bind:value={profile.interface.awg.jc} oninput={markDirty} placeholder="optional" /></label>
          <label>Jmin <input bind:value={profile.interface.awg.jmin} oninput={markDirty} placeholder="optional" /></label>
          <label>Jmax <input bind:value={profile.interface.awg.jmax} oninput={markDirty} placeholder="optional" /></label>
          <label>S1 <input bind:value={profile.interface.awg.s1} oninput={markDirty} placeholder="optional" /></label>
          <label>S2 <input bind:value={profile.interface.awg.s2} oninput={markDirty} placeholder="optional" /></label>
          <label>H1 <input bind:value={profile.interface.awg.h1} oninput={markDirty} placeholder="e.g. 1-100" /></label>
          <label>H2 <input bind:value={profile.interface.awg.h2} oninput={markDirty} placeholder="optional" /></label>
          <label>H3 <input bind:value={profile.interface.awg.h3} oninput={markDirty} placeholder="optional" /></label>
          <label>H4 <input bind:value={profile.interface.awg.h4} oninput={markDirty} placeholder="optional" /></label>
        {/if}
        <p class="muted">AWG obfuscates traffic patterns; WireGuard encryption is unchanged.</p>
      </fieldset>

      {#if profile.peers[0]}
        {@const peer = profile.peers[0]}
        <fieldset disabled={readOnly}>
          <legend>Peer</legend>
          <label>Public Key <input bind:value={peer.publicKey} oninput={markDirty} /></label>
          <label>Preshared Key <input bind:value={peer.presharedKey} oninput={markDirty} /></label>
          <label>Endpoint <input bind:value={peer.endpoint} oninput={markDirty} /></label>
          <label>Allowed IPs <input bind:value={peer.allowedIPs} oninput={markDirty} /></label>
          <label>Keepalive <input bind:value={peer.persistentKeepalive} oninput={markDirty} /></label>
        </fieldset>
      {/if}

      {#if profile.peers.length > 1}
        <p class="muted">This config has {profile.peers.length - 1} additional peer(s) not shown in the structured editor.</p>
      {/if}
    </form>
  {/if}
{/if}

<ConfirmDialog
  bind:open={qrConfirmOpen}
  title="Export for QR"
  message="The QR payload includes your private key. Only share with trusted devices."
  confirmLabel="Copy anyway"
  danger
  onConfirm={exportQrConfirmed}
/>

<style>
  .toolbar { display: flex; justify-content: space-between; gap: 1rem; margin-bottom: 1rem; flex-wrap: wrap; }
  .toolbar-actions { display: flex; gap: 0.5rem; flex-wrap: wrap; }
  .pill { margin-left: 0.5rem; font-size: 0.78rem; color: var(--mg-text-muted); }
  .pill.warn { color: #fcc419; }
  .export-menu { display: flex; flex-wrap: wrap; gap: 0.5rem; padding: 0.75rem; margin-bottom: 1rem; }
  .editor { padding: 1rem; display: flex; flex-direction: column; gap: 1rem; }
  fieldset { border: 1px solid var(--mg-border); border-radius: 8px; padding: 0.75rem; display: grid; gap: 0.5rem; }
  fieldset:disabled { opacity: 0.65; }
  label { display: grid; gap: 0.25rem; font-size: 0.88rem; font-weight: 600; }
  input { padding: 0.45rem 0.55rem; border-radius: 6px; border: 1px solid var(--mg-border); background: var(--mg-bg); color: var(--mg-text); }
  .check { display: flex; align-items: center; gap: 0.35rem; font-weight: 500; }
  .muted { color: var(--mg-text-muted); }
  .err { color: #ff6b6b; }
</style>
