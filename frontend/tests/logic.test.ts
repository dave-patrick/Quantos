import { describe, expect, it } from 'vitest';
import { merge3, minimalChange, wordCount } from '../src/lib/text';
import { parseDate, parseQuickAdd } from '../src/lib/quickadd';
import { addDays, isoDate } from '../src/lib/dates';
import { parsePasted, propertyKey } from '../src/lib/query';

describe('three-way merge (keeps unsaved edits when the file changes elsewhere)', () => {
  const base = 'line 1\nline 2\nline 3\n';
  it('takes changes from both sides when they touch different lines', () => {
    expect(merge3(base, 'line 1 edited\nline 2\nline 3\n', 'line 1\nline 2\nline 3\n- 09:30 captured\n'))
      .toBe('line 1 edited\nline 2\nline 3\n- 09:30 captured\n');
  });
  it('reports a conflict when both sides change the same line', () => {
    expect(merge3(base, 'line 1\nmine\nline 3\n', 'line 1\ntheirs\nline 3\n')).toBeNull();
  });
  it('keeps both when each side only added lines at the end (capture while typing)', () => {
    expect(merge3(base, 'line 1\nline 2\nline 3\nTyped while capturing', 'line 1\nline 2\nline 3\n- 18:30 captured\n'))
      .toBe('line 1\nline 2\nline 3\n- 18:30 captured\nTyped while capturing\n');
  });
  it('still asks when an addition on one side meets a change on the other', () => {
    expect(merge3(base, 'line 1\nline 2\nline 3 changed\n', 'line 1\nline 2\nline 3 also changed\n')).toBeNull();
  });
  it('accepts identical changes on both sides', () => {
    expect(merge3(base, 'same\nline 2\nline 3\n', 'same\nline 2\nline 3\n')).toBe('same\nline 2\nline 3\n');
  });
  it('returns the other side when one side is unchanged', () => {
    expect(merge3(base, base, 'x')).toBe('x');
    expect(merge3(base, 'y', base)).toBe('y');
  });
});

describe('minimal change', () => {
  it('replaces only the differing middle', () => {
    expect(minimalChange('hello world', 'hello brave world')).toEqual({ from: 6, to: 6, insert: 'brave ' });
    expect(minimalChange('same', 'same')).toBeNull();
  });
});

describe('quick add', () => {
  const today = '2026-10-15'; // a Thursday
  it('parses priority, dates, effort, tags, projects and recurrence', () => {
    const q = parseQuickAdd('Call Sam due:fri !2 ~1h #client +apollo-launch every week @tomorrow', today);
    expect(q.title).toBe('Call Sam');
    expect(q.priority).toBe(2);
    expect(q.due).toBe('2026-10-16');
    expect(q.planned).toBe('2026-10-16');
    expect(q.effort).toBe(60);
    expect(q.tags).toEqual(['client']);
    expect(q.project).toBe('apollo launch');
    expect(q.recurrence).toBe('weekly');
  });
  it('understands relative and numeric dates', () => {
    expect(parseDate('+3d', today)).toBe('2026-10-18');
    expect(parseDate('+2w', today)).toBe('2026-10-29');
    expect(parseDate('thu', today)).toBe('2026-10-22'); // next Thursday, not today
    expect(parseDate('1/1', today)).toBe('2027-01-01');
    expect(parseDate('banana', today)).toBeUndefined();
  });
  it('leaves unrecognised tokens in the title', () => {
    expect(parseQuickAdd('Fix bug #12a due:never', today).title).toBe('Fix bug due:never');
  });
  it('turns "every 2 weeks" into an RRULE', () => {
    expect(parseQuickAdd('Payroll every 2 weeks', today).recurrence).toBe('FREQ=WEEKLY;INTERVAL=2');
  });
});

describe('structured paste', () => {
  it('reads tab-separated rows from a spreadsheet', () => {
    const p = parsePasted('Title\tStatus\tDue\nQ4 budget\tdraft\t2026-11-01\nHiring\topen\t\n');
    expect(p.headers).toEqual(['Title', 'Status', 'Due']);
    expect(p.rows).toEqual([['Q4 budget', 'draft', '2026-11-01'], ['Hiring', 'open', '']]);
  });
  it('reads CSV with quoted commas', () => {
    expect(parsePasted('name,notes\n"Smith, J","said ""hi"""').rows).toEqual([['Smith, J', 'said "hi"']]);
  });
  it('reads Markdown tables and skips the divider', () => {
    expect(parsePasted('| a | b |\n|---|---|\n| 1 | 2 |').rows).toEqual([['1', '2']]);
  });
  it('treats a single column of lines as titles', () => {
    expect(parsePasted('First\nSecond')).toEqual({ headers: ['title'], rows: [['First'], ['Second']] });
    expect(parsePasted('Title\nOnly row').rows).toEqual([['Only row']]);
  });
  it('makes property keys from headers', () => {
    expect(propertyKey('Due Date (UK)')).toBe('due_date_uk');
  });
});

describe('dates and counting', () => {
  it('adds days across month ends in local time', () => {
    expect(addDays('2026-10-31', 1)).toBe('2026-11-01');
    expect(isoDate(new Date(2026, 0, 5))).toBe('2026-01-05');
  });
  it('counts words outside code blocks', () => {
    expect(wordCount('Hello there, world.\n```\nnot these words\n```\n[[01JABCDEF0123456789XYZABCD|Two words]] - [ ] Task <!--task:01JABCDEF0123456789XYZABCD-->')).toBe(6);
  });
});
