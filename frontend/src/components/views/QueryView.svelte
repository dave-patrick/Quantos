<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { onChange } from '../../lib/events';
  import { layout, type Tab } from '../../lib/layout.svelte';
  import { views, type SavedView, type ViewLayout, type Filter } from '../../lib/views.svelte';
  import { supertags } from '../../lib/supertags.svelte';
  import { cellValue, display, parsePasted, propertyKey, runQuery, type QueryRow } from '../../lib/query';
  import { openNode, whereFrom } from '../../lib/links.svelte';
  import { renderMarkdown } from '../../lib/render';
  import DataTable from '../DataTable.svelte';
  import Icon from '../Icon.svelte';

  let { tab, presetTags = null, embedded = false }: { tab: Tab; presetTags?: string[] | null; embedded?: boolean } = $props();

  const blank = (): SavedView => ({
    id: crypto.randomUUID(), name: 'New view', query: { types: [], tags: presetTags ?? [], workspaces: 'active', filters: [] },
    layout: 'table', sort: [{ field: 'modified', dir: 'desc' }], group: [], columns: []
  });

  let view = $state<SavedView>(blank());
  let rows = $state<QueryRow[]>([]);
  let total = $state(0);
  let columns = $state<string[]>([]);
  let error = $state<string | null>(null);
  let editingSpec = $state(false);
  let pasteOpen = $state(false);
  let pasteText = $state('');
  let saved = $state(false);
  let timer: ReturnType<typeof setTimeout>;

  onMount(() => {
    if (tab.arg && tab.arg !== 'new') {
      const existing = views.get(tab.arg);
      if (existing) { view = structuredClone($state.snapshot(existing)) as SavedView; saved = true; }
    }
    if (!saved && !embedded) editingSpec = true;
    run();
    return onChange(() => { clearTimeout(timer); timer = setTimeout(run, 400); });
  });

  async function run() {
    try {
      const result = await runQuery({ ...view.query, sort: view.sort, columns: view.columns?.length ? view.columns : undefined, limit: 1000 });
      rows = result.rows; total = result.total; columns = result.columns; error = result.error;
    } catch (e) { error = (e as Error).message; }
  }

  function changed() { clearTimeout(timer); timer = setTimeout(run, 250); }

  async function save() {
    view.name = view.name.trim() || 'Untitled view';
    await views.save($state.snapshot(view) as SavedView);
    saved = true;
    tab.arg = view.id;
    tab.title = view.name;
    layout.persist();
    app.toast('View saved.', 'ok');
  }

  async function remove() {
    if (!(await app.confirm('Delete this view?', 'The notes it shows are not affected.', 'Delete view', 'danger'))) return;
    await views.remove(view.id);
    layout.close(layout.activePane, tab.key);
  }

  // Multi-level grouping: "status › priority".
  const grouped = $derived.by((): [string, QueryRow[]][] | null => {
    const keys = (view.group ?? []).filter(Boolean);
    if (!keys.length) return null;
    const map = new Map<string, QueryRow[]>();
    for (const r of rows) {
      const k = keys.map((g) => display(cellValue(r, g)) || '—').join(' › ');
      map.set(k, [...(map.get(k) ?? []), r]);
    }
    return [...map].sort(([a], [b]) => a.localeCompare(b));
  });

  const kanbanField = $derived(view.kanbanField || view.group?.[0] || 'status');
  const kanbanColumns = $derived.by(() => {
    const values = new Map<string, QueryRow[]>();
    const def = supertags.list.flatMap((t) => t.allFields).find((f) => f.key === kanbanField);
    for (const o of def?.options ?? []) values.set(o, []);
    for (const r of rows) {
      const v = display(cellValue(r, kanbanField)) || '—';
      values.set(v, [...(values.get(v) ?? []), r]);
    }
    return [...values];
  });

  let dragId: string | null = null;
  async function dropKanban(value: string) {
    const id = dragId; dragId = null;
    if (!id) return;
    try {
      await api.patch(`nodes/${id}`, value === '—' ? { remove: [kanbanField] } : { set: { [kanbanField]: value } });
      run();
    } catch (e) { app.error(e); }
  }

  // Structured paste: rows from a spreadsheet, CSV or Markdown table become notes with properties.
  const pasted = $derived(pasteText.trim() ? parsePasted(pasteText) : null);
  async function createPasted() {
    if (!pasted) return;
    const titleIndex = Math.max(0, pasted.headers.findIndex((h) => /^(title|name)$/i.test(h)));
    const body = {
      type: view.query.types?.[0] ?? 'note', tags: view.query.tags ?? [], workspace: app.workspace,
      rows: pasted.rows.map((cells) => ({
        title: cells[titleIndex] ?? '',
        properties: Object.fromEntries(pasted.headers.map((h, i) => [propertyKey(h), cells[i] ?? '']).filter(([k, v], i) => i !== titleIndex && k && v !== ''))
      }))
    };
    try {
      const created = await api.post<unknown[]>('nodes/batch', body);
      app.toast(`Created ${created.length} note(s).`, 'ok');
      pasteText = ''; pasteOpen = false; run();
    } catch (e) { app.error(e); }
  }

  function addFilter() { view.query.filters = [...(view.query.filters ?? []), { field: 'status', op: 'eq', value: '' }]; }
  function addSort() { view.sort = [...(view.sort ?? []), { field: 'title', dir: 'asc' }]; }
  const types = ['note', 'task', 'daily', 'person', 'project', 'event', 'template'];
  const layouts: { key: ViewLayout; icon: string; label: string }[] = [
    { key: 'table', icon: 'table', label: 'Table' }, { key: 'list', icon: 'list', label: 'List' }, { key: 'kanban', icon: 'kanban', label: 'Board' },
    { key: 'grid', icon: 'grid', label: 'Grid' }, { key: 'gallery', icon: 'layers', label: 'Gallery' }
  ];
  const ops: [Filter['op'], string][] = [['eq', 'is'], ['neq', 'is not'], ['contains', 'contains'], ['lt', '<'], ['lte', '≤'], ['gt', '>'], ['gte', '≥'], ['empty', 'is empty'], ['notempty', 'has a value'], ['in', 'is one of']];
  const cover = (r: QueryRow) => { const c = r.properties.cover; return typeof c === 'string' && /^attachments\//.test(c) ? '/api/files/' + c : typeof c === 'string' && /^https?:/.test(c) ? null : null; };
</script>

<div class="scroll query" class:embedded>
  <div class="inner">
    {#if !embedded}
      <header>
        <input class="name" bind:value={view.name} aria-label="View name" />
        <div class="tools">
          <div class="seg">
            {#each layouts as l (l.key)}<button class:on={view.layout === l.key} title={l.label} onclick={() => (view.layout = l.key)}><Icon name={l.icon} size={14} /></button>{/each}
          </div>
          <button class="btn ghost" onclick={() => (editingSpec = !editingSpec)}><Icon name="filter" size={14} /> {editingSpec ? 'Hide query' : 'Edit query'}</button>
          <button class="btn ghost" onclick={() => (pasteOpen = !pasteOpen)}><Icon name="upload" size={14} /> Paste rows</button>
          <label class="pin"><input type="checkbox" bind:checked={view.pinned} /> Pin</label>
          <label class="pin" title="Show as a folder in the sidebar"><input type="checkbox" bind:checked={view.virtualFolder} /> Folder</label>
          <button class="btn primary" onclick={save}>Save</button>
          {#if saved}<button class="icon-btn" title="Delete view" onclick={remove}><Icon name="trash" size={15} /></button>{/if}
        </div>
      </header>
    {/if}

    {#if editingSpec}
      <section class="spec">
        <div class="row">
          <span class="label">Types</span>
          {#each types as t (t)}
            <label class="chip"><input type="checkbox" checked={view.query.types?.includes(t)} onchange={(e) => {
              const on = (e.target as HTMLInputElement).checked; view.query.types = on ? [...(view.query.types ?? []), t] : (view.query.types ?? []).filter((x) => x !== t); changed(); }} /> {t}</label>
          {/each}
        </div>
        <div class="row">
          <span class="label">Supertags</span>
          <input class="input" list="tag-list" value={(view.query.tags ?? []).join(', ')} placeholder="meeting, project"
            onchange={(e) => { view.query.tags = (e.target as HTMLInputElement).value.split(',').map((s) => s.trim().replace(/^#/, '')).filter(Boolean); changed(); }} />
          <datalist id="tag-list">{#each supertags.list as t (t.id)}<option value={t.name}></option>{/each}</datalist>
          <span class="label">Workspaces</span>
          <select class="input" value={typeof view.query.workspaces === 'string' ? view.query.workspaces : 'active'} onchange={(e) => { view.query.workspaces = (e.target as HTMLSelectElement).value as 'active' | 'all'; changed(); }}>
            <option value="active">This workspace</option><option value="all">All workspaces</option>
          </select>
          <span class="label">Text</span>
          <input class="input" bind:value={view.query.text} oninput={changed} placeholder="contains words" />
          <label class="chip"><input type="checkbox" bind:checked={view.query.includeArchived} onchange={changed} /> archived</label>
        </div>
        {#each view.query.filters ?? [] as f, i (i)}
          <div class="row">
            <span class="label">{i === 0 ? 'Where' : 'and'}</span>
            <input class="input" list="prop-list" bind:value={f.field} onchange={changed} placeholder="property" />
            <select class="input" bind:value={f.op} onchange={changed}>{#each ops as [k, l] (k)}<option value={k}>{l}</option>{/each}</select>
            {#if f.op !== 'empty' && f.op !== 'notempty'}<input class="input" value={String(f.value ?? '')} oninput={(e) => { f.value = (e.target as HTMLInputElement).value; changed(); }} placeholder="value, or today, today+7" />{/if}
            <button class="icon-btn" aria-label="Remove condition" onclick={() => { view.query.filters = view.query.filters!.filter((_, j) => j !== i); changed(); }}><Icon name="x" size={13} /></button>
          </div>
        {/each}
        <datalist id="prop-list">{#each ['title', 'type', 'tags', 'created', 'modified', ...new Set(rows.flatMap((r) => Object.keys(r.properties)))] as p (p)}<option value={p}></option>{/each}</datalist>
        <div class="row">
          <button class="btn ghost" onclick={addFilter}><Icon name="plus" size={13} /> Condition</button>
          <span class="label">Sort</span>
          {#each view.sort ?? [] as s, i (i)}
            <input class="input small" list="prop-list" bind:value={s.field} onchange={changed} />
            <select class="input small" bind:value={s.dir} onchange={changed}><option value="asc">↑</option><option value="desc">↓</option></select>
            <button class="icon-btn" aria-label="Remove sort" onclick={() => { view.sort = view.sort!.filter((_, j) => j !== i); changed(); }}><Icon name="x" size={13} /></button>
          {/each}
          <button class="btn ghost" onclick={addSort}><Icon name="plus" size={13} /> Sort</button>
        </div>
        <div class="row">
          <span class="label">Group by</span>
          <input class="input" list="prop-list" value={(view.group ?? []).join(', ')} placeholder="status, priority"
            onchange={(e) => (view.group = (e.target as HTMLInputElement).value.split(',').map((s) => s.trim()).filter(Boolean))} />
          <span class="label">Columns</span>
          <input class="input" list="prop-list" value={(view.columns ?? []).join(', ')} placeholder="automatic"
            onchange={(e) => { view.columns = (e.target as HTMLInputElement).value.split(',').map((s) => s.trim()).filter(Boolean); changed(); }} />
          {#if view.layout === 'kanban'}<span class="label">Board field</span><input class="input small" list="prop-list" bind:value={view.kanbanField} placeholder="status" />{/if}
        </div>
      </section>
    {/if}

    {#if pasteOpen}
      <section class="paste">
        <p class="muted">Paste rows from a spreadsheet, a CSV file or a Markdown table. The first row names the properties; a “title” or “name” column becomes the note title.
          New notes get this view's type and supertags.</p>
        <textarea bind:value={pasteText} rows="6" placeholder={'title\tstatus\tdue\nQ4 budget\tdraft\t2026-11-01'}></textarea>
        {#if pasted}<p class="faint">{pasted.rows.length} row(s), properties: {pasted.headers.join(', ')}</p>{/if}
        <button class="btn primary" disabled={!pasted?.rows.length} onclick={createPasted}>Create {pasted?.rows.length ?? 0} note(s)</button>
      </section>
    {/if}

    {#if error}<p class="error">{error}</p>{/if}
    <p class="faint count">{total} result{total === 1 ? '' : 's'}{total > rows.length ? `, showing ${rows.length}` : ''}</p>

    {#if view.layout === 'table'}
      <DataTable {rows} {columns} groups={grouped} onchanged={run} />
    {:else if view.layout === 'list'}
      {#each grouped ?? [['', rows] as [string, QueryRow[]]] as [name, list] (name)}
        {#if name}<h3 class="label group">{name}</h3>{/if}
        {#each list as r (r.id)}
          <button class="list-row" onclick={(e) => openNode(r.id, whereFrom(e), r.title)}>
            <Icon name={r.type === 'task' ? 'check-square' : r.type === 'person' ? 'user' : 'file-text'} size={14} />
            <span class="t">{r.title}</span>
            <span class="faint">{columns.filter((c) => c !== 'title').slice(0, 3).map((c) => display(cellValue(r, c))).filter(Boolean).join(' · ')}</span>
          </button>
        {/each}
      {/each}
    {:else if view.layout === 'kanban'}
      <div class="board">
        {#each kanbanColumns as [value, list] (value)}
          <div class="col" role="list" ondragover={(e) => e.preventDefault()} ondrop={() => dropKanban(value)}>
            <h3 class="label">{value} <span class="faint">{list.length}</span></h3>
            {#each list as r (r.id)}
              <div class="card" role="listitem" draggable="true" ondragstart={() => (dragId = r.id)}>
                <button class="t" onclick={(e) => openNode(r.id, whereFrom(e), r.title)}>{r.title}</button>
                {#each columns.filter((c) => c !== 'title' && c !== kanbanField).slice(0, 3) as c (c)}
                  {#if display(cellValue(r, c))}<span class="meta">{c.replace(/_/g, ' ')}: {display(cellValue(r, c))}</span>{/if}
                {/each}
              </div>
            {/each}
          </div>
        {/each}
      </div>
    {:else}
      <div class="cards" class:gallery={view.layout === 'gallery'}>
        {#each rows as r (r.id)}
          <button class="doc-card" onclick={(e) => openNode(r.id, whereFrom(e), r.title)}>
            {#if view.layout === 'gallery'}
              {#if cover(r)}<img src={cover(r)} alt="" />{:else}<div class="cover-placeholder"><Icon name="file-text" size={26} /></div>{/if}
            {/if}
            <strong>{r.title}</strong>
            {#each r.tags as t (t)}<span class="tag">#{t}</span>{/each}
            {#each columns.filter((c) => c !== 'title').slice(0, 4) as c (c)}
              {#if display(cellValue(r, c))}<span class="meta">{c.replace(/_/g, ' ')}: {display(cellValue(r, c))}</span>{/if}
            {/each}
          </button>
        {/each}
      </div>
    {/if}
  </div>
</div>

<style>
  .query { flex: 1; padding: 0 24px; }
  .query.embedded { padding: 0; }
  .inner { padding: 18px 0 60px; }
  .embedded .inner { padding: 0; }
  header { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 10px; }
  .name { flex: 1; min-width: 200px; border: 0; background: transparent; font: 700 26px/1.2 var(--font-display); color: var(--text); }
  .name:focus { outline: none; box-shadow: none; }
  .tools { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; }
  .seg { display: inline-flex; border: 1px solid var(--line-strong); border-radius: 7px; overflow: hidden; }
  .seg button { border: 0; background: none; color: var(--text-muted); padding: 5px 9px; display: grid; }
  .seg button.on { background: var(--accent-wash); color: var(--accent); }
  .pin { display: flex; gap: 4px; align-items: center; font-size: 13px; color: var(--text-muted); }
  .spec, .paste { padding: 12px; border: 1px solid var(--line); border-radius: 10px; background: var(--bg-raised); margin-bottom: 12px; display: grid; gap: 8px; }
  .row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
  .row .input { width: auto; min-width: 120px; height: 28px; font-size: 13px; }
  .row .input.small { min-width: 90px; }
  .chip { display: inline-flex; gap: 4px; align-items: center; font-size: 13px; color: var(--text-soft); }
  textarea { width: 100%; font: 13px var(--font-mono); background: var(--bg-input); border: 1px solid var(--line-strong); border-radius: 7px; padding: 8px; color: var(--text); }
  .error { color: var(--danger); }
  .count { font-size: 12px; margin: 4px 0 8px; }
  .group { margin: 14px 0 4px; }
  .list-row { display: flex; gap: 10px; align-items: center; width: 100%; text-align: left; border: 0; background: none; padding: 7px 8px; border-radius: 6px; color: var(--text-soft); }
  .list-row:hover { background: var(--bg-hover); }
  .list-row .t { color: var(--text); font-weight: 600; }
  .board { display: grid; grid-auto-flow: column; grid-auto-columns: minmax(240px, 1fr); gap: 10px; overflow-x: auto; align-items: start; }
  .col { background: var(--bg-raised); border: 1px solid var(--line); border-radius: 10px; padding: 8px; min-height: 160px; }
  .card { background: var(--bg); border: 1px solid var(--line); border-radius: 8px; padding: 8px 10px; margin-bottom: 6px; display: grid; gap: 2px; cursor: grab; }
  .card .t { border: 0; background: none; text-align: left; padding: 0; color: var(--text); font-weight: 600; }
  .meta { font-size: 12px; color: var(--text-muted); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 12px; }
  .doc-card { display: grid; gap: 4px; align-content: start; text-align: left; border: 1px solid var(--line); border-radius: 10px; background: var(--bg-raised); padding: 12px; color: var(--text-soft); min-height: 110px; }
  .doc-card:hover { border-color: var(--accent); }
  .doc-card strong { color: var(--text); font-size: 15px; }
  .gallery .doc-card { padding: 0 0 12px; overflow: hidden; }
  .gallery .doc-card > :not(img):not(.cover-placeholder) { margin: 0 12px; }
  .doc-card img, .cover-placeholder { width: 100%; height: 130px; object-fit: cover; background: var(--bg-panel); display: grid; place-items: center; color: var(--text-faint); margin-bottom: 6px; }
  .tag { font: 11px var(--font-mono); color: var(--accent); }
</style>
