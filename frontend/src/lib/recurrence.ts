import { addDays, isoDate, parseIsoDate } from './dates';

export type DayCode = 'MO' | 'TU' | 'WE' | 'TH' | 'FR' | 'SA' | 'SU';
export type RecurrenceFreq = 'DAILY' | 'WEEKLY' | 'MONTHLY' | 'YEARLY';
export type RecurrencePreset = 'none' | 'daily' | 'weekdays' | 'weekly' | 'monthly' | 'yearly' | 'custom';

export interface ParsedRecurrence {
  freq: RecurrenceFreq;
  interval: number;
  byDay: DayCode[];
  byDayOrdinal: number | null;
  byMonthDay: number | null;
  byMonth: number | null;
  until: string | null;
  count: number | null;
}

export const DAYS: { code: DayCode; short: string; full: string; jsDay: number }[] = [
  { code: 'MO', short: 'Mon', full: 'Monday', jsDay: 1 },
  { code: 'TU', short: 'Tue', full: 'Tuesday', jsDay: 2 },
  { code: 'WE', short: 'Wed', full: 'Wednesday', jsDay: 3 },
  { code: 'TH', short: 'Thu', full: 'Thursday', jsDay: 4 },
  { code: 'FR', short: 'Fri', full: 'Friday', jsDay: 5 },
  { code: 'SA', short: 'Sat', full: 'Saturday', jsDay: 6 },
  { code: 'SU', short: 'Sun', full: 'Sunday', jsDay: 0 }
];

export const MONTHS: { value: number; short: string; full: string }[] = [
  { value: 1, short: 'Jan', full: 'January' },
  { value: 2, short: 'Feb', full: 'February' },
  { value: 3, short: 'Mar', full: 'March' },
  { value: 4, short: 'Apr', full: 'April' },
  { value: 5, short: 'May', full: 'May' },
  { value: 6, short: 'Jun', full: 'June' },
  { value: 7, short: 'Jul', full: 'July' },
  { value: 8, short: 'Aug', full: 'August' },
  { value: 9, short: 'Sep', full: 'September' },
  { value: 10, short: 'Oct', full: 'October' },
  { value: 11, short: 'Nov', full: 'November' },
  { value: 12, short: 'Dec', full: 'December' }
];

const DAY_ORDER = new Map<DayCode, number>(DAYS.map((d, i) => [d.code, i]));
const WEEKDAYS: DayCode[] = ['MO', 'TU', 'WE', 'TH', 'FR'];

export function dayCodeFromJs(jsDay: number): DayCode {
  return DAYS.find((d) => d.jsDay === jsDay)?.code ?? 'MO';
}

function parseUntil(raw: string): string | null {
  const text = raw.trim();
  if (/^\d{4}-\d{2}-\d{2}$/.test(text)) return text;
  if (/^\d{8}/.test(text)) return `${text.slice(0, 4)}-${text.slice(4, 6)}-${text.slice(6, 8)}`;
  return null;
}

