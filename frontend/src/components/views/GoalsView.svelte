<script lang="ts">
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { openNode, whereFrom } from '../../lib/links.svelte';
  import { onChange } from '../../lib/events';
  import type { NodeDto } from '../../lib/types';
  import Icon from '../Icon.svelte';

  /** Goals (Release 1.1): goals and projects with progress rolled up from their tasks, plus how work is flowing. */
  interface Goal { id: string; title: string; type: 'goal' | 'project'; parent: string | null; target: string | null; health: string; progress: number; expected: number | null; tasks: number; done: number; overdue: number; blocked: number; children: string[] }
  interface Week { week: string; created: number; completed: number }
  interface Analytics { byStatus: Record<string, number>; open: number; overdue: number; blocked: number; waiting: number; dueThisWeek: number; cycleDays: number | null; weeks: Week[] }

  let goals = $state<Goal[]>([]);
  let stats = $state<Analytics | null>(null);
  let collapsed = $state<Record<string, boolean>>({});
  let showProjects = $state(true);

  async function load() {
    try {
      const ws = encodeURIComponent(app.workspace);
      [goals, stats] = await Promise.all([api.get<Goal[]>(`plan/goals?workspace=${ws}`), api.get<Analytics>(`plan/analytics?workspace=${ws}&weeks=8`)]);
    } catch (e) { app.error(e); }
  }
  $effect(() => { void app.workspace; load(); });
  let pending: ReturnType<typeof setTimeout> | undefined;
  $effect(() => onChange((e) => { if (['Updated', 'Created', 'Trashed', 'Restored', 'Archived'].includes(e.kind)) { clearTimeout(pending); pending = setTimeout(load, 500); } }));

  const byId = $derived(new Map(goals.map((g) => [g.id, g])));
  const visible = (g: Goal) => showProjects || g.type === 'goal';
  const roots = $derived(goals.filter((g) => visible(g) && (!g.parent || !byId.has(g.parent) || !visible(byId.get(g.parent)!))));

  // A flat list in tree order, so the template stays simple.
  const rows = $derived.by(() => {
    const out: { g: Goal; depth: number }[] = [];
    const walk = (g: Goal, depth: number, seen: Set<string>) => {
      if (seen.has(g.id)) return;
      seen.add(g.id);
      out.push({ g, depth });
      if (collapsed[g.id]) return;
      for (const c of g.children.map((id) => byId.get(id)).filter((x): x is Goal => !!x && visible(x))) walk(c, depth + 1, seen);
    };
    for (const r of roots.filter((g) => g.type === 'goal').concat(roots.filter((g) => g.type !== 'goal'))) walk(r, 0, new Set());
    return out;
  });

  async function newGoal() {
    const title = await app.ask('New goal', { placeholder: 'What you want to achieve', confirmLabel: 'Create' });
    if (!title?.trim()) return;
    try {
      const node = await api.post<NodeDto>('nodes', { title: title.trim(), type: 'goal', workspace: app.workspace });
      openNode(node.id, 'new', node.title);
      app.toast('Goal created. Give it a target date, link projects to it with their “goal” property, and tasks with “project”.', 'info');
      await load();
    } catch (e) { app.error(e); }
  }

  const maxWeek = $derived(Math.max(1, ...(stats?.weeks.flatMap((w) => [w.created, w.completed]) ?? [1])));
  const week = (iso: string) => new Date(iso + 'T12:00:00').toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  const pct = (n: number) => Math.round(n * 100) + '%';
</script>

