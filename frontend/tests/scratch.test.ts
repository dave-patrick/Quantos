import { describe, expect, it } from 'vitest';
import { sendRange } from '../src/lib/scratch';

describe('scratchpad send range', () => {
  const doc = 'First idea\nstill first\n\nSecond thought\n\nThird';

  it('takes the selection when there is one', () => {
    expect(sendRange(doc, 0, 10)).toEqual({ from: 0, to: 10, text: 'First idea' });
  });

  it('otherwise takes the paragraph around the cursor, with its line break', () => {
    const at = doc.indexOf('Second') + 3;
    const r = sendRange(doc, at, at)!;
    expect(r.text).toBe('Second thought');
    expect(doc.slice(0, r.from) + doc.slice(r.to)).toBe('First idea\nstill first\n\n\nThird');
    expect(sendRange(doc, 5, 5)!.text).toBe('First idea\nstill first');
    expect(sendRange(doc, doc.length, doc.length)!.text).toBe('Third');
  });

  it('sends nothing from a blank line', () => {
    expect(sendRange(doc, doc.indexOf('\n\n') + 1, doc.indexOf('\n\n') + 1)).toBeNull();
  });
});
