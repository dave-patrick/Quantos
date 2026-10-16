<script lang="ts">
  import { api } from '../../lib/api';
  import { tasks, type TaskDto } from '../../lib/tasks.svelte';
  import { app } from '../../lib/state.svelte';
  import { isoDate, relativeTime } from '../../lib/dates';
  import TaskRow from '../TaskRow.svelte';

  let days = $state(14);
  const today = isoDate();
  const cutoff = $derived(Date.now() - days * 86400000);
  // Stale tasks (BUILD-PLAN 8B.14): open, untouched for a while, or rolled over again and again.
  const stale = $derived(tasks.list
    .filter((t) => t.workspace === app.workspace && !t.archived && t.status !== 'done' && t.status !== 'cancelled')
    .filter((t) => (t.modified && new Date(t.modified).getTime() < cutoff) || t.rolledOver >= 3)
    .sort((a, b) => (a.modified ?? '').localeCompare(b.modified ?? '')));

  async function act(t: TaskDto, action: 'today' | 'keep' | 'someday' | 'cancel' | 'trash') {
    try {
      if (action === 'today') await tasks.update(t.id, { planned: today, my_day: today, rolled_over: 0 });
      else if (action === 'keep') await tasks.update(t.id, { reviewed: today, rolled_over: 0 });
      else if (action === 'someday') await tasks.update(t.id, { planned: null, tags: [...new Set([...t.tags, 'someday'])], rolled_over: 0 });
      else if (action === 'cancel') await tasks.setStatus(t.id, 'cancelled');
      else await api.post(`nodes/${t.id}/trash`);
    } catch (e) { app.error(e); }
  }
</script>

<div class="scroll view">
  <div class="inner">
    <h1>Stale tasks</h1>
    <p class="muted">Open tasks untouched for
      <select class="input inline" bind:value={days}><option value={7}>a week</option><option value={14}>two weeks</option><option value={30}>a month</option><option value={90}>three months</option></select>
      or rolled over three or more times. Decide what to do with each.</p>
    {#each stale as t (t.id)}
      <div class="item">
        <TaskRow task={t} showMyDay={false} />
        <div class="actions">
          <span class="faint">touched {relativeTime(t.modified)}{t.rolledOver ? ` · rolled over ${t.rolledOver}×` : ''}</span>
          <button class="btn ghost" onclick={() => act(t, 'today')}>Do today</button>
          <button class="btn ghost" onclick={() => act(t, 'keep')}>Still relevant</button>
          <button class="btn ghost" onclick={() => act(t, 'someday')}>Someday</button>
          <button class="btn ghost" onclick={() => act(t, 'cancel')}>Cancel</button>
          <button class="btn ghost danger" onclick={() => act(t, 'trash')}>Trash</button>
        </div>
      </div>
    {:else}
      <div class="empty-state"><h3>Nothing stale</h3><p>Every open task has been looked at recently.</p></div>
    {/each}
  </div>
</div>

<style>
  .view { flex: 1; padding: 0 28px; }
  .inner { max-width: 900px; margin: 0 auto; padding: 24px 0 80px; }
  h1 { font: 700 28px/1.2 var(--font-display); }
  .inline { display: inline-block; width: auto; height: 28px; }
  .item { border-bottom: 1px solid var(--line); padding: 6px 0; }
  .actions { display: flex; gap: 4px; align-items: center; padding-left: 40px; flex-wrap: wrap; font-size: 12.5px; }
  .actions .faint { margin-right: auto; }
  .actions .btn { height: 26px; font-size: 12.5px; padding: 0 8px; }
</style>
