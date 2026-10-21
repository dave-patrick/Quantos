<script lang="ts">
  import { tick } from 'svelte';
  import { menu as menuKeys } from '../lib/menu';
  import { layout, type Pane, type Tab } from '../lib/layout.svelte';
  import { docs } from '../lib/docs.svelte';
  import Icon from './Icon.svelte';
  import TabView from './TabView.svelte';

  let { pane, index, today }: { pane: Pane; index: number; today: string } = $props();

  const active = $derived(pane.tabs.find((t) => t.key === pane.active) ?? null);
  const focused = $derived(layout.activePane === index);
  let menu = $state<{ tab: Tab; x: number; y: number } | null>(null);
  let dragKey: string | null = null;

  function dirty(tab: Tab) {
    if (!tab.nodeId) return false;
    const d = docs.peek(tab.nodeId);
    return !!d && (d.dirty || d.status === 'saving' || d.status === 'conflict');
  }

  function onAux(e: MouseEvent, tab: Tab) {
    if (e.button === 1) { e.preventDefault(); layout.close(index, tab.key); }
  }

  function drop(e: DragEvent, target: Tab) {
    e.preventDefault();
    if (!dragKey || dragKey === target.key) return;
    const from = pane.tabs.findIndex((t) => t.key === dragKey);
    const to = pane.tabs.findIndex((t) => t.key === target.key);
    if (from < 0 || to < 0 || pane.tabs[from].pinned !== pane.tabs[to].pinned) return;
    const [moved] = pane.tabs.splice(from, 1);
    pane.tabs.splice(to, 0, moved);
    layout.persist();
  }
  /** Tab strip keys: arrows move between tabs, Home/End jump, Delete closes, Shift+F10 opens the menu. */
  function tabKeys(e: KeyboardEvent, tab: Tab) {
    const keys = pane.tabs.map((t) => t.key);
    const i = keys.indexOf(tab.key);
    const strip = (e.currentTarget as HTMLElement).closest('[role=tablist]');
    const focusTab = (k: string | undefined) => {
      if (!k) return;
      e.preventDefault();
      layout.activate(index, k);
      tick().then(() => strip?.querySelector<HTMLElement>('[role=tab][aria-selected=true]')?.focus());
    };
    if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); layout.activate(index, tab.key); }
    else if (e.key === 'ArrowRight') focusTab(keys[(i + 1) % keys.length]);
    else if (e.key === 'ArrowLeft') focusTab(keys[(i - 1 + keys.length) % keys.length]);
    else if (e.key === 'Home') focusTab(keys[0]);
    else if (e.key === 'End') focusTab(keys[keys.length - 1]);
    else if (e.key === 'Delete' && !tab.pinned) { e.preventDefault(); layout.close(index, tab.key); }
    else if (e.key === 'ContextMenu' || (e.key === 'F10' && e.shiftKey)) {
      e.preventDefault();
      const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
      menu = { tab, x: r.left, y: r.bottom };
    }
  }
</script>

