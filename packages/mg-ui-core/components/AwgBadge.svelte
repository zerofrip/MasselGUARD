<script lang="ts">
  let { active = false }: { active?: boolean } = $props();
</script>

{#if active}
  <span class="badge awg">AWG</span>
{/if}

<style>
  .badge {
    display: inline-block;
    font-size: 0.72rem;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    padding: 0.15rem 0.45rem;
    border-radius: 4px;
    border: 1px solid #7048e8;
    color: #b197fc;
    background: rgba(112, 72, 232, 0.12);
  }
</style>
