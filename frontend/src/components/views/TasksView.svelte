<script lang="ts">
  import { tasks, type TaskDto } from '../../lib/tasks.svelte';
  import { app } from '../../lib/state.svelte';
  import { isoDate } from '../../lib/dates';
  import type { Tab } from '../../lib/layout.svelte';
  import { api } from '../../lib/api';
  import TaskRow from '../TaskRow.svelte';
  import QuickAdd from '../QuickAdd.svelte';
  import Icon from '../Icon.svelte';

  let { tab }: { tab: Tab } = $props();
  void tab;

  type Group = 'none' | 'due' | 'project' | 'priority' | 'status' | 'stage' | 'assignee';
  let group = $state<Group>('due');
  let scope = $state<'workspace' | 'all'>('workspace');
  let mode = $state<'list' | 'board'>('list');
  let filter = $state('');
  let showDone = $state(false);
  const today = isoDate();

  const label = (v: string | null) => (v ? (/\|([^\]]+)\]\]$/.exec(v)?.[1] ?? v) : null);

  const scoped = $derived(tasks.list.filter((t) => !t.archived && (scope === 'all' || t.workspace === app.workspace)
    && (!filter.trim() || t.title.toLowerCase().includes(filter.trim().toLowerCase()))));
  const open = $derived(scoped.filter((t) => t.status !== 'done' && t.status !== 'cancelled'));
  const done = $derived(scoped.filter((t) => t.status === 'done' || t.status === 'cancelled')
    .sort((a, b) => (b.completed ?? '').localeCompare(a.completed ?? '')).slice(0, 200));

  function sortTasks(list: TaskDto[]) {
    return [...list].sort((a, b) =>
      (a.priority ?? 9) - (b.priority ?? 9) || (a.due ?? '9999').localeCompare(b.due ?? '9999') || (a.planned ?? '9999').localeCompare(b.planned ?? '9999') || a.title.localeCompare(b.title));
  }

  function dueBucket(t: TaskDto) {
    const d = (t.due ?? t.planned)?.slice(0, 10);
    if (!d) return 'No date';
    if (d < today) return 'Overdue';
    if (d === today) return 'Today';
    const days = Math.round((new Date(d + 'T00:00:00').getTime() - new Date(today + 'T00:00:00').getTime()) / 86400000);
    if (days <= 7) return 'Next 7 days';
    return 'Later';
  }

  const groups = $derived.by(() => {
    const map = new Map<string, TaskDto[]>();
    const key = (t: TaskDto): string[] => {
      switch (group) {
        case 'due': return [dueBucket(t)];
        case 'project': return [label(t.project) ?? 'No project'];
        case 'priority': return [t.priority ? `P${t.priority}` : 'No priority'];
        case 'status': return [t.status];
        case 'stage': return [t.stage ?? 'No stage'];
        case 'assignee': return t.assignees.length ? t.assignees.map((a) => label(a)!) : ['Unassigned'];
        default: return ['All'];
      }
    };
    // Subtasks sit under their parent when the parent is in the list.
    const ids = new Set(open.map((t) => t.id));
    for (const t of sortTasks(open.filter((t) => !t.parent || !ids.has(t.parent)))) for (const k of key(t)) map.set(k, [...(map.get(k) ?? []), t]);
    const order = group === 'due' ? ['Overdue', 'Today', 'Next 7 days', 'Later', 'No date'] : group === 'status' ? ['doing', 'todo', 'waiting'] : [...map.keys()].sort();
    return order.filter((k) => map.has(k)).map((k) => [k, map.get(k)!] as const).concat([...map].filter(([k]) => !order.includes(k)));
  });

  const childrenOf = (id: string) => sortTasks(open.filter((t) => t.parent === id));

  // Kanban by status, or by the project's custom stages.
  const columns = $derived.by(() => {
    if (group === 'stage') {
      const stages = [...new Set(open.map((t) => t.stage ?? 'No stage'))];
      return stages.map((s) => [s, sortTasks(open.filter((t) => (t.stage ?? 'No stage') === s))] as const);
    }
    return (['todo', 'doing', 'waiting', 'done'] as const).map((s) => [s, sortTasks(scoped.filter((t) => t.status === s)).slice(0, 100)] as const);
  });

  let dragId: string | null = null;
  async function dropOn(column: string) {
    const id = dragId; dragId = null;
    if (!id) return;
    try {
      if (group === 'stage') await tasks.update(id, { stage: column === 'No stage' ? null : column });
      else await tasks.setStatus(id, column as TaskDto['status']);
    } catch (e) { app.error(e); }
  }

  async function exportCalendar() {
    try { const r = await api.post<{ path: string }>('tasks/calendar', { name: app.workspace }); app.toast(`Calendar saved to ${r.path}`, 'ok'); }
    catch (e) { app.error(e); }
  }
