<script lang="ts">
  import type { Snippet } from 'svelte';
  import Wordmark from './Wordmark.svelte';
  let { title, loading = false, children }: { title: string; loading?: boolean; children?: Snippet } = $props();
</script>

<main class="splash">
  <div class="card" class:loading>
    <Wordmark height={44} />
    {#if loading}
      <div class="bar" aria-label="Loading"><span></span></div>
    {:else}
      <h1>{title}</h1>
      {@render children?.()}
    {/if}
  </div>
</main>

<style>
  .splash { height: 100%; display: grid; place-items: center; padding: 24px;
    background: radial-gradient(1200px 600px at 70% -10%, #15263c 0%, transparent 60%), var(--bg); }
  .card { width: min(520px, 100%); display: grid; gap: 18px; justify-items: start; }
  /* While loading there's no text to align with, so the mark sits in the middle. */
  .card.loading { width: auto; justify-items: center; }
  h1 { font: 700 26px/1.25 var(--font-display); letter-spacing: .01em; }
  .card :global(p) { color: var(--text-soft); margin: 0; }
  .card :global(code) { color: var(--accent); }
  .bar { width: 220px; height: 3px; background: var(--line); border-radius: 3px; overflow: hidden; }
  .bar span { display: block; width: 40%; height: 100%; background: var(--accent); animation: slide 1.1s var(--ease) infinite; }
  @keyframes slide { from { transform: translateX(-100%); } to { transform: translateX(260%); } }
</style>
