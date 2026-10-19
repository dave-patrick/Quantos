<script lang="ts">
  import { onDestroy, tick, untrack } from 'svelte';
  import { api } from '../lib/api';
  import { app } from '../lib/state.svelte';
  import type { Doc } from '../lib/docs.svelte';
  import { prefs } from '../lib/prefs.svelte';
  import { openNode, openByTitle } from '../lib/links.svelte';
  import { renderMarkdown } from '../lib/render';
  import type { NodeDto } from '../lib/types';
  import { COLORS, anchor, bounds, contained, edgePath, facing, newId, parseCanvas, serializeCanvas, type Canvas, type CanvasEdge, type CanvasNode, type Side } from '../lib/canvas';
  import Icon from './Icon.svelte';

  /** Whiteboard (Release 1.2): cards and arrows on an endless board, saved as JSON Canvas in the note. */
  let { doc }: { doc: Doc } = $props();

  let canvas = $state<Canvas>({ nodes: [], edges: [] });
  let problem = $state<string | null>(null);
  let view = $state({ x: 40, y: 40, zoom: 1 });
  let selected = $state<Set<string>>(new Set());
  let selectedEdge = $state<string | null>(null);
  let editing = $state<string | null>(null);
  let board = $state<HTMLDivElement | null>(null);
  let gesture: null | { kind: 'pan'; sx: number; sy: number; vx: number; vy: number }
    | { kind: 'move'; sx: number; sy: number; start: Map<string, { x: number; y: number }>; moved: boolean }
    | { kind: 'resize'; id: string; sx: number; sy: number; w: number; h: number }
    | { kind: 'select'; x0: number; y0: number } = null;
  let linking = $state<{ from: string; side: Side; x: number; y: number } | null>(null);
  let box = $state<{ x: number; y: number; w: number; h: number } | null>(null);
  let notes = $state<Record<string, { title: string; html: string } | null>>({});
  const undo: string[] = [], redo: string[] = [];

  const readOnly = $derived(!!doc.node?.locked || !!doc.node?.readOnly || prefs.reading);

  // Load from the note, and again when the file changes underneath (another window, an agent, sync).
  let lastSaved = '';
  $effect(() => {
    void doc.externalVersion;
    const body = doc.body;
    untrack(() => {
      if (body === lastSaved) return;
      const r = parseCanvas(body);
      canvas = r.canvas; problem = r.error; lastSaved = body;
      if (!fitted && canvas.nodes.length) queueMicrotask(fit);
      for (const n of canvas.nodes) if (n.type === 'file' && n.node) loadNote(n.node);
    });
  });
  let fitted = false;

  let saveTimer: ReturnType<typeof setTimeout> | undefined;
  function commit(record = true) {
    if (readOnly) return;
    if (record) { undo.push(lastSaved); if (undo.length > 80) undo.shift(); redo.length = 0; }
    clearTimeout(saveTimer);
    saveTimer = setTimeout(flush, 250);
  }
  function flush() {
    const body = serializeCanvas(doc.body, canvas);
    if (body === doc.body) return;
    lastSaved = body;
    doc.edit(body);
  }
  onDestroy(() => { clearTimeout(saveTimer); if (!readOnly) flush(); });

  function restore(body: string) {
    const r = parseCanvas(body);
    canvas = r.canvas; selected = new Set(); selectedEdge = null;
    lastSaved = '';
    flush();
  }
  function doUndo() { const prev = undo.pop(); if (prev === undefined) return; redo.push(serializeCanvas(doc.body, canvas)); restore(prev); }
  function doRedo() { const next = redo.pop(); if (next === undefined) return; undo.push(serializeCanvas(doc.body, canvas)); restore(next); }

  async function loadNote(id: string) {
    if (id in notes) return;
    notes[id] = null;
    try {
      const n = await api.get<NodeDto>(`nodes/${id}`);
      notes[id] = { title: n.title, html: renderMarkdown(n.body.slice(0, 900), { links: false }) };
    } catch { notes[id] = { title: app.titleIndex.get(id)?.title ?? 'Missing note', html: '<em>This note isn’t in the vault any more.</em>' }; }
  }

  // ---- coordinates -----------------------------------------------------------------------------
  function world(e: { clientX: number; clientY: number }) {
    const r = board!.getBoundingClientRect();
    return { x: (e.clientX - r.left - view.x) / view.zoom, y: (e.clientY - r.top - view.y) / view.zoom };
  }
  function centre() {
    const r = board!.getBoundingClientRect();
    return { x: (r.width / 2 - view.x) / view.zoom, y: (r.height / 2 - view.y) / view.zoom };
  }
  function fit() {
    fitted = true;
    const b = bounds(canvas.nodes);
    if (!b || !board) return;
    const r = board.getBoundingClientRect();
    const zoom = Math.min(1.5, Math.max(0.2, Math.min((r.width - 80) / b.width, (r.height - 80) / b.height)));
    view = { zoom, x: (r.width - b.width * zoom) / 2 - b.x * zoom, y: (r.height - b.height * zoom) / 2 - b.y * zoom };
  }
  function zoomBy(f: number, at?: { clientX: number; clientY: number }) {
    const r = board!.getBoundingClientRect();
    const px = at ? at.clientX - r.left : r.width / 2, py = at ? at.clientY - r.top : r.height / 2;
    const zoom = Math.min(3, Math.max(0.15, view.zoom * f));
    view = { zoom, x: px - ((px - view.x) / view.zoom) * zoom, y: py - ((py - view.y) / view.zoom) * zoom };
  }

  // ---- cards -----------------------------------------------------------------------------------
  const byId = $derived(new Map(canvas.nodes.map((n) => [n.id, n])));
  // Groups first, so they sit behind the cards on them.
  const ordered = $derived([...canvas.nodes].sort((a, b) => Number(b.type === 'group') - Number(a.type === 'group')));

  function add(n: Omit<CanvasNode, 'id'>, edit = false) {
    if (readOnly) return;
    const card = { id: newId(), ...n };
    canvas.nodes.push(card);
    selected = new Set([card.id]); selectedEdge = null;
    if (edit) editing = card.id;
    commit();
    return card;
  }
  function addText(at = centre()) { add({ type: 'text', x: at.x - 120, y: at.y - 50, width: 240, height: 100, text: '' }, true); }
  async function addNote(at = centre()) {
    const n = await app.pickNote('Put a note on the whiteboard', { confirmLabel: 'Add' });
    if (!n) return;
    add({ type: 'file', x: at.x - 140, y: at.y - 90, width: 280, height: 180, node: n.id, file: n.path });
    loadNote(n.id);
  }
  async function addLink(at = centre()) {
    const url = await app.ask('Add a web link', { placeholder: 'https://…', confirmLabel: 'Add' });
    if (!url?.trim()) return;
    if (!/^https?:\/\//i.test(url.trim())) { app.toast('Links start with http:// or https://', 'error'); return; }
    add({ type: 'link', x: at.x - 140, y: at.y - 40, width: 280, height: 80, url: url.trim() });
  }
  function addGroup(at = centre()) {
    const sel = canvas.nodes.filter((n) => selected.has(n.id) && n.type !== 'group');
    const b = bounds(sel);
    if (b) add({ type: 'group', x: b.x - 30, y: b.y - 50, width: b.width + 60, height: b.height + 80, label: 'Group' });
    else add({ type: 'group', x: at.x - 250, y: at.y - 170, width: 500, height: 340, label: 'Group' });
  }
  function remove() {
    if (readOnly) return;
    if (selectedEdge) { canvas.edges = canvas.edges.filter((e) => e.id !== selectedEdge); selectedEdge = null; commit(); return; }
    if (!selected.size) return;
    canvas.nodes = canvas.nodes.filter((n) => !selected.has(n.id));
    canvas.edges = canvas.edges.filter((e) => !selected.has(e.fromNode) && !selected.has(e.toNode));
    selected = new Set();
    commit();
  }
  function setColor(c: string | undefined) {
    if (readOnly) return;
    for (const n of canvas.nodes) if (selected.has(n.id)) n.color = c;
    for (const e of canvas.edges) if (e.id === selectedEdge) e.color = c;
    commit();
  }
  function duplicate() {
    if (readOnly || !selected.size) return;
    const copies = canvas.nodes.filter((n) => selected.has(n.id)).map((n) => ({ ...n, id: newId(), x: n.x + 30, y: n.y + 30 }));
    canvas.nodes.push(...copies);
    selected = new Set(copies.map((c) => c.id));
    commit();
  }

  // ---- pointer gestures ------------------------------------------------------------------------
  function downBoard(e: PointerEvent) {
    if (e.button !== 0 && e.button !== 1) return;
    board!.setPointerCapture(e.pointerId);
    editing = null; selectedEdge = null;
    if (e.shiftKey && e.button === 0) { const p = world(e); gesture = { kind: 'select', x0: p.x, y0: p.y }; box = { x: p.x, y: p.y, w: 0, h: 0 }; return; }
    selected = new Set();
    gesture = { kind: 'pan', sx: e.clientX, sy: e.clientY, vx: view.x, vy: view.y };
  }
  function downCard(e: PointerEvent, n: CanvasNode) {
    if (e.button !== 0 || editing === n.id) return;
    e.stopPropagation();
    board!.setPointerCapture(e.pointerId);
    selectedEdge = null;
    if (e.shiftKey) { const s = new Set(selected); s.has(n.id) ? s.delete(n.id) : s.add(n.id); selected = s; }
    else if (!selected.has(n.id)) selected = new Set([n.id]);
    if (readOnly) return;
    // A group carries the cards sitting on it.
    const moving = new Set(selected);
    for (const id of selected) { const g = byId.get(id); if (g?.type === 'group') for (const c of canvas.nodes) if (c.id !== g.id && contained(g, c)) moving.add(c.id); }
    gesture = { kind: 'move', sx: e.clientX, sy: e.clientY, moved: false, start: new Map([...moving].map((id) => [id, { x: byId.get(id)!.x, y: byId.get(id)!.y }])) };
  }
  function downResize(e: PointerEvent, n: CanvasNode) {
    e.stopPropagation();
    board!.setPointerCapture(e.pointerId);
    gesture = { kind: 'resize', id: n.id, sx: e.clientX, sy: e.clientY, w: n.width, h: n.height };
  }
  function downLink(e: PointerEvent, n: CanvasNode, side: Side) {
    e.stopPropagation();
    board!.setPointerCapture(e.pointerId);
    const p = world(e);
    linking = { from: n.id, side, x: p.x, y: p.y };
  }
  function moveBoard(e: PointerEvent) {
    if (linking) { const p = world(e); linking.x = p.x; linking.y = p.y; return; }
    const g = gesture;
    if (!g) return;
    if (g.kind === 'pan') view = { ...view, x: g.vx + e.clientX - g.sx, y: g.vy + e.clientY - g.sy };
    else if (g.kind === 'move') {
      const dx = (e.clientX - g.sx) / view.zoom, dy = (e.clientY - g.sy) / view.zoom;
      if (Math.abs(dx) + Math.abs(dy) > 2) g.moved = true;
      for (const [id, s] of g.start) { const n = byId.get(id); if (n) { n.x = s.x + dx; n.y = s.y + dy; } }
    } else if (g.kind === 'resize') {
      const n = byId.get(g.id);
      if (n) { n.width = Math.max(80, g.w + (e.clientX - g.sx) / view.zoom); n.height = Math.max(40, g.h + (e.clientY - g.sy) / view.zoom); }
    } else if (g.kind === 'select') {
      const p = world(e);
      box = { x: Math.min(g.x0, p.x), y: Math.min(g.y0, p.y), w: Math.abs(p.x - g.x0), h: Math.abs(p.y - g.y0) };
    }
  }
  function upBoard(e: PointerEvent) {
    if (linking) {
      const target = (document.elementFromPoint(e.clientX, e.clientY) as HTMLElement | null)?.closest<HTMLElement>('[data-card]')?.dataset.card;
      const from = byId.get(linking.from), to = target ? byId.get(target) : undefined;
      if (from && to && to.id !== from.id && !readOnly) {
        canvas.edges.push({ id: newId(), fromNode: from.id, toNode: to.id, fromSide: linking.side, toSide: facing(from, to)[1], toEnd: 'arrow' });
        commit();
      } else if (from && !to && !readOnly) {
        // Dropped on empty board: a new card, already connected.
        const p = world(e);
        const card = add({ type: 'text', x: p.x - 110, y: p.y - 40, width: 220, height: 80, text: '' }, true)!;
        canvas.edges.push({ id: newId(), fromNode: from.id, toNode: card.id, fromSide: linking.side, toSide: facing(from, card)[1], toEnd: 'arrow' });
      }
      linking = null;
      return;
    }
    const g = gesture;
    gesture = null;
    if (!g) return;
    if (g.kind === 'move' && g.moved) commit();
    if (g.kind === 'resize') commit();
    if (g.kind === 'select' && box) {
      const b = { x: box.x, y: box.y, width: box.w, height: box.h };
      selected = new Set(canvas.nodes.filter((n) => contained(b, n)).map((n) => n.id));
      box = null;
    }
  }
  function wheel(e: WheelEvent) {
    e.preventDefault();
    if (e.ctrlKey || e.metaKey) zoomBy(Math.exp(-e.deltaY * 0.0015), e);
    else view = { ...view, x: view.x - e.deltaX, y: view.y - e.deltaY };
  }
  function dblBoard(e: MouseEvent) { if (!readOnly && e.target === e.currentTarget) addText(world(e)); }

  function open(n: CanvasNode, e?: MouseEvent | KeyboardEvent) {
    if (n.type === 'file' && n.node) openNode(n.node, e && (e.ctrlKey || e.shiftKey) ? 'other' : 'new', notes[n.node]?.title);
    else if (n.type === 'link' && n.url) window.open(n.url, '_blank', 'noopener');
    else if ((n.type === 'text' || n.type === 'group') && !readOnly) { editing = n.id; tick().then(() => board?.querySelector<HTMLElement>('.card-edit')?.focus()); }
  }

  function clickRendered(e: MouseEvent) {
    const a = (e.target as HTMLElement).closest('a');
    if (!a) return;
    if (a.classList.contains('wikilink')) { e.preventDefault(); e.stopPropagation(); openByTitle(a.dataset.target ?? '', 'new'); }
  }

  async function labelEdge(edge: CanvasEdge) {
    if (readOnly) return;
    const label = await app.ask('Label this arrow', { placeholder: 'Label (empty for none)', confirmLabel: 'Save', value: edge.label ?? '' });
    if (label === null) return;
    edge.label = label.trim() || undefined;
    commit();
  }

  function keys(e: KeyboardEvent) {
    if (editing || (e.target as HTMLElement).closest('input, textarea')) return;
    const mod = e.ctrlKey || e.metaKey;
    if (mod && e.key.toLowerCase() === 'z') { e.preventDefault(); e.shiftKey ? doRedo() : doUndo(); }
    else if (mod && e.key.toLowerCase() === 'y') { e.preventDefault(); doRedo(); }
    else if (mod && e.key.toLowerCase() === 'a') { e.preventDefault(); selected = new Set(canvas.nodes.map((n) => n.id)); }
    else if (mod && e.key.toLowerCase() === 'd') { e.preventDefault(); duplicate(); }
    else if (mod && e.key === '0') { e.preventDefault(); fit(); }
    else if (e.key === '+' || e.key === '=') zoomBy(1.2);
    else if (e.key === '-') zoomBy(1 / 1.2);
    else if (e.key === 'Delete' || e.key === 'Backspace') { e.preventDefault(); remove(); }
    else if (e.key === 'Escape') { selected = new Set(); selectedEdge = null; }
    else if (e.key.startsWith('Arrow') && selected.size && !readOnly) {
      e.preventDefault();
      const step = e.shiftKey ? 1 : 10;
      const dx = e.key === 'ArrowLeft' ? -step : e.key === 'ArrowRight' ? step : 0, dy = e.key === 'ArrowUp' ? -step : e.key === 'ArrowDown' ? step : 0;
      for (const n of canvas.nodes) if (selected.has(n.id)) { n.x += dx; n.y += dy; }
      commit();
    } else if (e.key === 'Enter' && selected.size === 1) { e.preventDefault(); const n = byId.get([...selected][0]); if (n) open(n, e); }
  }

  const sides: Side[] = ['top', 'right', 'bottom', 'left'];
  const color = (c?: string) => (c ? COLORS[c] ?? c : undefined);
  const host = (u: string) => { try { return new URL(u).host; } catch { return u; } };
  const ends = (e: CanvasEdge) => {
    const a = byId.get(e.fromNode), b = byId.get(e.toNode);
    if (!a || !b) return null;
    const [fs, ts] = facing(a, b);
    const s1 = e.fromSide ?? fs, s2 = e.toSide ?? ts;
    const p1 = anchor(a, s1), p2 = anchor(b, s2);
    return { d: edgePath(p1, s1, p2, s2), mid: { x: (p1.x + p2.x) / 2, y: (p1.y + p2.y) / 2 } };
  };
</script>

<div class="wb">
  <div class="tools" role="toolbar" aria-label="Whiteboard">
    {#if !readOnly}
      <button class="btn ghost" onclick={() => addText()} title="Add a text card (or double-click the board)"><Icon name="file-text" size={14} /> Text</button>
      <button class="btn ghost" onclick={() => addNote()}><Icon name="plus" size={14} /> Note</button>
      <button class="btn ghost" onclick={() => addLink()}><Icon name="link" size={14} /> Link</button>
      <button class="btn ghost" onclick={() => addGroup()} title="Group the selected cards"><Icon name="layers" size={14} /> Group</button>
      {#if selected.size || selectedEdge}
        <span class="sep"></span>
        <span class="swatches" role="group" aria-label="Colour">
          <button class="sw none" aria-label="No colour" onclick={() => setColor(undefined)}></button>
          {#each Object.entries(COLORS) as [k, c] (k)}<button class="sw" aria-label={`Colour ${k}`} style={`background:${c}`} onclick={() => setColor(k)}></button>{/each}
        </span>
        <button class="icon-btn" title="Delete (Del)" aria-label="Delete selection" onclick={remove}><Icon name="trash" size={15} /></button>
      {/if}
    {:else}
      <span class="faint ro"><Icon name="lock" size={13} /> Read-only</span>
    {/if}
    <span class="grow"></span>
    <button class="icon-btn" aria-label="Zoom out" onclick={() => zoomBy(1 / 1.2)}>−</button>
    <span class="zoom mono">{Math.round(view.zoom * 100)}%</span>
    <button class="icon-btn" aria-label="Zoom in" onclick={() => zoomBy(1.2)}>+</button>
    <button class="btn ghost" onclick={fit} title="Fit everything (Ctrl+0)">Fit</button>
  </div>
  {#if problem}<p class="warn">The board's data couldn't be read ({problem}). Fix it in the source view; nothing here is saved until then.</p>{/if}

  <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
  <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
  <!-- svelte-ignore a11y_click_events_have_key_events -->
  <div class="board" bind:this={board} tabindex="0" role="application" aria-label="Whiteboard: drag to pan, Ctrl+scroll to zoom, double-click to add a card"
    onpointerdown={downBoard} onpointermove={moveBoard} onpointerup={upBoard} onwheel={wheel} ondblclick={dblBoard} onkeydown={keys} onclick={clickRendered}
    style={`background-position:${view.x}px ${view.y}px;background-size:${24 * view.zoom}px ${24 * view.zoom}px`}>
    <div class="world" style={`transform:translate(${view.x}px,${view.y}px) scale(${view.zoom})`}>
      <svg class="edges" aria-hidden="true">
        <defs><marker id="wb-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="8" markerHeight="8" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" fill="context-stroke" /></marker></defs>
        {#each canvas.edges as e (e.id)}
          {@const p = ends(e)}
          {#if p}
            <path d={p.d} class="hit" role="presentation" onpointerdown={(ev) => { ev.stopPropagation(); selectedEdge = e.id; selected = new Set(); }} ondblclick={(ev) => { ev.stopPropagation(); labelEdge(e); }} />
            <path d={p.d} class="line" class:on={selectedEdge === e.id} style={color(e.color) ? `stroke:${color(e.color)}` : ''} marker-end={e.toEnd === 'none' ? undefined : 'url(#wb-arrow)'} />
          {/if}
        {/each}
        {#if linking}
          {@const a = byId.get(linking.from)}
          {#if a}{@const p = anchor(a, linking.side)}<path d={edgePath(p, linking.side, { x: linking.x, y: linking.y }, 'left')} class="line pending" />{/if}
        {/if}
      </svg>
      {#each canvas.edges as e (e.id)}
        {@const p = ends(e)}
        {#if p && e.label}<span class="elabel" style={`left:${p.mid.x}px;top:${p.mid.y}px`}>{e.label}</span>{/if}
      {/each}

      {#each ordered as n (n.id)}
        <div class="card {n.type}" class:on={selected.has(n.id)} data-card={n.id} role="group" tabindex="0"
          aria-label={n.type === 'text' ? (n.text?.slice(0, 80) || 'Empty card') : n.type === 'file' ? `Note: ${notes[n.node ?? '']?.title ?? n.file}` : n.type === 'link' ? `Link: ${n.url}` : `Group: ${n.label ?? ''}`}
          style={`left:${n.x}px;top:${n.y}px;width:${n.width}px;height:${n.height}px;${color(n.color) ? `--c:${color(n.color)}` : ''}`}
          onpointerdown={(e) => downCard(e, n)} ondblclick={(e) => { e.stopPropagation(); open(n, e); }} onfocus={() => { if (!selected.has(n.id)) selected = new Set([n.id]); }}>
          {#if n.type === 'group'}
            {#if editing === n.id}<input class="card-edit glabel" bind:value={n.label} onblur={() => { editing = null; commit(); }} onkeydown={(e) => { if (e.key === 'Enter' || e.key === 'Escape') (e.target as HTMLElement).blur(); }} />
            {:else}<span class="glabel">{n.label || 'Group'}</span>{/if}
          {:else if n.type === 'text'}
            {#if editing === n.id}
              <textarea class="card-edit" bind:value={n.text} onblur={() => { editing = null; commit(); }} onkeydown={(e) => { if (e.key === 'Escape') (e.target as HTMLElement).blur(); }} placeholder="Write here. Markdown works."></textarea>
            {:else}
              <div class="md rendered">{#if n.text}{@html renderMarkdown(n.text)}{:else}<span class="faint">Double-click to write</span>{/if}</div>
            {/if}
          {:else if n.type === 'file'}
            {@const note = n.node ? notes[n.node] : null}
            <div class="ftitle"><Icon name="file-text" size={13} /> {note?.title ?? app.titleIndex.get(n.node ?? '')?.title ?? n.file ?? 'Note'}</div>
            <div class="md rendered">{#if note}{@html note.html}{:else}<span class="faint">Loading…</span>{/if}</div>
          {:else}
            <div class="ftitle"><Icon name="link" size={13} /> {host(n.url ?? '')}</div>
            <div class="url faint">{n.url}</div>
          {/if}
          {#if !readOnly && selected.has(n.id) && selected.size === 1}
            {#each sides as s (s)}<span class="handle {s}" role="presentation" title="Drag to connect" onpointerdown={(e) => downLink(e, n, s)}></span>{/each}
            <span class="resize" role="presentation" onpointerdown={(e) => downResize(e, n)}></span>
          {/if}
        </div>
      {/each}
      {#if box}<div class="lasso" style={`left:${box.x}px;top:${box.y}px;width:${box.w}px;height:${box.h}px`}></div>{/if}
    </div>
    {#if !canvas.nodes.length}
      <div class="hint faint">{readOnly ? 'This whiteboard is empty.' : 'Double-click anywhere to add a card, or use the buttons above. Drag from a card’s edge dot to connect it.'}</div>
    {/if}
  </div>
</div>

<style>
  .wb { display: flex; flex-direction: column; gap: 8px; height: max(420px, calc(100cqh - 250px)); }
  .tools { display: flex; align-items: center; gap: 4px; flex-wrap: wrap; }
  .sep { width: 1px; height: 20px; background: var(--line); margin: 0 4px; }
  .grow { flex: 1; }
  .ro { display: inline-flex; gap: 5px; align-items: center; font-size: 12.5px; }
  .zoom { width: 44px; text-align: center; font-size: 12px; color: var(--text-muted); }
  .mono { font-family: var(--font-mono); }
  .swatches { display: inline-flex; gap: 3px; }
  .sw { width: 16px; height: 16px; border-radius: 50%; border: 1px solid var(--line-strong); cursor: pointer; padding: 0; }
  .sw.none { background: linear-gradient(135deg, transparent 45%, var(--danger) 45% 55%, transparent 55%), var(--bg-raised); }
  .warn { color: var(--warn); font-size: 13px; margin: 0; }
  .board { position: relative; flex: 1; overflow: hidden; border: 1px solid var(--line); border-radius: 10px; cursor: grab; touch-action: none; outline: none;
    background-color: var(--bg); background-image: radial-gradient(circle, color-mix(in srgb, var(--text) 14%, transparent) 1px, transparent 1.2px); }
  .board:focus-visible { border-color: var(--accent); }
  .board:active { cursor: grabbing; }
  .world { position: absolute; left: 0; top: 0; transform-origin: 0 0; }
  .edges { position: absolute; left: 0; top: 0; width: 1px; height: 1px; overflow: visible; }
  .line { fill: none; stroke: var(--text-muted); stroke-width: 2; pointer-events: none; }
  .line.on { stroke: var(--accent); stroke-width: 3; }
  .line.pending { stroke: var(--accent); stroke-dasharray: 6 4; }
  .hit { fill: none; stroke: transparent; stroke-width: 14; pointer-events: stroke; cursor: pointer; }
  .elabel { position: absolute; transform: translate(-50%, -50%); background: var(--bg); color: var(--text-soft); font-size: 12px; padding: 1px 6px; border-radius: 4px; white-space: nowrap; pointer-events: none; }
  .card { position: absolute; border-radius: 10px; background: var(--bg-raised); border: 1.5px solid var(--c, var(--line-strong)); box-shadow: 0 2px 10px rgb(0 0 0 / .25); cursor: default; display: flex; flex-direction: column; overflow: visible; color: var(--text); }
  .card.on { outline: 2px solid var(--accent); outline-offset: 2px; }
  .card:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
  .card.group { background: color-mix(in srgb, var(--c, var(--text-faint)) 7%, transparent); border-style: dashed; box-shadow: none; }
  .card.text { background: color-mix(in srgb, var(--c, transparent) 12%, var(--bg-raised)); }
  .glabel { position: absolute; top: -26px; left: 0; font: 700 14px var(--font-display); color: var(--c, var(--text-soft)); background: none; border: 0; padding: 0; }
  input.glabel { border-bottom: 1px solid var(--accent); color: var(--text); outline: none; }
  .md { padding: 10px 12px; overflow: hidden; font-size: 14px; line-height: 1.5; flex: 1; min-height: 0; }
  .md :global(p) { margin: 0 0 .5em; }
  .md :global(h1), .md :global(h2), .md :global(h3) { font: 700 1.1em/1.25 var(--font-display); margin: 0 0 .4em; }
  .md :global(ul), .md :global(ol) { margin: 0 0 .5em; padding-left: 1.2em; }
  .card-edit { flex: 1; width: 100%; resize: none; border: 0; background: var(--bg-input); color: var(--text); font: 14px/1.5 var(--font-body); padding: 10px 12px; border-radius: 9px; outline: none; }
  .ftitle { display: flex; align-items: center; gap: 6px; padding: 8px 12px 0; font: 600 13.5px var(--font-body); color: var(--accent); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .file .md { font-size: 12.5px; color: var(--text-soft); -webkit-mask-image: linear-gradient(#000 70%, transparent); mask-image: linear-gradient(#000 70%, transparent); }
  .url { padding: 4px 12px; font: 12px var(--font-mono); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .handle { position: absolute; width: 12px; height: 12px; border-radius: 50%; background: var(--accent); border: 2px solid var(--bg); cursor: crosshair; z-index: 2; }
  .handle.top { top: -7px; left: calc(50% - 6px); }
  .handle.bottom { bottom: -7px; left: calc(50% - 6px); }
  .handle.left { left: -7px; top: calc(50% - 6px); }
  .handle.right { right: -7px; top: calc(50% - 6px); }
  .resize { position: absolute; right: -2px; bottom: -2px; width: 14px; height: 14px; cursor: nwse-resize; background: linear-gradient(135deg, transparent 50%, var(--accent) 50%); border-radius: 0 0 9px 0; }
  .lasso { position: absolute; border: 1px dashed var(--accent); background: color-mix(in srgb, var(--accent) 10%, transparent); pointer-events: none; }
  .hint { position: absolute; inset: 0; display: grid; place-items: center; text-align: center; padding: 20px; pointer-events: none; font-size: 14px; }
</style>
