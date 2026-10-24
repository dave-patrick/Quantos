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
  import MyDayView from './views/MyDayView.svelte';
  import { isoDate } from '../lib/dates';
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
    { key: 'quickadd', icon: 'plus', label: 'Quick add' },
    { key: 'myday', icon: 'sun', label: 'My Day' }
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

  // Drag the grip between two cards to divide their combined height.
  let box = $state<HTMLElement | null>(null);
  let resizing = $state<{ index: number; startY: number; upper: number; lower: number } | null>(null);
  function down(e: PointerEvent, index: number) {
    resizing = { index, startY: e.clientY, upper: app.rightHeights[index] ?? 1, lower: app.rightHeights[index + 1] ?? 1 };
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
  }
  function move(e: PointerEvent) {
    if (!resizing || !box) return;
    const weights = app.rightHeights.reduce((sum, value) => sum + value, 0);
    const availableHeight = box.clientHeight - 8 - 8 * (app.rightPanels.length - 1);
    if (availableHeight <= 0 || weights <= 0) return;
    const pair = resizing.upper + resizing.lower;
    const delta = (e.clientY - resizing.startY) * weights / availableHeight;
    const upper = Math.min(pair * 0.9, Math.max(pair * 0.1, resizing.upper + delta));
    const next = [...app.rightHeights];
    next[resizing.index] = upper;
    next[resizing.index + 1] = pair - upper;
    app.rightHeights = next;
  }
  function up() { resizing = null; }
  function resizeByKey(e: KeyboardEvent, index: number) {
    const fraction = e.key === 'ArrowUp' ? -0.05 : e.key === 'ArrowDown' ? 0.05 : 0;
    if (!fraction) return;
    e.preventDefault();
    const upper = app.rightHeights[index] ?? 1;
    const lower = app.rightHeights[index + 1] ?? 1;
    const pair = upper + lower;
    const nextUpper = Math.min(pair * 0.9, Math.max(pair * 0.1, upper + pair * fraction));
    const next = [...app.rightHeights];
    next[index] = nextUpper;
    next[index + 1] = pair - nextUpper;
    app.rightHeights = next;
  }

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
        {:else if tab === 'myday'}<MyDayView today={isoDate()} docked />
        {:else}<NotificationsPanel />{/if}
      </div>
      {#if i === app.rightPanels.length - 1 && available.length}
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
    </section>
    {#if i < app.rightPanels.length - 1}
      <button class="divider" type="button" aria-label="Resize {typeOf(tab).label} and {typeOf(app.rightPanels[i + 1]).label} panels"
        title="Drag to resize adjacent panels" onpointerdown={(e) => down(e, i)} onpointermove={move} onpointerup={up} onpointercancel={up} onkeydown={(e) => resizeByKey(e, i)}></button>
    {/if}
  {/each}
</aside>

<style>
  .rightbar { grid-area: right; display: flex; flex-direction: column; min-height: 0; padding: 0 8px 8px 0; background: transparent; }
  .part { display: flex; flex-direction: column; min-height: 60px; overflow: hidden; border: 1px solid var(--line); border-radius: 12px; background: var(--bg-panel); }
  .part.dragging { opacity: .6; }
  .head { display: flex; align-items: center; gap: 6px; padding: 4px 8px; border-bottom: 1px solid var(--line); flex: none; background: var(--bg-panel); cursor: grab; user-select: none; }
  .head:active { cursor: grabbing; }
  .grip { color: var(--text-faint); font-size: 10px; letter-spacing: -1px; margin-right: 2px; }
  .title { font: 600 12.5px var(--font-body); color: var(--text-soft); }
  .pip { min-width: 14px; height: 14px; padding: 0 3px; border-radius: 7px; background: var(--accent); color: var(--accent-ink); font: 700 9px/14px var(--font-mono); margin-left: 2px; }
  .close { margin-left: auto; border: 0; background: none; color: var(--text-faint); padding: 2px 4px; border-radius: 4px; cursor: pointer; }
  .close:hover { color: var(--danger); background: var(--bg-hover); }
  .panel { flex: 1; min-height: 0; display: flex; flex-direction: column; overflow: hidden; }
  .quick { padding: 8px; }
  .divider { flex: none; height: 8px; display: grid; place-items: center; padding: 0; border: 0; background: transparent; cursor: row-resize; touch-action: none; outline: none; }
  .divider::after { content: ''; width: 34px; height: 2px; border-radius: 2px; background: var(--line-strong); opacity: 0; transition: opacity .12s ease; }
  .divider:hover::after, .divider:focus-visible::after { opacity: 1; background: var(--text-faint); }
  .addwrap { flex: none; padding: 6px; position: relative; }
  .addbtn { width: 100%; height: 30px; display: flex; align-items: center; justify-content: center; gap: 6px; border: 1px dashed var(--line-strong); border-radius: 6px; background: none; color: var(--text-faint); cursor: pointer; font-size: 12.5px; }
  .addbtn:hover { color: var(--text); background: var(--bg-hover); border-style: solid; }
  .addmenu { position: absolute; bottom: 42px; left: 6px; right: 6px; background: var(--bg-raised); border: 1px solid var(--line-strong); border-radius: 8px; box-shadow: var(--shadow); padding: 4px; display: grid; gap: 2px; z-index: 5; }
  .addmenu button { display: flex; align-items: center; gap: 8px; text-align: left; padding: 7px 10px; border: 0; border-radius: 5px; background: none; color: var(--text-soft); font-size: 13px; cursor: pointer; }
  .addmenu button:hover { background: var(--bg-hover); color: var(--text); }
</style>
