<script lang="ts">
  import { app, type RightTab } from '../lib/state.svelte';
  import { notifications } from '../lib/notifications.svelte';
  import FileTree from './FileTree.svelte';
  import LinksPanel from './LinksPanel.svelte';
  import InspectorPanel from './InspectorPanel.svelte';
  import NotificationsPanel from './NotificationsPanel.svelte';
  import LocalGraph from './LocalGraph.svelte';
  import Scratchpad from './Scratchpad.svelte';
  import TasksPanel from './TasksPanel.svelte';
  import QuickAdd from './QuickAdd.svelte';
  import Icon from './Icon.svelte';

  // The panels that can live in the right sidebar.
  const types: { key: RightTab; icon: string; label: string }[] = [
    { key: 'files', icon: 'folder', label: 'Files' },
    { key: 'inspector', icon: 'tag', label: 'Properties' },
    { key: 'links', icon: 'link', label: 'Links' },
    { key: 'graph', icon: 'graph', label: 'Graph' },
    { key: 'scratch', icon: 'edit', label: 'Scratchpad' },
    { key: 'notifications', icon: 'bell', label: 'Notifications' },
    { key: 'tasks', icon: 'check-square', label: 'Tasks' },
    { key: 'quickadd', icon: 'plus', label: 'Quick add' }
  ];
  const typeOf = (k: RightTab) => types.find((t) => t.key === k)!;

  // Drag-to-reorder.
  let draggingIndex = $state<number | null>(null);
  function onDragStart(e: DragEvent, i: number) {
    draggingIndex = i;
    e.dataTransfer?.setData('text/plain', String(i));
    if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move';
  }
  function onDragOver(e: DragEvent, i: number) {
    e.preventDefault();
    if (draggingIndex === null || draggingIndex === i) return;
    // Reorder live so the user sees where it will land.
    const list = [...app.rightPanels];
    const [moved] = list.splice(draggingIndex, 1);
    list.splice(i, 0, moved);
    const heights = [...app.rightHeights];
    const [h] = heights.splice(draggingIndex, 1);
    heights.splice(i, 0, h);
    app.rightPanels = list;
    app.rightHeights = heights;
    draggingIndex = i;
  }
  function onDragEnd() {
    draggingIndex = null;
  }

  // Per-panel height handles: dragging a divider changes the two adjacent flex ratios.
  let box = $state<HTMLElement | null>(null);
  let resizing = $state<number | null>(null); // index of the divider (between panel i and i+1)
  function down(e: PointerEvent, i: number) {
    resizing = i;
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
  }
  function move(e: PointerEvent) {
    if (resizing === null || !box) return;
    const r = box.getBoundingClientRect();
    const total = r.height;
    const splitY = e.clientY - r.top;
    // The split point as a fraction of the total height, clamped so neither side vanishes.
    const frac = Math.min(0.9, Math.max(0.1, splitY / total));
    // Convert: panels 0..i share [0, frac], panels i+1..end share [frac, 1].
    const before = app.rightHeights.slice(0, resizing + 1).reduce((a, b) => a + b, 0);
    const after = app.rightHeights.slice(resizing + 1).reduce((a, b) => a + b, 0);
    const left = frac * (before + after);
    const right = (1 - frac) * (before + after);
    const sum = before + after;
    const next = [...app.rightHeights];
    next[resizing] = left * (next[resizing] / before);
    next[resizing + 1] = right * (next[resizing + 1] / after);
    // Renormalise so the two sides keep their internal ratios.
    const leftSum = next.slice(0, resizing + 1).reduce((a, b) => a + b, 0);
    const rightSum = next.slice(resizing + 1).reduce((a, b) => a + b, 0);
    if (leftSum > 0 && rightSum > 0) {
      for (let k = 0; k <= resizing; k++) next[k] = next[k] * (left / leftSum);
      for (let k = resizing + 1; k < next.length; k++) next[k] = next[k] * (right / rightSum);
    }
    app.rightHeights = next;
  }
  function up() { resizing = null; }

  // Add panel menu.
  let addMenu = $state(false);
  function addPanel(key: RightTab) {
    if (app.rightPanels.includes(key)) return;
    app.rightPanels = [...app.rightPanels, key];
    app.rightHeights = [...app.rightHeights, 1];
    addMenu = false;
  }
  function removePanel(i: number) {
    if (app.rightPanels.length <= 1) return;
    app.rightPanels = app.rightPanels.filter((_, j) => j !== i);
    app.rightHeights = app.rightHeights.filter((_, j) => j !== i);
  }

  const available = $derived(types.filter((t) => !app.rightPanels.includes(t.key)));
</script>

