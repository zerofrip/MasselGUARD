<script lang="ts">
  let {
    values = [],
    width = 120,
    height = 32,
    stroke = 'var(--mg-accent)',
    fill = 'color-mix(in srgb, var(--mg-accent) 20%, transparent)',
  }: {
    values?: number[];
    width?: number;
    height?: number;
    stroke?: string;
    fill?: string;
  } = $props();

  const paths = $derived(buildPaths(values, width, height));

  function buildPaths(vals: number[], w: number, h: number): { line: string; area: string } {
    if (!vals.length) return { line: '', area: '' };
    const max = Math.max(...vals, 1);
    const step = vals.length > 1 ? w / (vals.length - 1) : w;
    const pts = vals.map((v, i) => {
      const x = i * step;
      const y = h - (v / max) * (h - 2) - 1;
      return `${x},${y}`;
    });
    const line = `M ${pts.join(' L ')}`;
    return { line, area: `${line} L ${w},${h} L 0,${h} Z` };
  }
</script>

<svg {width} {height} viewBox="0 0 {width} {height}" aria-hidden="true" class="sparkline">
  {#if paths.line}
    <path d={paths.area} {fill} stroke="none" />
    <path d={paths.line} fill="none" {stroke} stroke-width="1.5" stroke-linejoin="round" stroke-linecap="round" />
  {:else}
    <line x1="0" y1={height / 2} x2={width} y2={height / 2} stroke="var(--mg-border)" stroke-width="1" />
  {/if}
</svg>

<style>
  .sparkline {
    display: block;
  }
</style>
