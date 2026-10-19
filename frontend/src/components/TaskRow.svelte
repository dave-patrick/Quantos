<script lang="ts">
  import { day } from '../lib/today.svelte';
  import { tasks, type TaskDto } from '../lib/tasks.svelte';
  import { openNode, whereFrom } from '../lib/links.svelte';
  import { app } from '../lib/state.svelte';
  import { api } from '../lib/api';
  import ContextMenu, { type ContextMenuItem } from './ContextMenu.svelte';
  import Icon from './Icon.svelte';

  let { task, showMyDay = true, draggable = false, ondragstart, compact = false }:
    { task: TaskDto; showMyDay?: boolean; draggable?: boolean; ondragstart?: (e: DragEvent) => void; compact?: boolean } = $props();

  const today = $derived(day.today);
  const closed = $derived(task.status === 'done' || task.status === 'cancelled');
  const dueDay = $derived(task.due?.slice(0, 10) ?? null);
  const overdue = $derived(!closed && !!dueDay && dueDay < today);
  const inMyDay = $derived(task.myDay === today);
  const projectName = $derived(task.project ? (/\|([^\]]+)\]\]$/.exec(task.project)?.[1] ?? task.project) : null);

  /** The right-click menu for a task. */
  let menu = $state<{ x: number; y: number } | null>(null);

  async function toggle() {
    try { await tasks.setStatus(task.id, closed ? 'todo' : 'done'); } catch (e) { app.error(e); }
  }

  async function toggleMyDay() {
    try { await tasks.update(task.id, { my_day: inMyDay ? null : today }); } catch (e) { app.error(e); }
  }

  async function setStatus(status: TaskDto['status']) {
    try { await tasks.setStatus(task.id, status); } catch (e) { app.error(e); }
  }

  async function setDue() {
    const value = await app.ask('Due date', { placeholder: 'YYYY-MM-DD', value: task.due ?? '' });
    if (value === null) return;
    const clean = value.trim();
    try { await tasks.update(task.id, clean ? { due: clean } : {}, clean ? [] : ['due']); } catch (e) { app.error(e); }
  }

  async function setPriority() {
    const value = await app.ask('Priority', { placeholder: '1 (highest) to 4', value: task.priority ? String(task.priority) : '' });
    if (value === null) return;
    const n = Number(value.trim());
    try { await tasks.update(task.id, n >= 1 && n <= 4 ? { priority: n } : {}, n >= 1 && n <= 4 ? [] : ['priority']); } catch (e) { app.error(e); }
  }

  /** A task is a note, so it goes to the trash like any other note, and can be brought back. */
  async function trash() {
    try {
      await api.post(`nodes/${task.id}/trash`);
      app.toast('Moved to trash.', 'ok', { label: 'Undo', run: async () => {
        const entries = await api.get<{ entryId: string; id: string }[]>('trash');
        const entry = entries.find((e) => e.id === task.id);
        if (entry) await api.post(`trash/${entry.entryId}/restore`);
        await tasks.load();
      } });
    } catch (e) { app.error(e); }
  }

  const items = $derived.by((): ContextMenuItem[] => [
    { label: closed ? 'Mark as not done' : 'Mark as done', icon: 'check', run: toggle },
    { label: 'Set to doing', icon: 'play', run: () => setStatus('doing') },
    { label: 'Set to waiting', icon: 'clock', run: () => setStatus('waiting') },
    { label: 'Set a due date…', icon: 'calendar', run: setDue },
    { label: 'Set a priority…', icon: 'flag', run: setPriority },
    { separator: true },
    { label: 'Open the task', icon: 'file-text', run: () => openNode(task.id, 'current', task.title) },
    { separator: true },
    { label: 'Move to trash', icon: 'trash', danger: true, run: trash }
  ]);

  function fmt(d: string) {
    const day = d.slice(0, 10);
    if (day === today) return 'today';
    const date = new Date(day + 'T00:00:00');
    const diff = Math.round((date.getTime() - new Date(today + 'T00:00:00').getTime()) / 86400000);
    if (diff === 1) return 'tomorrow';
    if (diff === -1) return 'yesterday';
    return date.toLocaleDateString(undefined, { day: 'numeric', month: 'short' }) + (d.length > 10 ? ' ' + d.slice(11, 16) : '');
  }
</script>

