import { describe, expect, it } from 'vitest';
import { addDays, daysBetween, layout, moved, span, type GanttTask } from '../src/lib/gantt';

const A = '01JQEXAMP0E0000000000000A1', B = '01JQEXAMP0E0000000000000B2', C = '01JQEXAMP0E0000000000000C3';
const task = (over: Partial<GanttTask>): GanttTask => ({ id: A, title: 'T', status: 'todo', planned: null, due: null, effort: null, dependsOn: [], project: null, created: null, completed: null, timeBlock: null, ...over });

describe('gantt', () => {
  it('does date arithmetic across months', () => {
    expect(addDays('2026-10-30', 3)).toBe('2026-11-02');
    expect(daysBetween('2026-10-30', '2026-11-02')).toBe(3);
  });

  it('places a task from its dates, or its estimate', () => {
    expect(span(task({ planned: '2026-10-20', due: '2026-10-24T17:00:00-07:00' }))).toEqual({ start: '2026-10-20', end: '2026-10-24', estimated: false });
    expect(span(task({ planned: '2026-10-20', effort: 960 }))).toEqual({ start: '2026-10-20', end: '2026-10-21', estimated: true });
    expect(span(task({ due: '2026-10-24' }))).toEqual({ start: '2026-10-24', end: '2026-10-24', estimated: true });
    expect(span(task({}))).toBeNull();
  });

  it('draws dependencies and flags the ones out of order', () => {
    const l = layout([
      task({ id: A, title: 'Design', planned: '2026-10-19', due: '2026-10-22', project: '[[01JQEXAMP0E000000000000030|Apollo]]' }),
      task({ id: B, title: 'Build', planned: '2026-10-21', due: '2026-10-28', dependsOn: [`[[${A}|Design]]`], project: '[[01JQEXAMP0E000000000000030|Apollo]]' }),
      task({ id: C, title: 'Someday' })
    ]);
    expect(l.bars.map((b) => b.title)).toEqual(['Design', 'Build']);
    expect(l.bars[0].group).toBe('Apollo');
    expect(l.edges).toEqual([{ from: A, to: B, late: true }]);
    expect(l.unscheduled.map((t) => t.title)).toEqual(['Someday']);
    expect([l.from, l.to]).toEqual(['2026-10-19', '2026-10-28']);
  });

  it('moves both dates together', () => {
    expect(moved(task({ planned: '2026-10-20', due: '2026-10-24' }), 3)).toEqual({ planned: '2026-10-23', due: '2026-10-27' });
    expect(moved(task({ due: '2026-10-24' }), -1)).toEqual({ due: '2026-10-23' });
  });
});
