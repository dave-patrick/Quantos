import { describe, expect, it } from 'vitest';
import { DEFAULT_LOOK, PRESETS, baseOf, contrast, fontStack, lookCss, lookVars, mix, readable, resetElements } from '../src/lib/look';

describe('look', () => {
  it('groups main labels and secondary text without fading explicit colours', () => {
    for (const base of ['dark', 'light'] as const) {
      const vars = lookVars({ ...DEFAULT_LOOK, text: '#123456', secondaryText: '#987654', textMuted: '#ffffff', textFaint: '#000000' }, base);
      expect(vars['--text']).toBe('#123456');
      expect(vars['--text-soft']).toBe('#123456');
      expect(vars['--text-muted']).toBe('#987654');
      expect(vars['--text-faint']).toBe('#987654');
    }
  });

  it('preserves legacy text shades until their replacement is selected', () => {
    const vars = lookVars({ ...DEFAULT_LOOK, text: '#123456', textSoft: '#abcdef', textMuted: '#135790', textFaint: '#246802' }, 'dark');
    expect(vars['--text-soft']).toBe('#abcdef');
    expect(vars['--text-muted']).toBe('#135790');
    expect(vars['--text-faint']).toBe('#246802');
    const reset = resetElements({ ...DEFAULT_LOOK, text: '#123456', secondaryText: '#987654', textSoft: '#abcdef', textMuted: '#135790', textFaint: '#246802' });
    expect(reset.text).toBe('#123456');
    expect(reset.secondaryText).toBeNull();
    expect(reset.textSoft).toBeNull();
    expect(reset.textMuted).toBeNull();
    expect(reset.textFaint).toBeNull();
  });
  it('uses the selected primary text colour on built-in light and dark backgrounds', () => {
    for (const base of ['dark', 'light'] as const) {
      const vars = lookVars({ ...DEFAULT_LOOK, text: '#e75480' }, base);
      expect(vars['--text']).toBe('#e75480');
      expect(vars['--text-soft']).toBeDefined();
      expect(vars['--bg']).toBeUndefined();
    }
  });

  it('keeps the picked colour exactly instead of silently changing its contrast', () => {
    const vars = lookVars({ ...DEFAULT_LOOK, bg: '#2a2b2d', text: '#222222', textMuted: '#abcdef' }, 'dark');
    expect(vars['--text']).toBe('#222222');
    expect(vars['--text-muted']).toBe('#abcdef');
  });
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

  it('supports granular element overrides', () => {
    const custom = {
      ...DEFAULT_LOOK,
      bg: '#1c1d21',
      sidebarBg: '#121316',
      editorBg: '#23242a',
      headerBg: '#1a1b1f',
      statusbarBg: '#141518',
      selectBg: '#354865',
      line: '#3a3b40',
      ok: '#68d391'
    };
    const v = lookVars(custom, 'dark');
    expect(v['--bg']).toBe('#1c1d21');
    expect(v['--sidebar-bg']).toBe('#121316');
    expect(v['--editor-bg']).toBe('#23242a');
    expect(v['--header-bg']).toBe('#1a1b1f');
    expect(v['--statusbar-bg']).toBe('#141518');
    expect(v['--bg-select']).toBe('#354865');
    expect(v['--line']).toBe('#3a3b40');
    expect(v['--ok']).toBe('#68d391');

    const css = lookCss(custom, 'dark');
    expect(css).toContain('--sidebar-bg: #121316;');
    expect(css).toContain('--editor-bg: #23242a;');
  });

  it('resets elements while preserving base styling', () => {
    const custom = {
      ...DEFAULT_LOOK,
      bg: '#000000',
      accent: '#ff0066',
      sidebarBg: '#111111',
      editorBg: '#222222',
      fontEditor: 'Georgia'
    };
    const reset = resetElements(custom);
    expect(reset.bg).toBe('#000000');
    expect(reset.accent).toBe('#ff0066');
    expect(reset.fontEditor).toBe('Georgia');
    expect(reset.sidebarBg).toBeNull();
    expect(reset.editorBg).toBeNull();
  });
});

