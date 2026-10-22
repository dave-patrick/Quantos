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
  .panel { display: flex; flex-direction: column; min-height: 0; flex: 1; }
  .head { display: flex; align-items: center; gap: 8px; padding: 8px 10px; border-bottom: 1px solid var(--line); flex: none; }
  .title { font: 600 13px var(--font-body); color: var(--text-soft); }
  .count { font: 11px var(--font-mono); margin-left: auto; }
  .body { flex: 1; min-height: 0; overflow: auto; padding: 8px 10px; }
  .list { display: flex; flex-direction: column; gap: 2px; }
  .empty { font-size: 12.5px; margin: 6px 0; }
  .done { font-size: 11.5px; margin: 8px 0 0; }
</style>
