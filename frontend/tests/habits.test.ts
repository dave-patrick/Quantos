import { describe, expect, it } from 'vitest';
import { daysBack, keyFor, met, streaks, summary, type Tracker } from '../src/lib/habits';

const run: Tracker = { key: 'run', label: 'Run', kind: 'check' };
const sleep: Tracker = { key: 'sleep', label: 'Sleep', kind: 'number', unit: 'h', goal: 7 };
const coffee: Tracker = { key: 'coffee', label: 'Coffee', kind: 'number', goal: 2, lower: true };

describe('habits', () => {
  it('lists days ending today, across month ends', () => {
    expect(daysBack('2026-03-02', 3)).toEqual(['2026-02-28', '2026-03-01', '2026-03-02']);
  });

  it('decides when a day counts', () => {
    expect(met(run, true)).toBe(true);
    expect(met(run, undefined)).toBe(false);
    expect(met(sleep, 7.5)).toBe(true);
    expect(met(sleep, '6')).toBe(false);
    expect(met(coffee, 3)).toBe(false);
    expect(met(coffee, 1)).toBe(true);
  });

  it('counts streaks, forgiving a today not filled in yet', () => {
    const days = daysBack('2026-10-18', 6);
    const values = { '2026-10-13': { run: true }, '2026-10-14': { run: true }, '2026-10-15': { run: true }, '2026-10-16': { run: true }, '2026-10-17': { run: true } };
    expect(streaks(run, days, values)).toEqual({ current: 5, best: 5 });
    const broken = { ...values, '2026-10-15': {} };
    expect(streaks(run, days, broken)).toEqual({ current: 2, best: 2 });
  });

  it('summarises numbers', () => {
    const days = daysBack('2026-10-18', 4);
    const s = summary(sleep, days, { '2026-10-15': { sleep: 6 }, '2026-10-16': { sleep: 8 }, '2026-10-18': { sleep: 7 } });
    expect(s.average).toBe(7);
    expect(s.hits).toBe(2);
    expect(s.rate).toBe(0.5);
  });

  it('makes safe, unique property names', () => {
    expect(keyFor('Read 20 pages!', [])).toBe('read_20_pages');
    expect(keyFor('Title', [])).toBe('habit_title');
    expect(keyFor('Run', ['run'])).toBe('run_2');
    expect(keyFor('10k steps', [])).toBe('k_steps');
  });
});
