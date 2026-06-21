<script lang="ts">
  import Button from '@mg-ui-core/components/Button.svelte';

  let {
    open = $bindable(false),
    title = 'Confirm',
    message = '',
    confirmLabel = 'Confirm',
    danger = false,
    onConfirm,
  }: {
    open?: boolean;
    title?: string;
    message?: string;
    confirmLabel?: string;
    danger?: boolean;
    onConfirm?: () => void | Promise<void>;
  } = $props();

  let busy = $state(false);

  async function confirm() {
    busy = true;
    try {
      await onConfirm?.();
      open = false;
    } finally {
      busy = false;
    }
  }
</script>

{#if open}
  <div class="backdrop" role="presentation" onclick={() => (open = false)}></div>
  <div class="dialog card" role="dialog" aria-modal="true" aria-labelledby="confirm-title">
    <h3 id="confirm-title">{title}</h3>
    <p>{message}</p>
    <div class="actions">
      <Button onclick={() => (open = false)} disabled={busy}>Cancel</Button>
      <Button onclick={confirm} disabled={busy} variant={danger ? 'danger' : 'primary'}>
        {busy ? '…' : confirmLabel}
      </Button>
    </div>
  </div>
{/if}

<style>
  .backdrop {
    position: fixed;
    inset: 0;
    background: rgb(0 0 0 / 0.45);
    z-index: 9000;
  }
  .dialog {
    position: fixed;
    top: 50%;
    left: 50%;
    transform: translate(-50%, -50%);
    z-index: 9001;
    min-width: min(420px, 92vw);
    padding: 1.25rem;
  }
  .actions {
    display: flex;
    justify-content: flex-end;
    gap: 0.5rem;
    margin-top: 1rem;
  }
</style>
