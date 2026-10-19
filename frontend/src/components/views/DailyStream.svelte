<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { onChange } from '../../lib/events';
  import { addDays, isoDate } from '../../lib/dates';
  import type { Tab } from '../../lib/layout.svelte';
  import type { NodeDto } from '../../lib/types';
  import DayEntry from '../DayEntry.svelte';
  import { prefs } from '../../lib/prefs.svelte';
  import { tasks } from '../../lib/tasks.svelte';

  let { tab, today }: { tab: Tab; today: string } = $props();
  void tab;

  let days = $state<{ date: string; node: NodeDto | null }[]>([]);
  let loading = $state(false);
  let sentinel = $state<HTMLDivElement | null>(null);
  let currentToday = $state(isoDate());
  const workspace = app.workspace;

  async function load(from: string, to: string, append: boolean) {
    loading = true;
    try {
      const batch = await api.get<{ date: string; node: NodeDto | null }[]>(`daily-stream/${workspace}?from=${from}&to=${to}`);
      days = append ? [...days, ...batch] : batch;
    } finally { loading = false; }
  }

  async function reload() {
    currentToday = isoDate();
    await load(addDays(currentToday, -13), addDays(currentToday, 1), false);
  }

  async function more() {
    if (loading || !days.length) return;
    const last = days[days.length - 1].date;
    await load(addDays(last, -14), addDays(last, -1), true);
  }

  onMount(() => {
    reload();
    const off = onChange((e) => {
      if ((e.kind === 'Created' || e.kind === 'Restored') && e.path?.includes('/daily/')) {
        const date = /(\d{4}-\d{2}-\d{2})\.md$/.exec(e.path)?.[1];
        const day = days.find((d) => d.date === date);
        if (day && !day.node && e.id) api.get<NodeDto>(`nodes/${e.id}`).then((n) => (day.node = n)).catch(() => {});
      }
    });
    // A daily note created in another window (quick capture) appears here even if its live update was missed.
    const findNew = () => {
      if (document.visibilityState !== 'visible') return;
      for (const day of days.filter((d) => !d.node && d.date <= currentToday && d.date >= addDays(currentToday, -1)))
        api.get<NodeDto>(`daily/${workspace}/${day.date}`).then((n) => { if (!day.node) day.node = n; }).catch(() => {});
    };
    addEventListener('focus', findNew);
    document.addEventListener('visibilitychange', findNew);
    const io = new IntersectionObserver((entries) => { if (entries.some((x) => x.isIntersecting)) more(); }, { rootMargin: '600px' });
    const t = setInterval(() => { if (sentinel) { io.observe(sentinel); clearInterval(t); } }, 100);
    return () => { off(); io.disconnect(); clearInterval(t); removeEventListener('focus', findNew); document.removeEventListener('visibilitychange', findNew); };
  });

  // What each day holds: its note's words and the tasks planned, due or done that day.
  $effect(() => { if (!tasks.list.length) tasks.load().catch(() => {}); });
  const taskDays = $derived.by(() => {
    const count = new Map<string, number>();
    for (const t of tasks.list) {
      if (t.workspace !== workspace || t.status === 'cancelled') continue;
      for (const d of new Set([t.planned, t.due, t.completed].map((x) => x?.slice(0, 10)).filter(Boolean) as string[])) count.set(d, (count.get(d) ?? 0) + 1);
    }
    return count;
  });
  function summary(day: { date: string; node: NodeDto | null }) {
    const words = day.node ? (day.node.body.match(/[\p{L}\p{N}]+/gu) ?? []).length : 0;
    const n = taskDays.get(day.date) ?? 0;
    return { has: words > 0 || n > 0, text: [words ? `${words} ${words === 1 ? 'word' : 'words'}` : '', n ? `${n} ${n === 1 ? 'task' : 'tasks'}` : ''].filter(Boolean).join(' · ') };
  }
  const shown = $derived(prefs.dailyHideEmpty ? days.filter((d) => d.date === currentToday || summary(d).has) : days);

  // Keep editing past midnight: the new day appears without losing the open entry.
  $effect(() => { if (today && today !== currentToday) reload(); });
</script>

<div class="scroll stream">
  <div class="inner">
    <header class="head">
      <h1>Daily</h1>
      <span class="faint">{app.workspace}</span>
      <label class="hide"><input type="checkbox" bind:checked={prefs.dailyHideEmpty} /> Hide empty days</label>
    </header>
    {#each shown as day (day.date)}
      <DayEntry date={day.date} node={day.node} {workspace} isToday={day.date === currentToday} isFuture={day.date > currentToday} contents={summary(day).text}
        oncreated={(n) => (day.node = n)} />
    {/each}
    <div bind:this={sentinel} class="sentinel">{loading ? 'Loading…' : ''}</div>
  </div>
</div>

<style>
  .stream { flex: 1; padding: 0 28px; }
  .inner { max-width: calc(var(--editor-width) + 40px); margin: 0 auto; padding-bottom: 30vh; }
  .head { display: flex; align-items: baseline; gap: 12px; padding: 26px 0 10px; }
  h1 { font: 700 30px/1.2 var(--font-display); }
  .hide { margin-left: auto; display: inline-flex; gap: 6px; align-items: center; font-size: 13px; color: var(--text-muted); }
  .sentinel { height: 40px; text-align: center; color: var(--text-faint); font-size: 13px; }
</style>
