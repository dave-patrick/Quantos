<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { ago } from '../../lib/today.svelte';
  import { openNode, openByTitle, whereFrom } from '../../lib/links.svelte';
  import { onChange } from '../../lib/events';
  import { renderMarkdown } from '../../lib/render';
  import type { NodeDto, NodeSummary, TreeFolder } from '../../lib/types';
  import Icon from '../Icon.svelte';
  import { prefs } from '../../lib/prefs.svelte';

  /** Notes browser: every note in a filterable, sortable list with a preview beside it. */
  let excerpts = $state<Record<string, string>>({});
  let query = $state('');
  let type = $state('');
  let tag = $state('');
  let folder = $state('');
  let sort = $state<'modified' | 'created' | 'title'>('modified');
  let showArchived = $state(false);
  let everywhere = $state(false);
  let current = $state<string | null>(null);
  let preview = $state<NodeDto | null>(null);
  let picked = $state<Set<string>>(new Set());

  // Tree layout: the same notes, nested in their folders. Dragging a note onto a folder moves it there.
  interface Folder { name: string; path: string; folders: Folder[]; notes: NodeSummary[] }
  const LABELS: Record<string, string> = { nodes: 'Notes', daily: 'Daily', tasks: 'Tasks', supertags: 'Supertags', archive: 'Archive', templates: 'Templates', synced_blocks: 'Synced blocks' };
  // Empty folders count too: they're where notes get dragged to.
  let folderTree = $state<TreeFolder | null>(null);
  $effect(() => { void app.workspace; api.get<TreeFolder>(`tree?workspace=${encodeURIComponent(app.workspace)}`).then((t) => (folderTree = t)).catch(() => {}); });
  const tree = $derived.by(() => {
    const top: Folder = { name: '', path: '', folders: [], notes: [] };
    const seed = (t: TreeFolder, into: Folder, depth: number) => {
      for (const c of t.folders) {
        if (c.name === 'archive' && !showArchived) continue;
        const f: Folder = { name: depth === 0 ? LABELS[c.name] ?? c.name : c.name, path: c.path, folders: [], notes: [] };
        into.folders.push(f);
        seed(c, f, depth + 1);
      }
    };
    if (folderTree && !everywhere) seed(folderTree, top, 0);
    for (const n of shown.slice(0, 3000)) {
      const parts = n.path.split('/');
      const scoped = parts[0] === 'workspaces' || parts[0] === 'linked';
      const dirs = scoped ? parts.slice(2, -1) : parts.slice(0, -1);
      const base = scoped ? parts.slice(0, 2) : [];
      let at = top;
      if (everywhere && scoped) {
        let ws = at.folders.find((f) => f.path === base.join('/'));
        if (!ws) at.folders.push(ws = { name: parts[1], path: base.join('/'), folders: [], notes: [] });
        at = ws;
      }
      dirs.forEach((d, i) => {
        const path = [...base, ...dirs.slice(0, i + 1)].join('/');
        let f = at.folders.find((x) => x.path === path);
        if (!f) at.folders.push(f = { name: i === 0 ? LABELS[d] ?? d : d, path, folders: [], notes: [] });
        at = f;
      });
      at.notes.push(n);
    }
    const order = (f: Folder) => { f.folders.sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true })); f.folders.forEach(order); };
    order(top);
    return top;
  });
  let closed = $state<Record<string, boolean>>({});
  let dropFolder = $state<string | null>(null);
  let dragging: string | null = null;
  const count = (f: Folder): number => f.notes.length + f.folders.reduce((a, c) => a + count(c), 0);

  async function moveTo(id: string, folder: Folder) {
    const n = app.titleIndex.get(id);
    if (!n || n.path.slice(0, n.path.lastIndexOf('/')) === folder.path) return;
    try {
      await api.post(`nodes/${id}/move`, { folder: folder.path });
      app.toast(`Moved “${n.title}” to ${folder.name}.`, 'ok');
      app.scheduleTitles();
    } catch (e) { app.error(e); }
  }
  let list = $state<HTMLUListElement | null>(null);

  async function loadExcerpts() { try { excerpts = await api.get<Record<string, string>>('nodes/excerpts'); } catch { /* the list still works */ } }
  onMount(() => {
    loadExcerpts();
    let t: ReturnType<typeof setTimeout>;
    return onChange((e) => { if (['Created', 'Updated', 'Moved'].includes(e.kind)) { clearTimeout(t); t = setTimeout(loadExcerpts, 800); } });
  });

  const TYPES: [string, string][] = [['', 'All types'], ['note', 'Notes'], ['task', 'Tasks'], ['person', 'People'], ['project', 'Projects'], ['daily', 'Daily notes'], ['canvas', 'Whiteboards'], ['event', 'Events'], ['template', 'Templates']];
  const icons: Record<string, string> = { task: 'check-square', person: 'user', daily: 'calendar', canvas: 'layers', template: 'copy', supertag: 'hash', project: 'target' };

  /** The folder a note sits in, under its workspace's Notes (or type) folder. */
  const folderOf = (n: NodeSummary) => n.path.split('/').slice(3, -1).join('/');

  const scope = $derived(app.titles.filter((n) => (everywhere || n.workspace === app.workspace) && (showArchived || !n.archived) && n.type !== 'synced_block'));
  const tags = $derived([...new Set(scope.flatMap((n) => n.tags))].sort((a, b) => a.localeCompare(b)));
  const folders = $derived([...new Set(scope.map(folderOf).filter(Boolean))].sort());
  const shown = $derived.by(() => {
    const q = query.trim().toLowerCase();
    const words = q.split(/\s+/).filter(Boolean);
    const out = scope.filter((n) => (!type || n.type === type) && (!tag || n.tags.includes(tag)) && (!folder || folderOf(n) === folder || folderOf(n).startsWith(folder + '/'))
      && (!words.length || words.every((w) => (n.title + ' ' + (excerpts[n.id] ?? '') + ' ' + n.tags.join(' ')).toLowerCase().includes(w))));
    const key = (n: NodeSummary) => (sort === 'title' ? n.title.toLowerCase() : (sort === 'created' ? n.created : n.modified) ?? '');
    return out.sort((a, b) => (sort === 'title' ? key(a).localeCompare(key(b)) : key(b).localeCompare(key(a))));
  });

  async function select(id: string) {
    current = id;
    preview = null;
    try { const n = await api.get<NodeDto>(`nodes/${id}`); if (current === id) preview = n; } catch (e) { app.error(e); }
  }

  function keys(e: KeyboardEvent) {
    const i = shown.findIndex((n) => n.id === current);
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      const next = shown[Math.max(0, Math.min(shown.length - 1, i + (e.key === 'ArrowDown' ? 1 : -1)))];
      if (next) { select(next.id); tick().then(() => list?.querySelector<HTMLElement>(`[data-id="${next.id}"] .pick`)?.focus()); }
    } else if (e.key === 'Enter' && current) { e.preventDefault(); openNode(current, e.ctrlKey ? 'new' : 'current', shown[i]?.title); }
    else if (e.key === ' ' && current && !(e.target as HTMLElement).closest('input')) { e.preventDefault(); togglePick(current); }
  }

  function togglePick(id: string) { const s = new Set(picked); s.has(id) ? s.delete(id) : s.add(id); picked = s; }
  const allPicked = $derived(shown.length > 0 && shown.every((n) => picked.has(n.id)));
  function pickAll() { picked = allPicked ? new Set() : new Set(shown.map((n) => n.id)); }

  async function bulk(kind: 'archive' | 'trash' | 'tag') {
    const ids = [...picked];
    if (!ids.length) return;
    if (kind === 'trash' && !(await app.confirm(`Move ${ids.length} ${ids.length === 1 ? 'note' : 'notes'} to the trash?`, 'You can restore them from the trash.', 'Move to trash'))) return;
    let name: string | null = null;
    if (kind === 'tag') { name = (await app.ask('Add a tag', { placeholder: 'tag (without #)', confirmLabel: 'Add' }))?.replace(/^#/, '').trim() ?? null; if (!name) return; }
    let done = 0;
    for (const id of ids) {
      try {
        if (kind === 'archive') await api.post(`nodes/${id}/archive`, { archived: true });
        else if (kind === 'trash') await api.post(`nodes/${id}/trash`);
        else { const n = app.titleIndex.get(id); if (n && !n.tags.includes(name!)) await api.patch(`nodes/${id}`, { set: { tags: [...n.tags, name] } }); }
        done++;
      } catch (e) { app.error(e); }
    }
    picked = new Set();
    app.toast(`${kind === 'archive' ? 'Archived' : kind === 'trash' ? 'Moved to the trash:' : `Tagged #${name}:`} ${done} ${done === 1 ? 'note' : 'notes'}.`, 'ok');
    app.scheduleTitles();
  }

  function previewClick(e: MouseEvent) {
    const a = (e.target as HTMLElement).closest('a.wikilink') as HTMLAnchorElement | null;
    if (a) { e.preventDefault(); openByTitle(a.dataset.target ?? '', whereFrom(e)); }
  }
  const props = $derived(preview ? Object.entries(preview.properties).filter(([, v]) => v !== null && v !== '' && typeof v !== 'object').slice(0, 8) : []);
  const plainProp = (v: unknown) => String(v).replace(/\[\[[0-9A-HJKMNP-TV-Z]{26}\|([^\]]+)\]\]/g, '$1');
</script>

{#snippet branch(f: Folder, depth: number)}
  {#each f.folders as sub (sub.path)}
    <li class="folder" class:drop={dropFolder === sub.path}
      ondragover={(e) => { if (dragging) { e.preventDefault(); dropFolder = sub.path; } }} ondragleave={() => { if (dropFolder === sub.path) dropFolder = null; }}
      ondrop={(e) => { e.preventDefault(); const id = dragging; dragging = null; dropFolder = null; if (id) moveTo(id, sub); }}>
      <button class="fold" aria-expanded={!closed[sub.path]} style={`padding-left:${8 + depth * 16}px`} onclick={() => (closed[sub.path] = !closed[sub.path])}>
        <Icon name={closed[sub.path] ? 'chevron-right' : 'chevron-down'} size={12} /><Icon name={closed[sub.path] ? 'folder' : 'folder-open'} size={14} /> {sub.name} <span class="faint n">{count(sub)}</span>
      </button>
      {#if !closed[sub.path]}<ul class="sub">{@render branch(sub, depth + 1)}</ul>{/if}
    </li>
  {/each}
  {#each f.notes as n (n.id)}
    <li class="leaf" class:on={current === n.id} style={`padding-left:${26 + depth * 16}px`}>
      <input type="checkbox" aria-label={`Select ${n.title}`} checked={picked.has(n.id)} onchange={() => togglePick(n.id)} />
      <button class="lname" draggable="true" ondragstart={(e) => { dragging = n.id; e.dataTransfer?.setData('text/plain', n.title); }} ondragend={() => { dragging = null; dropFolder = null; }}
        onclick={() => select(n.id)} ondblclick={(e) => openNode(n.id, whereFrom(e), n.title)}>
        <Icon name={icons[n.type] ?? 'file-text'} size={13} /> <span>{n.title || 'Untitled'}</span> <span class="faint when">{ago(n.modified)}</span>
      </button>
    </li>
  {/each}
{/snippet}

<div class="browser">
  <div class="tools">
    <h1><Icon name="file-text" size={20} /> Notes</h1>
    <input class="input q" aria-label="Filter notes" placeholder="Filter by words in the title or text" bind:value={query} />
    <select class="input" aria-label="Type" bind:value={type}>{#each TYPES as [v, l] (v)}<option value={v}>{l}</option>{/each}</select>
    {#if tags.length}<select class="input" aria-label="Tag" bind:value={tag}><option value="">All tags</option>{#each tags as t (t)}<option value={t}>#{t}</option>{/each}</select>{/if}
    {#if folders.length}<select class="input" aria-label="Folder" bind:value={folder}><option value="">All folders</option>{#each folders as f (f)}<option value={f}>{f}</option>{/each}</select>{/if}
    <select class="input" aria-label="Sort" bind:value={sort}><option value="modified">Recently edited</option><option value="created">Recently created</option><option value="title">Title A–Z</option></select>
    <label class="check"><input type="checkbox" bind:checked={showArchived} /> Archived</label>
    <label class="check"><input type="checkbox" bind:checked={everywhere} /> All workspaces</label>
  </div>

  <div class="panes">
    <section class="left">
      <div class="bar">
        <label class="check"><input type="checkbox" aria-label="Select all shown" checked={allPicked} onchange={pickAll} /> {shown.length} {shown.length === 1 ? 'note' : 'notes'}</label>
        <span class="seg" role="radiogroup" aria-label="Layout">
          <button role="radio" aria-checked={!prefs.notesTree} class:on={!prefs.notesTree} onclick={() => (prefs.notesTree = false)}>List</button>
          <button role="radio" aria-checked={prefs.notesTree} class:on={prefs.notesTree} onclick={() => (prefs.notesTree = true)}>Tree</button>
        </span>
        {#if picked.size}
          <span class="sel">{picked.size} selected</span>
          <button class="btn ghost" onclick={() => bulk('tag')}><Icon name="hash" size={13} /> Tag</button>
          <button class="btn ghost" onclick={() => bulk('archive')}><Icon name="archive" size={13} /> Archive</button>
          <button class="btn ghost" onclick={() => bulk('trash')}><Icon name="trash" size={13} /> Trash</button>
        {/if}
      </div>
      {#if prefs.notesTree}
        <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
        <ul class="scroll list tree" aria-label="Notes by folder">{@render branch(tree, 0)}</ul>
      {:else}
      <!-- Each row: a tick box, then the note as a button (arrows move between rows, Enter opens). -->
      <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
      <ul class="scroll list" bind:this={list} aria-label="Notes" onkeydown={keys}>
        {#each shown.slice(0, 600) as n (n.id)}
          <li class="row" class:on={current === n.id} class:arch={n.archived} data-id={n.id}>
            <input type="checkbox" aria-label={`Select ${n.title}`} checked={picked.has(n.id)} onchange={() => togglePick(n.id)} />
            <button class="pick" aria-current={current === n.id ? 'true' : undefined} onclick={() => select(n.id)} ondblclick={(e) => openNode(n.id, whereFrom(e), n.title)}>
            <Icon name={icons[n.type] ?? 'file-text'} size={14} />
            <span class="txt">
              <span class="t">{n.title || 'Untitled'}</span>
              {#if n.type === 'canvas'}<span class="x">Whiteboard</span>{:else if excerpts[n.id]}<span class="x">{excerpts[n.id].replace(/\[\^[^\]\s]+\]/g, '')}</span>{/if}
              <span class="meta">
                <span>{ago(n.modified)}</span>
                {#if everywhere}<span>{n.workspace}</span>{/if}
                {#if folderOf(n)}<span>{folderOf(n)}</span>{/if}
                {#each n.tags.slice(0, 4) as t (t)}<span class="tag">#{t}</span>{/each}
              </span>
            </span>
            </button>
          </li>
        {:else}
          <li class="faint empty">No notes match.</li>
        {/each}
        {#if shown.length > 600}<li class="faint empty">Showing the first 600; filter to narrow it down.</li>{/if}
      </ul>
      {/if}
    </section>

    <section class="right scroll">
      {#if preview}
        <header>
          <h2>{preview.title}</h2>
          <button class="btn primary" onclick={(e) => openNode(preview!.id, whereFrom(e), preview!.title)}>Open</button>
        </header>
        <p class="faint path">{preview.type} · {preview.path} · edited {ago(preview.modified)}</p>
        {#if props.length}
          <dl class="props">{#each props as [k, v] (k)}<dt>{k}</dt><dd>{plainProp(v)}</dd>{/each}</dl>
        {/if}
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <!-- svelte-ignore a11y_click_events_have_key_events -->
        <div class="rendered md" onclick={previewClick}>{@html renderMarkdown(preview.body.slice(0, 20000))}</div>
      {:else if current}
        <p class="faint">Loading…</p>
      {:else}
        <p class="faint pick">Select a note to preview it. Double-click or press Enter to open it; Space ticks it for the actions above.</p>
      {/if}
    </section>
  </div>
</div>

<style>
  .browser { flex: 1; display: flex; flex-direction: column; min-height: 0; padding: 14px 18px 10px; gap: 10px; }
  .tools { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
  h1 { display: flex; align-items: center; gap: 8px; font: 700 22px/1.2 var(--font-display); margin-right: 6px; }
  .tools .input { width: auto; }
  .tools .q { flex: 1 1 220px; }
  .check { display: inline-flex; gap: 5px; align-items: center; font-size: 13px; color: var(--text-muted); }
  .panes { flex: 1; min-height: 0; display: grid; grid-template-columns: minmax(300px, 420px) 1fr; gap: 12px; }
  .left { display: flex; flex-direction: column; min-height: 0; border: 1px solid var(--line); border-radius: 10px; background: var(--bg-raised); }
  .bar { display: flex; align-items: center; gap: 6px; padding: 6px 10px; border-bottom: 1px solid var(--line); flex-wrap: wrap; min-height: 38px; }
  .sel { font-size: 12.5px; color: var(--accent); margin-left: auto; }
  .list { flex: 1; list-style: none; margin: 0; padding: 0; }
  .seg { display: inline-flex; border: 1px solid var(--line-strong); border-radius: 6px; overflow: hidden; margin-left: auto; }
  .seg button { border: 0; background: none; color: var(--text-muted); padding: 2px 9px; font: 12.5px var(--font-body); cursor: pointer; }
  .seg button + button { border-left: 1px solid var(--line-strong); }
  .seg button.on { background: var(--accent-wash); color: var(--accent-strong); }
  .tree { padding: 4px 0; }
  .sub { list-style: none; margin: 0; padding: 0; }
  .folder.drop > .fold { background: var(--accent-wash); outline: 1px dashed var(--accent); }
  .fold { display: flex; align-items: center; gap: 6px; width: 100%; border: 0; background: none; color: var(--text-soft); font: 600 13.5px var(--font-body); padding: 5px 8px; cursor: pointer; text-align: left; }
  .fold:hover { background: var(--bg-hover); }
  .n { font: 11px var(--font-mono); margin-left: auto; }
  .leaf { display: flex; align-items: center; gap: 6px; padding-right: 8px; }
  .leaf:hover { background: var(--bg-hover); }
  .leaf.on { background: var(--bg-active); }
  .lname { display: flex; align-items: center; gap: 6px; flex: 1; min-width: 0; border: 0; background: none; color: var(--text); font: 13.5px var(--font-body); padding: 4px 0; cursor: pointer; text-align: left; }
  .lname span:first-of-type { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .lname .when { margin-left: auto; font: 11px var(--font-mono); white-space: nowrap; }
  .row { display: grid; grid-template-columns: auto 1fr; gap: 8px; align-items: start; padding: 8px 10px; border-bottom: 1px solid var(--line); }
  .pick { display: grid; grid-template-columns: auto 1fr; gap: 8px; align-items: start; border: 0; background: none; padding: 0; text-align: left; color: inherit; font: inherit; cursor: pointer; min-width: 0; }
  .pick:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; border-radius: 4px; }
  .row :global(.icon) { margin-top: 3px; color: var(--text-faint); }
  .row input { margin-top: 3px; }
  .row:hover { background: var(--bg-hover); }
  .row.on { background: var(--bg-active); }
  .row.arch .t { color: var(--text-faint); }
  .txt { min-width: 0; display: block; }
  .t, .x, .meta { display: block; }
  .t { font-weight: 600; font-size: 14px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .x { font-size: 12.5px; color: var(--text-muted); display: -webkit-box; -webkit-line-clamp: 2; line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; margin-top: 1px; }
  .pick .meta { display: flex; flex-wrap: wrap; gap: 4px 10px; font: 11px var(--font-mono); color: var(--text-faint); margin-top: 3px; }
  .tag { color: var(--accent); }
  .empty { padding: 16px; font-size: 13px; }
  .right { border: 1px solid var(--line); border-radius: 10px; padding: 16px 22px; background: var(--bg); }
  .right header { display: flex; align-items: center; gap: 12px; justify-content: space-between; }
  h2 { font: 700 24px/1.2 var(--font-display); }
  .path { font-size: 12px; margin: 4px 0 10px; word-break: break-all; }
  .props { display: grid; grid-template-columns: auto 1fr; gap: 3px 14px; font-size: 13px; margin: 0 0 14px; padding: 8px 10px; border: 1px solid var(--line); border-radius: 8px; }
  .props dt { color: var(--text-muted); }
  .props dd { margin: 0; overflow-wrap: anywhere; }
  .md { font-size: 15px; line-height: 1.6; }
  .pick { margin-top: 30px; text-align: center; font-size: 13.5px; }
  @container pane (max-width: 820px) {
    .panes { grid-template-columns: 1fr; grid-template-rows: 1fr auto; }
    .right { max-height: 40%; }
  }
</style>
