<script lang="ts">
  let {
    connected = false,
    label = 'Unknown',
  }: { connected?: boolean; label?: string } = $props();
</script>

<span class="pill" class:connected>{label}</span>

<style>
  .pill {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    padding: 0.2rem 0.65rem;
    border-radius: 999px;
    font-size: 0.8rem;
    font-weight: 600;
    background: var(--mg-surface-hover);
    color: var(--mg-text-muted);
  }

  .pill::before {
    content: '';
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: var(--mg-text-muted);
  }

  .pill.connected {
    color: var(--mg-success);
  }

  .pill.connected::before {
    background: var(--mg-success);
    box-shadow: 0 0 8px var(--mg-success);
  }
</style>
