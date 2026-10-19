<script lang="ts">
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { day } from '../../lib/today.svelte';
  import { openNode, whereFrom } from '../../lib/links.svelte';
  import { tasks, type TaskDto } from '../../lib/tasks.svelte';
  import { onChange } from '../../lib/events';
  import { hours } from '../../lib/focus.svelte';
  import Icon from '../Icon.svelte';

  /** Sprints (Release 1.1): time-boxed plans made of ordinary tasks, with a burndown. */
  interface Sprint { id: string; title: string; start: string | null; end: string | null; goal: string | null; state: 'planned' | 'active' | 'done'; capacity: number | null; tasks: number; done: number; effort: number; effortDone: number }
  interface Burn { date: string; remaining: number | null; ideal: number }
  interface Detail { sprint: Sprint; tasks: TaskDto[]; burndown: Burn[]; unit: 'min' | 'tasks' }

  let sprints = $state<Sprint[]>([]);
  let selected = $state<string | null>(null);
  let detail = $state<Detail | null>(null);
  let backlog = $state<TaskDto[]>([]);
  let adding = $state(false);
  let creating = $state<{ title: string; start: string; weeks: number; goal: string; hours: string } | null>(null);
  let carryTo = $state('');

  async function load() {
    try {
      sprints = await api.get<Sprint[]>(`plan/sprints?workspace=${encodeURIComponent(app.workspace)}`);
      if (!selected || !sprints.some((s) => s.id === selected)) selected = (sprints.find((s) => s.state === 'active') ?? sprints[0])?.id ?? null;
      await loadDetail();
    } catch (e) { app.error(e); }
  }
  async function loadDetail() {
    detail = selected ? await api.get<Detail>(`plan/sprints/${selected}`) : null;
    if (adding) backlog = await api.get<TaskDto[]>(`plan/backlog?workspace=${encodeURIComponent(app.workspace)}`);
  }
  $effect(() => { void app.workspace; load(); });
  $effect(() => onChange((e) => { if (['Updated', 'Created', 'Trashed', 'Restored'].includes(e.kind)) schedule(); }));
  let pending: ReturnType<typeof setTimeout> | undefined;
  function schedule() { clearTimeout(pending); pending = setTimeout(load, 400); }

  function pick(id: string) { selected = id; adding = false; loadDetail().catch((e) => app.error(e)); }

  function nextMonday(from: string) {
    const d = new Date(from + 'T12:00:00');
    const add = (8 - d.getDay()) % 7;
    d.setDate(d.getDate() + (add === 0 ? 0 : add));
    return d.toISOString().slice(0, 10);
  }

  function startCreate() {
    const last = sprints[0];
    const start = last?.end && last.end >= day.today ? nextMonday(new Date(new Date(last.end + 'T12:00:00').getTime() + 86400000).toISOString().slice(0, 10)) : day.today;
    creating = { title: `Sprint ${sprints.length + 1}`, start, weeks: 2, goal: '', hours: '' };
  }

  async function create() {
    if (!creating) return;
    const s = new Date(creating.start + 'T12:00:00');
    s.setDate(s.getDate() + creating.weeks * 7 - 1);
    const end = s.toISOString().slice(0, 10);
    const capacity = creating.hours.trim() ? Math.round(Number(creating.hours) * 60) : null;
    try {
      const node = await api.post<{ id: string }>('plan/sprints', { workspace: app.workspace, title: creating.title, start: creating.start, end, goal: creating.goal || null, capacity });
      creating = null; selected = node.id; await load();
    } catch (e) { app.error(e); }
  }

  async function addTask(t: TaskDto) {
    if (!detail) return;
    await tasks.update(t.id, { sprint: `[[${detail.sprint.id}|${detail.sprint.title}]]` });
    backlog = backlog.filter((x) => x.id !== t.id);
    await loadDetail();
  }
  async function removeTask(t: TaskDto) { await tasks.update(t.id, {}, ['sprint']); await loadDetail(); }
  async function toggle(t: TaskDto) { await tasks.setStatus(t.id, t.status === 'done' ? 'todo' : 'done'); await loadDetail(); }

  async function carry() {
    if (!detail) return;
    try {
      const r = await api.post<{ moved: number }>(`plan/sprints/${detail.sprint.id}/carry`, { to: carryTo || null });
      app.toast(`Moved ${r.moved} unfinished ${r.moved === 1 ? 'task' : 'tasks'} ${carryTo ? 'to ' + sprints.find((s) => s.id === carryTo)?.title : 'back to the backlog'}.`, 'ok');
      await load();
    } catch (e) { app.error(e); }
  }

  async function toggleBacklog() {
    adding = !adding;
    if (adding) backlog = await api.get<TaskDto[]>(`plan/backlog?workspace=${encodeURIComponent(app.workspace)}`);
  }

  const groups = $derived(detail ? (['doing', 'todo', 'waiting', 'done'] as const).map((s) => ({ status: s, list: detail!.tasks.filter((t) => t.status === s) })).filter((g) => g.list.length) : []);
  const labels: Record<string, string> = { doing: 'Doing', todo: 'To do', waiting: 'Waiting', done: 'Done' };
  const others = $derived(sprints.filter((s) => s.id !== selected && s.state !== 'done'));
  const range = (s: Sprint) => s.start && s.end ? `${new Date(s.start + 'T12:00:00').toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} – ${new Date(s.end + 'T12:00:00').toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}` : '';
  const pct = (a: number, b: number) => (b ? Math.round((a / b) * 100) : 0);

  // Burndown chart geometry.
  const W = 560, H = 180, P = 28;
  const chart = $derived.by(() => {
    if (!detail?.burndown.length) return null;
    const pts = detail.burndown;
    const max = Math.max(1, ...pts.map((p) => Math.max(p.ideal, p.remaining ?? 0)));
    const x = (i: number) => P + (i * (W - P * 2)) / Math.max(1, pts.length - 1);
    const y = (v: number) => H - P + -((v / max) * (H - P * 2));
    const ideal = pts.map((p, i) => `${x(i)},${y(p.ideal)}`).join(' ');
    const actual = pts.flatMap((p, i) => (p.remaining === null ? [] : [`${x(i)},${y(p.remaining)}`])).join(' ');
    return { ideal, actual, max, x, y, pts };
  });
