import { describe, expect, it } from 'vitest';
import { DEFAULT_LOOK, PRESETS, baseOf, contrast, fontStack, lookCss, lookVars, mix, readable } from '../src/lib/look';

describe('look', () => {
  it('mixes and measures colours', () => {
    expect(mix('#000000', '#ffffff', 0.5)).toBe('#808080');
    expect(contrast('#000000', '#ffffff')).toBeCloseTo(21, 0);
    expect(contrast(readable('#777777', '#ffffff', 4.5), '#ffffff')).toBeGreaterThanOrEqual(4.5);
  });

  it('works out a readable palette from any background', () => {
    for (const p of PRESETS.filter((x) => x.look.bg)) {
      const look = { ...DEFAULT_LOOK, ...p.look };
      const v = lookVars(look, baseOf(look, 'dark'));
      expect(contrast(v['--text'], v['--bg']), p.label).toBeGreaterThanOrEqual(7);
      expect(contrast(v['--text-faint'], v['--bg']), p.label).toBeGreaterThanOrEqual(4.5);
      expect(contrast(v['--text-muted'], v['--bg-raised']), p.label).toBeGreaterThanOrEqual(4.3);
      expect(contrast(v['--accent'], v['--bg']), p.label).toBeGreaterThanOrEqual(4.5);
      expect(contrast(v['--accent'], v['--accent-wash']), p.label).toBeGreaterThanOrEqual(4.4);
    }
  });

  it('changes only what is set', () => {
    const v = lookVars({ ...DEFAULT_LOOK, accent: '#ff0066' }, 'dark');
    expect(v['--bg']).toBeUndefined();
    expect(v['--accent']).toBeDefined();
    expect(lookCss(DEFAULT_LOOK, 'dark')).toBe('');
    expect(baseOf({ ...DEFAULT_LOOK, bg: '#fdf6e3' }, 'dark')).toBe('light');
  });

  it('builds safe font stacks', () => {
    expect(fontStack('Georgia', 'editor')).toBe('"Georgia", serif');
    expect(fontStack('My Font"; }', 'mono')).toBe('"My Font", monospace');
  });
});
