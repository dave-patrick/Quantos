import { addDays, isoDate } from './dates';

export interface QuickTask {
  title: string;
  priority?: number;
  due?: string;
  planned?: string;
  effort?: number;
  tags: string[];
  recurrence?: string;
  project?: string;
  myDay?: boolean;
}

const weekdays = ['sun', 'mon', 'tue', 'wed', 'thu', 'fri', 'sat'];

/** Natural dates: today, tomorrow, mon..sun, +3d, 2026-10-20, 20/10. */
export function parseDate(word: string, today = isoDate()): string | undefined {
  const w = word.toLowerCase();
  if (w === 'today' || w === 'tod') return today;
  if (w === 'tomorrow' || w === 'tom') return addDays(today, 1);
  if (w === 'yesterday') return addDays(today, -1);
  if (/^\d{4}-\d{2}-\d{2}$/.test(w)) return w;
  const rel = /^\+(\d+)([dw])$/.exec(w);
  if (rel) return addDays(today, Number(rel[1]) * (rel[2] === 'w' ? 7 : 1));
  const dm = /^(\d{1,2})\/(\d{1,2})$/.exec(w);
  if (dm) {
    const year = Number(today.slice(0, 4));
    const candidate = `${year}-${dm[2].padStart(2, '0')}-${dm[1].padStart(2, '0')}`;
    return candidate < today ? `${year + 1}-${dm[2].padStart(2, '0')}-${dm[1].padStart(2, '0')}` : candidate;
  }
  const idx = weekdays.findIndex((d) => w.startsWith(d) && w.length <= 9);
  if (idx >= 0) {
    const now = new Date(today + 'T00:00:00').getDay();
    const diff = (idx - now + 7) % 7 || 7;
    return addDays(today, diff);
  }
  return undefined;
}

/**
 * Quick-add tokens (BUILD-PLAN 8C.3): !1-!4 priority, due:fri, plan:tomorrow (or @tomorrow), ~45m / ~2h effort,
 * #tag, +project, "every week" / "every day" / "every weekday" / "every month" recurrence, and "!myday".
 */
export function parseQuickAdd(text: string, today = isoDate()): QuickTask {
  const result: QuickTask = { title: '', tags: [] };
  let rest = text;
  const every = /\bevery\s+(day|weekday|week|month|year|\d+\s+(?:days|weeks|months))\b/i.exec(rest);
  if (every) {
    const what = every[1].toLowerCase();
    const n = /^(\d+)\s+(days|weeks|months)$/.exec(what);
    result.recurrence = n ? `FREQ=${({ days: 'DAILY', weeks: 'WEEKLY', months: 'MONTHLY' } as const)[n[2] as 'days']};INTERVAL=${n[1]}` : what === 'day' ? 'daily' : what === 'weekday' ? 'weekdays' : what + 'ly';
    rest = rest.replace(every[0], ' ');
  }
  const words: string[] = [];
  for (const token of rest.split(/\s+/).filter(Boolean)) {
    let m: RegExpExecArray | null;
    if ((m = /^!([1-4])$/.exec(token))) result.priority = Number(m[1]);
    else if (token.toLowerCase() === '!myday') result.myDay = true;
    else if ((m = /^due:(.+)$/i.exec(token)) && parseDate(m[1], today)) result.due = parseDate(m[1], today);
    else if ((m = /^(?:plan:|@)(.+)$/i.exec(token)) && parseDate(m[1], today)) result.planned = parseDate(m[1], today);
    else if ((m = /^~(\d+)(m|h)?$/i.exec(token))) result.effort = Number(m[1]) * (m[2]?.toLowerCase() === 'h' ? 60 : 1);
    else if ((m = /^#([\w\-/]+)$/.exec(token))) result.tags.push(m[1]);
    else if ((m = /^\+([\w\-]+)$/.exec(token))) result.project = m[1].replace(/[-_]/g, ' ');
    else words.push(token);
  }
  result.title = words.join(' ').trim();
  return result;
}
