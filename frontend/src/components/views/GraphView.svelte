<script lang="ts">
  import { untrack } from 'svelte';
  import type { Tab } from '../../lib/layout.svelte';
  import { layout } from '../../lib/layout.svelte';
  import { app } from '../../lib/state.svelte';
  import { searchTitles } from '../../lib/links.svelte';
  import Graph from '../Graph.svelte';

  let { tab }: { tab: Tab } = $props();
  // Where the tab was left: a note's id, 'global', or nothing for the workspace overview.
  const start = untrack(() => tab.arg);
  let center = $state<string | null>(start && start !== 'global' ? start : null);
  let hops = $state(2);
  let query = $state('');
  const centerTitle = $derived(center ? app.titleIndex.get(center)?.title : null);

  // The global knowledge graph (Release 1.2): everything at once, across workspaces if wanted.
  let global = $state(start === 'global');
  let everywhere = $state(true);
  let tags = $state(false);
  let limit = $state(300);
  let colorBy = $state<'type' | 'workspace'>('workspace');
  let find = $state('');
  const TYPES = [['note', 'Notes'], ['task', 'Tasks'], ['person', 'People'], ['project', 'Projects'], ['daily', 'Daily'], ['canvas', 'Whiteboards']] as const;
  let types = $state<Record<string, boolean>>({ note: true, task: true, person: true, project: true, daily: false, canvas: true });
  const source = $derived(global
    ? `graph/global?limit=${limit}&tags=${tags}&types=${Object.keys(types).filter((k) => types[k]).join(',')}${everywhere ? '' : `&workspace=${encodeURIComponent(app.workspace)}`}`
    : null);

  function pick(id: string | null) {
    center = id;
    global = false;
    tab.arg = id ?? undefined;
    layout.persist();
  }
  function setGlobal(on: boolean) {
    global = on;
    if (on) center = null;
    tab.arg = on ? 'global' : center ?? undefined;
    layout.persist();
  }
</script>

<div class="view">
  <header>
    <h1>Graph</h1>
    <div class="seg" role="radiogroup" aria-label="What to show">
      <button role="radio" aria-checked={!global} class:on={!global} onclick={() => setGlobal(false)}>{center ? 'Around a note' : 'Most connected'}</button>
      <button role="radio" aria-checked={global} class:on={global} onclick={() => setGlobal(true)}>Everything</button>
    </div>
    <div class="tools">
      {#if !global}
        <input class="input" aria-label="Centre on a note" list="graph-nodes" placeholder="Centre on a note…" bind:value={query}
          onchange={() => { const hit = searchTitles(query, 5).find((n) => n.title === query) ?? searchTitles(query, 1)[0]; if (hit) pick(hit.id); }} />
        <datalist id="graph-nodes">{#each app.titles.slice(0, 300) as n (n.id)}<option value={n.title}></option>{/each}</datalist>
        <select class="input" aria-label="Distance" bind:value={hops}><option value={1}>1 hop</option><option value={2}>2 hops</option></select>
        {#if center}<button class="btn ghost" onclick={() => { pick(null); query = ''; }}>Whole workspace</button>{/if}
      {:else}
        <input class="input" aria-label="Highlight notes" placeholder="Highlight…" bind:value={find} />
        <select class="input" aria-label="Where" bind:value={everywhere}><option value={true}>All workspaces</option><option value={false}>This workspace</option></select>
        <select class="input" aria-label="Colour by" bind:value={colorBy}><option value="workspace">Colour by workspace</option><option value="type">Colour by type</option></select>
        <select class="input" aria-label="How many notes" bind:value={limit}><option value={150}>150 notes</option><option value={300}>300 notes</option><option value={600}>600 notes</option><option value={1000}>1000 notes</option></select>
      {/if}
    </div>
  </header>
  {#if global}
    <div class="filters">
      {#each TYPES as [k, label] (k)}<label class="check"><input type="checkbox" bind:checked={types[k]} /> {label}</label>{/each}
      <label class="check"><input type="checkbox" bind:checked={tags} /> Tags as hubs</label>
    </div>
    <p class="faint">Every linked note{everywhere ? ' in every workspace' : ` in ${app.workspace}`}, through text, properties, embeds and synced blocks; bigger dots have more links. Click a tag to search for it.</p>
  {:else}
    <p class="faint">{centerTitle ? `Notes within ${hops} link${hops > 1 ? 's' : ''} of “${centerTitle}”.` : `The most connected notes in ${app.workspace}.`} Scroll to zoom, drag to pan, click a note to open it.</p>
  {/if}
  <Graph centerId={global ? null : center} workspace={global || center ? null : app.workspace} {hops} {source} {colorBy} highlight={global ? find : ''}
    labels={!global} ontag={(t) => layout.open({ kind: 'search', arg: t, title: `Search: ${t}` }, 'new')} height={Math.max(420, innerHeight - (global ? 260 : 220))} />
</div>

<style>
  .view { flex: 1; padding: 18px 24px; overflow: auto; }
  header { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
  h1 { font: 700 28px/1.2 var(--font-display); }
  .seg { display: inline-flex; border: 1px solid var(--line-strong); border-radius: 7px; overflow: hidden; }
  .seg button { border: 0; background: none; color: var(--text-muted); padding: 5px 10px; font: 13px var(--font-body); cursor: pointer; }
  .seg button + button { border-left: 1px solid var(--line-strong); }
  .seg button.on { background: var(--accent-wash); color: var(--accent); }
  .tools { display: flex; gap: 6px; flex-wrap: wrap; margin-left: auto; }
  .tools .input { width: auto; height: 30px; }
  .filters { display: flex; flex-wrap: wrap; gap: 4px 14px; margin-top: 10px; }
  .check { display: inline-flex; gap: 5px; align-items: center; font-size: 13px; color: var(--text-muted); }
  p { margin: 6px 0 12px; font-size: 13px; }
</style>