</script>

<div class="scroll view">
  <div class="inner" class:wide={mode === 'board'}>
    <header>
      <h1>Tasks</h1>
      <div class="controls">
        <input class="input" aria-label="Filter tasks" placeholder="Filter" bind:value={filter} />
        <select class="input" bind:value={group} aria-label="Group by">
          <option value="due">By date</option><option value="project">By project</option><option value="priority">By priority</option>
          <option value="status">By status</option><option value="stage">By stage</option><option value="assignee">By person</option><option value="none">No grouping</option>
        </select>
        <select class="input" aria-label="Workspaces" bind:value={scope}><option value="workspace">{app.workspace}</option><option value="all">All workspaces</option></select>
        <div class="seg">
          <button class:on={mode === 'list'} onclick={() => (mode = 'list')} title="List"><Icon name="list" size={14} /></button>
          <button class:on={mode === 'board'} onclick={() => (mode = 'board')} title="Board"><Icon name="kanban" size={14} /></button>
        </div>
        <button class="icon-btn" title="Save a calendar file (.ics) to the exports folder" onclick={exportCalendar}><Icon name="calendar" size={15} /></button>
      </div>
    </header>

    <QuickAdd />

    {#if mode === 'list'}
      {#each groups as [name, list] (name)}
        <section>
          <h2 class="label" class:danger={name === 'Overdue'}>{name} <span class="faint">{list.length}</span></h2>
          {#each list as t (t.id)}
            <TaskRow task={t} />
            {#each childrenOf(t.id) as c (c.id)}<div class="sub"><TaskRow task={c} compact /></div>{/each}
          {/each}
        </section>
      {:else}
        <div class="empty-state"><h3>Nothing to do</h3><p>Add a task above, or type <code>- [ ]</code> in any note.</p></div>
      {/each}

      <!-- Completed-task drawer (BUILD-PLAN 8B.3) -->
      <section class="drawer">
        <button class="drawer-toggle" onclick={() => (showDone = !showDone)}>
          <Icon name={showDone ? 'chevron-down' : 'chevron-right'} size={13} /> Completed <span class="faint">{done.length}</span>
        </button>
        {#if showDone}{#each done as t (t.id)}<TaskRow task={t} compact />{/each}{/if}
      </section>
    {:else}
      <div class="board">
        {#each columns as [name, list] (name)}
          <div class="column" role="list" ondragover={(e) => e.preventDefault()} ondrop={() => dropOn(name)}>
            <h2 class="label">{name} <span class="faint">{list.length}</span></h2>
            {#each list as t (t.id)}
              <div class="card"><TaskRow task={t} compact draggable ondragstart={() => (dragId = t.id)} /></div>
            {/each}
          </div>
        {/each}
      </div>
    {/if}
  </div>
</div>

<style>
  .view { flex: 1; padding: 0 28px; }
  .inner { max-width: 920px; margin: 0 auto; padding: 22px 0 80px; }
  .inner.wide { max-width: none; }
  header { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
  h1 { font: 700 28px/1.2 var(--font-display); }
  .controls { display: flex; gap: 6px; align-items: center; }
  .controls .input { height: 30px; width: auto; font-size: 13px; }
  .seg { display: inline-flex; border: 1px solid var(--line-strong); border-radius: 7px; overflow: hidden; }
  .seg button { border: 0; background: none; color: var(--text-muted); padding: 5px 9px; display: grid; }
  .seg button.on { background: var(--accent-wash); color: var(--accent); }
  section { margin-top: 18px; }
  h2 { margin: 0 0 4px 4px; }
  h2.danger { color: var(--danger); }
  .sub { margin-left: 26px; }
  .drawer { border-top: 1px solid var(--line); padding-top: 10px; margin-top: 30px; }
  .drawer-toggle { display: flex; gap: 6px; align-items: center; border: 0; background: none; color: var(--text-muted); font-weight: 600; padding: 4px; }
  .board { display: grid; grid-auto-flow: column; grid-auto-columns: minmax(260px, 1fr); gap: 12px; margin-top: 14px; overflow-x: auto; align-items: start; }
  .column { background: var(--bg-raised); border: 1px solid var(--line); border-radius: 10px; padding: 10px; min-height: 200px; }
  .column h2 { text-transform: uppercase; }
  /* Board cards are narrow: the title gets the whole first line and the chips move below it. */
  .card :global(.task) { flex-wrap: wrap; row-gap: 4px; }
  .card :global(.task .title) { flex: 1 1 0; white-space: normal; }
  .card :global(.task .chips) { order: 1; flex-basis: 100%; flex-wrap: wrap; padding-left: 26px; }
  .card { background: var(--bg); border: 1px solid var(--line); border-radius: 8px; margin-bottom: 6px; }
  code { color: var(--accent); }
</style>
