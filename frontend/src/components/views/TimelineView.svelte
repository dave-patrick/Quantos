<script lang="ts">
  import { tasks, type TaskDto } from '../../lib/tasks.svelte';
  import { app } from '../../lib/state.svelte';
  import { addDays, isoDate, longDate } from '../../lib/dates';
  import { openNode, whereFrom } from '../../lib/links.svelte';
  import TaskRow from '../TaskRow.svelte';
  import Icon from '../Icon.svelte';

  let { today: watched }: { today: string } = $props();
  let day = $state(isoDate());
  const startHour = 6, endHour = 23, slot = 15, rowPx = 14; // 15-minute rows
  const rows = ((endHour - startHour) * 60) / slot;

  const mine = $derived(tasks.list.filter((t) => t.workspace === app.workspace && !t.archived));
  const blocks = $derived(mine.filter((t) => t.timeBlock && t.timeBlock.start.slice(0, 10) === day));
  const unscheduled = $derived(mine.filter((t) => t.status !== 'done' && t.status !== 'cancelled' && !blocks.includes(t) &&
    (t.myDay === day || t.planned?.slice(0, 10) === day || t.due?.slice(0, 10) === day || (day === isoDate() && !!t.due && t.due.slice(0, 10) < day))));

  /** Local wall-clock offset string, so blocks are recorded with this device's time zone. */
  function stamp(date: string, minutes: number) {
    const d = new Date(date + 'T00:00:00');
    d.setMinutes(minutes);
    const off = -d.getTimezoneOffset();
    const sign = off >= 0 ? '+' : '-';
    const pad = (n: number) => String(Math.floor(Math.abs(n))).padStart(2, '0');
    return `${date}T${pad(d.getHours())}:${pad(d.getMinutes())}:00${sign}${pad(off / 60)}:${pad(off % 60)}`;
  }
  const minutesOf = (iso: string) => { const d = new Date(iso); return d.getHours() * 60 + d.getMinutes(); };
  const top = (iso: string) => ((minutesOf(iso) - startHour * 60) / slot) * rowPx;
  const height = (t: TaskDto) => Math.max(rowPx, ((minutesOf(t.timeBlock!.end) - minutesOf(t.timeBlock!.start)) / slot) * rowPx);

  let dragId: string | null = null;
  let dragOffset = 0;

  async function dropAt(e: DragEvent, grid: HTMLElement) {
    const id = dragId; dragId = null;
    if (!id) return;
    const t = mine.find((x) => x.id === id);
    if (!t) return;
    const y = e.clientY - grid.getBoundingClientRect().top - dragOffset;
    const start = startHour * 60 + Math.max(0, Math.round(y / rowPx)) * slot;
    const length = t.timeBlock ? minutesOf(t.timeBlock.end) - minutesOf(t.timeBlock.start) : Math.max(15, Math.min(240, t.effort ?? 30));
    try { await tasks.update(id, { time_block: { start: stamp(day, start), end: stamp(day, start + length) } }); }
    catch (err) { app.error(err); }
  }

  async function resize(t: TaskDto, delta: number) {
    const s = minutesOf(t.timeBlock!.start), e = minutesOf(t.timeBlock!.end);
    const end = Math.max(s + slot, e + delta);
    await tasks.update(t.id, { time_block: { start: t.timeBlock!.start, end: stamp(day, end) } }).catch((err) => app.error(err));
  }

  async function unschedule(t: TaskDto) {
    await tasks.update(t.id, { time_block: null }).catch((err) => app.error(err));
  }

  let grid = $state<HTMLDivElement | null>(null);
  const nowLine = $derived(day === isoDate() ? ((new Date().getHours() * 60 + new Date().getMinutes() - startHour * 60) / slot) * rowPx : -1);
  $effect(() => { void watched; });
</script>

