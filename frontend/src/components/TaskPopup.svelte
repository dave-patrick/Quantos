<script lang="ts">
  import { onMount, untrack } from 'svelte';
  import { taskPopup, closeTaskPopup } from '../lib/taskPopup.svelte';
  import { docs } from '../lib/docs.svelte';
  import { layout, tabKey, type Tab } from '../lib/layout.svelte';
  import { app } from '../lib/state.svelte';
  import { modal } from '../lib/modal';
  import NodeView from './views/NodeView.svelte';
  import Icon from './Icon.svelte';
  let { id }: { id: string } = $props();
  const tab: Tab = untrack(() => ({ kind: 'node', nodeId: id, key: tabKey({ kind: 'node', nodeId: id }), title: 'Task', pinned: false, scroll: 0, lastActive: 0 }));
  let closeButton: HTMLButtonElement;
  onMount(() => closeButton.focus({ preventScroll: true }));
  function onkeydown(event: KeyboardEvent) {
    if (event.key === 'Tab' && !event.defaultPrevented && !app.asking && !app.dialog && !(document.activeElement instanceof HTMLElement && document.activeElement.closest('.task-popup'))) {
      event.preventDefault(); closeButton.focus(); return;
    }
    if (event.key !== 'Escape' || event.defaultPrevented || app.asking || app.dialog) return;
    event.preventDefault();
    void closeTaskPopup();
  }
</script>
<svelte:window {onkeydown} />
<div class="scrim">
  <div class="task-popup" role="dialog" aria-modal="true" aria-labelledby="task-popup-title"
    inert={!!app.asking || !!app.dialog} aria-busy={taskPopup.busy} use:modal={{ onclose: () => { void closeTaskPopup(); } }}>
    <header>
      <h2 id="task-popup-title">{docs.peek(id)?.node?.type === 'note' ? 'Edit note' : 'Edit task'}</h2>
      <button class="icon-btn close" bind:this={closeButton} aria-label="Close task" disabled={taskPopup.busy} onclick={() => closeTaskPopup()}><Icon name="x" /></button>
    </header>
    {#if taskPopup.error}<p class="error" role="alert">{taskPopup.error}</p>{/if}
    <div class="editor" inert={taskPopup.busy}><NodeView {id} {tab} paneIndex={layout.activePane} popup /></div>
  </div>
</div>
<style>
  .scrim { position: fixed; inset: 0; padding: 16px; display: grid; place-items: center; background: rgb(4 8 14 / .6); z-index: 70; }
  .task-popup { width: min(920px, 100%); height: min(860px, calc(100dvh - 32px)); min-width: 0; display: flex; flex-direction: column; background: var(--bg-panel); color: var(--text); border: 1px solid var(--line-strong); border-radius: 12px; overflow: hidden; box-shadow: var(--shadow); }
  header { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 4px 12px; border-bottom: 1px solid var(--line); flex: none; }
  h2 { font: 600 16px var(--font-body); }
  .close { min-width: 48px; min-height: 48px; }
  .editor { flex: 1; min-height: 0; display: flex; flex-direction: column; }
  .error { margin: 8px 16px; color: var(--danger); font-size: 14px; }
  @media (max-width: 600px) { .scrim { padding: 0; } .task-popup { height: 100dvh; border-radius: 0; } }
</style>
