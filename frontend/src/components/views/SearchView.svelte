<script lang="ts">
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { layout, type Tab } from '../../lib/layout.svelte';
  import { openNode, whereFrom } from '../../lib/links.svelte';
  import { snippetHtml } from '../../lib/text';
  import type { SearchHit } from '../../lib/types';
  import Icon from '../Icon.svelte';
  import { onMount } from 'svelte';
  import { ai } from '../../lib/ai.svelte';

  let { tab }: { tab: Tab } = $props();
  let query = $state(tab.arg ?? '');
  let scope = $state<'all' | 'active'>('all');
  // Words: the full-text index. Meaning: the AI embedding index, when it's set up (Release 1.1).
  let mode = $state<'words' | 'meaning'>('words');
  onMount(() => { ai.load(); });
  let hits = $state<SearchHit[]>([]);
  let busy = $state(false);
  let timer: ReturnType<typeof setTimeout>;

  // Global search grouped by workspace (BUILD-PLAN 8C.13).
  const groups = $derived.by(() => {
    const map = new Map<string, SearchHit[]>();
    for (const h of hits) map.set(h.workspace || 'global', [...(map.get(h.workspace || 'global') ?? []), h]);
    return [...map].sort(([a], [b]) => (a === app.workspace ? -1 : b === app.workspace ? 1 : a.localeCompare(b)));
  });

  async function search() {
    const q = query.trim();
    tab.arg = q;
    tab.title = q ? `Search: ${q}` : 'Search';
    layout.persist();
    if (!q) { hits = []; return; }
    busy = true;
    try {
      if (mode === 'meaning') {
        const found = await ai.search(q, scope === 'active' ? app.workspace : undefined);
        hits = found.map((h) => ({ id: h.id, title: h.title, workspace: h.workspace, path: h.path, type: app.titleIndex.get(h.id)?.type ?? 'note',
          snippet: h.text.replace(/^[^\n]*\n\n/, '').slice(0, 220) + (h.text.length > 220 ? '…' : '') }));
      } else hits = await api.get<SearchHit[]>(`search?q=${encodeURIComponent(q)}&limit=200${scope === 'active' ? `&workspace=${app.workspace}` : ''}`);
    }
    catch (e) { app.error(e); }
    finally { busy = false; }
  }

  $effect(() => { void scope; void mode; clearTimeout(timer); timer = setTimeout(search, 200); });
  const delay = $derived(mode === 'meaning' ? 600 : 200);
</script>

<div class="scroll view phone-search">
  <div class="inner">
    <div class="box">
      <Icon name="search" size={18} />
      <!-- svelte-ignore a11y_autofocus -->
      <input class="q" aria-label="Search" bind:value={query} oninput={() => { clearTimeout(timer); timer = setTimeout(search, delay); }} placeholder="Search every note, task and daily entry" autofocus />
      {#if ai.searchReady}<select class="input scope" aria-label="Match by" bind:value={mode}><option value="words">Words</option><option value="meaning">Meaning</option></select>{/if}
      <select class="input scope" aria-label="Search in" bind:value={scope}><option value="all">All workspaces</option><option value="active">This workspace</option></select>
      <button class="icon-btn" title="Find and replace across notes (Ctrl+Shift+H)" aria-label="Find and replace" onclick={() => layout.open({ kind: 'replace', title: 'Find and replace' }, 'current')}><Icon name="replace" /></button>
    </div>
    {#if busy}<p class="faint">Searching…</p>{/if}
    {#if query.trim() && !busy && hits.length === 0}<div class="empty-state"><h3>No matches</h3><p>Try fewer words or a different spelling.</p></div>{/if}
    {#each groups as [ws, list] (ws)}
      <section>
        <h2 class="label">{ws} <span class="faint">· {list.length}</span></h2>
        {#each list as hit (hit.id)}
          <button class="hit" onclick={(e) => openNode(hit.id, whereFrom(e), hit.title)}>
            <span class="title"><Icon name={hit.type === 'task' ? 'check-square' : hit.type === 'daily' ? 'calendar' : 'file-text'} size={14} /> {hit.title}</span>
            <span class="snippet">{@html snippetHtml(hit.snippet)}</span>
            <span class="path faint mono">{hit.path}</span>
          </button>
        {/each}
      </section>
    {/each}
  </div>
</div>

<style>
  .view { flex: 1; padding: 0 28px; }
  .inner { max-width: 860px; margin: 0 auto; padding: 22px 0 80px; }
  .box { display: flex; align-items: center; gap: 10px; padding: 0 12px; border: 1px solid var(--line-strong); border-radius: 10px; background: var(--bg-input); margin-bottom: 20px; }
  .box:focus-within { border-color: var(--accent); }
  .q { flex: 1; height: 46px; border: 0; background: transparent; font-size: 17px; }
  .q:focus { outline: none; }
  .scope { width: auto; height: 30px; }
  section { margin-bottom: 22px; }
  h2 { margin-bottom: 6px; }
  .hit { display: grid; gap: 3px; width: 100%; text-align: left; padding: 10px 12px; border: 0; border-radius: 8px; background: none; color: var(--text-soft); }
  .hit:hover { background: var(--bg-hover); }
  .title { display: flex; align-items: center; gap: 8px; font-weight: 650; color: var(--text); }
  .snippet { font-size: 14px; }
  .snippet :global(mark) { background: color-mix(in srgb, var(--accent) 30%, transparent); color: var(--text); border-radius: 2px; padding: 0 1px; }
  .path { font-size: 11px; }
</style>
