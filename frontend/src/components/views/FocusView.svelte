<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { focus, clock, hours, LENGTHS } from '../../lib/focus.svelte';
  import { tasks } from '../../lib/tasks.svelte';
  import { openNode, whereFrom } from '../../lib/links.svelte';
  import Icon from '../Icon.svelte';

  /** Focus (Release 1.1): start a timer on a task, see today's sessions and the last weeks' totals. */
  interface Session { start: string; end: string; minutes: number; taskId: string | null; label: string }
  interface Day { date: string; minutes: number; sessions: Session[] }
  interface Total { taskId: string | null; label: string; minutes: number; sessions: number }
  interface Summary { days: Day[]; byTask: Total[]; minutes: number }

  let summary = $state<Summary | null>(null);
  let span = $state(7);
  let taskId = $state('');
  let label = $state('');
  let length = $state(focus.lastLength);
  let custom = $state('');

  const open = $derived(tasks.list.filter((t) => t.workspace === app.workspace && (t.status === 'todo' || t.status === 'doing'))
    .sort((a, b) => Number(!!b.myDay) - Number(!!a.myDay) || Number(b.status === 'doing') - Number(a.status === 'doing') || a.title.localeCompare(b.title)));
  const today = $derived(summary?.days[summary.days.length - 1]);
  const most = $derived(Math.max(60, ...(summary?.days.map((d) => d.minutes) ?? [0])));

  async function load() {
    try { summary = await api.get<Summary>(`focus?workspace=${encodeURIComponent(app.workspace)}&days=${span}`); } catch (e) { app.error(e); }
  }
  onMount(() => { if (!tasks.list.length) tasks.load().catch(() => {}); });
  $effect(() => { void focus.logged; void span; void app.workspace; load(); });

  function start() {
    const minutes = custom ? Math.max(1, Math.min(240, Math.round(Number(custom)))) : length;
    if (!minutes) return;
    const t = open.find((x) => x.id === taskId);
    focus.start(minutes, t ? { id: t.id, title: t.title, workspace: t.workspace } : null, label);
  }

  const day = (iso: string) => new Date(iso + 'T12:00:00').toLocaleDateString(undefined, { weekday: 'short', day: 'numeric' });
</script>