<div class="scroll goals">
  <div class="inner">
    <header>
      <h1><Icon name="target" size={22} /> Goals</h1>
      <label class="check"><input type="checkbox" bind:checked={showProjects} /> Show projects</label>
      <button class="btn" onclick={newGoal}><Icon name="plus" size={14} /> New goal</button>
    </header>

    {#if stats}
      <div class="cards">
        <div class="card"><b>{stats.open}</b><span>open tasks</span></div>
        <div class="card" class:bad={stats.overdue > 0}><b>{stats.overdue}</b><span>overdue</span></div>
        <div class="card"><b>{stats.dueThisWeek}</b><span>due this week</span></div>
        <div class="card" class:warn={stats.blocked > 0}><b>{stats.blocked}</b><span>blocked</span></div>
        <div class="card"><b>{stats.waiting}</b><span>waiting</span></div>
        <div class="card"><b>{stats.cycleDays ?? '—'}</b><span>days from created to done (last 30 days)</span></div>
      </div>
      <figure class="weeks">
        <div class="cols" role="img" aria-label={`Tasks created and completed per week: ${stats.weeks.map((w) => `${week(w.week)} ${w.created} created, ${w.completed} done`).join('; ')}`}>
          {#each stats.weeks as w (w.week)}
            <div class="col" title={`Week of ${week(w.week)}: ${w.created} created, ${w.completed} done`}>
              <div class="pair"><span class="c" style={`height:${(w.created / maxWeek) * 100}%`}></span><span class="d" style={`height:${(w.completed / maxWeek) * 100}%`}></span></div>
              <span class="wk">{week(w.week)}</span>
            </div>
          {/each}
        </div>
        <figcaption class="faint"><span class="key c"></span> created <span class="key d"></span> completed, per week</figcaption>
      </figure>
    {/if}

    <h2>Progress</h2>
    {#if rows.length}
      <div class="tree" role="tree" aria-label="Goals and projects">
        {#each rows as { g, depth } (g.id)}
          <div class="goal" role="treeitem" aria-level={depth + 1} aria-selected="false" aria-expanded={g.children.length ? !collapsed[g.id] : undefined} style={`padding-left:${depth * 22}px`}>
            {#if g.children.some((id) => byId.has(id) && visible(byId.get(id)!))}
              <button class="twist" aria-label={collapsed[g.id] ? 'Expand' : 'Collapse'} onclick={() => (collapsed[g.id] = !collapsed[g.id])}>{collapsed[g.id] ? '▸' : '▾'}</button>
            {:else}<span class="twist"></span>{/if}
            <span class="kind {g.type}">{g.type}</span>
            <button class="title" onclick={(e) => openNode(g.id, whereFrom(e) === 'current' ? 'new' : whereFrom(e), g.title)}>{g.title}</button>
            <span class="progress" title={g.expected !== null ? `${pct(g.progress)} done; ${pct(g.expected)} of the time to the target has passed` : `${pct(g.progress)} done`}>
              <span class="fill" style={`width:${g.progress * 100}%`}></span>
              {#if g.expected !== null}<span class="exp" style={`left:${g.expected * 100}%`}></span>{/if}
            </span>
            <span class="mono">{g.done}/{g.tasks}</span>
            <span class="health h-{g.health.replace(' ', '-')}">{g.health}</span>
            <span class="mono faint target">{g.target ?? ''}</span>
            {#if g.overdue}<span class="mono bad" title="Overdue tasks">{g.overdue} late</span>{/if}
          </div>
        {/each}
      </div>
      <p class="faint hint">Progress counts the tasks linked with <code>project</code>, including those of projects (linked with <code>goal</code>) and sub-goals (linked with <code>parent</code>). With a <code>target</code> date, the marker shows how much of the time has passed.</p>
    {:else}
      <div class="empty-state"><h3>No goals or projects yet</h3><p>A goal gathers projects and tasks; its progress and health are worked out from them.</p><button class="btn primary" onclick={newGoal}>New goal</button></div>
    {/if}
  </div>
</div>

<style>
  .goals { flex: 1; }
  .inner { max-width: 1000px; margin: 0 auto; padding: 24px 28px 40px; }
  header { display: flex; align-items: center; gap: 14px; flex-wrap: wrap; }
  h1 { display: flex; align-items: center; gap: 10px; font: 700 28px/1.2 var(--font-display); margin-right: auto; }
  h2 { font: 700 16px/1.3 var(--font-display); margin: 24px 0 10px; }
  .check { display: inline-flex; gap: 6px; align-items: center; font-size: 13px; color: var(--text-muted); }
  .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 10px; margin: 18px 0; }
  .card { display: grid; gap: 2px; padding: 10px 14px; border: 1px solid var(--line); border-radius: 10px; background: var(--bg-raised); }
  .card b { font: 700 20px var(--font-mono); }
  .card span { font-size: 12px; color: var(--text-muted); }
  .card.bad b, .bad { color: var(--danger); }
  .card.warn b { color: var(--warn); }
  .weeks { margin: 0; }
  .cols { display: flex; align-items: stretch; gap: 8px; height: 130px; padding-bottom: 18px; border-bottom: 1px solid var(--line); }
  .col { flex: 1; display: flex; flex-direction: column; position: relative; }
  .pair { flex: 1; display: flex; align-items: flex-end; gap: 2px; justify-content: center; }
  .pair span { width: 40%; max-width: 18px; min-height: 1px; border-radius: 2px 2px 0 0; }
  .c, .key.c { background: var(--text-faint); }
  .d, .key.d { background: var(--accent); }
  .wk { position: absolute; bottom: -17px; left: 0; right: 0; text-align: center; font: 10.5px var(--font-mono); color: var(--text-faint); white-space: nowrap; }
  figcaption { font-size: 12px; margin-top: 22px; display: flex; align-items: center; gap: 6px; }
  .key { display: inline-block; width: 10px; height: 10px; border-radius: 2px; }
  .tree { display: grid; }
  .goal { display: flex; align-items: center; gap: 10px; padding-block: 7px; border-bottom: 1px solid var(--line); font-size: 14px; }
  .twist { width: 16px; border: 0; background: none; color: var(--text-faint); padding: 0; cursor: pointer; flex: none; }
  .kind { font: 600 10px/1 var(--font-mono); text-transform: uppercase; letter-spacing: .04em; color: var(--text-faint); width: 54px; flex: none; }
  .kind.goal { color: var(--accent); }
  .title { flex: 1; min-width: 0; border: 0; background: none; color: var(--text); font: 600 14px var(--font-body); text-align: left; padding: 0; cursor: pointer; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .title:hover { color: var(--accent); }
  .progress { position: relative; width: 160px; height: 8px; background: var(--line); border-radius: 4px; flex: none; }
  .progress .fill { position: absolute; inset: 0 auto 0 0; background: var(--accent); border-radius: 4px; }
  .progress .exp { position: absolute; top: -3px; bottom: -3px; width: 2px; background: var(--text); opacity: .7; }
  .mono { font: 12px var(--font-mono); flex: none; }
  .target { width: 80px; }
  .health { font: 600 11px var(--font-mono); padding: 2px 7px; border-radius: 999px; border: 1px solid var(--line-strong); color: var(--text-muted); flex: none; width: 72px; text-align: center; }
  .h-on-track, .h-done { color: var(--ok); border-color: color-mix(in srgb, var(--ok) 45%, transparent); }
  .h-at-risk { color: var(--warn); border-color: color-mix(in srgb, var(--warn) 45%, transparent); }
  .h-off-track { color: var(--danger); border-color: color-mix(in srgb, var(--danger) 45%, transparent); }
  .hint { font-size: 12.5px; margin-top: 10px; }
  @container pane (max-width: 760px) {
    .progress { width: 80px; }
    .target { display: none; }
  }
</style>
