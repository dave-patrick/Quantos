import { describe, expect, it } from 'vitest';
import { priorityLabel, effortLabel, duration } from '../src/lib/taskLevels';
import { parseQuickAdd } from '../src/lib/quickadd';

describe('task levels', () => {
  it('labels priorities in their existing order', () => {
    expect([1, 2, 3, 4].map(priorityLabel)).toEqual(['Critical', 'High', 'Medium', 'Low']);
  });
  it('keeps levels distinct from legacy duration estimates', () => {
    expect(parseQuickAdd('Task ~high').effort).toBe('high');
    expect(parseQuickAdd('Task ~2h').effort).toBe(120);
    expect(effortLabel('medium')).toBe('Medium');
    expect(duration('high')).toBeNull();
    expect(duration(120)).toBe(120);
  });
});
