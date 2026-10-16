<script lang="ts">
  import { tasks } from '../lib/tasks.svelte';
  import { app } from '../lib/state.svelte';
  import TaskRow from './TaskRow.svelte';

  let { date, isToday, noteId = null }: { date: string; isToday: boolean; noteId?: string | null } = $props();
  // Tasks scheduled for this day that aren't already written into its note.
  const list = $derived(tasks.list.filter((t) => t.workspace === app.workspace && !t.archived && t.source !== noteId &&
    (t.due?.slice(0, 10) === date || t.planned?.slice(0, 10) === date || (isToday && t.myDay === date))));
</script>

{#if list.length}
  <div class="day-tasks" role="list">
    {#each list as t (t.id)}<TaskRow task={t} compact />{/each}
  </div>
{/if}

<style>
  .day-tasks { margin: 2px 0 8px; padding: 4px; border-radius: 8px; background: var(--bg-raised); border: 1px solid var(--line); }
</style>
