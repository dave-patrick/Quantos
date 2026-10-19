<script lang="ts">
  import { onDestroy } from 'svelte';
  import { docs } from '../../lib/docs.svelte';
  import { app } from '../../lib/state.svelte';
  import { prefs } from '../../lib/prefs.svelte';
  import { openNode } from '../../lib/links.svelte';
  import { annotate, editAnnotation, outline, removeAnnotation, type Annotation, type Branch } from '../../lib/mindmap';
  import Icon from '../Icon.svelte';

  /** Mind map (Release 1.2): a note's outline as branches, with annotations kept as footnotes. */
  let { id }: { id: string } = $props();
  // The tab is keyed by note, so the id never changes for this component.
  // svelte-ignore state_referenced_locally
  const noteId = id;
  const doc = docs.acquire(noteId);
  onDestroy(() => docs.release(noteId));

  let collapsed = $state<Record<string, boolean>>({});
  let selected = $state<string | null>(null);
  let zoom = $state(1);

  const readOnly = $derived(!!doc.node?.locked || !!doc.node?.readOnly || prefs.reading);
  const root = $derived(outline(doc.node?.title ?? '', doc.body));

  const COL = 250, ROW = 40, NOTE = 24, W = 210;
  interface Placed { b: Branch; x: number; y: number; h: number; level: number; parent: Placed | null; hidden: number }

  // A tidy tree: leaves stack down the page, parents sit beside the middle of their children.
  const placed = $derived.by(() => {
    const out: Placed[] = [];
    let cursor = 20;
    const count = (b: Branch): number => b.children.reduce((n, c) => n + 1 + count(c), 0);
    const walk = (b: Branch, level: number, parent: Placed | null): Placed => {
      const h = ROW + b.notes.length * NOTE;
      const me: Placed = { b, x: 20 + level * COL, y: 0, h, level, parent, hidden: collapsed[b.key] ? count(b) : 0 };
      out.push(me);
      const kids = collapsed[b.key] ? [] : b.children.map((c) => walk(c, level + 1, me));
      if (kids.length) {
        const top = kids[0].y, bottom = kids[kids.length - 1].y + kids[kids.length - 1].h;
        me.y = Math.max(cursor - h, (top + bottom) / 2 - h / 2);
        if (me.y + h > cursor) cursor = me.y + h + 8;
      } else { me.y = cursor; cursor += h + 8; }
      return me;
    };
    walk(root, 0, null);
    return out;
  });
  const size = $derived({ w: Math.max(...placed.map((p) => p.x + W)) + 40, h: Math.max(...placed.map((p) => p.y + p.h)) + 40 });

  const link = (p: Placed) => {
    const a = p.parent!;
    const x1 = a.x + W, y1 = a.y + ROW / 2, x2 = p.x, y2 = p.y + ROW / 2;
    const mid = (x1 + x2) / 2;
    return `M${x1},${y1} C${mid},${y1} ${mid},${y2} ${x2},${y2}`;
  };

  async function add(b: Branch) {
    if (readOnly || b.line < 0) return;
    const text = await app.ask(`Annotate “${b.text}”`, { placeholder: 'Your note on this branch', confirmLabel: 'Add' });
    if (!text?.trim()) return;
    doc.edit(annotate(doc.body, b.line, text));
  }
  async function change(n: Annotation) {
    if (readOnly) return;
    const text = await app.ask('Edit annotation', { value: n.text, placeholder: 'Empty to remove it', confirmLabel: 'Save' });
    if (text === null) return;
    if (!text.trim()) {
      if (await app.confirm('Remove this annotation?', n.text, 'Remove')) doc.edit(removeAnnotation(doc.body, n.id));
    } else doc.edit(editAnnotation(doc.body, n.id, text));
  }
  function keys(e: KeyboardEvent, b: Branch) {
    if (e.key === 'a' || e.key === 'A') { e.preventDefault(); add(b); }
    else if (e.key === ' ' && b.children.length) { e.preventDefault(); collapsed[b.key] = !collapsed[b.key]; }
  }
  function allOpen(open: boolean) {
    const next: Record<string, boolean> = {};
    if (!open) for (const p of placed) if (p.b.children.length && p.b.kind !== 'root') next[p.b.key] = true;
    collapsed = next;
  }
</script>