<!-- Tracks which pane is active; focus changes already cover keyboard users. -->
<!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
<section class="pane" class:focused onfocusin={() => (layout.activePane = index)} onmousedown={() => (layout.activePane = index)} aria-label="Pane {index + 1}">
  <div class="tabs">
    <div class="strip" role="tablist" aria-label="Open tabs">
    {#each pane.tabs as tab (tab.key)}
      <!-- The tab is a button; the close button sits beside it, not inside (one control can't contain another). -->
      <div class="tab" class:active={tab.key === pane.active} class:pinned={tab.pinned} role="presentation" draggable="true"
        ondragstart={() => (dragKey = tab.key)} ondragover={(e) => e.preventDefault()} ondrop={(e) => drop(e, tab)}
        onauxclick={(e) => onAux(e, tab)}
        oncontextmenu={(e) => { e.preventDefault(); menu = { tab, x: e.clientX, y: e.clientY }; }}>
        <button class="tab-button" role="tab" tabindex={tab.key === pane.active ? 0 : -1} aria-selected={tab.key === pane.active}
          onclick={() => layout.activate(index, tab.key)} onkeydown={(e) => tabKeys(e, tab)} title={tab.title}>
          {#if tab.pinned}<Icon name="pin" size={12} />{/if}
          <span class="title">{tab.title}</span>
          {#if dirty(tab)}<span class="dot" title="Unsaved changes"></span>{/if}
        </button>
        {#if !tab.pinned}
          <button class="close" tabindex="-1" aria-hidden="true" title="Close (Delete)" onclick={(e) => { e.stopPropagation(); layout.close(index, tab.key); }}><Icon name="x" size={12} /></button>
        {/if}
      </div>
    {/each}
    </div>
    <div class="spacer"></div>
    <span class="count" title="Tabs in this pane (limit 10)">{pane.tabs.length}/10</span>
    {#if layout.panes.length > 1}
      <button class="icon-btn" aria-label="Close this pane" onclick={() => layout.closePane(index)}><Icon name="x" size={14} /></button>
    {/if}
  </div>

  <div class="content">
    {#if active}
      {#key active.key}
        <TabView tab={active} paneIndex={index} {today} />
      {/key}
    {:else}
      <div class="empty-state">
        <h3>No tab open</h3>
        <p>Open something from the sidebar, or press <kbd>Ctrl</kbd> <kbd>P</kbd> to jump to a note.</p>
      </div>
    {/if}
  </div>
</section>

{#if menu}
  <button class="backdrop" aria-label="Close menu" onclick={() => (menu = null)}></button>
  <div class="menu" style="left:{menu.x}px; top:{menu.y}px" role="menu" tabindex="-1" use:menuKeys={{ onclose: () => (menu = null) }}>
    <button role="menuitem" onclick={() => { layout.togglePin(index, menu!.tab.key); menu = null; }}>{menu.tab.pinned ? 'Unpin tab' : 'Pin tab'}</button>
    <button role="menuitem" onclick={() => { const t = menu!.tab; menu = null; layout.open({ kind: t.kind, nodeId: t.nodeId, arg: t.arg, title: t.title }, 'other'); }}>Open in other pane</button>
    <button role="menuitem" onclick={() => { const t = menu!.tab; menu = null; layout.moveToLocation(index, t.key, 'left'); }}>Move to left</button>
    <button role="menuitem" onclick={() => { const t = menu!.tab; menu = null; layout.moveToLocation(index, t.key, 'right'); }}>Move to right</button>
    <button role="menuitem" onclick={() => { const t = menu!.tab; menu = null; layout.moveToLocation(index, t.key, 'top'); }}>Move to top</button>
    <button role="menuitem" onclick={() => { const t = menu!.tab; menu = null; layout.moveToLocation(index, t.key, 'bottom'); }}>Move to bottom</button>
    <button role="menuitem" onclick={() => { layout.closeOthers(index, menu!.tab.key); menu = null; }}>Close other tabs</button>
    <button role="menuitem" onclick={() => { layout.close(index, menu!.tab.key); menu = null; }}>Close tab</button>
  </div>
{/if}

<style>
  .pane { display: flex; flex-direction: column; min-width: 0; min-height: 0; }
  .tabs { display: flex; align-items: stretch; height: var(--tab-h); background: var(--bg-raised); border-bottom: 1px solid var(--line); overflow-x: auto; scrollbar-width: none; flex: none; }
  .tabs::-webkit-scrollbar { display: none; }
  .strip { display: flex; align-items: stretch; min-width: 0; }
  .tab { display: flex; align-items: center; gap: 6px; max-width: 210px; min-width: 90px; padding: 0 6px 0 12px; border-right: 1px solid var(--line);
    color: var(--text-muted); font-size: 13px; cursor: default; user-select: none; position: relative; }
  .tab:hover { background: var(--bg-hover); color: var(--text-soft); }
  .tab.active { background: var(--bg); color: var(--text); }
  .pane.focused .tab.active::before { content: ''; position: absolute; left: 0; right: 0; top: 0; height: 2px; background: var(--accent); }
  .tab.pinned { min-width: 0; max-width: 150px; padding-right: 10px; }
  .tab-button { display: flex; align-items: center; gap: 6px; flex: 1; min-width: 0; height: 100%; padding: 0; border: 0; background: none; color: inherit; font: inherit; text-align: left; cursor: default; }
  .tab-button:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; border-radius: 3px; }
  .title { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .dot { width: 7px; height: 7px; border-radius: 50%; background: var(--accent); flex: none; }
  .close { display: grid; place-items: center; width: 20px; height: 20px; border-radius: 4px; border: 0; background: none; color: var(--text-faint); opacity: 0; }
  .tab:hover .close, .tab.active .close { opacity: 1; }
  .close:hover { background: var(--bg-active); color: var(--text); }
  .spacer { flex: 1; }
  .count { align-self: center; padding: 0 8px; font: 11px var(--font-mono); color: var(--text-faint); }
  .tabs .icon-btn { align-self: center; margin-right: 4px; }
  /* Views size themselves to the pane, not the window: a split pane is often half as wide. */  
  .content { flex: 1; min-height: 0; display: flex; flex-direction: column; container: pane / inline-size; }
  .backdrop { position: fixed; inset: 0; border: 0; background: transparent; z-index: 45; cursor: default; }
  .menu { position: fixed; z-index: 46; min-width: 190px; padding: 5px; border-radius: 8px; background: var(--bg-raised); border: 1px solid var(--line-strong); box-shadow: var(--shadow); display: grid; }
  .menu button { text-align: left; padding: 7px 10px; border: 0; border-radius: 5px; background: none; color: var(--text-soft); font-size: 13.5px; }
  .menu button:hover { background: var(--bg-hover); color: var(--text); }
</style>
