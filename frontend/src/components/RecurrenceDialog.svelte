<script lang="ts">
  import { untrack } from 'svelte';
  import { modal } from '../lib/modal';
  import { addDays, isoDate, longDate, parseIsoDate } from '../lib/dates';
  import { recurrenceDialog } from '../lib/recurrenceDialog.svelte';
  import {
    DAYS,
    MONTHS,
    dayCodeFromJs,
    defaultCustomRecurrence,
    describeRecurrence,
    formatRecurrenceRule,
    nextOccurrence,
    parseRecurrence,
    type DayCode,
    type ParsedRecurrence,
    type RecurrenceFreq
  } from '../lib/recurrence';

  const request = $derived(recurrenceDialog.request);

  let freq = $state<RecurrenceFreq>('WEEKLY');
  let interval = $state(1);
  let byDay = $state<DayCode[]>(['MO']);
  let monthlyMode = $state<'day' | 'lastDay' | 'weekday'>('day');
  let monthDay = $state(1);
  let monthOrdinal = $state(1);
  let monthWeekday = $state<DayCode>('MO');
  let yearlyMode = $state<'scheduled' | 'date' | 'weekday'>('scheduled');
  let yearMonth = $state(1);
  let yearDay = $state(1);
  let yearOrdinal = $state(1);
  let yearWeekday = $state<DayCode>('MO');
  let endMode = $state<'never' | 'until' | 'count'>('never');
  let untilDate = $state('');
  let countNum = $state(5);
  let intervalInput = $state<HTMLInputElement | null>(null);

  function baseIso(scheduled?: string | null): string {
    return scheduled && /^\d{4}-\d{2}-\d{2}/.test(scheduled) ? scheduled.slice(0, 10) : isoDate();
  }

  function applyParsed(parsed: ParsedRecurrence, scheduled?: string | null) {
    const base = baseIso(scheduled);
    const d = parseIsoDate(base);
    const baseDay = d.getDate();
    const baseMonth = d.getMonth() + 1;
    const baseWeekday = dayCodeFromJs(d.getDay());

    freq = parsed.freq;
    interval = Math.max(1, parsed.interval);
    byDay = parsed.freq === 'WEEKLY' ? [...parsed.byDay] : [baseWeekday];

    if (parsed.freq === 'MONTHLY' && parsed.byDayOrdinal !== null && parsed.byDay.length > 0) {
      monthlyMode = 'weekday';
      monthOrdinal = parsed.byDayOrdinal;
      monthWeekday = parsed.byDay[0];
    } else if (parsed.freq === 'MONTHLY' && parsed.byMonthDay === -1) {
      monthlyMode = 'lastDay';
      monthDay = baseDay;
      monthOrdinal = 1;
      monthWeekday = baseWeekday;
    } else {
      monthlyMode = 'day';
      monthDay = parsed.byMonthDay && parsed.byMonthDay > 0 ? parsed.byMonthDay : baseDay;
      monthOrdinal = 1;
      monthWeekday = baseWeekday;
    }

    if (parsed.freq === 'YEARLY' && parsed.byDayOrdinal !== null && parsed.byDay.length > 0 && parsed.byMonth !== null) {
      yearlyMode = 'weekday';
      yearMonth = parsed.byMonth;
      yearOrdinal = parsed.byDayOrdinal;
      yearWeekday = parsed.byDay[0];
      yearDay = baseDay;
    } else if (parsed.freq === 'YEARLY' && parsed.byMonth !== null && parsed.byMonthDay !== null && parsed.byMonthDay > 0) {
      yearlyMode = 'date';
      yearMonth = parsed.byMonth;
      yearDay = parsed.byMonthDay;
      yearOrdinal = 1;
      yearWeekday = baseWeekday;
    } else {
      yearlyMode = 'scheduled';
      yearMonth = baseMonth;
      yearDay = baseDay;
      yearOrdinal = 1;
      yearWeekday = baseWeekday;
    }

    if (parsed.until) {
      endMode = 'until';
      untilDate = parsed.until;
      countNum = 5;
    } else if (parsed.count !== null) {
      endMode = 'count';
      countNum = parsed.count;
      untilDate = addDays(base, 90);
    } else {
      endMode = 'never';
      untilDate = addDays(base, 90);
      countNum = 5;
    }
  }

  $effect(() => {
    const req = request;
    if (!req) return;
    untrack(() => {
      const parsed = defaultCustomRecurrence(req.value, req.scheduled);
      applyParsed(parsed, req.scheduled);
      queueMicrotask(() => intervalInput?.focus());
    });
  });

  function toggleDay(code: DayCode) {
    if (byDay.includes(code)) byDay = byDay.filter((d) => d !== code);
    else byDay = [...byDay, code];
  }

  const currentRule = $derived.by((): ParsedRecurrence => {
    const safeInterval = Math.max(1, Math.min(999, Math.round(Number(interval) || 1)));
    const until = endMode === 'until' && /^\d{4}-\d{2}-\d{2}$/.test(untilDate) ? untilDate : null;
    const count = endMode === 'count' ? Math.max(1, Math.min(999, Math.round(Number(countNum) || 1))) : null;

    if (freq === 'DAILY') {
      return { freq, interval: safeInterval, byDay: [], byDayOrdinal: null, byMonthDay: null, byMonth: null, until, count };
    }
    if (freq === 'WEEKLY') {
      return { freq, interval: safeInterval, byDay: [...byDay], byDayOrdinal: null, byMonthDay: null, byMonth: null, until, count };
    }
    if (freq === 'MONTHLY') {
      if (monthlyMode === 'lastDay') return { freq, interval: safeInterval, byDay: [], byDayOrdinal: null, byMonthDay: -1, byMonth: null, until, count };
      if (monthlyMode === 'weekday') return { freq, interval: safeInterval, byDay: [monthWeekday], byDayOrdinal: monthOrdinal, byMonthDay: null, byMonth: null, until, count };
      const md = Math.max(1, Math.min(31, Math.round(Number(monthDay) || 1)));
      return { freq, interval: safeInterval, byDay: [], byDayOrdinal: null, byMonthDay: md, byMonth: null, until, count };
    }
    if (yearlyMode === 'weekday') {
      return { freq, interval: safeInterval, byDay: [yearWeekday], byDayOrdinal: yearOrdinal, byMonthDay: null, byMonth: yearMonth, until, count };
    }
    if (yearlyMode === 'date') {
      const yd = Math.max(1, Math.min(31, Math.round(Number(yearDay) || 1)));
      return { freq, interval: safeInterval, byDay: [], byDayOrdinal: null, byMonthDay: yd, byMonth: yearMonth, until, count };
    }
    return { freq, interval: safeInterval, byDay: [], byDayOrdinal: null, byMonthDay: null, byMonth: null, until, count };
  });

  const formattedRule = $derived(formatRecurrenceRule(currentRule));
  const summary = $derived(describeRecurrence(currentRule));
  const nextDate = $derived(nextOccurrence(currentRule, request?.scheduled));

  const presets = [
    { id: 'daily', label: 'Daily' },
    { id: 'weekdays', label: 'Weekdays' },
    { id: 'weekly', label: 'Weekly' },
    { id: 'monthly', label: 'Monthly' },
    { id: 'yearly', label: 'Annually' }
  ] as const;

  function pickPreset(id: string) {
    const parsed = parseRecurrence(id);
    if (parsed) applyParsed(parsed, request?.scheduled);
  }

  function save() {
    request?.done(formattedRule);
  }