export function parseRecurrence(rule: string | null | undefined): ParsedRecurrence | null {
  if (!rule || !rule.trim()) return null;
  const text = rule.trim();
  const base = (freq: RecurrenceFreq, byDay: DayCode[] = []): ParsedRecurrence => ({
    freq,
    interval: 1,
    byDay,
    byDayOrdinal: null,
    byMonthDay: null,
    byMonth: null,
    until: null,
    count: null
  });
  switch (text.toLowerCase()) {
    case 'daily':
    case 'every day':
      return base('DAILY');
    case 'weekdays':
    case 'every weekday':
      return base('WEEKLY', [...WEEKDAYS]);
    case 'weekly':
    case 'every week':
      return base('WEEKLY');
    case 'monthly':
    case 'every month':
      return base('MONTHLY');
    case 'yearly':
    case 'annually':
    case 'anually':
    case 'every year':
      return base('YEARLY');
  }

  const parts = new Map<string, string>();
  for (const part of text.replace(/^RRULE:/i, '').split(';')) {
    if (!part.trim()) continue;
    const eq = part.indexOf('=');
    if (eq <= 0) continue;
    parts.set(part.slice(0, eq).trim().toUpperCase(), part.slice(eq + 1).trim().toUpperCase());
  }
  const freq = parts.get('FREQ');
  if (freq !== 'DAILY' && freq !== 'WEEKLY' && freq !== 'MONTHLY' && freq !== 'YEARLY') return null;

  const rawInterval = Number(parts.get('INTERVAL'));
  const interval = Number.isInteger(rawInterval) && rawInterval > 0 ? rawInterval : 1;

  const byDay: DayCode[] = [];
  let byDayOrdinal: number | null = null;
  const rawByDay = parts.get('BYDAY');
  if (rawByDay) {
    for (const rawToken of rawByDay.split(',')) {
      const token = rawToken.trim();
      const m = /^([+-]?[1-5])?(MO|TU|WE|TH|FR|SA|SU)$/.exec(token);
      if (!m) continue;
      if (m[1] && byDayOrdinal === null) byDayOrdinal = Number(m[1]);
      byDay.push(m[2] as DayCode);
    }
  }
  if (byDayOrdinal === null && byDay.length === 1 && parts.has('BYSETPOS')) {
    const pos = Number(parts.get('BYSETPOS'));
    if (Number.isInteger(pos) && pos >= -5 && pos <= 5 && pos !== 0) byDayOrdinal = pos;
  }

  const rawMd = parts.get('BYMONTHDAY');
  const mdNum = rawMd !== undefined ? Number(rawMd) : NaN;
  const byMonthDay = Number.isInteger(mdNum) && ((mdNum >= 1 && mdNum <= 31) || mdNum === -1) ? mdNum : null;

  const rawBm = parts.get('BYMONTH');
  const bmNum = rawBm !== undefined ? Number(rawBm) : NaN;
  const byMonth = Number.isInteger(bmNum) && bmNum >= 1 && bmNum <= 12 ? bmNum : null;

  const until = parts.has('UNTIL') ? parseUntil(parts.get('UNTIL')!) : null;
  const rawCnt = parts.get('COUNT');
  const cntNum = rawCnt !== undefined ? Number(rawCnt) : NaN;
  const count = Number.isInteger(cntNum) && cntNum > 0 ? cntNum : null;

  return { freq, interval, byDay, byDayOrdinal, byMonthDay, byMonth, until, count };
}

function sameDays(a: DayCode[], b: DayCode[]): boolean {
  if (a.length !== b.length) return false;
  const sa = [...a].sort();
  const sb = [...b].sort();
  return sa.every((v, i) => v === sb[i]);
}

export function classifyRecurrence(rule: string | null | undefined): RecurrencePreset {
  if (!rule || !rule.trim()) return 'none';
  const parsed = parseRecurrence(rule);
  if (!parsed) return 'custom';
  if (parsed.interval === 1 && parsed.byDayOrdinal === null && parsed.byMonthDay === null && parsed.byMonth === null && !parsed.until && parsed.count === null) {
    if (parsed.freq === 'DAILY' && parsed.byDay.length === 0) return 'daily';
    if (parsed.freq === 'WEEKLY' && parsed.byDay.length === 0) return 'weekly';
    if (parsed.freq === 'WEEKLY' && sameDays(parsed.byDay, WEEKDAYS)) return 'weekdays';
    if (parsed.freq === 'MONTHLY' && parsed.byDay.length === 0) return 'monthly';
    if (parsed.freq === 'YEARLY' && parsed.byDay.length === 0) return 'yearly';
  }
  return 'custom';
}

export function formatRecurrenceRule(rule: ParsedRecurrence): string {
  const sortedDays = [...new Set(rule.byDay)].sort((a, b) => (DAY_ORDER.get(a) ?? 0) - (DAY_ORDER.get(b) ?? 0));
  const noModifiers = rule.byDayOrdinal === null && rule.byMonthDay === null && rule.byMonth === null && !rule.until && rule.count === null;
  if (rule.interval === 1 && noModifiers) {
    if (rule.freq === 'DAILY' && sortedDays.length === 0) return 'daily';
    if (rule.freq === 'WEEKLY' && sortedDays.length === 0) return 'weekly';
    if (rule.freq === 'WEEKLY' && sameDays(sortedDays, WEEKDAYS)) return 'weekdays';
    if (rule.freq === 'MONTHLY' && sortedDays.length === 0) return 'monthly';
    if (rule.freq === 'YEARLY' && sortedDays.length === 0) return 'yearly';
  }
  const parts = [`FREQ=${rule.freq}`];
  if (rule.interval > 1) parts.push(`INTERVAL=${rule.interval}`);
  if (rule.byMonth !== null) parts.push(`BYMONTH=${rule.byMonth}`);
  if (rule.byDayOrdinal !== null && sortedDays.length > 0) parts.push(`BYDAY=${rule.byDayOrdinal}${sortedDays[0]}`);
  else if (sortedDays.length > 0) parts.push(`BYDAY=${sortedDays.join(',')}`);
  if (rule.byMonthDay !== null) parts.push(`BYMONTHDAY=${rule.byMonthDay}`);
  if (rule.until) parts.push(`UNTIL=${rule.until}`);
  else if (rule.count !== null && rule.count > 0) parts.push(`COUNT=${rule.count}`);
  return parts.join(';');
}

