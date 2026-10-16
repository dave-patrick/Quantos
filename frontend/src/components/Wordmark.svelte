<script lang="ts">
  import wordmark from '../assets/quantos-wordmark.svg?raw';
  let { height = 22 }: { height?: number } = $props();
</script>

<span class="wordmark" style="height:{height}px">{@html wordmark}</span>

<style>
  .wordmark { display: inline-block; color: var(--text); line-height: 0; }
  .wordmark :global(svg) { height: 100%; width: auto; display: block; }
</style>