<div class="scroll focus">
  <div class="inner">
    <h1><Icon name="timer" size={22} /> Focus</h1>

    {#if focus.running}
      <section class="timer" class:brk={focus.running.phase === 'break'}>
        <div class="big" aria-live="off">{clock(focus.remaining)}</div>
        <div class="what">{focus.running.phase === 'break' ? 'Break' : focus.running.title || 'Focus'}</div>
        <div class="row">
          <button class="btn" onclick={() => focus.stop()}>{focus.running.phase === 'break' ? 'End break' : 'Stop and log'}</button>
        </div>
      </section>
    {:else}
      <section class="setup">
        <div class="field"><span>On</span>
          <select class="input" aria-label="Task" bind:value={taskId}>
            <option value="">No task</option>
            {#each open as t (t.id)}<option value={t.id}>{t.myDay ? '☀ ' : ''}{t.title}</option>{/each}
          </select>
        </div>
        {#if !taskId}<div class="field"><span>For</span><input class="input" aria-label="What it's for" placeholder="What this session is for (optional)" bind:value={label} /></div>{/if}
        <div class="field"><span>Length</span>
          <div class="lengths" role="radiogroup" aria-label="Length">
            {#each LENGTHS as m (m)}<button role="radio" aria-checked={!custom && length === m} class:on={!custom && length === m} onclick={() => { length = m; custom = ''; }}>{m} min</button>{/each}
            <input class="input mins" aria-label="Other length in minutes" placeholder="Other" inputmode="numeric" bind:value={custom} />
          </div>
        </div>
        <div class="row"><button class="btn primary" onclick={start}><Icon name="play" size={14} /> Start focus</button>
          <span class="faint">Each session is noted in today's daily note{taskId ? ' and added to the task’s time spent' : ''}.</span></div>
      </section>
    {/if}

    {#if summary}
      <h2>Today <span class="faint">· {hours(today?.minutes ?? 0)}</span></h2>
      {#if today?.sessions.length}
        <ul class="sessions">
          {#each today.sessions as s, i (i)}
            <li><span class="mono">{s.start}–{s.end}</span><span class="mono faint">{s.minutes} min</span>
              {#if s.taskId}<button class="link" onclick={(e) => openNode(s.taskId!, whereFrom(e), s.label)}>{s.label}</button>{:else}<span>{s.label}</span>{/if}</li>
          {/each}
        </ul>
      {:else}<p class="faint">No focus sessions yet today.</p>{/if}

      <h2>
        Last <select class="input span" aria-label="Period" bind:value={span}><option value={7}>7 days</option><option value={14}>14 days</option><option value={30}>30 days</option></select>
        <span class="faint">· {hours(summary.minutes)}</span>
      </h2>
      <div class="chart" role="img" aria-label={`Focus minutes per day: ${summary.days.map((d) => `${day(d.date)} ${d.minutes}`).join(', ')}`}>
        {#each summary.days as d (d.date)}
          <div class="bar" title={`${day(d.date)}: ${hours(d.minutes)}`}><div class="fill" style={`height:${(d.minutes / most) * 100}%`}></div><span class="d">{span <= 14 ? day(d.date) : ''}</span></div>
        {/each}
      </div>
      {#if summary.byTask.length}
        <table class="totals">
          <thead><tr><th>On</th><th>Sessions</th><th>Time</th></tr></thead>
          <tbody>
            {#each summary.byTask as t (t.taskId ?? t.label)}
              <tr><td>{#if t.taskId}<button class="link" onclick={(e) => openNode(t.taskId!, whereFrom(e), t.label)}>{t.label}</button>{:else}{t.label || 'Unlabelled'}{/if}</td><td class="mono">{t.sessions}</td><td class="mono">{hours(t.minutes)}</td></tr>
            {/each}
          </tbody>
        </table>
      {/if}
    {/if}
  </div>
</div>

<style>
  .focus { flex: 1; }
  .inner { max-width: 760px; margin: 0 auto; padding: 24px 28px 40px; }
  h1 { display: flex; align-items: center; gap: 10px; font: 700 28px/1.2 var(--font-display); margin-bottom: 16px; }
  h2 { display: flex; align-items: center; gap: 8px; font: 700 16px/1.3 var(--font-display); margin: 26px 0 10px; }
  .timer { text-align: center; padding: 20px; border: 1px solid var(--line-strong); border-radius: 12px; background: var(--bg-raised); }
  .timer.brk .big { color: var(--ok); }
  .big { font: 700 64px/1 var(--font-mono); color: var(--accent-strong); letter-spacing: .02em; }
  .what { margin: 10px 0 14px; color: var(--text-soft); font-size: 16px; }
  .setup { display: grid; gap: 10px; padding: 16px; border: 1px solid var(--line); border-radius: 12px; background: var(--bg-raised); }
  .field { display: flex; align-items: center; gap: 10px; }
  .field > span { width: 60px; color: var(--text-soft); flex: none; }
  .field .input { flex: 1; }
  .lengths { display: flex; flex-wrap: wrap; gap: 6px; align-items: center; }
  .lengths button { border: 1px solid var(--line-strong); background: none; color: var(--text-soft); border-radius: 7px; padding: 5px 10px; font: 13px var(--font-body); cursor: pointer; }
  .lengths button.on { border-color: var(--accent); background: var(--accent-wash); color: var(--accent); }
  .lengths .mins { width: 80px; flex: none; }
  .row { display: flex; align-items: center; justify-content: center; gap: 12px; flex-wrap: wrap; }
  .setup .row { justify-content: flex-start; font-size: 13px; }
  .sessions { list-style: none; padding: 0; margin: 0; }
  .sessions li { display: flex; gap: 14px; align-items: baseline; padding: 5px 0; border-bottom: 1px solid var(--line); font-size: 14px; }
  .mono { font: 12.5px var(--font-mono); }
  .link { border: 0; background: none; padding: 0; color: var(--accent); font: inherit; cursor: pointer; text-align: left; }
  .link:hover { text-decoration: underline; }
  .span { width: auto; padding: 2px 6px; font-size: 14px; }
  .chart { display: flex; align-items: flex-end; gap: 4px; height: 140px; padding-bottom: 18px; border-bottom: 1px solid var(--line); }
  .bar { flex: 1; height: 100%; display: flex; flex-direction: column; justify-content: flex-end; position: relative; }
  .fill { background: var(--accent); border-radius: 3px 3px 0 0; min-height: 1px; opacity: .85; }
  .d { position: absolute; bottom: -17px; left: 0; right: 0; text-align: center; font: 10.5px var(--font-mono); color: var(--text-faint); white-space: nowrap; }
  .totals { width: 100%; border-collapse: collapse; margin-top: 14px; font-size: 14px; }
  .totals th { text-align: left; color: var(--text-muted); font-weight: 600; padding: 4px 6px; border-bottom: 1px solid var(--line); }
  .totals td { padding: 5px 6px; border-bottom: 1px solid var(--line); }
</style>
