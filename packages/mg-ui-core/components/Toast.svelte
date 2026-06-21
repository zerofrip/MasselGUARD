<script lang="ts">
  import { toastState, dismissToast } from '$lib/stores/toast.svelte';
</script>

<div class="toast-stack" aria-live="polite">
  {#each toastState.items as t (t.id)}
    <div class="toast toast-{t.kind}" role="status">
      <span>{t.message}</span>
      <button type="button" class="toast-close" onclick={() => dismissToast(t.id)} aria-label="Dismiss">×</button>
    </div>
  {/each}
</div>

<style>
  .toast-stack {
    position: fixed;
    bottom: 1rem;
    right: 1rem;
    z-index: 9999;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    max-width: min(420px, calc(100vw - 2rem));
  }

  .toast {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.75rem;
    padding: 0.65rem 0.85rem;
    border-radius: 8px;
    border: 1px solid var(--mg-border);
    background: var(--mg-surface);
    box-shadow: 0 8px 24px rgb(0 0 0 / 0.25);
    font-size: 0.9rem;
  }

  .toast-success { border-color: #2d6a4f; }
  .toast-error { border-color: #9b2226; }
  .toast-close {
    background: none;
    border: none;
    color: var(--mg-text-muted);
    cursor: pointer;
    font-size: 1.1rem;
    line-height: 1;
  }
</style>
