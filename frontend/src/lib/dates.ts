/** Dates use the device's current time zone (BUILD-PLAN decision log). */
export function isoDate(d: Date = new Date()): string {
  const y = d.getFullYear(), m = String(d.getMonth() + 1).padStart(2, '0'), day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

export function parseIsoDate(s: string): Date {
  const [y, m, d] = s.slice(0, 10).split('-').map(Number);
  return new Date(y, m - 1, d);
}

export function addDays(iso: string, days: number): string {
  const d = parseIsoDate(iso);
  d.setDate(d.getDate() + days);
  return isoDate(d);
}

export function dayLabel(iso: string): string {
  const today = isoDate();
  if (iso === today) return 'Today';
  if (iso === addDays(today, -1)) return 'Yesterday';
  if (iso === addDays(today, 1)) return 'Tomorrow';
  return parseIsoDate(iso).toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long' });
}

export function longDate(iso: string): string {
  return parseIsoDate(iso).toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
}

export function relativeTime(value: string | null | undefined): string {
  if (!value) return '';
  const then = new Date(value).getTime();
  if (Number.isNaN(then)) return value;
  const diff = (Date.now() - then) / 1000;
  if (diff < 45) return 'just now';
  if (diff < 3600) return `${Math.round(diff / 60)} min ago`;
  if (diff < 86400) return `${Math.round(diff / 3600)} h ago`;
  if (diff < 86400 * 7) return `${Math.round(diff / 86400)} d ago`;
  return new Date(value).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
}

export function timeOfDay(value: string): string {
  return new Date(value).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

/** Fires when the local date changes (midnight, sleep/resume, or a time zone change). */
export function watchDate(callback: (today: string) => void): () => void {
  let current = isoDate();
  const check = () => {
    const now = isoDate();
    if (now !== current) { current = now; callback(now); }
  };
  const timer = setInterval(check, 30_000);
  document.addEventListener('visibilitychange', check);
  window.addEventListener('focus', check);
  return () => {
    clearInterval(timer);
    document.removeEventListener('visibilitychange', check);
    window.removeEventListener('focus', check);
  };
}