</script>

{#if request}
  <div class="scrim">
    <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="rec-dialog-title" use:modal={{ onclose: () => request?.done(undefined) }}>
      <form class="form" onsubmit={(e) => { e.preventDefault(); save(); }}>
        <h2 id="rec-dialog-title">{request.showPresets ? 'Recurrence settings' : 'Custom recurrence'}</h2>

        {#if request.showPresets}
          <div class="section">
            <span class="label">Quick presets</span>
            <div class="presets">
              {#each presets as p (p.id)}
                <button type="button" class="pill" class:on={formattedRule === p.id} aria-pressed={formattedRule === p.id} onclick={() => pickPreset(p.id)}>{p.label}</button>
              {/each}
            </div>
          </div>
        {/if}

        <div class="row">
          <label class="inline-label" for="rec-interval">Repeat every</label>
          <input id="rec-interval" class="input num" type="number" min="1" max="999" bind:this={intervalInput} bind:value={interval} />
          <select class="input unit" aria-label="Repeat unit" bind:value={freq}>
            <option value="DAILY">{interval === 1 ? 'day' : 'days'}</option>
            <option value="WEEKLY">{interval === 1 ? 'week' : 'weeks'}</option>
            <option value="MONTHLY">{interval === 1 ? 'month' : 'months'}</option>
            <option value="YEARLY">{interval === 1 ? 'year' : 'years'}</option>
          </select>
        </div>

        {#if freq === 'WEEKLY'}
          <div class="section" role="group" aria-label="Repeat on days">
            <span class="label">Repeat on</span>
            <div class="days">
              {#each DAYS as d (d.code)}
                <button type="button" class="day" class:on={byDay.includes(d.code)} aria-pressed={byDay.includes(d.code)}
                  title={d.full} onclick={() => toggleDay(d.code)}>{d.short}</button>
              {/each}
            </div>
            {#if byDay.length === 0}<span class="hint">Repeats on the task's scheduled weekday.</span>{/if}
          </div>
        {:else if freq === 'MONTHLY'}
          <fieldset class="choices">
            <legend class="label">Monthly on</legend>
            <label class="choice">
              <input type="radio" name="monthly-mode" value="day" bind:group={monthlyMode} />
              <span>On day</span>
              <input type="number" class="input num-sm" min="1" max="31" bind:value={monthDay} aria-label="Day of month" disabled={monthlyMode !== 'day'} />
            </label>
            <label class="choice">
              <input type="radio" name="monthly-mode" value="lastDay" bind:group={monthlyMode} />
              <span>On the last day of the month</span>
            </label>
            <label class="choice wrap">
              <input type="radio" name="monthly-mode" value="weekday" bind:group={monthlyMode} />
              <span>On the</span>
              <select class="input inline-select" aria-label="Week of month" bind:value={monthOrdinal} disabled={monthlyMode !== 'weekday'}>
                <option value={1}>1st</option><option value={2}>2nd</option><option value={3}>3rd</option><option value={4}>4th</option><option value={-1}>last</option>
              </select>
              <select class="input inline-select" aria-label="Weekday of month" bind:value={monthWeekday} disabled={monthlyMode !== 'weekday'}>
                {#each DAYS as d (d.code)}<option value={d.code}>{d.full}</option>{/each}
              </select>
            </label>
          </fieldset>
        {:else if freq === 'YEARLY'}
          <fieldset class="choices">
            <legend class="label">Annually on</legend>
            <label class="choice">
              <input type="radio" name="yearly-mode" value="scheduled" bind:group={yearlyMode} />
              <span>Task's scheduled date</span>
            </label>
            <label class="choice wrap">
              <input type="radio" name="yearly-mode" value="date" bind:group={yearlyMode} />
              <span>On</span>
              <select class="input inline-select" aria-label="Month" bind:value={yearMonth} disabled={yearlyMode !== 'date'}>
                {#each MONTHS as m (m.value)}<option value={m.value}>{m.full}</option>{/each}
              </select>
              <input type="number" class="input num-sm" min="1" max="31" bind:value={yearDay} aria-label="Day of month" disabled={yearlyMode !== 'date'} />
            </label>
            <label class="choice wrap">
              <input type="radio" name="yearly-mode" value="weekday" bind:group={yearlyMode} />
              <span>On the</span>
              <select class="input inline-select" aria-label="Week of month" bind:value={yearOrdinal} disabled={yearlyMode !== 'weekday'}>
                <option value={1}>1st</option><option value={2}>2nd</option><option value={3}>3rd</option><option value={4}>4th</option><option value={-1}>last</option>
              </select>
              <select class="input inline-select" aria-label="Weekday" bind:value={yearWeekday} disabled={yearlyMode !== 'weekday'}>
                {#each DAYS as d (d.code)}<option value={d.code}>{d.full}</option>{/each}
              </select>
              <span>of</span>
              <select class="input inline-select" aria-label="Month for weekday" bind:value={yearMonth} disabled={yearlyMode !== 'weekday'}>
                {#each MONTHS as m (m.value)}<option value={m.value}>{m.full}</option>{/each}
              </select>
            </label>
          </fieldset>
        {/if}

        <fieldset class="choices">
          <legend class="label">Ends</legend>
          <label class="choice">
            <input type="radio" name="end-mode" value="never" bind:group={endMode} />
            <span>Never</span>
          </label>
          <label class="choice wrap">
            <input type="radio" name="end-mode" value="until" bind:group={endMode} />
            <span>On date</span>
            <input type="date" class="input date" bind:value={untilDate} aria-label="End date" disabled={endMode !== 'until'} />
          </label>
          <label class="choice wrap">
            <input type="radio" name="end-mode" value="count" bind:group={endMode} />
            <span>After</span>
            <input type="number" class="input num-sm" min="1" max="999" bind:value={countNum} aria-label="Occurrence count" disabled={endMode !== 'count'} />
            <span>{countNum === 1 ? 'occurrence' : 'occurrences'}</span>
          </label>
        </fieldset>

        <div class="preview" aria-live="polite">
          <div class="summary">{summary}</div>
          <div class="next">{nextDate ? `Next: ${longDate(nextDate)}` : 'No further occurrences before the end date.'}</div>
        </div>

        <div class="actions">
          {#if request.value || request.showPresets}
            <button type="button" class="btn ghost danger-btn" onclick={() => request?.done(null)}>Remove recurrence</button>
          {/if}
          <span class="spacer"></span>
          <button type="button" class="btn" onclick={() => request?.done(undefined)}>Cancel</button>
          <button type="submit" class="btn primary">Save</button>
        </div>
      </form>
    </div>
  </div>
{/if}

<style>
  .scrim { position: fixed; inset: 0; z-index: 90; background: rgb(4 8 14 / .6); display: grid; place-items: center; padding: 16px; }
  .dialog { width: min(500px, 100%); max-height: calc(100dvh - 32px); overflow-y: auto; background: var(--bg-raised); border: 1px solid var(--line-strong); border-radius: 12px; padding: 20px 22px; box-shadow: var(--shadow); }
  .form { display: grid; gap: 14px; }
  h2 { font: 700 18px/1.3 var(--font-display); }
  .section { display: grid; gap: 6px; }
  .row { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .inline-label { font-size: 14px; color: var(--text-soft); }
  .num { width: 74px; text-align: center; }
  .num-sm { width: 68px; text-align: center; height: 28px; }
  .unit { width: auto; min-width: 110px; }
  .inline-select { width: auto; height: 28px; padding: 0 8px; font-size: 13.5px; }
  .date { width: auto; height: 28px; }
  .presets, .days { display: flex; flex-wrap: wrap; gap: 6px; }
  .pill, .day { border: 1px solid var(--line-strong); background: var(--bg-input); color: var(--text-soft); border-radius: 7px; padding: 5px 10px; font-size: 13px; font-weight: 500; }
  .pill:hover, .day:hover { background: var(--bg-hover); color: var(--text); }
  .pill.on, .day.on { background: var(--accent-wash); border-color: var(--accent); color: var(--accent-strong); font-weight: 600; }
  .hint { font-size: 12px; color: var(--text-muted); }
  fieldset.choices { border: 0; margin: 0; padding: 0; display: grid; gap: 8px; }
  fieldset.choices legend { margin-bottom: 4px; }
  .choice { display: flex; align-items: center; gap: 8px; font-size: 14px; color: var(--text-soft); }
  .choice.wrap { flex-wrap: wrap; }
  .choice input[type='radio'] { width: 16px; height: 16px; accent-color: var(--accent); margin: 0; }
  .preview { padding: 10px 12px; border-radius: 8px; background: var(--bg-panel); border: 1px solid var(--line); display: grid; gap: 2px; }
  .summary { font-weight: 600; font-size: 13.5px; color: var(--text); }
  .next { font-size: 12px; color: var(--text-muted); }
  .actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin-top: 2px; }
  .spacer { flex: 1; }
  .danger-btn { color: var(--danger); }
  .danger-btn:hover { background: var(--danger-wash); color: var(--danger); }
</style>
