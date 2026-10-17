import { describe, expect, it } from 'vitest';
import { fieldProblem } from '../src/lib/fields';
import type { FieldDef } from '../src/lib/supertags.svelte';

const f = (type: FieldDef['type'], extra: Partial<FieldDef> = {}): FieldDef => ({ name: 'x', key: 'x', type, ...extra });

describe('field rules (forms block, properties warn)', () => {
  it('requires a value only when the field is required', () => {
    expect(fieldProblem(f('text', { required: true }), '  ')).toBe('Required');
    expect(fieldProblem(f('text'), '')).toBeNull();
  });
  it('checks dates are real', () => {
    expect(fieldProblem(f('date'), '2026-02-28')).toBeNull();
    expect(fieldProblem(f('date'), '2026-02-30')).not.toBeNull();
    expect(fieldProblem(f('date'), '2026-13-01')).not.toBeNull();
    expect(fieldProblem(f('datetime'), '2026-10-17T09:30')).toBeNull();
  });
  it('checks numbers, links and email addresses', () => {
    expect(fieldProblem(f('number'), '12.5')).toBeNull();
    expect(fieldProblem(f('number'), 'twelve')).not.toBeNull();
    expect(fieldProblem(f('url'), 'example.com')).not.toBeNull();
    expect(fieldProblem(f('url'), 'https://example.com/a')).toBeNull();
    expect(fieldProblem(f('email'), 'sam@example')).not.toBeNull();
    expect(fieldProblem(f('email'), 'sam@example.com')).toBeNull();
  });
  it('checks every chosen option, including status and multi-select', () => {
    const opts = { options: ['draft', 'final'] };
    expect(fieldProblem(f('status', opts), 'final')).toBeNull();
    expect(fieldProblem(f('select', opts), 'lost')).toBe('Choose from: draft, final');
    expect(fieldProblem(f('multiselect', opts), ['draft', 'lost'])).not.toBeNull();
    expect(fieldProblem(f('multiselect', opts), ['draft', 'final'])).toBeNull();
  });
});