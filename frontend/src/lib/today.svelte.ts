import { isoDate, relativeTime, watchDate } from './dates';

/** Today's date that updates itself at midnight, after sleep or on a time zone change. */
export const day = $state({ today: isoDate(), minute: 0 });
watchDate((d) => (day.today = d));
setInterval(() => day.minute++, 60_000);

/** "5 min ago" that keeps counting while it's on screen. */
export function ago(value: string | null | undefined): string {
  void day.minute;
  return relativeTime(value);
}