</script>

<div class="sprints">
  <aside class="scroll list">
    <div class="head"><h2 class="label">Sprints</h2><button class="icon-btn" title="New sprint" aria-label="New sprint" onclick={startCreate}><Icon name="plus" size={15} /></button></div>
    {#each sprints as s (s.id)}
      <button class="sp" class:on={s.id === selected} aria-pressed={s.id === selected} onclick={() => pick(s.id)}>
        <span class="t">{s.title} <span class="state {s.state}">{s.state}</span></span>
        <span class="faint">{range(s)} · {s.done}/{s.tasks}</span>
        <span class="bar"><span style={`width:${pct(s.done, s.tasks)}%`}></span></span>
      </button>
    {:else}
      <p class="faint">No sprints yet.</p>
    {/each}
  </aside>

  <section class="scroll main">
    {#if creating}
      <form class="create" onsubmit={(e) => { e.preventDefault(); create(); }}>
        <h1>New sprint</h1>
        <label>Name <input class="input" bind:value={creating.title} /></label>
        <label>Starts <input class="input" type="date" bind:value={creating.start} /></label>
        <label>Length <select class="input" bind:value={creating.weeks}><option value={1}>1 week</option><option value={2}>2 weeks</option><option value={3}>3 weeks</option><option value={4}>4 weeks</option></select></label>
        <label>Goal <input class="input" placeholder="What this sprint is for (optional)" bind:value={creating.goal} /></label>
        <label>Capacity <input class="input" inputmode="decimal" placeholder="Hours available (optional)" bind:value={creating.hours} /></label>
        <div class="row"><button class="btn primary" type="submit" disabled={!creating.title.trim() || !creating.start}>Create sprint</button><button class="btn ghost" type="button" onclick={() => (creating = null)}>Cancel</button></div>
      </form>
    {:else if detail}
      {@const s = detail.sprint}
      <header>
        <h1><button class="link" onclick={(e) => openNode(s.id, whereFrom(e) === 'current' ? 'new' : whereFrom(e), s.title)}>{s.title}</button> <span class="state {s.state}">{s.state}</span></h1>
        <p class="faint">{range(s)}{s.goal ? ` · ${s.goal}` : ''}</p>
      </header>

      <div class="cards">
        <div class="card"><b>{s.done}/{s.tasks}</b><span>tasks done</span></div>
        <div class="card"><b>{hours(s.effortDone)} / {hours(s.effort)}</b><span>estimated work done</span></div>
        {#if s.capacity}<div class="card" class:over={s.effort > s.capacity}><b>{pct(s.effort, s.capacity)}%</b><span>of {hours(s.capacity)} capacity planned</span></div>{/if}
      </div>

      {#if chart}
        <figure class="burn">
          <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={`Burndown: ${detail.unit === 'min' ? 'minutes' : 'tasks'} remaining per day`}>
            <line x1={P} y1={H - P} x2={W - P} y2={H - P} class="axis" />
            <line x1={P} y1={P} x2={P} y2={H - P} class="axis" />
            <text x={P - 6} y={P + 4} class="lbl" text-anchor="end">{detail.unit === 'min' ? hours(chart.max) : chart.max}</text>
            <polyline points={chart.ideal} class="ideal" />
            {#if chart.actual}<polyline points={chart.actual} class="actual" />{/if}
            {#each chart.pts as p, i (p.date)}
              {#if p.remaining !== null}<circle cx={chart.x(i)} cy={chart.y(p.remaining)} r="2.5" class="dot"><title>{p.date}: {detail.unit === 'min' ? hours(p.remaining) : p.remaining} left</title></circle>{/if}
            {/each}
          </svg>
          <figcaption class="faint">Remaining {detail.unit === 'min' ? 'estimated work' : 'tasks (add estimates to every task to burn down by time)'}; the dashed line is an even pace.</figcaption>
        </figure>
      {/if}

      <div class="toolbar">
        <button class="btn" onclick={toggleBacklog}><Icon name="plus" size={14} /> {adding ? 'Done adding' : 'Add tasks from the backlog'}</button>
        {#if s.done < s.tasks}
          <span class="carry">
            <select class="input" aria-label="Move unfinished tasks to" bind:value={carryTo}><option value="">Backlog</option>{#each others as o (o.id)}<option value={o.id}>{o.title}</option>{/each}</select>
            <button class="btn ghost" onclick={carry}>Move unfinished</button>
          </span>
        {/if}
      </div>

      {#if adding}
        <div class="backlog">
          {#each backlog as t (t.id)}
            <div class="task"><button class="icon-btn" aria-label={`Add ${t.title}`} onclick={() => addTask(t)}><Icon name="plus" size={14} /></button><span class="tt">{t.title}</span>{#if t.effort}<span class="faint mono">{hours(t.effort)}</span>{/if}{#if t.due}<span class="faint mono">due {t.due.slice(0, 10)}</span>{/if}</div>
          {:else}<p class="faint">The backlog is empty: every open task is in a sprint.</p>{/each}
        </div>
      {/if}

      {#each groups as g (g.status)}
        <h3>{labels[g.status]} <span class="faint">· {g.list.length}</span></h3>
        {#each g.list as t (t.id)}
          <div class="task" class:done={t.status === 'done'}>
            <input type="checkbox" aria-label={`Done: ${t.title}`} checked={t.status === 'done'} onchange={() => toggle(t)} />
            <button class="tt link" onclick={(e) => openNode(t.id, whereFrom(e) === 'current' ? 'new' : whereFrom(e), t.title)}>{t.title}</button>
            {#if t.effort}<span class="faint mono">{hours(t.effort)}</span>{/if}
            {#if t.blocked}<span class="chip warn">blocked</span>{/if}
            <button class="icon-btn rm" title="Take out of this sprint" aria-label={`Take ${t.title} out of the sprint`} onclick={() => removeTask(t)}><Icon name="x" size={13} /></button>
          </div>
        {/each}
      {:else}
        {#if !adding}<p class="faint">No tasks in this sprint yet. Add some from the backlog, or set a task's <code>sprint</code> property.</p>{/if}
      {/each}
    {:else}
      <div class="empty-state"><h3>Plan in sprints</h3><p>A sprint is a short stretch of time with a set of tasks and an optional goal and capacity.</p><button class="btn primary" onclick={startCreate}>New sprint</button></div>
    {/if}
  </section>
</div>

<style>
  .sprints { flex: 1; display: grid; grid-template-columns: 260px 1fr; min-height: 0; }
  .list { border-right: 1px solid var(--line); padding: 14px 10px; }
  .head { display: flex; align-items: center; justify-content: space-between; margin: 0 6px 8px; }
  .sp { display: grid; gap: 3px; width: 100%; text-align: left; border: 0; background: none; color: var(--text-soft); padding: 8px 10px; border-radius: 7px; font-size: 13.5px; cursor: pointer; }
  .sp:hover { background: var(--bg-hover); }
  .sp.on { background: var(--bg-active); color: var(--text); }
  .sp .t { font-weight: 600; display: flex; gap: 6px; align-items: center; }
  .sp .faint { font-size: 12px; color: var(--text-muted); }
  .bar { height: 4px; background: var(--line); border-radius: 2px; overflow: hidden; }
  .bar span { display: block; height: 100%; background: var(--accent); }
  .state { font: 600 10px/1 var(--font-mono); text-transform: uppercase; letter-spacing: .04em; padding: 3px 6px; border-radius: 4px; border: 1px solid var(--line-strong); color: var(--text-muted); }
  .state.active { color: var(--ok); border-color: color-mix(in srgb, var(--ok) 50%, transparent); }
  .state.planned { color: var(--accent); border-color: color-mix(in srgb, var(--accent) 50%, transparent); }
  .main { padding: 22px 28px 40px; }
  h1 { display: flex; align-items: center; gap: 10px; font: 700 26px/1.2 var(--font-display); margin: 0 0 4px; }
  h3 { font: 700 14px/1.3 var(--font-display); margin: 18px 0 6px; }
  .link { border: 0; background: none; padding: 0; color: inherit; font: inherit; cursor: pointer; text-align: left; }
  .link:hover { color: var(--accent); }
  .cards { display: flex; flex-wrap: wrap; gap: 10px; margin: 16px 0; }
  .card { display: grid; gap: 2px; padding: 10px 14px; border: 1px solid var(--line); border-radius: 10px; background: var(--bg-raised); min-width: 140px; }
  .card b { font: 700 18px var(--font-mono); }
  .card span { font-size: 12px; color: var(--text-muted); }
  .card.over b { color: var(--warn); }
  .burn { margin: 0 0 14px; max-width: 620px; }
  .burn svg { width: 100%; height: auto; }
  .axis { stroke: var(--line-strong); }
  .ideal { fill: none; stroke: var(--text-faint); stroke-dasharray: 4 4; }
  .actual { fill: none; stroke: var(--accent); stroke-width: 2; }
  .dot { fill: var(--accent); }
  .lbl { fill: var(--text-faint); font: 10px var(--font-mono); }
  figcaption { font-size: 12px; }
  .toolbar { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin: 6px 0; }
  .carry { display: inline-flex; gap: 6px; margin-left: auto; }
  .carry .input { width: auto; }
  .backlog { border: 1px dashed var(--line-strong); border-radius: 10px; padding: 6px 10px; margin: 8px 0 14px; max-height: 280px; overflow: auto; }
  .task { display: flex; align-items: center; gap: 8px; padding: 4px 0; font-size: 14px; border-bottom: 1px solid var(--line); }
  .task .tt { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .task.done .tt { color: var(--text-faint); text-decoration: line-through; }
  .task .rm { opacity: 0; }
  .task:hover .rm, .task .rm:focus-visible { opacity: 1; }
  .mono { font: 12px var(--font-mono); }
  .chip.warn { font: 600 10.5px var(--font-mono); color: var(--warn); }
  .create { display: grid; gap: 10px; max-width: 460px; }
  .create label { display: grid; gap: 4px; font-size: 13px; color: var(--text-soft); }
  .row { display: flex; gap: 6px; }
  @container pane (max-width: 760px) {
    .sprints { grid-template-columns: 1fr; grid-template-rows: auto 1fr; }
    .list { border-right: 0; border-bottom: 1px solid var(--line); max-height: 200px; }
  }
</style>
