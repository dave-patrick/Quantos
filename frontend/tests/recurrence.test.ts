// @vitest-environment happy-dom
import { describe, expect, it } from 'vitest';
import {
  classifyRecurrence,
  defaultCustomRecurrence,
  describeRecurrence,
  formatRecurrenceRule,
  nextOccurrence,
  parseRecurrence
} from '../src/lib/recurrence';

describe('recurrence parsing and formatting', () => {
  it('classifies daily, weekdays, weekly, monthly, annually and custom rules', () => {
    expect(classifyRecurrence(null)).toBe('none');
    expect(classifyRecurrence('')).toBe('none');
    expect(classifyRecurrence('daily')).toBe('daily');
    expect(classifyRecurrence('FREQ=DAILY;INTERVAL=1')).toBe('daily');
    expect(classifyRecurrence('weekdays')).toBe('weekdays');
    expect(classifyRecurrence('FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR')).toBe('weekdays');
    expect(classifyRecurrence('weekly')).toBe('weekly');
    expect(classifyRecurrence('monthly')).toBe('monthly');
    expect(classifyRecurrence('yearly')).toBe('yearly');
    expect(classifyRecurrence('annually')).toBe('yearly');
    expect(classifyRecurrence('FREQ=WEEKLY;INTERVAL=2')).toBe('custom');
    expect(classifyRecurrence('FREQ=WEEKLY;BYDAY=MO,WE,FR')).toBe('custom');
    expect(classifyRecurrence('FREQ=MONTHLY;BYMONTHDAY=-1')).toBe('custom');
    expect(classifyRecurrence('FREQ=MONTHLY;BYDAY=1MO')).toBe('custom');
    expect(classifyRecurrence('FREQ=DAILY;UNTIL=2027-01-31')).toBe('custom');
  });

  it('formats simple presets and complex custom rules cleanly', () => {
    expect(formatRecurrenceRule(parseRecurrence('daily')!)).toBe('daily');
    expect(formatRecurrenceRule(parseRecurrence('annually')!)).toBe('yearly');
    expect(formatRecurrenceRule(parseRecurrence('weekdays')!)).toBe('weekdays');

    expect(formatRecurrenceRule({
      freq: 'WEEKLY',
      interval: 2,
      byDay: ['FR', 'MO', 'WE'],
      byDayOrdinal: null,
      byMonthDay: null,
      byMonth: null,
      until: null,
      count: null
    })).toBe('FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR');

    expect(formatRecurrenceRule({
      freq: 'MONTHLY',
      interval: 1,
      byDay: ['FR'],
      byDayOrdinal: -1,
      byMonthDay: null,
      byMonth: null,
      until: '2027-12-31',
      count: null
    })).toBe('FREQ=MONTHLY;BYDAY=-1FR;UNTIL=2027-12-31');

    expect(formatRecurrenceRule({
      freq: 'YEARLY',
      interval: 2,
      byDay: [],
      byDayOrdinal: null,
      byMonthDay: 15,
      byMonth: 3,
      until: null,
      count: 4
    })).toBe('FREQ=YEARLY;INTERVAL=2;BYMONTH=3;BYMONTHDAY=15;COUNT=4');
  });

  it('describes presets and custom rules in plain English', () => {
    expect(describeRecurrence('daily')).toBe('Daily');
    expect(describeRecurrence('weekdays')).toBe('Weekdays (Mon–Fri)');
    expect(describeRecurrence('weekly')).toBe('Weekly');
    expect(describeRecurrence('monthly')).toBe('Monthly');
    expect(describeRecurrence('annually')).toBe('Annually');
    expect(describeRecurrence('FREQ=DAILY;INTERVAL=3')).toBe('Every 3 days');
    expect(describeRecurrence('FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,FR')).toBe('Every 2 weeks on Mon, Fri');
    expect(describeRecurrence('FREQ=MONTHLY;BYMONTHDAY=-1')).toBe('Monthly on the last day');
    expect(describeRecurrence('FREQ=MONTHLY;BYDAY=1MO')).toBe('Monthly on the 1st Monday');
    expect(describeRecurrence('FREQ=YEARLY;BYMONTH=11;BYDAY=4TH;COUNT=5')).toBe('Annually on the 4th Thursday of November, 5 times');
  });

  it('computes next occurrences matching backend rules', () => {
    expect(nextOccurrence(parseRecurrence('daily')!, '2026-11-24')).toBe('2026-11-25');
    expect(nextOccurrence(parseRecurrence('FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,FR')!, '2026-11-23')).toBe('2026-11-27');
    expect(nextOccurrence(parseRecurrence('FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,FR')!, '2026-11-27')).toBe('2026-12-07');
    expect(nextOccurrence(parseRecurrence('FREQ=MONTHLY;BYMONTHDAY=-1')!, '2027-01-31')).toBe('2027-02-28');
    expect(nextOccurrence(parseRecurrence('FREQ=MONTHLY;BYDAY=1MO')!, '2026-11-02')).toBe('2026-12-07');
    expect(nextOccurrence(parseRecurrence('FREQ=MONTHLY;BYDAY=-1FR')!, '2026-11-27')).toBe('2026-12-25');
    expect(nextOccurrence(parseRecurrence('FREQ=YEARLY;BYMONTH=11;BYDAY=4TH')!, '2026-11-26')).toBe('2027-11-25');
    expect(nextOccurrence(parseRecurrence('FREQ=DAILY;INTERVAL=5;UNTIL=2026-11-26')!, '2026-11-24')).toBeNull();
  });

  it('seeds custom recurrence defaults from the scheduled date when none is set', () => {
    const def = defaultCustomRecurrence(null, '2026-11-24'); // Tuesday
    expect(def.freq).toBe('WEEKLY');
    expect(def.interval).toBe(1);
    expect(def.byDay).toEqual(['TU']);
  });
});
