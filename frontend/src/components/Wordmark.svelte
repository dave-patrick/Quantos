<script lang="ts">
  import wordmark from '../assets/quantos-wordmark.svg?raw';
  import pkg from '../../package.json';
  let { height = 22 }: { height?: number } = $props();
  // The build mark is empty in a released build; a staging copy shows the commit it came from.
  const build = __QUANTOS_BUILD__;
</script>

<span class="wordmark" style="height:{height}px">{@html wordmark}<span class="ver">v{pkg.version}{#if build}<span class="build">{build}</span>{/if}</span></span>

<style>
  .wordmark { display: inline-flex; align-items: center; gap: 6px; color: var(--text); line-height: 0; }
  .wordmark :global(svg) { height: 100%; width: auto; display: block; }
  .ver { font: 10px var(--font-mono); color: var(--text-faint); line-height: 1; align-self: flex-end; padding-bottom: 2px; display: inline-flex; gap: 5px; align-items: baseline; }
  .build { color: var(--text-faint); opacity: .75; }
</style>
