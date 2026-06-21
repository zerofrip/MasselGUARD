<script lang="ts">
  type Variant = 'success' | 'warning' | 'muted' | 'accent';

  let {
    variant = 'muted' as Variant,
    children,
  }: { variant?: Variant; children?: import('svelte').Snippet } = $props();
</script>

<span class="badge badge-{variant}">{@render children?.()}</span>

<style>
  .badge-accent {
    background: color-mix(in srgb, var(--mg-accent) 20%, transparent);
    color: var(--mg-accent);
  }
</style>
