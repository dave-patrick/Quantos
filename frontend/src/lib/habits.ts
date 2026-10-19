/**
 * Habit and metric tracking (Release 1.1). Trackers are defined in Vault/settings.json ("trackers");
 * values are properties on daily notes. These helpers turn values into streaks and summaries.
 */
export interface Tracker {
  key: string;
  label: string;
  kind: 'check' | 'number';
  unit?: string;
  /** For numbers: the daily target (met when the value reaches it, or stays under it when `lower` is set). */
  goal?: number;
  lower?: boolean;
}

export type DayValues = Record<string, Record<string, unknown>>;

export function isoDay(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export function daysBack(today: string, count: number): string[] {
  const [y, m, d] = today.split('-').map(Number);
  return Array.from({ length: count }, (_, i) => isoDay(new Date(y, m - 1, d - (count - 1 - i))));
}

export function num(v: unknown): number | null {
  if (typeof v === 'number') return Number.isFinite(v) ? v : null;
  if (typeof v === 'string' && v.trim() && Number.isFinite(Number(v))) return Number(v);
  return null;
}

/** Whether a day counts as done: ticked, or the number meets the goal (any number counts without a goal). */
export function met(t: Tracker, v: unknown): boolean {
  if (v === undefined || v === null || v === false || v === '') return false;
  if (t.kind === 'check') return v === true || v === 'true' || v === 'yes' || v === 1;
  const n = num(v);
  if (n === null) return false;
  if (t.goal === undefined) return true;
  return t.lower ? n <= t.goal : n >= t.goal;
}

/** Streak ending today (or yesterday, when today isn't filled in yet) and the longest in the range. */
export function streaks(t: Tracker, days: string[], values: DayValues): { current: number; best: number } {
  let best = 0, run = 0;
  for (const d of days) { run = met(t, values[d]?.[t.key]) ? run + 1 : 0; best = Math.max(best, run); }
  let current = 0;
  let i = days.length - 1;
  if (i >= 0 && !met(t, values[days[i]]?.[t.key])) i--;
  for (; i >= 0 && met(t, values[days[i]]?.[t.key]); i--) current++;
  return { current, best };
}

export function summary(t: Tracker, days: string[], values: DayValues) {
  const filled = days.map((d) => values[d]?.[t.key]).filter((v) => v !== undefined && v !== null);
  const hits = days.filter((d) => met(t, values[d]?.[t.key])).length;
  const nums = filled.map(num).filter((n): n is number => n !== null);
  return {
    rate: days.length ? hits / days.length : 0,
    hits,
    average: nums.length ? nums.reduce((a, b) => a + b, 0) / nums.length : null,
    min: nums.length ? Math.min(...nums) : null,
    max: nums.length ? Math.max(...nums) : null,
    ...streaks(t, days, values)
  };
}

/** A property-safe key from a label: "Read 20 pages" → "read_20_pages". */
export function keyFor(label: string, taken: string[]): string {
  let base = label.toLowerCase().normalize('NFKD').replace(/[^\w\s]/g, '').trim().replace(/\s+/g, '_').replace(/^[^a-z]+/, '').slice(0, 32) || 'habit';
  const reserved = ['id', 'title', 'type', 'workspace', 'tags', 'aliases', 'created', 'modified', 'schema', 'archived', 'locked', 'date'];
  if (reserved.includes(base)) base = 'habit_' + base;
  let key = base, n = 2;
  while (taken.includes(key)) key = `${base}_${n++}`;
  return key;
}