<div class="timeline">
  <aside class="scroll side">
    <h2 class="label">To schedule</h2>
    <p class="faint small">Drag a task onto the day to block time for it.</p>
    {#each unscheduled as t (t.id)}
      <div class="drag"><TaskRow task={t} compact draggable ondragstart={(e) => { dragId = t.id; dragOffset = 0; e.dataTransfer?.setData('text/plain', t.title); }} /></div>
    {:else}
      <p class="faint small">Nothing waiting. Tasks planned or due this day, and My Day picks, appear here.</p>
    {/each}
  </aside>
  <section class="scroll main">
    <header>
      <button class="icon-btn" aria-label="Previous day" onclick={() => (day = addDays(day, -1))}><Icon name="arrow-left" /></button>
      <h1>{longDate(day)}</h1>
      <button class="icon-btn" aria-label="Next day" onclick={() => (day = addDays(day, 1))}><Icon name="arrow-right" /></button>
      <button class="btn ghost" onclick={() => (day = isoDate())}>Today</button>
    </header>
    <div class="grid" bind:this={grid} style="height:{rows * rowPx}px" role="list"
      ondragover={(e) => e.preventDefault()} ondrop={(e) => grid && dropAt(e, grid)}>
      {#each Array(endHour - startHour) as _, h (h)}
        <div class="hour" style="top:{h * (60 / slot) * rowPx}px"><span>{String(startHour + h).padStart(2, '0')}:00</span></div>
      {/each}
      {#if nowLine >= 0}<div class="now" style="top:{nowLine}px"></div>{/if}
      {#each blocks as t (t.id)}
        <div class="block p{t.priority ?? 0}" class:done={t.status === 'done'} style="top:{top(t.timeBlock!.start)}px; height:{height(t)}px" role="listitem"
          draggable="true" ondragstart={(e) => { dragId = t.id; dragOffset = e.offsetY; }}>
          <button class="name" onclick={(e) => openNode(t.id, whereFrom(e), t.title)}>{t.title}</button>
          <span class="time">{t.timeBlock!.start.slice(11, 16)}–{t.timeBlock!.end.slice(11, 16)}</span>
          <span class="tools">
            <button title="Shorter" onclick={() => resize(t, -slot)}>−</button>
            <button title="Longer" onclick={() => resize(t, slot)}>+</button>
            <button title="Remove from timeline" onclick={() => unschedule(t)}>×</button>
          </span>
        </div>
      {/each}
    </div>
  </section>
</div>

<style>
  .timeline { flex: 1; display: grid; grid-template-columns: 300px 1fr; min-height: 0; }
  .side { border-right: 1px solid var(--line); padding: 16px 10px; }
  .small { font-size: 12.5px; margin: 4px 0 10px; }
  .drag { cursor: grab; }
  .main { padding: 0 24px 40px; }
  header { display: flex; align-items: center; gap: 8px; padding: 16px 0; position: sticky; top: 0; background: var(--bg); z-index: 2; }
  h1 { font: 700 20px/1.2 var(--font-display); }
  .grid { position: relative; margin-left: 56px; border-left: 1px solid var(--line); }
  .hour { position: absolute; left: 0; right: 0; border-top: 1px solid var(--line); }
  .hour span { position: absolute; left: -54px; top: -8px; font: 11px var(--font-mono); color: var(--text-faint); }
  .now { position: absolute; left: -4px; right: 0; border-top: 2px solid var(--danger); z-index: 1; }
  .block { position: absolute; left: 6px; right: 6px; border-radius: 6px; background: var(--accent-wash); border: 1px solid color-mix(in srgb, var(--accent) 50%, transparent);
    border-left: 4px solid var(--accent); padding: 2px 8px; display: flex; gap: 8px; align-items: flex-start; overflow: hidden; cursor: grab; }
  .block.p1 { border-left-color: var(--p1); } .block.p2 { border-left-color: var(--p2); }
  .block.done { opacity: .55; }
  .name { border: 0; background: none; color: var(--text); padding: 0; font-weight: 600; font-size: 13px; text-align: left; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .time { font: 11px var(--font-mono); color: var(--text-muted); }
  .tools { display: none; gap: 2px; }
  .block:hover .tools { display: flex; }
  .tools button { border: 0; background: var(--bg-raised); color: var(--text-soft); width: 18px; height: 18px; border-radius: 4px; font-size: 12px; line-height: 1; }
  @container pane (max-width: 760px) {
    .timeline { grid-template-columns: 1fr; grid-template-rows: auto 1fr; }
    .side { border-right: 0; border-bottom: 1px solid var(--line); max-height: 200px; }
  }
</style>
