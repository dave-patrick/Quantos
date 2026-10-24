<script lang="ts">
  import { tasks, type TaskDto } from '../lib/tasks.svelte';
  import { app } from '../lib/state.svelte';
  import TaskRow from './TaskRow.svelte';
  import Icon from './Icon.svelte';
  import QuickAdd from './QuickAdd.svelte';

  // Open tasks (todo/doing/waiting) first, then the rest. Compact rows keep the sidebar readable.
  const openTasks = $derived(tasks.list.filter((t) => t.status !== 'done' && t.status !== 'cancelled').slice(0, 40));
  const doneCount = $derived(tasks.list.filter((t) => t.status === 'done' || t.status === 'cancelled').length);
</script>

<section class="panel" aria-label="Tasks">
  <div class="head">
    <Icon name="check-square" size={14} />
    <span class="title">Tasks</span>
    <span class="faint count">{openTasks.length} open</span>
  </div>
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
  .panel { display: flex; flex-direction: column; min-height: 0; flex: 1; background: var(--bg-raised); border-left: 1px solid var(--line); }
  .head { display: flex; align-items: center; gap: 8px; padding: 10px 12px; border-bottom: 1px solid var(--line); flex: none; }
  .title { font: 600 13px var(--font-body); color: var(--text-soft); }
  .count { font: 11px var(--font-mono); margin-left: auto; color: var(--text-muted); }
  .body { flex: 1; min-height: 0; overflow: auto; padding: 0; }
  .list { display: flex; flex-direction: column; gap: 1px; }
  .empty { font-size: 12.5px; margin: 6px 0; padding: 0 12px; color: var(--text-muted); }
  .done { font-size: 11.5px; margin: 8px 0 0; padding: 0 12px; color: var(--text-muted); }
  /* Compact task rows in the sidebar: tight, readable, chips wrap under title. */
  .list :global(.task.compact) { min-height: calc(34px * var(--d)); padding-top: calc(5px * var(--d)); padding-bottom: calc(5px * var(--d)); border-radius: 8px; margin: 4px 8px; border: 1px solid var(--line); background: var(--bg-raised); box-shadow: 0 1px 3px rgb(0 0 0 / 0.1); }
  .list :global(.task.compact:hover) { background: var(--bg-hover); border-color: var(--line-strong); box-shadow: 0 2px 4px rgb(0 0 0 / 0.15); }
  .list :global(.task.compact:hover) { background: var(--bg-hover); }
  .list :global(.task.compact .strip) { left: 4px; top: 5px; bottom: 5px; width: 3px; }
  .list :global(.task.compact .main) { gap: 8px; }
  .list :global(.task.compact .title) { font-size: 13px; }
  .list :global(.task.compact .chips) { gap: 4px; }
  .list :global(.task.compact .chip) { font-size: 10px; padding: 0 5px; border-radius: 8px; }
  .list :global(.task.compact .chip.warn), .list :global(.task.compact .chip.danger) { font-size: 10px; }
  .list :global(.task.compact .del) { opacity: 0; }
  .list :global(.task.compact:hover .del) { opacity: 1; }
</style>
