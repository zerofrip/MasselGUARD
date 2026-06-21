<script lang="ts">
  type Variant = 'primary' | 'ghost' | 'danger';

  let {
    variant = 'primary' as Variant,
    disabled = false,
    onclick,
    children,
  }: {
    variant?: Variant;
    disabled?: boolean;
    onclick?: () => void;
    children?: import('svelte').Snippet;
  } = $props();
</script>

<button class="btn btn-{variant}" {disabled} {onclick} type="button">
  {@render children?.()}
</button>
