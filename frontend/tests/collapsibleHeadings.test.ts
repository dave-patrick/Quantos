// @vitest-environment happy-dom
import { describe, expect, it } from 'vitest';
import { EditorState } from '@codemirror/state';
import { renderMarkdown } from '../src/lib/render';
import { headingOn } from '../src/lib/editor/headings';

describe('collapsible headings', () => {
  it('keeps the fold mark out of the heading text', () => {
    const html = renderMarkdown('## [-] Folded section\n\nText under it.\n');
    expect(html).toContain('Folded section');
    expect(html).not.toContain('[-]');
  });
  it('leaves a normal heading alone', () => {
    const html = renderMarkdown('## Plain section\n');
    expect(html).toContain('Plain section');
    expect(html).not.toContain('[-]');
  });
  it('reads the mark and the heading text off the line', () => {
    const state = EditorState.create({ doc: '## [-] Folded section\nText under it.\n' });
    const heading = headingOn(state, 1);
    expect(heading?.level).toBe(2);
    expect(heading?.mark).toBe('-');
    expect(state.doc.sliceString(heading!.from, heading!.to)).toBe('Folded section');
  });
  it('reads a plain heading without a mark', () => {
    const state = EditorState.create({ doc: '### Plain\n' });
    const heading = headingOn(state, 1);
    expect(heading?.mark).toBeUndefined();
    expect(state.doc.sliceString(heading!.from, heading!.to)).toBe('Plain');
  });
});
