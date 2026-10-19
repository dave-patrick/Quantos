<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { day } from '../../lib/today.svelte';
  import { vaultSettings } from '../../lib/vaultSettings.svelte';
  import { openNode } from '../../lib/links.svelte';
  import type { NodeDto } from '../../lib/types';
  import { daysBack, keyFor, met, num, summary, type DayValues, type Tracker } from '../../lib/habits';
  import Icon from '../Icon.svelte';

  /** Habits and metrics (Release 1.1): one row per tracker, one column per day; values live on daily notes. */
  let span = $state(14);
  let values = $state<DayValues>({});
  let editing = $state<{ key: string; date: string; text: string } | null>(null);
  let form = $state<(Tracker & { original?: string }) | null>(null);
  let numberInput = $state<HTMLInputElement | null>(null);

  const trackers = $derived((Array.isArray(vaultSettings.data.trackers) ? vaultSettings.data.trackers : []) as Tracker[]);
  const days = $derived(daysBack(day.today, span));

  async function load() {
    if (!trackers.length) { values = {}; return; }
    try {
      const r = await api.get<{ days: DayValues }>(`habits/${encodeURIComponent(app.workspace)}?from=${days[0]}&to=${days[days.length - 1]}&keys=${trackers.map((t) => t.key).join(',')}`);
      values = r.days;
    } catch (e) { app.error(e); }
  }
  onMount(() => { vaultSettings.load(); });
  $effect(() => { void days; void trackers; void app.workspace; load(); });

  async function put(t: Tracker, date: string, value: unknown) {
    const before = values[date]?.[t.key];
    values = { ...values, [date]: { ...(values[date] ?? {}), [t.key]: value ?? undefined } };
    try { await api.put(`habits/${encodeURIComponent(app.workspace)}/${date}`, { key: t.key, value: value ?? null }); }
    catch (e) { values = { ...values, [date]: { ...(values[date] ?? {}), [t.key]: before } }; app.error(e); }
  }

  function cell(t: Tracker, date: string) {
    if (t.kind === 'check') put(t, date, met(t, values[date]?.[t.key]) ? null : true);
    else {
      editing = { key: t.key, date, text: String(values[date]?.[t.key] ?? '') };
      tick().then(() => { numberInput?.focus(); numberInput?.select(); });
    }
  }

  function commit(t: Tracker) {
    if (!editing) return;
    const { date, text } = editing;
    editing = null;
    const n = text.trim() === '' ? null : num(text.replace(',', '.'));
    if (text.trim() !== '' && n === null) { app.toast('That isn’t a number.', 'error'); return; }
    if (n !== num(values[date]?.[t.key])) put(t, date, n);
  }

  function newTracker() { form = { key: '', label: '', kind: 'check' }; }
  function edit(t: Tracker) { form = { ...t, original: t.key }; }

  async function saveForm() {
    if (!form || !form.label.trim()) return;
    const { original, ...t } = form;
    t.label = t.label.trim();
    if (!original) t.key = keyFor(t.label, trackers.map((x) => x.key));
    if (t.kind === 'check') { delete t.unit; delete t.goal; delete t.lower; }
    else { if (t.goal === undefined || t.goal === null || Number.isNaN(Number(t.goal))) delete t.goal; else t.goal = Number(t.goal); if (!t.unit?.trim()) delete t.unit; if (!t.lower) delete t.lower; }
    const next = original ? trackers.map((x) => (x.key === original ? t : x)) : [...trackers, t];
    form = null;
    try { await vaultSettings.set('trackers', next); } catch (e) { app.error(e); }
  }

  async function remove(t: Tracker) {
    const ok = await app.confirm(`Stop tracking “${t.label}”?`, `The values already in your daily notes stay there (as the “${t.key}” property).`, 'Stop tracking');
    if (!ok) return;
    form = null;
    await vaultSettings.set('trackers', trackers.filter((x) => x.key !== t.key));
  }

  async function move(t: Tracker, by: number) {
    const list = [...trackers];
    const i = list.findIndex((x) => x.key === t.key), j = i + by;
    if (j < 0 || j >= list.length) return;
    [list[i], list[j]] = [list[j], list[i]];
    await vaultSettings.set('trackers', list);
  }

  async function openDaily(date: string) {
    try { const n = await api.post<NodeDto>(`daily/${encodeURIComponent(app.workspace)}/${date}`); openNode(n.id, 'new', n.title); } catch (e) { app.error(e); }
  }

  const dayName = (iso: string) => new Date(iso + 'T12:00:00').toLocaleDateString(undefined, { weekday: 'narrow' });
  const dayNum = (iso: string) => Number(iso.slice(8));
  const fmt = (n: number | null, unit?: string) => (n === null ? '—' : `${Math.round(n * 10) / 10}${unit ? ' ' + unit : ''}`);
