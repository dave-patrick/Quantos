<script lang="ts">
  import type { Tab } from '../../lib/layout.svelte';
  import { layout } from '../../lib/layout.svelte';
  import { app } from '../../lib/state.svelte';
  import { searchTitles } from '../../lib/links.svelte';
  import Graph from '../Graph.svelte';

  let { tab }: { tab: Tab } = $props();
  let center = $state<string | null>(tab.arg ?? null);
  let hops = $state(2);
  let query = $state('');
  const centerTitle = $derived(center ? app.titleIndex.get(center)?.title : null);

  function pick(id: string | null) {
    center = id;
    tab.arg = id ?? undefined;
    layout.persist();
  }
</script>

<div class="view">
  <header>
    <h1>Graph</h1>
    <div class="tools">
      <input class="input" list="graph-nodes" placeholder="Centre on a note…" bind:value={query}
        onchange={() => { const hit = searchTitles(query, 5).find((n) => n.title === query) ?? searchTitles(query, 1)[0]; if (hit) pick(hit.id); }} />
      <datalist id="graph-nodes">{#each app.titles.slice(0, 300) as n (n.id)}<option value={n.title}></option>{/each}</datalist>
      <select class="input" bind:value={hops}><option value={1}>1 hop</option><option value={2}>2 hops</option></select>
      {#if center}<button class="btn ghost" onclick={() => { pick(null); query = ''; }}>Whole workspace</button>{/if}
    </div>
  </header>
  <p class="faint">{centerTitle ? `Notes within ${hops} link${hops > 1 ? 's' : ''} of “${centerTitle}”.` : `The most connected notes in ${app.workspace}.`} Scroll to zoom, drag to pan, click a note to open it.</p>
  <Graph centerId={center} workspace={center ? null : app.workspace} {hops} height={Math.max(420, innerHeight - 220)} />
</div>

<style>
  .view { flex: 1; padding: 18px 24px; overflow: auto; }
  header { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
  h1 { font: 700 28px/1.2 var(--font-display); }
  .tools { display: flex; gap: 6px; }
  .tools .input { width: auto; height: 30px; }
  p { margin: 6px 0 12px; font-size: 13px; }
</style>
