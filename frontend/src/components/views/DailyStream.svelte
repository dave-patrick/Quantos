<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { onChange } from '../../lib/events';
  import { addDays, isoDate } from '../../lib/dates';
  import type { Tab } from '../../lib/layout.svelte';
  import type { NodeDto } from '../../lib/types';
  import DayEntry from '../DayEntry.svelte';

  let { tab, today }: { tab: Tab; today: string } = $props();
  void tab;

  let days = $state<{ date: string; node: NodeDto | null }[]>([]);
  let loading = $state(false);
  let sentinel = $state<HTMLDivElement | null>(null);
  let currentToday = isoDate();
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
    const io = new IntersectionObserver((entries) => { if (entries.some((x) => x.isIntersecting)) more(); }, { rootMargin: '600px' });
    const t = setInterval(() => { if (sentinel) { io.observe(sentinel); clearInterval(t); } }, 100);
    return () => { off(); io.disconnect(); clearInterval(t); };
  });

  // Keep editing past midnight: the new day appears without losing the open entry.
  $effect(() => { if (today && today !== currentToday) reload(); });
</script>

<div class="scroll stream">
  <div class="inner">
    <header class="head">
      <h1>Daily</h1>
      <span class="faint">{app.workspace}</span>
    </header>
    {#each days as day (day.date)}
      <DayEntry date={day.date} node={day.node} {workspace} isToday={day.date === currentToday} isFuture={day.date > currentToday}
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
  .sentinel { height: 40px; text-align: center; color: var(--text-faint); font-size: 13px; }
</style>
