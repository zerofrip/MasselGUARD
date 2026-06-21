<script lang="ts">
  let { message = 'This feature is experimental. Backend enforcement may be incomplete.' } = $props();
</script>

<div class="banner" role="status">
  <strong>Experimental</strong>
  <p>{message}</p>
</div>

<style>
  .banner {
    border: 1px dashed var(--mg-warning);
    background: color-mix(in srgb, var(--mg-warning) 8%, var(--mg-surface));
    border-radius: var(--mg-radius);
    padding: 0.75rem 1rem;
    margin-bottom: 1rem;
  }

  .banner strong {
    color: var(--mg-warning);
    display: block;
    margin-bottom: 0.25rem;
  }

  .banner p {
    margin: 0;
    color: var(--mg-text-muted);
    font-size: 0.9rem;
  }
</style>
