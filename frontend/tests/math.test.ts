// @vitest-environment happy-dom
import { describe, expect, it } from 'vitest';
import { findMath, renderMath } from '../src/lib/diagrams';
import { renderMarkdown } from '../src/lib/render';

describe('findMath', () => {
  it('finds inline and display math', () => {
    const m = findMath('Energy $E = mc^2$ and\n$$\n\\int_0^1 x\\,dx\n$$\n');
    expect(m.map((x) => [x.tex, x.display])).toEqual([['E = mc^2', false], ['\\int_0^1 x\\,dx', true]]);
  });
  it('leaves prices, code and escaped dollars alone', () => {
    expect(findMath('It cost $5 and then $10 more.')).toEqual([]);
    expect(findMath('Run `echo $a$` here\n```\n$x$\n```\n')).toEqual([]);
    expect(findMath('Not \\$x$ math')).toEqual([]);
    expect(findMath('$ spaced $')).toEqual([]);
  });
});

describe('math in rendered Markdown', () => {
  it('keeps TeX away from Markdown and leaves a placeholder', () => {
    const html = renderMarkdown('See $a_1 * b_2 * c$ now.');
    expect(html).toContain('data-q-tex="a_1 * b_2 * c"');
    expect(html).not.toContain('<em>');
  });
  it('renders TeX with KaTeX', async () => {
    expect(await renderMath('x^2', false)).toContain('katex');
  });
});
