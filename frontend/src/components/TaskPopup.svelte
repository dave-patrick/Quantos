<script lang="ts">
  import { onMount, tick, untrack } from 'svelte';
  import { taskPopup } from '../lib/taskPopup.svelte';
  import { docs } from '../lib/docs.svelte';
  import { layout, tabKey, type Tab } from '../lib/layout.svelte';
  import { app } from '../lib/state.svelte';
  import { tasks } from '../lib/tasks.svelte';
  import { modal } from '../lib/modal';
  import NodeView from './views/NodeView.svelte';
  import Icon from './Icon.svelte';
  let { id }: { id: string } = $props();
  const tab: Tab = untrack(() => ({ kind: 'node', nodeId: id, key: tabKey({ kind: 'node', nodeId: id }), title: 'Task', pinned: false, scroll: 0, lastActive: 0 }));
  let box: HTMLDialogElement;
  let closing = $state(false);
  async function close() {
    if (closing) return;
    closing = true;
    try {
      await docs.flushAll();
      const doc = docs.get(id);
      await doc.flush();
      if (doc.dirty || doc.conflict || doc.status === 'error') { app.toast('Resolve the task’s unsaved changes before closing.', 'warn'); return; }
      await tasks.load();
      await docs.refreshOpen();
      const returnTo = taskPopup.returnTo;
      taskPopup.id = null;
      taskPopup.returnTo = null;
      await tick();
      returnTo?.();
      requestAnimationFrame(() => returnTo?.());
    } catch (e) { app.error(e); }
    finally { closing = false; }
  }
  onMount(() => { box.showModal(); tick().then(() => box.querySelector<HTMLButtonElement>('.close')?.focus()); });
</script>
<dialog bind:this={box} class="task-popup" aria-label="Edit task" use:modal={{ onclose: close }} oncancel={(e) => { e.preventDefault(); close(); }}>
  <header><span>Edit task</span><button class="btn ghost" onclick={async () => { await close(); if (!taskPopup.id) layout.open({ kind: 'supertags', arg: 'task', title: '#task' }); }}>Edit #task properties</button><button class="icon-btn close" aria-label="Close task" disabled={closing} onclick={close}><Icon name="x" /></button></header>
  <NodeView {id} {tab} paneIndex={layout.activePane} popup />
</dialog>
<style>
  .task-popup { position: fixed; inset: 0; margin: auto; width: min(920px, calc(100% - 32px)); height: min(860px, calc(100dvh - 32px)); max-width: none; max-height: none; padding: 0; background: var(--bg-panel); color: var(--text); border: 1px solid var(--line-strong); border-radius: 12px; overflow: hidden; }
  .task-popup[open] { display: flex; flex-direction: column; }
  .task-popup::backdrop { background: rgb(4 8 14 / .6); }
  header { display: flex; align-items: center; gap: 8px; padding: 8px 12px; border-bottom: 1px solid var(--line); flex: none; }
  header span { margin-right: auto; font-weight: 600; }
  .close { min-width: 44px; min-height: 44px; }
  @media (max-width: 600px) { .task-popup { width: 100%; height: 100dvh; border-radius: 0; } }
</style>
