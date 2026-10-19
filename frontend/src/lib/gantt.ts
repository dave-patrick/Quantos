/**
 * Dependency Gantt (Release 1.2): where each task sits in time, from its own dates, and which
 * dependencies are out of order. Pure functions, so the chart and the tests agree.
 */
export interface GanttTask {
  id: string; title: string; status: string; planned: string | null; due: string | null; effort: number | null;
  dependsOn: string[]; project: string | null; created: string | null; completed: string | null; timeBlock: { start: string; end: string } | null;
}
export interface Bar { id: string; title: string; start: string; end: string; done: boolean; group: string; estimated: boolean }
export interface Edge { from: string; to: string; late: boolean }

const DAY = 86_400_000;
const WORKDAY_MINUTES = 8 * 60;

export const linkId = (v: string | null | undefined) => (v ? /^\[\[([0-9A-HJKMNP-TV-Z]{26})/.exec(v)?.[1] ?? (/^[0-9A-HJKMNP-TV-Z]{26}$/.test(v) ? v : null) : null);
export const linkTitle = (v: string | null | undefined) => (v ? /\|([^\]]+)\]\]$/.exec(v)?.[1] ?? v.replace(/^\[\[|\]\]$/g, '') : '');
const d10 = (v: string | null | undefined) => (v && /^\d{4}-\d{2}-\d{2}/.test(v) ? v.slice(0, 10) : null);

export function addDays(iso: string, n: number) {
  const [y, m, d] = iso.split('-').map(Number);
  const t = new Date(Date.UTC(y, m - 1, d) + n * DAY);
  return t.toISOString().slice(0, 10);
}
export function daysBetween(a: string, b: string) {
  const p = (s: string) => { const [y, m, d] = s.split('-').map(Number); return Date.UTC(y, m - 1, d); };
  return Math.round((p(b) - p(a)) / DAY);
}

/**
 * A task's span: from its time block or planned day to its due day. With only one of them, the other
 * end comes from the effort estimate (eight-hour days), or the bar is one day long. Tasks with no
 * date at all aren't placed.
 */
export function span(t: GanttTask): { start: string; end: string; estimated: boolean } | null {
  const start = d10(t.timeBlock?.start) ?? d10(t.planned);
  const end = d10(t.due) ?? d10(t.timeBlock?.end);
  const days = Math.max(1, Math.ceil((t.effort ?? 0) / WORKDAY_MINUTES));
  if (start && end) return end >= start ? { start, end, estimated: false } : { start: end, end: start, estimated: false };
  if (start) return { start, end: addDays(start, days - 1), estimated: true };
  if (end) return { start: addDays(end, -(days - 1)), end, estimated: true };
  return null;
}

export function layout(tasks: GanttTask[]): { bars: Bar[]; edges: Edge[]; unscheduled: GanttTask[]; from: string | null; to: string | null } {
  const bars: Bar[] = [];
  const unscheduled: GanttTask[] = [];
  for (const t of tasks) {
    const s = span(t);
    if (!s) { unscheduled.push(t); continue; }
    bars.push({ id: t.id, title: t.title, ...s, done: t.status === 'done', group: linkTitle(t.project) || '' });
  }
  const byId = new Map(bars.map((b) => [b.id, b]));
  const edges: Edge[] = [];
  for (const t of tasks) {
    const to = byId.get(t.id);
    if (!to) continue;
    for (const dep of t.dependsOn) {
      const from = byId.get(linkId(dep) ?? '');
      // Late: the task starts on or before the day its dependency is due to finish.
      if (from) edges.push({ from: from.id, to: to.id, late: !from.done && to.start <= from.end });
    }
  }
  // Grouped by project (no project last), then by start.
  bars.sort((a, b) => (a.group === '') !== (b.group === '') ? (a.group === '' ? 1 : -1) : a.group.localeCompare(b.group) || a.start.localeCompare(b.start) || a.title.localeCompare(b.title));
  const from = bars.reduce<string | null>((m, b) => (!m || b.start < m ? b.start : m), null);
  const to = bars.reduce<string | null>((m, b) => (!m || b.end > m ? b.end : m), null);
  return { bars, edges, unscheduled, from, to };
}

/** Moving a bar shifts both of its stored dates by the same number of days. */
export function moved(t: GanttTask, days: number): { planned?: string; due?: string } {
  const out: { planned?: string; due?: string } = {};
  if (d10(t.planned)) out.planned = addDays(d10(t.planned)!, days);
  if (d10(t.due)) out.due = addDays(d10(t.due)!, days);
  if (!out.planned && !out.due) { const s = span(t); if (s) out.planned = addDays(s.start, days); }
  return out;
}
