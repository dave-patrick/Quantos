import { describe, expect, it } from 'vitest';
import { toSlides } from '../src/lib/slides';

describe('toSlides', () => {
  it('splits on --- lines with a blank line before them, and keeps speaker notes apart', () => {
    const s = toSlides('Deck', '# Launch\n\nWhy now\n\n---\n\n## Plan\n- one\n\nNotes:\nMention the budget\n\n---\n\nThanks');
    expect(s.map((x) => x.body)).toEqual(['# Launch\n\nWhy now', '## Plan\n- one', 'Thanks']);
    expect(s[1].notes).toBe('Mention the budget');
  });

  it('treats a --- under text as a heading underline, not a break', () => {
    expect(toSlides('Deck', 'Title\n---\nText')).toHaveLength(2); // title slide + the one slide
  });

  it('splits at headings when there are no breaks, ignoring code', () => {
    const s = toSlides('Plan', 'Intro line\n# One\nA\n```\n# not a heading\n```\n## Two\nB\n### Three stays\nC');
    expect(s.map((x) => x.body.split('\n')[0])).toEqual(['# Plan', 'Intro line', '# One', '## Two']);
  });

  it('adds a title slide only when the deck has no top heading first', () => {
    expect(toSlides('Plan', '# Plan\nHi')[0].body).toBe('# Plan\nHi');
    expect(toSlides('Plan', '')[0].body).toBe('# Plan');
  });

  it('drops task markers', () => {
    expect(toSlides('T', '# T\n- [ ] Do it <!--task:01JQEXAMP0E000000000000050-->')[0].body).toBe('# T\n- [ ] Do it');
  });
});
