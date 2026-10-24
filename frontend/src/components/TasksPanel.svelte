<script lang="ts">
  import { tasks, type TaskDto } from '../lib/tasks.svelte';
  import { app } from '../lib/state.svelte';
  import TaskRow from './TaskRow.svelte';
  import QuickAdd from './QuickAdd.svelte';

  // Open tasks (todo/doing/waiting) first, then the rest. Compact rows keep the sidebar readable.
  const openTasks = $derived(tasks.list.filter((t) => t.status !== 'done' && t.status !== 'cancelled').slice(0, 40));
  const doneCount = $derived(tasks.list.filter((t) => t.status === 'done' || t.status === 'cancelled').length);
</script>

<section class="panel" aria-label="Tasks">
  <div class="body">
    <QuickAdd placeholder="Add a task…" />
    {#if tasks.list.length === 0}
      <p class="faint empty">No tasks yet.</p>
    {:else}
      <div class="list">
        {#each openTasks as t (t.id)}<TaskRow task={t} compact showMyDay={false} />{/each}
        {#if doneCount}<p class="faint done">{doneCount} finished</p>{/if}
      </div>
    {/if}
  </div>
</section>

<style>
  .panel { display: flex; flex-direction: column; min-height: 0; flex: 1; background: transparent; }
  .body { flex: 1; min-height: 0; overflow: auto; padding: 8px 10px; }
  .body :global(.quick) { min-height: 38px; margin: 0 0 8px; padding: 0 10px; border-style: solid; border-color: var(--line); background: var(--bg-input); }
  .body :global(.quick:focus-within) { border-color: var(--accent); }
  .body :global(.quick input) { height: 36px; }
  .list { display: flex; flex-direction: column; }
  .empty { font-size: 12.5px; margin: 8px 0; padding: 0 2px; color: var(--text-muted); }
  .done { font-size: 11.5px; margin: 8px 0 0; padding: 6px 2px 0; color: var(--text-muted); border-top: 1px solid var(--line); }
  /* In the narrow sidebar, tasks read as a compact list instead of nested cards. */
  .list :global(.task.compact) { min-height: calc(38px * var(--d)); padding: calc(5px * var(--d)) 2px; border-radius: 6px; border: 0; border-bottom: 1px solid var(--line); background: transparent; box-shadow: none; }
  .list :global(.task.compact:hover) { background: var(--bg-hover); box-shadow: none; }
  .list :global(.task.compact .strip) { left: 0; top: 6px; bottom: 6px; width: 2px; }
  .list :global(.task.compact .main) { gap: 8px; flex: 1 1 0; min-width: 0; }
  .list :global(.task.compact .title) { font-size: 13px; }
  .list :global(.task.compact .chips) { gap: 4px; flex: 0 0 auto; margin-left: 0; }
  .list :global(.task.compact .chip) { font-size: 10px; padding: 0 5px; border-radius: 8px; }
  .list :global(.task.compact .chip.warn), .list :global(.task.compact .chip.danger) { font-size: 10px; }
  .list :global(.task.compact .del) { opacity: 0; }
  .list :global(.task.compact:hover .del), .list :global(.task.compact .del:focus-visible) { opacity: 1; }
</style>
