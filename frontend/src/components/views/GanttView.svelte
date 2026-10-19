<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../../lib/state.svelte';
  import { day } from '../../lib/today.svelte';
  import { tasks, type TaskDto } from '../../lib/tasks.svelte';
  import { openNode, whereFrom } from '../../lib/links.svelte';
  import { addDays, daysBetween, layout as plan, linkId, linkTitle, moved, type Bar } from '../../lib/gantt';
  import Icon from '../Icon.svelte';

  /** Dependency Gantt (Release 1.2): tasks on a timeline from their own dates, with their dependencies. */
  let scale = $state<'day' | 'week' | 'month'>('day');
  let project = $state('');
  let showDone = $state(false);
  let drag = $state<{ id: string; mode: 'move' | 'end'; x: number; days: number } | null>(null);
  let scroller = $state<HTMLElement | null>(null);

  onMount(() => { if (!tasks.list.length) tasks.load().catch((e) => app.error(e)); });

  const PX = { day: 34, week: 14, month: 5 };
  const ROW = 30, HEAD = 44;
  const px = $derived(PX[scale]);

  const pool = $derived(tasks.list.filter((t) => t.workspace === app.workspace && !t.archived && t.status !== 'cancelled' && (showDone || t.status !== 'done')
    && (!project || linkId(t.project) === project || (project === '-' && !t.project))));
  const projects = $derived([...new Map(tasks.list.filter((t) => t.workspace === app.workspace && t.project).map((t) => [linkId(t.project) ?? t.project!, linkTitle(t.project)])).entries()].sort((a, b) => a[1].localeCompare(b[1])));
  const result = $derived(plan(pool));
  const byId = $derived(new Map(pool.map((t) => [t.id, t])));

  // The visible range: the tasks' span with some room, always including today.
  const from = $derived(addDays([result.from ?? day.today, day.today].sort()[0], scale === 'day' ? -3 : -14));
  const to = $derived(addDays([result.to ?? day.today, day.today].sort()[1], scale === 'day' ? 10 : 45));
  const total = $derived(daysBetween(from, to) + 1);
  const width = $derived(total * px);

  // Rows: a heading row whenever the project changes, then one row per bar.
  const rows = $derived.by(() => {
    const out: ({ kind: 'group'; label: string } | { kind: 'bar'; bar: Bar })[] = [];
    let group: string | null = null;
    for (const bar of result.bars) {
      if (bar.group !== group) { group = bar.group; out.push({ kind: 'group', label: group || 'No project' }); }
      out.push({ kind: 'bar', bar });
    }
    return out;
  });
  const rowOf = $derived(new Map(rows.flatMap((r, i) => (r.kind === 'bar' ? [[r.bar.id, i] as const] : []))));

  const x = (iso: string) => daysBetween(from, iso) * px;
  const shown = (b: Bar) => {
    if (drag?.id !== b.id) return b;
    return drag.mode === 'move' ? { ...b, start: addDays(b.start, drag.days), end: addDays(b.end, drag.days) } : { ...b, end: addDays(b.end, Math.max(drag.days, -daysBetween(b.start, b.end))) };
  };

  // Header ticks: days (with month names at the 1st), or Mondays / month starts when zoomed out.
  const ticks = $derived.by(() => {
    const out: { at: number; label: string; major: boolean }[] = [];
    for (let i = 0; i < total; i++) {
      const d = addDays(from, i);
      const date = new Date(d + 'T12:00:00');
      if (scale === 'day') out.push({ at: i * px, label: String(date.getDate()), major: date.getDate() === 1 || i === 0 });
      else if (scale === 'week' ? date.getDay() === 1 : date.getDate() === 1) out.push({ at: i * px, label: scale === 'week' ? `${date.getDate()} ${date.toLocaleDateString(undefined, { month: 'short' })}` : date.toLocaleDateString(undefined, { month: 'short', year: '2-digit' }), major: true });
    }
    return out;
  });
  const months = $derived(scale === 'day' ? ticks.filter((t) => t.major).map((t) => ({ at: t.at, label: new Date(addDays(from, Math.round(t.at / px)) + 'T12:00:00').toLocaleDateString(undefined, { month: 'long', year: 'numeric' }) })) : []);

  function down(e: PointerEvent, b: Bar, mode: 'move' | 'end') {
    if (e.button !== 0) return;
    e.preventDefault();
    e.stopPropagation();
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    drag = { id: b.id, mode, x: e.clientX, days: 0 };
  }
  function move(e: PointerEvent) { if (drag) drag.days = Math.round((e.clientX - drag.x) / px); }
  async function up(e: PointerEvent) {
    if (!drag) return;
    const d = drag;
    drag = null;
    if (!d.days) { if (d.mode === 'move') openTask(d.id, e); return; }
    await apply(d.id, d.mode, d.days);
  }

  async function apply(id: string, mode: 'move' | 'end', days: number) {
    const t = byId.get(id);
    const b = result.bars.find((x) => x.id === id);
    if (!t || !b) return;
    const set = mode === 'move' ? moved(t, days) : { due: addDays(b.end, Math.max(days, -daysBetween(b.start, b.end))), ...(t.planned || t.timeBlock ? {} : { planned: b.start }) };
    try { await tasks.update(id, set); } catch (err) { app.error(err); }
  }

  function keys(e: KeyboardEvent, b: Bar) {
    const step = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0;
    if (step) { e.preventDefault(); apply(b.id, e.shiftKey ? 'end' : 'move', step); }
    else if (e.key === 'Enter') { e.preventDefault(); openTask(b.id, e); }
  }

  function openTask(id: string, e?: MouseEvent | KeyboardEvent | PointerEvent) {
    const t = byId.get(id);
    openNode(id, e && (e.ctrlKey || e.shiftKey) ? whereFrom(e as MouseEvent) : 'new', t?.title);
  }

  async function schedule(t: TaskDto) {
    try { await tasks.update(t.id, { planned: day.today }); } catch (e) { app.error(e); }
  }

  function toToday() { scroller?.scrollTo({ left: Math.max(0, x(day.today) - 160), behavior: 'smooth' }); }
  $effect(() => { void scale; void from; queueMicrotask(toToday); });

  const edgePath = (fromBar: Bar, toBar: Bar, a: number, b: number) => {
    const x1 = x(fromBar.end) + px, y1 = HEAD + a * ROW + ROW / 2;
    const x2 = x(toBar.start), y2 = HEAD + b * ROW + ROW / 2;
    const mid = Math.max(x1 + 8, Math.min(x2 - 8, x1 + 12));
    return x2 >= x1 + 16 ? `M${x1},${y1} H${mid} V${y2} H${x2 - 2}` : `M${x1},${y1} h8 V${y1 + (y2 > y1 ? ROW / 2 : -ROW / 2)} H${x2 - 10} V${y2} H${x2 - 2}`;
  };
