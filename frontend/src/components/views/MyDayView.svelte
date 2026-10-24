<script lang="ts">
  import { api } from '../../lib/api';
  import { tasks, type TaskDto } from '../../lib/tasks.svelte';
  import { app } from '../../lib/state.svelte';
  import { isoDate, longDate } from '../../lib/dates';
  import TaskRow from '../TaskRow.svelte';
  import QuickAdd from '../QuickAdd.svelte';
  import Icon from '../Icon.svelte';

  let { today: watched, docked = false }: { today: string; docked?: boolean } = $props();
  const today = $derived(watched || isoDate());

  const mine = $derived(tasks.list.filter((t) => t.workspace === app.workspace && !t.archived));
  const isOpen = (t: TaskDto) => t.status !== 'done' && t.status !== 'cancelled';
  // My Day queue (BUILD-PLAN 8B.13): tasks you picked for today, in your order.
  const queue = $derived(mine.filter((t) => t.myDay === today).sort((a, b) => (a.myDayOrder ?? 999) - (b.myDayOrder ?? 999)));
  const queued = $derived(new Set(queue.map((t) => t.id)));
  const overdue = $derived(mine.filter((t) => isOpen(t) && !queued.has(t.id) && t.due && t.due.slice(0, 10) < today));
  const dueToday = $derived(mine.filter((t) => isOpen(t) && !queued.has(t.id) && t.due?.slice(0, 10) === today));
  const plannedToday = $derived(mine.filter((t) => isOpen(t) && !queued.has(t.id) && t.planned?.slice(0, 10) === today && t.due?.slice(0, 10) !== today));
  const stalePlanned = $derived(mine.filter((t) => isOpen(t) && t.planned && t.planned.slice(0, 10) < today));
  const openInQueue = $derived(queue.filter(isOpen));
  const effort = $derived(openInQueue.reduce((s, t) => s + (t.effortRollup ?? t.effort ?? 0), 0));

  async function rollover() {
    try {
      const r = await api.post<{ moved: number }>('tasks/rollover', { workspace: app.workspace, today });
      app.toast(`Moved ${r.moved} task(s) to today. Due dates didn't change.`, 'ok');
    } catch (e) { app.error(e); }
  }

  async function addAll(list: TaskDto[]) {
    for (const t of list) await tasks.update(t.id, { my_day: today, my_day_order: 500 }).catch((e) => app.error(e));
  }

  let dragId: string | null = null;
  async function drop(target: TaskDto) {
    const id = dragId; dragId = null;
    if (!id || id === target.id) return;
    const order = queue.map((t) => t.id).filter((x) => x !== id);
    order.splice(order.indexOf(target.id), 0, id);
    await saveOrder(order);
  }

  async function saveOrder(order: string[]) {
    for (const [i, tid] of order.entries()) {
      const t = queue.find((x) => x.id === tid);
      if (t && t.myDayOrder !== i) await tasks.update(tid, { my_day_order: i });
    }
  }

  /** Keyboard alternative to dragging: Alt+Up / Alt+Down move the focused task in the queue. */
  async function moveKey(e: KeyboardEvent, t: TaskDto) {
    if (!e.altKey || (e.key !== 'ArrowUp' && e.key !== 'ArrowDown')) return;
    e.preventDefault();
    const order = queue.map((x) => x.id);
    const i = order.indexOf(t.id);
    const j = e.key === 'ArrowUp' ? i - 1 : i + 1;
    if (j < 0 || j >= order.length) return;
    [order[i], order[j]] = [order[j], order[i]];
    const focused = document.activeElement as HTMLElement | null;
    await saveOrder(order);
    focused?.focus();
  }
</script>

<div class="scroll view" class:docked>
  <div class="inner">
    <header>
      <div>
        <h1><Icon name="sun" size={24} /> My Day</h1>
        <p class="muted">{longDate(today)} · {openInQueue.length} open{effort ? ` · about ${Math.round(effort / 6) / 10} h planned` : ''}</p>
      </div>
    </header>

    {#if stalePlanned.length}
      <div class="banner">
        <Icon name="history" size={16} />
        <span>{stalePlanned.length} unfinished task{stalePlanned.length === 1 ? ' was' : 's were'} planned for an earlier day.</span>
        <button class="btn" onclick={rollover}>Move to today</button>
      </div>
    {/if}

    <QuickAdd defaults={{ myDay: true, planned: today }} placeholder="Add to My Day…" />

    <section>
      {#each queue as t (t.id)}
        <!-- Keys come from the focusable controls inside the row (checkbox, title). -->
        <!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
        <div role="listitem" title="Drag, or Alt+Up / Alt+Down, to reorder" ondragover={(e) => e.preventDefault()} ondrop={() => drop(t)} onkeydown={(e) => moveKey(e, t)}>
          <TaskRow task={t} draggable dense={docked} ondragstart={() => (dragId = t.id)} />
        </div>
      {:else}
        <div class="empty-state"><h3>Nothing picked for today yet</h3><p>{overdue.length || dueToday.length || plannedToday.length ? 'Choose from the suggestions below, add a task above, or press the sun on any task.' : 'Add a task above, or press the sun on any task to pick it for today.'}</p></div>
      {/each}
    </section>

    {#each [['Overdue', overdue], ['Due today', dueToday], ['Planned for today', plannedToday]] as [name, list] (name)}
      {#if (list as TaskDto[]).length}
        <section class="suggest">
          <div class="row"><h2 class="label">{name}</h2><button class="btn ghost" onclick={() => addAll(list as TaskDto[])}>Add all to My Day</button></div>
          {#each list as TaskDto[] as t (t.id)}<TaskRow task={t} />{/each}
        </section>
      {/if}
    {/each}
  </div>
</div>

<style>
  .view { flex: 1; padding: 0 28px; }
  .view.docked { padding: 0 10px; }
  .inner { max-width: 820px; margin: 0 auto; padding: 26px 0 80px; }
  .docked .inner { padding: 12px 0 24px; }
  h1 { font: 700 28px/1.2 var(--font-display); display: flex; gap: 10px; align-items: center; }
  h1 :global(.icon) { color: var(--warn); }
  header p { margin: 4px 0 10px; }
  .banner { display: flex; align-items: center; gap: 10px; padding: 10px 12px; border-radius: 8px; background: var(--accent-wash); color: var(--accent-strong); margin: 8px 0; font-size: 14px; }
  .banner span { flex: 1; }
  section { margin-top: 12px; }
  .suggest { margin-top: 26px; }
  .row { display: flex; justify-content: space-between; align-items: center; }
</style>
