<script lang="ts">
  import { app, type RightTab } from '../lib/state.svelte';
  import { notifications } from '../lib/notifications.svelte';
  import FileTree from './FileTree.svelte';
  import LinksPanel from './LinksPanel.svelte';
  import InspectorPanel from './InspectorPanel.svelte';
  import NotificationsPanel from './NotificationsPanel.svelte';
  import LocalGraph from './LocalGraph.svelte';
  import Scratchpad from './Scratchpad.svelte';
  import Icon from './Icon.svelte';

  const tabs: { key: RightTab; icon: string; label: string }[] = [
    { key: 'files', icon: 'folder', label: 'Files' },
    { key: 'inspector', icon: 'tag', label: 'Properties' },
    { key: 'links', icon: 'link', label: 'Links' },
    { key: 'graph', icon: 'graph', label: 'Graph' },
    { key: 'scratch', icon: 'edit', label: 'Scratchpad' },
    { key: 'notifications', icon: 'bell', label: 'Notifications' }
  ];

  /** The sidebar can show two panels, one above the other (e.g. the scratchpad over the file tree). */
  const split = $derived(app.rightTab2 !== null);
  const current = (which: 0 | 1) => (which === 0 ? app.rightTab : app.rightTab2 ?? 'files');
  function choose(which: 0 | 1, key: RightTab) { if (which === 0) app.rightTab = key; else app.rightTab2 = key; }
  function toggleSplit() { app.rightTab2 = split ? null : app.rightTab === 'files' ? 'scratch' : 'files'; }

  /** Arrow keys move between the panel tabs (tabs pattern); only the selected one is in the Tab order. */
  function tabKeys(e: KeyboardEvent, which: 0 | 1, i: number) {
    const next = e.key === 'ArrowRight' ? (i + 1) % tabs.length : e.key === 'ArrowLeft' ? (i - 1 + tabs.length) % tabs.length
      : e.key === 'Home' ? 0 : e.key === 'End' ? tabs.length - 1 : -1;
    if (next < 0) return;
    e.preventDefault();
    choose(which, tabs[next].key);
    const strip = (e.currentTarget as HTMLElement).parentElement;
    queueMicrotask(() => strip?.querySelectorAll<HTMLElement>('[role=tab]')[next]?.focus());
  }

  // Dragging (or arrow keys on) the divider sets how the height is shared.
  let box = $state<HTMLElement | null>(null);
  let dragging = false;
  function down(e: PointerEvent) { dragging = true; (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId); }
  function move(e: PointerEvent) {
    if (!dragging || !box) return;
    const r = box.getBoundingClientRect();
    app.rightSplit = Math.min(0.85, Math.max(0.15, (e.clientY - r.top) / r.height));
  }
  function dividerKeys(e: KeyboardEvent) {
    const step = e.key === 'ArrowUp' ? -0.05 : e.key === 'ArrowDown' ? 0.05 : 0;
    if (!step) return;
    e.preventDefault();
    app.rightSplit = Math.min(0.85, Math.max(0.15, app.rightSplit + step));
  }
</script>

{#snippet pane(which: 0 | 1)}
  <div class="tabs" role="tablist" aria-label={which === 0 ? (split ? 'Upper panel' : 'Panel') : 'Lower panel'}>
    {#each tabs as t, i (t.key)}
      <button role="tab" aria-selected={current(which) === t.key} tabindex={current(which) === t.key ? 0 : -1} aria-label={t.label} title={t.label} onkeydown={(e) => tabKeys(e, which, i)} class:on={current(which) === t.key} onclick={() => choose(which, t.key)}>
        <Icon name={t.icon} size={15} />
        {#if t.key === 'notifications' && notifications.unread}<span class="pip">{notifications.unread}</span>{/if}
      </button>
    {/each}
    {#if which === 0}
      <button class="splitbtn" title={split ? 'Show one panel' : 'Split into two panels'} aria-label={split ? 'Show one panel' : 'Split into two panels'} aria-pressed={split} onclick={toggleSplit}><Icon name={split ? 'square' : 'split-h'} size={14} /></button>
    {/if}
  </div>
  <div class="panel">
    {#if current(which) === 'files'}<FileTree />
    {:else if current(which) === 'inspector'}<InspectorPanel />
    {:else if current(which) === 'links'}<LinksPanel />
    {:else if current(which) === 'graph'}<LocalGraph />
    {:else if current(which) === 'scratch'}<Scratchpad docked />
    {:else}<NotificationsPanel />{/if}
  </div>
{/snippet}

<aside class="rightbar" aria-label="Inspector" bind:this={box}>
  {#if split}
    <section class="part" style={`flex:${app.rightSplit} 1 0`}>{@render pane(0)}</section>
    <!-- A focusable separator with a value is the standard resizable splitter. -->
    <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
    <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
    <div class="divider" role="separator" aria-orientation="horizontal" aria-label="Resize panels" aria-valuemin={15} aria-valuemax={85} aria-valuenow={Math.round(app.rightSplit * 100)} tabindex="0"
      onpointerdown={down} onpointermove={move} onpointerup={() => (dragging = false)} onkeydown={dividerKeys}></div>
    <section class="part" style={`flex:${1 - app.rightSplit} 1 0`}>{@render pane(1)}</section>
  {:else}
    <section class="part" style="flex:1 1 0">{@render pane(0)}</section>
  {/if}
</aside>

<style>
  .rightbar { grid-area: right; display: flex; flex-direction: column; min-height: 0; background: var(--bg-raised); border-left: 1px solid var(--line); }
  .part { display: flex; flex-direction: column; min-height: 80px; }
  .tabs { display: flex; gap: 2px; padding: 6px 6px 0; border-bottom: 1px solid var(--line); flex: none; }
  .tabs button { position: relative; flex: 1; display: grid; place-items: center; height: 32px; border: 0; background: none; color: var(--text-faint); border-radius: 6px 6px 0 0; }
  .tabs button:hover { color: var(--text); background: var(--bg-hover); }
  .tabs button.on { color: var(--accent); box-shadow: inset 0 -2px 0 var(--accent); }
  .tabs .splitbtn { flex: 0 0 28px; }
  .tabs .splitbtn[aria-pressed='true'] { color: var(--accent); }
  .pip { position: absolute; top: 3px; right: 8px; min-width: 15px; height: 15px; padding: 0 4px; border-radius: 8px; background: var(--accent); color: var(--accent-ink); font: 700 9.5px/15px var(--font-mono); }
  .panel { flex: 1; min-height: 0; display: flex; flex-direction: column; overflow: hidden; }
  .divider { flex: none; height: 6px; cursor: row-resize; background: var(--line); border-block: 2px solid var(--bg-raised); touch-action: none; }
  .divider:hover, .divider:focus-visible { background: var(--accent); outline: none; }
</style>
