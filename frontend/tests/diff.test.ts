import { describe, expect, it } from 'vitest';
import { compare, splitFrontmatter, wordDiff } from '../src/lib/diff';

const note = (props: string, body: string) => `---\nid: X\ntitle: Plan\n${props}modified: 2026-10-18T10:00:00-07:00\n---\n${body}`;

describe('wordDiff', () => {
  it('marks only the words that changed', () => {
    const w = wordDiff('Ship the beta on Friday.', 'Ship the release on Monday.');
    expect(w.old.filter((s) => s.kind === 'del').map((s) => s.text)).toEqual(['beta', 'Friday']);
    expect(w.new.filter((s) => s.kind === 'add').map((s) => s.text)).toEqual(['release', 'Monday']);
    expect(w.new.map((s) => s.text).join('')).toBe('Ship the release on Monday.');
  });
});

describe('compare', () => {
  it('lists property changes apart from the text and ignores the modified stamp', () => {
    const c = compare(note('status: Draft\n', 'Hello\n'), note('status: Done\nowner: Sam\n', 'Hello\n').replace('10:00', '11:00'));
    expect(c.props).toEqual([{ key: 'status', before: 'Draft', after: 'Done' }, { key: 'owner', before: null, after: 'Sam' }]);
    expect(c.changes).toBe(0);
  });

  it('folds unchanged stretches and counts changes', () => {
    const lines = Array.from({ length: 30 }, (_, i) => `Line ${i + 1}`);
    const changed = [...lines];
    changed[2] = 'Line three';
    changed.splice(25, 1);
    const c = compare(lines.join('\n'), changed.join('\n'));
    expect(c.changes).toBe(2);
    expect(c.removed).toBe(2);
    expect(c.added).toBe(1);
    expect(c.blocks.map((b) => b.kind)).toEqual(['rows', 'fold', 'rows', 'fold']);
    const first = c.blocks[0];
    expect(first.kind === 'rows' && first.rows.find((r) => r.kind === 'mod')?.oldNo).toBe(3);
  });

  it('reads multi-line frontmatter values as one property', () => {
    const { props, body } = splitFrontmatter('---\ntags:\n  - a\n  - b\ntitle: T\n---\nBody');
    expect(props.get('tags')).toBe('- a\n  - b');
    expect(body).toBe('Body');
  });
});
