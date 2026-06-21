<script lang="ts">
  import StatusPill from './StatusPill.svelte';
  import Button from './Button.svelte';

  let {
    state = 'disconnected',
    tunnel = '',
    onConnect,
    onDisconnect,
    onReconnect,
  }: {
    state?: string;
    tunnel?: string;
    onConnect?: () => void;
    onDisconnect?: () => void;
    onReconnect?: () => void;
  } = $props();

  const connected = $derived(state === 'connected' || state === 'active');
  const label = $derived(connected ? 'Connected' : state === 'connecting' ? 'Connecting…' : 'Disconnected');
</script>

<section class="hero card">
  <div class="hero-top">
    <StatusPill {connected} {label} />
    <h1>{tunnel || 'No tunnel'}</h1>
  </div>
  <div class="actions">
    {#if connected}
      <Button variant="ghost" onclick={onDisconnect}>Disconnect</Button>
      <Button variant="primary" onclick={onReconnect}>Reconnect</Button>
    {:else}
      <Button variant="primary" onclick={onConnect} disabled={!tunnel}>Connect</Button>
    {/if}
  </div>
</section>

<style>
  .hero {
    display: flex;
    flex-direction: column;
    gap: 1rem;
  }

  .hero-top h1 {
    margin: 0.5rem 0 0;
    font-size: 1.75rem;
  }

  .actions {
    display: flex;
    gap: 0.5rem;
  }
</style>