<div class="mm">
  <div class="tools">
    <h1><Icon name="graph" size={20} /> {doc.node?.title ?? 'Mind map'}</h1>
    <button class="btn ghost" onclick={() => allOpen(true)}>Expand all</button>
    <button class="btn ghost" onclick={() => allOpen(false)}>Collapse all</button>
    <span class="grow"></span>
    <button class="icon-btn" aria-label="Zoom out" onclick={() => (zoom = Math.max(0.4, zoom / 1.2))}>−</button>
    <span class="z mono">{Math.round(zoom * 100)}%</span>
    <button class="icon-btn" aria-label="Zoom in" onclick={() => (zoom = Math.min(2, zoom * 1.2))}>+</button>
    <button class="btn ghost" onclick={() => openNode(id, 'new', doc.node?.title)}>Open note</button>
  </div>
  <p class="faint hint">Branches come from the note's headings and list items. {readOnly ? '' : 'Select a branch and press A (or its 💬 button) to annotate it; annotations are footnotes in the note.'}</p>

  <div class="scroll stage">
    <div class="tree" style={`width:${size.w * zoom}px;height:${size.h * zoom}px`}>
      <div class="inner" style={`transform:scale(${zoom});width:${size.w}px;height:${size.h}px`}>
        <svg width={size.w} height={size.h} aria-hidden="true">
          {#each placed as p (p.b.key)}{#if p.parent}<path d={link(p)} />{/if}{/each}
        </svg>
        <div role="tree" aria-label={`Mind map of ${doc.node?.title ?? ''}`}>
          {#each placed as p (p.b.key)}
            <div class="branch {p.b.kind}" class:on={selected === p.b.key} class:done={p.b.done} style={`left:${p.x}px;top:${p.y}px;width:${W}px`}
              role="treeitem" aria-selected={selected === p.b.key} aria-expanded={p.b.children.length ? !collapsed[p.b.key] : undefined} aria-level={p.level + 1}
              tabindex="0" onclick={() => (selected = p.b.key)} onkeydown={(e) => keys(e, p.b)} onfocus={() => (selected = p.b.key)}>
              <div class="row">
                {#if p.b.done !== undefined}<span class="box" class:on={p.b.done} aria-hidden="true"></span>{/if}
                <span class="text" title={p.b.text}>{p.b.text || 'Untitled'}</span>
                {#if !readOnly && p.b.line >= 0}<button class="note-btn" title="Annotate (A)" aria-label={`Annotate ${p.b.text}`} onclick={(e) => { e.stopPropagation(); add(p.b); }}>💬</button>{/if}
              </div>
              {#each p.b.notes as n (n.id)}
                <button class="ann" title={n.text} onclick={(e) => { e.stopPropagation(); change(n); }}>{n.text.split('\n')[0]}</button>
              {/each}
              {#if p.b.children.length}
                <button class="fold" aria-label={collapsed[p.b.key] ? `Expand (${p.hidden} hidden)` : 'Collapse'} onclick={(e) => { e.stopPropagation(); collapsed[p.b.key] = !collapsed[p.b.key]; }}>{collapsed[p.b.key] ? p.hidden : '−'}</button>
              {/if}
            </div>
          {/each}
        </div>
      </div>
    </div>
    {#if !root.children.length && doc.status !== 'loading'}<p class="empty faint">This note has no headings or list items yet, so the map is just its title.</p>{/if}
  </div>
</div>

<style>
  .mm { flex: 1; display: flex; flex-direction: column; min-height: 0; padding: 14px 18px 10px; gap: 6px; }
  .tools { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
  h1 { display: flex; align-items: center; gap: 8px; font: 700 22px/1.2 var(--font-display); margin-right: 10px; }
  .grow { flex: 1; }
  .z { width: 44px; text-align: center; font-size: 12px; color: var(--text-muted); }
  .mono { font-family: var(--font-mono); }
  .hint { font-size: 12.5px; margin: 0; }
  .stage { flex: 1; border: 1px solid var(--line); border-radius: 10px; background: var(--bg); position: relative; }
  .tree { position: relative; }
  .inner { position: absolute; left: 0; top: 0; transform-origin: 0 0; }
  svg { position: absolute; left: 0; top: 0; }
  path { fill: none; stroke: var(--line-strong); stroke-width: 1.6; }
  .branch { position: absolute; border-radius: 9px; background: var(--bg-raised); border: 1px solid var(--line-strong); padding: 0 8px; cursor: pointer; outline: none; }
  .branch.root { background: var(--accent-wash); border-color: var(--accent); }
  .branch.root .text { font: 700 15px var(--font-display); color: var(--accent-strong); }
  .branch.heading .text { font-weight: 700; }
  .branch.on, .branch:focus-visible { box-shadow: 0 0 0 2px var(--accent); }
  .branch.done .text { color: var(--text-faint); text-decoration: line-through; }
  .row { display: flex; align-items: center; gap: 6px; height: 38px; }
  .text { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 13.5px; color: var(--text); }
  .box { width: 12px; height: 12px; border: 1.5px solid var(--text-faint); border-radius: 3px; flex: none; }
  .box.on { background: var(--accent); border-color: var(--accent); }
  .note-btn { border: 0; background: none; cursor: pointer; opacity: 0; font-size: 13px; padding: 2px; }
  .branch:hover .note-btn, .branch.on .note-btn, .note-btn:focus-visible { opacity: .8; }
  .ann { display: block; width: 100%; height: 20px; margin: 0 0 4px; text-align: left; border: 0; border-left: 3px solid var(--warn); border-radius: 3px; background: var(--warn-wash); color: var(--text-soft); font: 12px var(--font-body); padding: 0 6px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer; }
  .fold { position: absolute; right: -11px; top: 9px; width: 20px; height: 20px; border-radius: 50%; border: 1px solid var(--line-strong); background: var(--bg-panel); color: var(--text-muted); font: 600 11px var(--font-mono); cursor: pointer; padding: 0; }
  .empty { position: absolute; left: 20px; bottom: 14px; font-size: 13px; }
</style>
