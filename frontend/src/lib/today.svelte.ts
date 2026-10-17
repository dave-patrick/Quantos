import { isoDate, watchDate } from './dates';

/** Today's date that updates itself at midnight, after sleep or on a time zone change. */
export const day = $state({ today: isoDate() });
watchDate((d) => (day.today = d));