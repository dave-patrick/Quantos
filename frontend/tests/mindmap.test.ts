import { describe, expect, it } from 'vitest';
import { annotate, editAnnotation, outline, plain, removeAnnotation } from '../src/lib/mindmap';

const body = [
  'Intro text',
  '# Goals',
  '- Grow **revenue** [^a]',
  '  - New market',
  '- Hire',
  '## Risks',
  '- [ ] Budget <!--task:01JQEXAMP0E000000000000050-->',
  '```',
  '# not a heading',
  '```',
  '',
  '[^a]: Ask finance',
  '  for numbers'
].join('\n');

describe('mind map', () => {
  it('builds branches from headings and nested items, with footnotes as annotations', () => {
    const root = outline('Plan', body);
    expect(root.children.map((b) => b.text)).toEqual(['Goals']);
    const goals = root.children[0];
    expect(goals.children.map((b) => b.text)).toEqual(['Grow revenue', 'Hire', 'Risks']);
    expect(goals.children[0].children[0].text).toBe('New market');
    expect(goals.children[0].notes).toEqual([{ id: 'a', text: 'Ask finance\nfor numbers' }]);
    const risks = goals.children[2];
    expect(risks.children[0]).toMatchObject({ text: 'Budget', done: false, line: 6 });
  });

  it('adds, edits and removes annotations as footnotes', () => {
    const added = annotate(body, 6, 'Check with Sam');
    expect(added.split('\n')[6]).toBe('- [ ] Budget [^m1] <!--task:01JQEXAMP0E000000000000050-->');
    expect(added.trimEnd().endsWith('[^m1]: Check with Sam')).toBe(true);
    expect(outline('Plan', added).children[0].children[2].children[0].notes).toEqual([{ id: 'm1', text: 'Check with Sam' }]);

    const edited = editAnnotation(added, 'a', 'Ask Dana');
    expect(edited).toContain('[^a]: Ask Dana');
    expect(edited).not.toContain('for numbers');

    const removed = removeAnnotation(edited, 'a');
    expect(removed).not.toContain('[^a]');
    expect(removed.split('\n')[2]).toBe('- Grow **revenue**');
  });

  it('reads links and marks as plain words', () => {
    expect(plain('See [[01JQEXAMP0E000000000000030|Apollo]] and [site](https://x.y) `code`')).toBe('See Apollo and site code');
  });
});
