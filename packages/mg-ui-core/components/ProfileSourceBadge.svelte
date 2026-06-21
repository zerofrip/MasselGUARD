<script lang="ts">
  import type { ProfileSource } from '@mg-ui-core/types';

  let { source }: { source: ProfileSource } = $props();

  const labels: Record<ProfileSource, string> = {
    local: 'Local',
    companion: 'Companion',
    imported: 'Imported',
    managed: 'Managed',
  };
</script>

<span class="badge source-{source}">{labels[source] ?? source}</span>

<style>
  .badge {
    display: inline-block;
    font-size: 0.72rem;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.04em;
    padding: 0.15rem 0.45rem;
    border-radius: 4px;
    border: 1px solid var(--mg-border);
    color: var(--mg-text-muted);
  }
  .source-local { color: var(--mg-accent); }
  .source-companion { color: #74c0fc; }
  .source-imported { color: #b197fc; }
  .source-managed { color: #868e96; }
</style>