function ordinalLabel(ord: number): string {
  if (ord === -1) return 'last';
  if (ord === 1) return '1st';
  if (ord === 2) return '2nd';
  if (ord === 3) return '3rd';
  return `${ord}th`;
}

export function describeRecurrence(rule: string | ParsedRecurrence | null | undefined): string {
  if (!rule) return '';
  const parsed = typeof rule === 'string' ? parseRecurrence(rule) : rule;
  if (!parsed) return typeof rule === 'string' ? rule : '';

  const days = [...new Set(parsed.byDay)].sort((a, b) => (DAY_ORDER.get(a) ?? 0) - (DAY_ORDER.get(b) ?? 0));
  const dayNames = days.map((code) => DAYS.find((d) => d.code === code)?.short ?? code);
  const fullDay = (code: DayCode) => DAYS.find((d) => d.code === code)?.full ?? code;
  const monthInfo = (m: number) => MONTHS.find((x) => x.value === m);

  let core = '';
  if (parsed.freq === 'DAILY') {
    core = parsed.interval === 1 ? 'Daily' : `Every ${parsed.interval} days`;
  } else if (parsed.freq === 'WEEKLY') {
    if (parsed.interval === 1 && sameDays(days, WEEKDAYS)) core = 'Weekdays (Mon–Fri)';
    else if (days.length > 0) core = `${parsed.interval === 1 ? 'Weekly' : `Every ${parsed.interval} weeks`} on ${dayNames.join(', ')}`;
    else core = parsed.interval === 1 ? 'Weekly' : `Every ${parsed.interval} weeks`;
  } else if (parsed.freq === 'MONTHLY') {
    const prefix = parsed.interval === 1 ? 'Monthly' : `Every ${parsed.interval} months`;
    if (parsed.byDayOrdinal !== null && days.length > 0) core = `${prefix} on the ${ordinalLabel(parsed.byDayOrdinal)} ${fullDay(days[0])}`;
    else if (parsed.byMonthDay === -1) core = `${prefix} on the last day`;
    else if (parsed.byMonthDay !== null) core = `${prefix} on day ${parsed.byMonthDay}`;
    else core = prefix;
  } else {
    const prefix = parsed.interval === 1 ? 'Annually' : `Every ${parsed.interval} years`;
    const m = parsed.byMonth ? monthInfo(parsed.byMonth) : undefined;
    if (parsed.byDayOrdinal !== null && days.length > 0 && m) core = `${prefix} on the ${ordinalLabel(parsed.byDayOrdinal)} ${fullDay(days[0])} of ${m.full}`;
    else if (m && parsed.byMonthDay === -1) core = `${prefix} on the last day of ${m.full}`;
    else if (m && parsed.byMonthDay !== null) core = `${prefix} on ${parsed.byMonthDay} ${m.short}`;
    else core = prefix;
  }

  if (parsed.until) core += `, until ${parsed.until}`;
  else if (parsed.count !== null) core += parsed.count === 1 ? ', 1 time' : `, ${parsed.count} times`;
  return core;
}

function daysInMonth(year: number, month: number): number {
  return new Date(year, month, 0).getDate();
}

function nthWeekday(year: number, month: number, jsDay: number, ordinal: number): string | null {
  const total = daysInMonth(year, month);
  if (ordinal > 0) {
    const firstJsDay = new Date(year, month - 1, 1).getDay();
    const offset = (jsDay - firstJsDay + 7) % 7;
    const day = 1 + offset + 7 * (ordinal - 1);
    return day <= total ? `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}` : null;
  }
  const lastJsDay = new Date(year, month - 1, total).getDay();
  const back = (lastJsDay - jsDay + 7) % 7;
  const day = total - back - 7 * (-ordinal - 1);
  return day >= 1 ? `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}` : null;
}

