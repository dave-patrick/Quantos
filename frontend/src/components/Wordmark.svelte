<script lang="ts">
  import wordmark from '../assets/quantos-wordmark.svg?raw';
  import pkg from '../../package.json';
  let { height = 22 }: { height?: number } = $props();
</script>

<span class="wordmark" style="height:{height}px">{@html wordmark}<span class="ver">v{pkg.version}</span></span>

<style>
  .wordmark { display: inline-flex; align-items: center; gap: 6px; color: var(--text); line-height: 0; }
  .wordmark :global(svg) { height: 100%; width: auto; display: block; }
  .ver { font: 11px var(--font-mono); color: var(--text-faint); line-height: 1; align-self: flex-end; padding-bottom: 2px; }
</style>