<aside class="rightbar" aria-label="Inspector" bind:this={box}>
  {#each app.rightPanels as tab, i (i + ':' + tab)}
    <section class="part" class:dragging={draggingIndex === i} style={`flex:${app.rightHeights[i] ?? 1} 1 0`}>
      <header class="head" draggable="true" ondragstart={(e) => onDragStart(e, i)} ondragover={(e) => onDragOver(e, i)} ondragend={onDragEnd}>
        <span class="grip" aria-hidden="true">⋮⋮</span>
        <Icon name={typeOf(tab).icon} size={14} />
        <span class="title">{typeOf(tab).label}</span>
        {#if tab === 'notifications' && notifications.unread}<span class="pip">{notifications.unread}</span>{/if}
        <button class="close" title="Remove this panel" aria-label="Remove {typeOf(tab).label}" onclick={() => removePanel(i)}><Icon name="x" size={12} /></button>
      </header>
      <div class="panel">
        {#if tab === 'files'}<FileTree />
        {:else if tab === 'inspector'}<InspectorPanel />
        {:else if tab === 'links'}<LinksPanel />
        {:else if tab === 'graph'}<LocalGraph />
        {:else if tab === 'scratch'}<Scratchpad docked />
        {:else if tab === 'tasks'}<TasksPanel />
        {:else if tab === 'quickadd'}<div class="quick"><QuickAdd placeholder="Add a task…" /></div>
        {:else}<NotificationsPanel />{/if}
      </div>
    </section>
    {#if i < app.rightPanels.length - 1}
      <div class="divider" role="separator" aria-orientation="horizontal" aria-label="Resize panels" tabindex="0"
        onpointerdown={(e) => down(e, i)} onpointermove={move} onpointerup={up} onpointercancel={up} onkeydown={(e) => { const s = e.key === 'ArrowUp' ? -0.05 : e.key === 'ArrowDown' ? 0.05 : 0; if (!s) return; e.preventDefault(); app.rightHeights = app.rightHeights.map((h, j) => j === i ? Math.max(0.05, h + s) : j === i + 1 ? Math.max(0.05, h - s) : h); }}></div>
    {/if}
  {/each}
  {#if available.length}
    <div class="addwrap">
      <button class="addbtn" onclick={() => (addMenu = !addMenu)} aria-expanded={addMenu} aria-haspopup="menu"><Icon name="plus" size={14} /> Add a panel</button>
      {#if addMenu}
        <div class="addmenu" role="menu">
          {#each available as t (t.key)}
            <button role="menuitem" onclick={() => addPanel(t.key)}><Icon name={t.icon} size={14} /> {t.label}</button>
          {/each}
        </div>
      {/if}
    </div>
  {/if}
</aside>

<style>
  .rightbar { grid-area: right; display: flex; flex-direction: column; min-height: 0; padding: 8px; background: var(--bg-raised); border-left: 1px solid var(--line); }
  .part { display: flex; flex-direction: column; min-height: 60px; overflow: hidden; border: 1px solid var(--line); border-radius: 10px; margin: 0 0 8px; background: var(--bg); box-shadow: 0 2px 8px rgb(0 0 0 / 0.15); }
  .part.dragging { opacity: .6; }
  .head { display: flex; align-items: center; gap: 6px; padding: 4px 8px; border-bottom: 1px solid var(--line); flex: none; background: var(--bg-raised); cursor: grab; user-select: none; }
  .head:active { cursor: grabbing; }
  .grip { color: var(--text-faint); font-size: 10px; letter-spacing: -1px; margin-right: 2px; }
  .title { font: 600 12.5px var(--font-body); color: var(--text-soft); }
  .pip { min-width: 14px; height: 14px; padding: 0 3px; border-radius: 7px; background: var(--accent); color: var(--accent-ink); font: 700 9px/14px var(--font-mono); margin-left: 2px; }
  .close { margin-left: auto; border: 0; background: none; color: var(--text-faint); padding: 2px 4px; border-radius: 4px; cursor: pointer; }
  .close:hover { color: var(--danger); background: var(--bg-hover); }
  .panel { flex: 1; min-height: 0; display: flex; flex-direction: column; overflow: hidden; }
  .quick { padding: 8px; }
  .divider { flex: none; height: 6px; cursor: row-resize; background: var(--line); border-block: 2px solid var(--bg-raised); touch-action: none; }
  .divider:hover, .divider:focus-visible { background: var(--accent); outline: none; }
  .addwrap { flex: none; padding: 6px; position: relative; }
  .addbtn { width: 100%; height: 30px; display: flex; align-items: center; justify-content: center; gap: 6px; border: 1px dashed var(--line-strong); border-radius: 6px; background: none; color: var(--text-faint); cursor: pointer; font-size: 12.5px; }
  .addbtn:hover { color: var(--text); background: var(--bg-hover); border-style: solid; }
  .addmenu { position: absolute; bottom: 42px; left: 6px; right: 6px; background: var(--bg-raised); border: 1px solid var(--line-strong); border-radius: 8px; box-shadow: var(--shadow); padding: 4px; display: grid; gap: 2px; z-index: 5; }
  .addmenu button { display: flex; align-items: center; gap: 8px; text-align: left; padding: 7px 10px; border: 0; border-radius: 5px; background: none; color: var(--text-soft); font-size: 13px; cursor: pointer; }
  .addmenu button:hover { background: var(--bg-hover); color: var(--text); }
</style>