function startOfWeek(iso: string): string {
  const d = parseIsoDate(iso);
  const diff = (d.getDay() + 6) % 7;
  return addDays(iso, -diff);
}

function dayDiff(a: string, b: string): number {
  return Math.round((parseIsoDate(b).getTime() - parseIsoDate(a).getTime()) / 86400000);
}

export function nextOccurrence(rule: ParsedRecurrence, scheduledIso?: string | null): string | null {
  const scheduled = scheduledIso && /^\d{4}-\d{2}-\d{2}/.test(scheduledIso) ? scheduledIso.slice(0, 10) : isoDate();
  const [y, m, d] = scheduled.split('-').map(Number);
  let next: string | null = null;

  if (rule.freq === 'DAILY') {
    next = addDays(scheduled, rule.interval);
  } else if (rule.freq === 'WEEKLY') {
    if (rule.byDay.length === 0) {
      next = addDays(scheduled, 7 * rule.interval);
    } else {
      const targetJsDays = new Set(rule.byDay.map((code) => DAYS.find((x) => x.code === code)!.jsDay));
      const baseWeek = startOfWeek(scheduled);
      for (let i = 1; i <= 7 * rule.interval + 7; i++) {
        const cand = addDays(scheduled, i);
        const weeksAhead = Math.round(dayDiff(baseWeek, startOfWeek(cand)) / 7);
        if (targetJsDays.has(parseIsoDate(cand).getDay()) && (weeksAhead === 0 || weeksAhead % rule.interval === 0)) {
          next = cand;
          break;
        }
      }
      next ??= addDays(scheduled, 7 * rule.interval);
    }
  } else if (rule.freq === 'MONTHLY') {
    if (rule.byDayOrdinal !== null && rule.byDay.length > 0) {
      const jsDay = DAYS.find((x) => x.code === rule.byDay[0])!.jsDay;
      for (let step = 1; step <= 60; step++) {
        const target = new Date(y, m - 1 + rule.interval * step, 1);
        const hit = nthWeekday(target.getFullYear(), target.getMonth() + 1, jsDay, rule.byDayOrdinal);
        if (hit) { next = hit; break; }
      }
    }
    if (!next) {
      const target = new Date(y, m - 1 + rule.interval, 1);
      const ty = target.getFullYear();
      const tm = target.getMonth() + 1;
      const total = daysInMonth(ty, tm);
      const day = rule.byMonthDay === -1 ? total : Math.min(rule.byMonthDay ?? d, total);
      next = `${ty}-${String(tm).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
    }
  } else {
    const tm = rule.byMonth ?? m;
    if (rule.byDayOrdinal !== null && rule.byDay.length > 0) {
      const jsDay = DAYS.find((x) => x.code === rule.byDay[0])!.jsDay;
      for (let step = 1; step <= 20; step++) {
        const hit = nthWeekday(y + rule.interval * step, tm, jsDay, rule.byDayOrdinal);
        if (hit) { next = hit; break; }
      }
    }
    if (!next) {
      const ty = y + rule.interval;
      const total = daysInMonth(ty, tm);
      const day = rule.byMonthDay === -1 ? total : Math.min(rule.byMonthDay ?? d, total);
      next = `${ty}-${String(tm).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
    }
  }

  if (rule.until && next > rule.until) return null;
  return next;
}

export function defaultCustomRecurrence(currentRule: string | null | undefined, scheduledIso?: string | null): ParsedRecurrence {
  const existing = parseRecurrence(currentRule);
  if (existing) return { ...existing, byDay: [...existing.byDay] };
  const baseDate = scheduledIso && /^\d{4}-\d{2}-\d{2}/.test(scheduledIso) ? scheduledIso.slice(0, 10) : isoDate();
  return {
    freq: 'WEEKLY',
    interval: 1,
    byDay: [dayCodeFromJs(parseIsoDate(baseDate).getDay())],
    byDayOrdinal: null,
    byMonthDay: null,
    byMonth: null,
    until: null,
    count: null
  };
}
