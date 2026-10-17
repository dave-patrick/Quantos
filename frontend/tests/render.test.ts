// @vitest-environment happy-dom
import { describe, expect, it } from 'vitest';
import { footnotes, readable, renderMarkdown } from '../src/lib/render';

describe('footnotes', () => {
  it('numbers references in order and lists the definitions at the end', () => {
    const html = renderMarkdown('Second[^b] then first[^a].\n\n[^a]: Alpha note\n[^b]: Beta note\n  continued\n');
    expect(html).toContain('<sup class="footnote-ref"><a href="#fn-b" id="fnref-b">1</a></sup>');
    expect(html).toContain('<a href="#fn-a" id="fnref-a">2</a>');
    expect(html.indexOf('Beta note')).toBeLessThan(html.indexOf('Alpha note'));
    expect(html).toContain('continued');
    expect(html).not.toContain('[^a]');
  });
  it('leaves code and unknown references alone', () => {
    const { body, notes } = footnotes('Use `[^x]` here [^missing] and [^x].\n```\n[^x]\n```\n[^x]: Defined\n');
    expect(body).toContain('`[^x]`');
    expect(body).toContain('[^missing]');
    expect(body).toContain('```\n[^x]\n```');
    expect(notes).toEqual([{ id: 'x', text: 'Defined' }]);
  });
  it('changes nothing when there are no definitions', () => {
    expect(footnotes('Plain [^1] text').body).toBe('Plain [^1] text');
  });
});

describe('readable copy', () => {
  it('drops task markers and uses link titles', () => {
    expect(readable('- [ ] Call <!--task:01JABCDEF0123456789XYZABCD--> [[01JABCDEF0123456789XYZABCD|Sam]]'))
      .toBe('- [ ] Call  [[Sam]]');
  });
});