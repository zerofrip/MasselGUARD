<script lang="ts">
  import { goto } from '$app/navigation';
  import { onMount } from 'svelte';
  import Button from '@mg-ui-core/components/Button.svelte';
  import { api } from '$lib/api';
  import { showToast } from '$lib/stores/toast.svelte';
  import { buildConfigFromProfile, emptyProfile, validateClient } from '$lib/validation/wireguard';
  import type { WireGuardProfile } from '@mg-ui-core/types';

  let name = $state('');
  let group = $state('');
  let notes = $state('');
  let tagsText = $state('');
  let profile = $state<WireGuardProfile>(emptyProfile());
  let errors = $state<Record<string, string>>({});
  let dirty = $state(false);

  onMount(() => {
    const handler = (e: BeforeUnloadEvent) => {
      if (dirty) e.preventDefault();
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  });

  function markDirty() {
    dirty = true;
  }

  async function validateAndSave() {
    const config = buildConfigFromProfile(profile);
    const clientErrors = validateClient(name, config);
    errors = Object.fromEntries(clientErrors.map((e) => [e.field, e.message]));
    if (clientErrors.length) return;

    const server = await api.tunnelValidate({ name, config });
    if (!server.valid) {
      errors = Object.fromEntries(server.errors.map((e) => [e.field, e.message]));
      return;
    }

    await api.tunnelCreate({
      name,
      config,
      group,
      notes,
      tags: tagsText.split(',').map((t) => t.trim()).filter(Boolean),
    });
    showToast('success', `Created ${name}`);
    dirty = false;
    goto(`/tunnels/${encodeURIComponent(name)}`);
  }
</script>

<svelte:head><title>New tunnel · MasselGUARD</title></svelte:head>

<div class="toolbar">
  <h2>Create tunnel</h2>
  <Button variant="ghost" onclick={() => goto('/tunnels')}>Cancel</Button>
</div>

<form class="editor card" onsubmit={(e) => { e.preventDefault(); void validateAndSave(); }}>
  <fieldset>
    <legend>Profile</legend>
    <label>Name <input bind:value={name} oninput={markDirty} required /></label>
    {#if errors.name}<span class="err">{errors.name}</span>{/if}
    <label>Group <input bind:value={group} oninput={markDirty} /></label>
    <label>Notes <input bind:value={notes} oninput={markDirty} /></label>
    <label>Tags (comma-separated) <input bind:value={tagsText} oninput={markDirty} /></label>
  </fieldset>

  <fieldset>
    <legend>Interface</legend>
    <label>Private Key <input bind:value={profile.interface.privateKey} oninput={markDirty} /></label>
    {#if errors['interface.privateKey']}<span class="err">{errors['interface.privateKey']}</span>{/if}
    <label>Address <input bind:value={profile.interface.address} oninput={markDirty} placeholder="10.0.0.2/32" /></label>
    <label>DNS <input bind:value={profile.interface.dns} oninput={markDirty} /></label>
    <label>Listen Port <input bind:value={profile.interface.listenPort} oninput={markDirty} /></label>
    <label>MTU <input bind:value={profile.interface.mtu} oninput={markDirty} /></label>
  </fieldset>

  {#if profile.peers[0]}
    {@const peer = profile.peers[0]}
    <fieldset>
      <legend>Peer</legend>
      <label>Public Key <input bind:value={peer.publicKey} oninput={markDirty} /></label>
      <label>Preshared Key <input bind:value={peer.presharedKey} oninput={markDirty} /></label>
      <label>Endpoint <input bind:value={peer.endpoint} oninput={markDirty} placeholder="vpn.example.com:51820" /></label>
      {#if errors['peer.endpoint']}<span class="err">{errors['peer.endpoint']}</span>{/if}
      <label>Allowed IPs <input bind:value={peer.allowedIPs} oninput={markDirty} /></label>
      <label>Keepalive <input bind:value={peer.persistentKeepalive} oninput={markDirty} /></label>
    </fieldset>
  {/if}

  {#if errors.config}<p class="err">{errors.config}</p>{/if}

  <div class="actions">
    <Button onclick={validateAndSave}>Create tunnel</Button>
  </div>
</form>

<style>
  .toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
  .editor { padding: 1rem; display: flex; flex-direction: column; gap: 1rem; }
  fieldset { border: 1px solid var(--mg-border); border-radius: 8px; padding: 0.75rem; display: grid; gap: 0.5rem; }
  label { display: grid; gap: 0.25rem; font-size: 0.88rem; font-weight: 600; }
  input { padding: 0.45rem 0.55rem; border-radius: 6px; border: 1px solid var(--mg-border); background: var(--mg-bg); color: var(--mg-text); }
  .err { color: #ff6b6b; font-size: 0.82rem; }
  .actions { display: flex; justify-content: flex-end; }
</style>