</script>

<div class="scroll habits">
  <div class="inner">
    <header>
      <h1><Icon name="activity" size={22} /> Habits</h1>
      <select class="input span" aria-label="Days shown" bind:value={span}><option value={7}>7 days</option><option value={14}>14 days</option><option value={30}>30 days</option></select>
      <button class="btn" onclick={newTracker}><Icon name="plus" size={14} /> Track something</button>
    </header>
    <p class="faint intro">Each value is a property on that day's daily note, so it's plain text in your vault and you can query it. Click a day to tick it or type a number.</p>

    {#if form}
      <form class="form" onsubmit={(e) => { e.preventDefault(); saveForm(); }}>
        <input class="input" aria-label="Name" placeholder="What to track, e.g. Exercise or Sleep" bind:value={form.label} />
        <div class="kinds" role="radiogroup" aria-label="Kind">
          <button type="button" role="radio" aria-checked={form.kind === 'check'} class:on={form.kind === 'check'} onclick={() => form && (form.kind = 'check')}>Done or not</button>
          <button type="button" role="radio" aria-checked={form.kind === 'number'} class:on={form.kind === 'number'} onclick={() => form && (form.kind = 'number')}>A number</button>
        </div>
        {#if form.kind === 'number'}
          <input class="input small" aria-label="Unit" placeholder="Unit (h, km, pages)" bind:value={form.unit} />
          <input class="input small" aria-label="Daily goal" placeholder="Daily goal" inputmode="decimal" bind:value={form.goal} />
          <label class="check"><input type="checkbox" bind:checked={form.lower} /> Stay under the goal</label>
        {/if}
        <div class="row">
          <button class="btn primary" type="submit" disabled={!form.label.trim()}>{form.original ? 'Save' : 'Add'}</button>
          <button class="btn ghost" type="button" onclick={() => (form = null)}>Cancel</button>
          {#if form.original}{@const t = trackers.find((x) => x.key === form?.original)}{#if t}<button class="btn ghost danger" type="button" onclick={() => remove(t)}>Stop tracking…</button>{/if}{/if}
        </div>
      </form>
    {/if}

    {#if trackers.length}
      <div class="grid-wrap">
        <table class="grid">
          <thead>
            <tr>
              <th class="name"></th>
              {#each days as d (d)}
                <th class:today={d === day.today}><button class="daylink" title={`Open ${d}`} onclick={() => openDaily(d)}><span>{dayName(d)}</span><span>{dayNum(d)}</span></button></th>
              {/each}
              <th class="stat">Streak</th><th class="stat">{span} days</th>
            </tr>
          </thead>
          <tbody>
            {#each trackers as t, ti (t.key)}
              {@const s = summary(t, days, values)}
              <tr>
                <th class="name" scope="row">
                  <button class="tname" title="Edit" onclick={() => edit(t)}>{t.label}</button>
                  {#if t.kind === 'number' && t.goal !== undefined}<span class="faint goal">{t.lower ? '≤' : '≥'} {t.goal}{t.unit ? ' ' + t.unit : ''}</span>{/if}
                  <span class="order">
                    <button class="icon-btn" aria-label={`Move ${t.label} up`} disabled={ti === 0} onclick={() => move(t, -1)}><Icon name="arrow-up" size={12} /></button>
                    <button class="icon-btn" aria-label={`Move ${t.label} down`} disabled={ti === trackers.length - 1} onclick={() => move(t, 1)}><Icon name="arrow-down" size={12} /></button>
                  </span>
                </th>
                {#each days as d (d)}
                  {@const v = values[d]?.[t.key]}
                  <td class:today={d === day.today}>
                    {#if editing && editing.key === t.key && editing.date === d}
                      <input class="numin" bind:this={numberInput} aria-label={`${t.label} on ${d}`} inputmode="decimal" bind:value={editing.text}
                        onblur={() => commit(t)} onkeydown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); if (e.key === 'Escape') editing = null; }} />
                    {:else}
                      <button class="cell" class:hit={met(t, v)} class:some={v !== undefined && v !== null && !met(t, v)} aria-label={`${t.label} on ${d}: ${v ?? 'nothing'}`} aria-pressed={t.kind === 'check' ? met(t, v) : undefined} onclick={() => cell(t, d)}>
                        {#if t.kind === 'number' && v !== undefined && v !== null}{v}{:else if met(t, v)}✓{/if}
                      </button>
                    {/if}
                  </td>
                {/each}
                <td class="stat mono" title={`Best: ${s.best}`}>{s.current}{s.current ? ' 🔥' : ''}</td>
                <td class="stat mono" title={t.kind === 'number' ? `Range ${fmt(s.min)}–${fmt(s.max)}` : ''}>{t.kind === 'number' ? 'avg ' + fmt(s.average, t.unit) : Math.round(s.rate * 100) + '%'}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {:else if !form}
      <div class="empty-state"><h3>Nothing tracked yet</h3><p>Track a habit (Exercise, Read) or a number (Sleep in hours, Steps) day by day.</p>
        <button class="btn primary" onclick={newTracker}>Track something</button></div>
    {/if}
  </div>
</div>

<style>
  .habits { flex: 1; }
  .inner { max-width: 1100px; margin: 0 auto; padding: 24px 28px 40px; }
  header { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
  h1 { display: flex; align-items: center; gap: 10px; font: 700 28px/1.2 var(--font-display); margin-right: auto; }
  .span { width: auto; }
  .intro { font-size: 13.5px; margin: 6px 0 16px; }
  .form { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; padding: 12px; border: 1px solid var(--line); border-radius: 10px; background: var(--bg-raised); margin-bottom: 16px; }
  .form > .input:first-child { flex: 1 1 260px; }
  .small { width: 140px; }
  .kinds { display: inline-flex; border: 1px solid var(--line-strong); border-radius: 7px; overflow: hidden; }
  .kinds button { border: 0; background: none; color: var(--text-muted); padding: 6px 10px; font: 13px var(--font-body); cursor: pointer; }
  .kinds button + button { border-left: 1px solid var(--line-strong); }
  .kinds button.on { background: var(--accent-wash); color: var(--accent); }
  .check { display: inline-flex; gap: 6px; align-items: center; font-size: 13px; color: var(--text-muted); }
  .row { display: flex; gap: 6px; width: 100%; }
  .danger { color: var(--danger); margin-left: auto; }
  .grid-wrap { overflow-x: auto; }
  .grid { border-collapse: separate; border-spacing: 3px; }
  .grid th { font-weight: 500; }
  .daylink { display: grid; border: 0; background: none; color: var(--text-faint); font: 11px/1.3 var(--font-mono); padding: 2px 0; width: 30px; cursor: pointer; }
  .daylink:hover { color: var(--accent); }
  th.today .daylink { color: var(--accent); font-weight: 700; }
  .name { text-align: left; white-space: nowrap; padding-right: 10px; position: sticky; left: 0; background: var(--bg); z-index: 1; }
  .tname { border: 0; background: none; color: var(--text); font: 600 14px var(--font-body); padding: 0; cursor: pointer; }
  .tname:hover { color: var(--accent); }
  .goal { font: 11px var(--font-mono); margin-left: 6px; }
  .order { display: inline-flex; opacity: 0; margin-left: 4px; }
  tr:hover .order, .order:focus-within { opacity: 1; }
  td { padding: 0; }
  .cell { width: 30px; height: 30px; border: 1px solid var(--line); border-radius: 6px; background: var(--bg-input); color: var(--text); font: 600 11px var(--font-mono); cursor: pointer; padding: 0; overflow: hidden; }
  .cell:hover { border-color: var(--accent); }
  .cell.hit { background: color-mix(in srgb, var(--accent) 70%, transparent); border-color: var(--accent); color: var(--accent-ink); }
  .cell.some { background: var(--accent-wash); }
  td.today .cell { box-shadow: 0 0 0 1px var(--accent); }
  .numin { width: 44px; height: 30px; border: 1px solid var(--accent); border-radius: 6px; background: var(--bg-input); color: var(--text); font: 12px var(--font-mono); text-align: center; }
  .stat { padding: 0 8px; font-size: 12.5px; color: var(--text-soft); white-space: nowrap; text-align: right; }
  .mono { font-family: var(--font-mono); }
</style>