<div class="task p{task.priority ?? 0}" class:closed class:compact {draggable} {ondragstart} role="group" aria-label={task.title}
  oncontextmenu={(e) => { e.preventDefault(); menu = { x: e.clientX, y: e.clientY }; }}>
  <span class="strip" aria-hidden="true"></span>
  <span class="main">
    <input type="checkbox" checked={task.status === 'done'} onchange={toggle} aria-label={closed ? 'Reopen task' : 'Complete task'} />
    <button class="title" onclick={(e) => openNode(task.id, whereFrom(e), task.title)}>
      {task.title}
    </button>
  </span>
  <span class="chips">
    {#if task.blocked}<span class="chip warn" title="Waiting on another task"><Icon name="lock" size={11} /> blocked</span>{/if}
    {#if task.status === 'doing'}<span class="chip accent">doing</span>{/if}
    {#if task.status === 'waiting' || task.waitingOn}<span class="chip" title="Waiting on">⏳ {task.waitingOn ?? 'waiting'}</span>{/if}
    {#if task.recurrence}<span class="chip" title={task.recurrence}><Icon name="repeat" size={11} /></span>{/if}
    {#if task.subtaskCount}<span class="chip" title="Subtasks done">{task.subtasksDone}/{task.subtaskCount}</span>{/if}
    {#if task.effortRollup ?? task.effort}<span class="chip" title="Effort">{task.effortRollup ?? task.effort}m</span>{/if}
    {#if projectName && !compact}<span class="chip" title="Project"><Icon name="briefcase" size={11} /> {projectName}</span>{/if}
    {#if task.assignees.length && !compact}<span class="chip"><Icon name="user" size={11} /> {task.assignees.map((a) => /\|([^\]]+)\]\]$/.exec(a)?.[1] ?? a).join(', ')}</span>{/if}
    {#if task.planned && task.planned.slice(0, 10) !== dueDay}<span class="chip" title="Planned">◷ {fmt(task.planned)}</span>{/if}
    {#if task.due}<span class="chip" class:danger={overdue} title="Due">⚑ {fmt(task.due)}</span>{/if}
    {#if task.reminders.some((r) => !r.fired)}<span class="chip" title="Reminder set"><Icon name="bell" size={11} /></span>{/if}
  </span>
  {#if showMyDay && !closed}
    <button class="myday" class:on={inMyDay} title={inMyDay ? 'Remove from My Day' : 'Add to My Day'} onclick={toggleMyDay}><Icon name="sun" size={14} /></button>
  {/if}
  <button class="del" title="Move to trash" aria-label={'Move “' + task.title + '” to the trash'} onclick={trash}>
    <Icon name="x" size={14} />
  </button>
</div>

{#if menu}
  <ContextMenu x={menu.x} y={menu.y} {items} onclose={() => (menu = null)} />
{/if}

<style>
  /* Narrow places (timeline, side panels): chips move under the title rather than squeezing it. */
  .task { position: relative; display: flex; flex-wrap: wrap; align-items: center; row-gap: 2px; column-gap: 10px; padding: calc(7px * var(--d)) 10px calc(7px * var(--d)) 14px; border-radius: 7px; min-height: calc(38px * var(--d)); }
  .task:hover { background: var(--bg-hover); }
  .strip { position: absolute; left: 3px; top: 7px; bottom: 7px; width: 3px; border-radius: 3px; background: transparent; }
  .p1 .strip { background: var(--p1); } .p2 .strip { background: var(--p2); } .p3 .strip { background: var(--p3); } .p4 .strip { background: var(--p4); }
  input[type='checkbox'] { width: 16px; height: 16px; accent-color: var(--accent); flex: none; cursor: pointer; }
  .main { display: flex; align-items: center; gap: 10px; flex: 1 1 11em; min-width: 0; }
  .title { flex: 1; min-width: 0; text-align: left; border: 0; background: none; color: var(--text); padding: 0; font-size: 14.5px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .title:hover { color: var(--accent-strong); }
  .closed .title { color: var(--text-faint); text-decoration: line-through; }
  .chips { display: flex; gap: 4px; flex-wrap: wrap; margin-left: auto; min-width: 0; }
  .chip { display: inline-flex; align-items: center; gap: 3px; font: 11px/18px var(--font-mono); color: var(--text-muted); border: 1px solid var(--line); border-radius: 9px; padding: 0 6px; white-space: nowrap; }
  .chip.danger { color: var(--danger); border-color: color-mix(in srgb, var(--danger) 45%, transparent); }
  .chip.warn { color: var(--warn); border-color: color-mix(in srgb, var(--warn) 45%, transparent); }
  .chip.accent { color: var(--accent); border-color: color-mix(in srgb, var(--accent) 45%, transparent); }
  .myday { border: 0; background: none; color: var(--text-faint); padding: 4px; border-radius: 5px; opacity: 0; }
  .task:hover .myday, .myday.on { opacity: 1; }
  .myday.on { color: var(--warn); }
  /* Deleting a task is one click on the row itself. It stays quiet until the row is hovered or the
     button is reached with the keyboard. */
  .del { border: 0; background: none; color: var(--text-faint); padding: 4px; border-radius: 5px; opacity: 0; flex: none; }
  .task:hover .del, .del:focus-visible { opacity: 1; }
  .del:hover { color: var(--danger); background: var(--bg-hover); }
  .compact { min-height: calc(32px * var(--d)); padding-top: calc(4px * var(--d)); padding-bottom: calc(4px * var(--d)); }
</style>
