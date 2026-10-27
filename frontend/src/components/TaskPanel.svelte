<script lang="ts">
  import type { Doc } from '../lib/docs.svelte';
  import { tasks, type TaskDto } from '../lib/tasks.svelte';
  import { app } from '../lib/state.svelte';
  import { openNode, searchTitles, whereFrom } from '../lib/links.svelte';
  import { api } from '../lib/api';
  import TaskRow from './TaskRow.svelte';
  import QuickAdd from './QuickAdd.svelte';
  import Icon from './Icon.svelte';

  let { doc }: { doc: Doc } = $props();
  const id = $derived(doc.id);
  const task = $derived(tasks.version >= 0 ? tasks.get(id) : undefined);
  const subtasks = $derived(tasks.list.filter((t) => t.parent === id));
  const deps = $derived((task?.dependsOn ?? []).map((d) => tasks.get(d)).filter((t): t is TaskDto => !!t));
  const blocks = $derived(tasks.list.filter((t) => t.dependsOn.includes(id)));
  const parent = $derived(task?.parent ? tasks.get(task.parent) : undefined);
  const source = $derived(task?.source ? app.titleIndex.get(task.source) : undefined);

  let reminderAt = $state('');
  let depQuery = $state('');

  async function set(values: Record<string, unknown>) {
    try { await doc.flush(); await tasks.update(id, values); await doc.refresh(); } catch (e) { app.error(e); }
  }

  async function addReminder() {
    if (!reminderAt || !task) return;
    const at = new Date(reminderAt);
    const iso = at.toISOString();
    await set({ reminders: [...task.reminders.map((r) => ({ at: r.at, snoozed_until: r.snoozedUntil ?? undefined, fired: r.fired ?? undefined })), { at: iso }] });
    reminderAt = '';
  }

  async function removeReminder(i: number) {
    if (!task) return;
    await set({ reminders: task.reminders.filter((_, j) => j !== i).map((r) => ({ at: r.at, snoozed_until: r.snoozedUntil ?? undefined, fired: r.fired ?? undefined })) });
  }

  async function snooze(minutes: number) {
    try { await api.post(`tasks/${id}/snooze`, { minutes }); await tasks.load(); } catch (e) { app.error(e); }
  }

  async function addDependency(title: string) {
    const hit = searchTitles(title, 10).find((n) => n.type === 'task' && n.title === title && n.id !== id);
    if (!hit || !task) return;
    await set({ depends_on: [...task.dependsOn, hit.id] });
    depQuery = '';
  }

  const taskTitles = $derived(app.titles.filter((t) => t.type === 'task' && t.id !== id));
</script>

{#if task}
  <section class="panel" aria-label="Task details">
    <dl>
      <dt>Reminders</dt><dd class="col">
        {#each task.reminders as r, i (i)}
          <span class="reminder">
            <Icon name="bell" size={12} /> {new Date(r.snoozedUntil ?? r.at).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })}
            {#if r.snoozedUntil}<span class="faint">snoozed</span>{/if}{#if r.fired}<span class="faint">sent</span>{/if}
            <button class="x" aria-label="Remove reminder" onclick={() => removeReminder(i)}>×</button>
          </span>
        {/each}
        <span class="add"><input aria-label="New reminder" class="cell" type="datetime-local" bind:value={reminderAt} /><button class="btn" disabled={!reminderAt} onclick={addReminder}>Add</button>
          {#if task.reminders.some((r) => r.fired)}<button class="btn ghost" onclick={() => snooze(10)}>Snooze 10 min</button><button class="btn ghost" onclick={() => snooze(60)}>1 hour</button>{/if}</span>
      </dd>
      <dt>Depends on</dt><dd class="col">
        {#each deps as d (d.id)}<span class="dep"><TaskRow task={d} compact showMyDay={false} /><button class="x" aria-label="Remove dependency" onclick={() => set({ depends_on: task.dependsOn.filter((x) => x !== d.id) })}>×</button></span>{/each}
        <input aria-label="Add a dependency" class="cell" list="task-titles" bind:value={depQuery} placeholder="Add a task this one waits for" onchange={() => addDependency(depQuery)} />
        <datalist id="task-titles">{#each taskTitles as t (t.id)}<option value={t.title}></option>{/each}</datalist>
        {#if blocks.length}<span class="faint hint">Blocks: {blocks.map((b) => b.title).join(', ')}</span>{/if}
      </dd>
      {#if parent}<dt>Parent</dt><dd><button class="link" onclick={(e) => openNode(parent!.id, whereFrom(e), parent!.title)}>{parent.title}</button>
        <button class="x" aria-label="Detach from parent" onclick={() => set({ parent: null })}>×</button></dd>{/if}
      {#if source}<dt>From</dt><dd><button class="link" onclick={(e) => openNode(source!.id, whereFrom(e), source!.title)}>{source.title}</button></dd>{/if}
    </dl>

    <h3 class="label">Subtasks {#if subtasks.length}<span class="faint">{subtasks.filter((s) => s.status === 'done').length}/{subtasks.length}</span>{/if}</h3>
    {#each subtasks as s (s.id)}<TaskRow task={s} compact />{/each}
    <QuickAdd defaults={{ parent: id, project: task.project ?? undefined }} placeholder="Add a subtask…" />
  </section>
{/if}

<style>
  .panel { margin: 6px 0 16px; padding: 12px 14px; border-radius: 10px; background: var(--bg-raised); border: 1px solid var(--line); }
  dl { display: grid; grid-template-columns: 110px 1fr; gap: 4px 12px; margin: 0; align-items: center; }
  dt { font: 12px var(--font-mono); color: var(--text-muted); }
  dd { margin: 0; display: flex; align-items: center; gap: 8px; flex-wrap: wrap; min-width: 0; }
  dd.col { flex-direction: column; align-items: stretch; gap: 4px; }
  .cell { height: 28px; padding: 0 8px; border-radius: 5px; border: 1px solid var(--line); background: var(--bg-input); color: var(--text); font-size: 13.5px; min-width: 0; }
  .cell:focus { outline: none; border-color: var(--accent); }
  .hint { font-size: 12px; }
  .reminder { display: inline-flex; align-items: center; gap: 6px; font-size: 13px; }
  .add { display: flex; gap: 6px; align-items: center; flex-wrap: wrap; }
  .dep { display: flex; align-items: center; }
  .dep > :global(div) { flex: 1; }
  .x { border: 0; background: none; color: var(--text-faint); font-size: 16px; padding: 0 4px; }
  .x:hover { color: var(--danger); }
  .link { border: 0; background: none; color: var(--accent); padding: 0; text-decoration: underline; text-underline-offset: 3px; }
  h3 { margin: 14px 0 4px; display: flex; gap: 6px; }
</style>