</script>

<div class="gantt-view">
  <div class="bar-top">
    <h1><Icon name="gantt" size={20} /> Gantt</h1>
    <select class="input" aria-label="Project" bind:value={project}>
      <option value="">All projects</option>
      {#each projects as [id, title] (id)}<option value={id}>{title}</option>{/each}
      <option value="-">No project</option>
    </select>
    <div class="seg" role="radiogroup" aria-label="Zoom">
      {#each ['day', 'week', 'month'] as s (s)}<button role="radio" aria-checked={scale === s} class:on={scale === s} onclick={() => (scale = s as typeof scale)}>{s[0].toUpperCase() + s.slice(1)}</button>{/each}
    </div>
    <label class="check"><input type="checkbox" bind:checked={showDone} /> Done tasks</label>
    <button class="btn ghost" onclick={toToday}>Today</button>
    <span class="faint hint">Drag a bar to move it, its right edge to change the due date; ← → (Shift to resize) on a focused bar.</span>
  </div>

  <div class="scroll chart" bind:this={scroller}>
    <div class="names">
      <div class="corner" style={`height:${HEAD}px`}></div>
      {#each rows as r, i (r.kind === 'group' ? 'g:' + r.label : r.bar.id)}
        {#if r.kind === 'group'}<div class="name group">{r.label}</div>
        {:else}
          <button class="name" class:done={r.bar.done} title={r.bar.title} onclick={(e) => openTask(r.bar.id, e)}>{r.bar.title}</button>
        {/if}
      {/each}
    </div>
    <div class="timeline">
      <div class="canvas" style={`width:${width}px;height:${HEAD + rows.length * ROW + 8}px`}>
        <div class="head" style={`height:${HEAD}px`}>
          {#each months as m (m.at)}<span class="month" style={`left:${m.at}px`}>{m.label}</span>{/each}
          {#each ticks as t (t.at)}<span class="tick" class:major={t.major} style={`left:${t.at}px;width:${scale === 'day' ? px : 'auto'}px`}>{t.label}</span>{/each}
        </div>
        {#each ticks as t (t.at)}<span class="grid" class:major={t.major} style={`left:${t.at}px;top:${HEAD}px`}></span>{/each}
        {#if scale === 'day'}
          {#each Array.from({ length: total }, (_, i) => i) as i (i)}
            {@const wd = new Date(addDays(from, i) + 'T12:00:00').getDay()}
            {#if wd === 0 || wd === 6}<span class="weekend" style={`left:${i * px}px;width:${px}px;top:${HEAD}px`}></span>{/if}
          {/each}
        {/if}
        <span class="today" style={`left:${x(day.today) + px / 2}px;top:${HEAD - 6}px`} title="Today"></span>

        {#each rows as r, i (r.kind === 'group' ? 'g:' + r.label : r.bar.id)}
          {#if r.kind === 'group'}<div class="grouprow" style={`top:${HEAD + i * ROW}px`}></div>
          {:else}
            {@const b = shown(r.bar)}
            <div class="gbar" class:done={b.done} class:est={b.estimated} class:dragging={drag?.id === b.id}
              style={`left:${x(b.start)}px;width:${(daysBetween(b.start, b.end) + 1) * px - 2}px;top:${HEAD + i * ROW + 5}px`}
              role="button" tabindex="0" aria-label={`${b.title}: ${b.start} to ${b.end}${b.estimated ? ' (estimated)' : ''}`}
              onpointerdown={(e) => down(e, r.bar, 'move')} onpointermove={move} onpointerup={up} onpointercancel={() => (drag = null)} onkeydown={(e) => keys(e, r.bar)}>
              <span class="lbl">{b.title}</span>
              <span class="grip" role="presentation" onpointerdown={(e) => down(e, r.bar, 'end')}></span>
            </div>
          {/if}
        {/each}

        <svg class="edges" width={width} height={HEAD + rows.length * ROW + 8} aria-hidden="true">
          <defs>
            <marker id="g-arrow" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto"><path d="M0,0 L8,4 L0,8 z" class="head-ok" /></marker>
            <marker id="g-arrow-late" viewBox="0 0 8 8" refX="7" refY="4" markerWidth="7" markerHeight="7" orient="auto"><path d="M0,0 L8,4 L0,8 z" class="head-late" /></marker>
          </defs>
          {#each result.edges as e (e.from + e.to)}
            {@const a = rowOf.get(e.from)}{@const b = rowOf.get(e.to)}
            {#if a !== undefined && b !== undefined}
              {@const fb = shown(result.bars.find((x) => x.id === e.from)!)}{@const tb = shown(result.bars.find((x) => x.id === e.to)!)}
              {@const late = !fb.done && tb.start <= fb.end}
              <path d={edgePath(fb, tb, a, b)} class:late marker-end={late ? 'url(#g-arrow-late)' : 'url(#g-arrow)'}><title>{late ? 'Starts before the task it depends on is due to finish' : 'Depends on'}</title></path>
            {/if}
          {/each}
        </svg>
      </div>
    </div>
  </div>

  {#if result.edges.some((e) => e.late)}
    <p class="warn"><Icon name="alert" size={14} /> {result.edges.filter((e) => e.late).length} {result.edges.filter((e) => e.late).length === 1 ? 'task starts' : 'tasks start'} before what {result.edges.filter((e) => e.late).length === 1 ? 'it depends' : 'they depend'} on is due to finish (red arrows).</p>
  {/if}

  {#if result.unscheduled.length}
    <details class="unscheduled">
      <summary>{result.unscheduled.length} without dates</summary>
      {#each result.unscheduled as t (t.id)}
        <div class="un"><button class="link" onclick={(e) => openTask(t.id, e)}>{t.title}</button><button class="btn ghost" onclick={() => schedule(t as TaskDto)}>Plan for today</button></div>
      {/each}
    </details>
  {/if}
</div>

<style>
  .gantt-view { flex: 1; display: flex; flex-direction: column; min-height: 0; padding: 14px 18px 10px; gap: 10px; }
  .bar-top { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
  h1 { display: flex; align-items: center; gap: 8px; font: 700 22px/1.2 var(--font-display); margin-right: 8px; }
  .bar-top .input { width: auto; }
  .seg { display: inline-flex; border: 1px solid var(--line-strong); border-radius: 7px; overflow: hidden; }
  .seg button { border: 0; background: none; color: var(--text-muted); padding: 5px 10px; font: 13px var(--font-body); cursor: pointer; }
  .seg button + button { border-left: 1px solid var(--line-strong); }
  .seg button.on { background: var(--accent-wash); color: var(--accent); }
  .check { display: inline-flex; gap: 6px; align-items: center; font-size: 13px; color: var(--text-muted); }
  .hint { font-size: 12px; }
  .chart { flex: 1; min-height: 0; display: flex; align-items: flex-start; border: 1px solid var(--line); border-radius: 10px; background: var(--bg-raised); }
  .names { position: sticky; left: 0; z-index: 4; width: 220px; flex: none; border-right: 1px solid var(--line); background: var(--bg-raised); }
  .corner { position: sticky; top: 0; z-index: 5; background: var(--bg-raised); border-bottom: 1px solid var(--line); }
  .name { display: block; width: 100%; height: 30px; line-height: 30px; padding: 0 10px; border: 0; background: none; color: var(--text-soft); font: 13.5px var(--font-body); text-align: left; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer; }
  .name:hover { color: var(--accent); }
  .name.done { color: var(--text-faint); text-decoration: line-through; }
  .name.group { font: 700 11px/30px var(--font-mono); letter-spacing: .05em; text-transform: uppercase; color: var(--text-faint); cursor: default; }
  .timeline { position: relative; flex: none; }
  .canvas { position: relative; }
  .head { position: sticky; top: 0; z-index: 3; background: var(--bg-raised); border-bottom: 1px solid var(--line); }
  .month { position: absolute; top: 4px; font: 600 11px var(--font-body); color: var(--text-soft); padding-left: 4px; white-space: nowrap; }
  .tick { position: absolute; bottom: 4px; font: 10.5px var(--font-mono); color: var(--text-faint); text-align: center; white-space: nowrap; }
  .tick.major { color: var(--text-muted); }
  .grid { position: absolute; bottom: 0; width: 1px; background: var(--line); opacity: .35; }
  .grid.major { opacity: .8; }
  .weekend { position: absolute; bottom: 0; background: color-mix(in srgb, var(--text) 3%, transparent); }
  .today { position: absolute; bottom: 0; width: 2px; background: var(--accent); z-index: 2; opacity: .8; }
  .grouprow { position: absolute; left: 0; right: 0; height: 30px; border-top: 1px solid var(--line); }
  .gbar { position: absolute; height: 20px; border-radius: 5px; background: color-mix(in srgb, var(--accent) 75%, transparent); border: 1px solid var(--accent); color: var(--accent-ink); font: 600 11.5px/18px var(--font-body); padding: 0 6px; cursor: grab; z-index: 2; touch-action: none; user-select: none; display: flex; align-items: center; }
  .gbar:focus-visible { outline: 2px solid var(--text); outline-offset: 1px; }
  .gbar.est { background: repeating-linear-gradient(135deg, color-mix(in srgb, var(--accent) 60%, transparent) 0 6px, color-mix(in srgb, var(--accent) 40%, transparent) 6px 12px); }
  .gbar.done { background: var(--line-strong); border-color: var(--line-strong); color: var(--text-muted); }
  .gbar.dragging { cursor: grabbing; box-shadow: var(--shadow); }
  .lbl { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; pointer-events: none; flex: 1; }
  .grip { width: 8px; align-self: stretch; margin-right: -6px; cursor: ew-resize; }
  .edges { position: absolute; inset: 0; pointer-events: none; z-index: 1; }
  .edges path { fill: none; stroke: var(--text-muted); stroke-width: 1.4; pointer-events: stroke; }
  .edges path.late { stroke: var(--danger); stroke-width: 1.8; }
  .head-ok { fill: var(--text-muted); }
  .head-late { fill: var(--danger); }
  .warn { display: flex; gap: 6px; align-items: center; color: var(--danger); font-size: 13px; margin: 0; }
  .unscheduled summary { cursor: pointer; font-size: 13px; color: var(--text-muted); }
  .un { display: flex; align-items: center; gap: 10px; padding: 3px 0; font-size: 13.5px; }
  .link { border: 0; background: none; color: var(--text); font: inherit; padding: 0; cursor: pointer; }
  .link:hover { color: var(--accent); }
  @container pane (max-width: 760px) {
    .names { width: 120px; }
    .hint { display: none; }
  }
</style>
