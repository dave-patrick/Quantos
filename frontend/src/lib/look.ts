/**
 * Theming: any background, text and accent colour, and fonts for the interface, headings, editor and
 * code. From a background colour the rest of the palette (panels, hover, lines, softer text) is worked
 * out, light or dark, keeping text readable. Per device; a look can also be saved as a vault theme file.
 */
export interface Look {
  bg: string | null;          // null: the built-in dark or light palette
  text: string | null;        // null: chosen for contrast with the background
  accent: string | null;      // null: the accent preset (prefs.accent)
  fontUi: string | null;
  fontHeading: string | null;
  fontEditor: string | null;  // null: the Writing typeface setting
  fontMono: string | null;
  scale: number;              // interface text size, 0.9–1.25
}

export const DEFAULT_LOOK: Look = { bg: null, text: null, accent: null, fontUi: null, fontHeading: null, fontEditor: null, fontMono: null, scale: 1 };

export interface Preset { key: string; label: string; look: Partial<Look>; base?: 'dark' | 'light' }

/** Starting points. Quantos Dark and Light are the built-in palettes; the rest are generated. */
export const PRESETS: Preset[] = [
  { key: 'quantos-dark', label: 'Quantos Dark', look: {}, base: 'dark' },
  { key: 'quantos-light', label: 'Quantos Light', look: {}, base: 'light' },
  { key: 'midnight', label: 'Midnight', look: { bg: '#0b0b12', accent: '#b6a2ff' } },
  { key: 'graphite', label: 'Graphite', look: { bg: '#1c1d21', text: '#e8e6e3', accent: '#f4c46a' } },
  { key: 'nord', label: 'Nord', look: { bg: '#2e3440', text: '#eceff4', accent: '#88c0d0' } },
  { key: 'forest', label: 'Forest', look: { bg: '#0f1a14', text: '#e3efe7', accent: '#7fd8a6' } },
  { key: 'rose-pine', label: 'Rosé', look: { bg: '#191724', text: '#e0def4', accent: '#ebbcba' } },
  { key: 'solarized-dark', label: 'Solarized Dark', look: { bg: '#002b36', text: '#eee8d5', accent: '#2aa1e8' } },
  { key: 'solarized-light', label: 'Solarized Light', look: { bg: '#fdf6e3', text: '#073642', accent: '#1e6fb5' } },
  { key: 'paper', label: 'Paper', look: { bg: '#f7f3ea', text: '#2b2620', accent: '#a64a17', fontHeading: 'Georgia', fontEditor: 'Georgia' } },
  { key: 'sky', label: 'Sky', look: { bg: '#eef5ff', text: '#10233d', accent: '#1d5fd6' } },
  { key: 'contrast', label: 'High contrast', look: { bg: '#000000', text: '#ffffff', accent: '#ffd400' } }
];

/** Fonts that ship with Quantos, then ones found on most Windows PCs. Any installed font can be typed in. */
export const FONTS: { name: string; kind: 'sans' | 'serif' | 'mono' | 'display' }[] = [
  { name: 'Source Sans 3', kind: 'sans' }, { name: 'Chakra Petch', kind: 'display' }, { name: 'IBM Plex Mono', kind: 'mono' },
  { name: 'Segoe UI', kind: 'sans' }, { name: 'Calibri', kind: 'sans' }, { name: 'Arial', kind: 'sans' }, { name: 'Verdana', kind: 'sans' }, { name: 'Tahoma', kind: 'sans' },
  { name: 'Bahnschrift', kind: 'sans' }, { name: 'Candara', kind: 'sans' }, { name: 'Corbel', kind: 'sans' }, { name: 'Trebuchet MS', kind: 'sans' },
  { name: 'Georgia', kind: 'serif' }, { name: 'Cambria', kind: 'serif' }, { name: 'Constantia', kind: 'serif' }, { name: 'Palatino Linotype', kind: 'serif' },
  { name: 'Times New Roman', kind: 'serif' }, { name: 'Sitka Text', kind: 'serif' },
  { name: 'Cascadia Code', kind: 'mono' }, { name: 'Cascadia Mono', kind: 'mono' }, { name: 'Consolas', kind: 'mono' }, { name: 'Courier New', kind: 'mono' }
];

/** A CSS font-family value: the chosen font, then a fallback of the right kind. */
export function fontStack(name: string, role: 'ui' | 'heading' | 'editor' | 'mono'): string {
  const known = FONTS.find((f) => f.name.toLowerCase() === name.trim().toLowerCase());
  const kind = known?.kind ?? (role === 'mono' ? 'mono' : 'sans');
  const generic = kind === 'mono' ? 'monospace' : kind === 'serif' ? 'serif' : 'sans-serif';
  const clean = name.replace(/["';{}<>\\]/g, '').trim();
  return `"${clean}", ${generic}`;
}

// ---- colour maths ----------------------------------------------------------------------------

export function parseHex(hex: string): [number, number, number] | null {
  const m = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(hex.trim());
  if (!m) return null;
  const h = m[1].length === 3 ? m[1].split('').map((c) => c + c).join('') : m[1];
  return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16)) as [number, number, number];
}

export const toHex = (rgb: number[]) => '#' + rgb.map((v) => Math.round(Math.max(0, Math.min(255, v))).toString(16).padStart(2, '0')).join('');

export function mix(a: string, b: string, t: number): string {
  const x = parseHex(a)!, y = parseHex(b)!;
  return toHex(x.map((v, i) => v + (y[i] - v) * t));
}

export function luminance(hex: string): number {
  const c = parseHex(hex)!.map((v) => { const s = v / 255; return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; });
  return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
}

export function contrast(a: string, b: string): number {
  const [l1, l2] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (l1 + 0.05) / (l2 + 0.05);
}

export const isLight = (hex: string) => luminance(hex) > 0.4;

/** Moves a colour toward black or white until it reaches the contrast asked for against the background. */
export function readable(color: string, bg: string, min: number): string {
  if (contrast(color, bg) >= min) return color;
  const target = isLight(bg) ? '#000000' : '#ffffff';
  for (let t = 0.05; t <= 1; t += 0.05) { const c = mix(color, target, t); if (contrast(c, bg) >= min) return c; }
  return target;
}

/** CSS variables for a look, or only the parts it changes (accent alone, say). */
export function lookVars(look: Look, base: 'dark' | 'light'): Record<string, string> {
  const v: Record<string, string> = {};
  const bg = look.bg && parseHex(look.bg) ? toHex(parseHex(look.bg)!) : null;
  const surface = bg ?? (base === 'light' ? '#f5f7fb' : '#2a2b2d');
  if (bg) {
    const light = isLight(bg);
    const text = look.text && parseHex(look.text) ? readable(look.text, bg, 7) : light ? '#141b24' : '#edf2fa';
    v['--bg'] = bg;
    v['--bg-raised'] = light ? mix(bg, '#ffffff', 0.55) : mix(bg, text, 0.035);
    v['--bg-panel'] = mix(bg, text, light ? 0.04 : 0.055);
    v['--bg-hover'] = mix(bg, text, light ? 0.07 : 0.09);
    v['--bg-active'] = mix(bg, text, light ? 0.11 : 0.13);
    v['--bg-input'] = light ? mix(bg, '#ffffff', 0.7) : mix(bg, '#000000', 0.12);
    v['--line'] = mix(bg, text, light ? 0.14 : 0.16);
    v['--line-strong'] = mix(bg, text, light ? 0.22 : 0.22);
    v['--text'] = text;
    v['--text-soft'] = readable(mix(text, bg, 0.15), bg, 7);
    v['--text-muted'] = readable(mix(text, bg, 0.3), bg, 4.8);
    v['--text-faint'] = readable(mix(text, bg, 0.42), bg, 4.6);
    v['--shadow'] = light ? '0 12px 32px rgb(0 0 0 / 0.14)' : '0 12px 36px rgb(0 0 0 / 0.45)';
  }
  if (look.accent && parseHex(look.accent)) {
    const raw = toHex(parseHex(look.accent)!);
    // Readable on the background and on its own tint (selected buttons, chips), so check against the tint.
    const tint = mix(surface, raw, isLight(surface) ? 0.12 : 0.14);
    const accent = readable(readable(raw, surface, 4.6), tint, 4.6);
    const text = v['--text'] ?? (base === 'light' ? '#0c131d' : '#edf2fa');
    v['--accent'] = accent;
    v['--line-focus'] = accent;
    v['--accent-strong'] = readable(mix(accent, text, 0.25), surface, 5);
    v['--accent-wash'] = mix(surface, accent, isLight(surface) ? 0.12 : 0.14);
    v['--accent-ink'] = contrast(accent, '#000000') >= contrast(accent, '#ffffff') ? '#0b0f14' : '#ffffff';
    v['--bg-select'] = mix(surface, accent, isLight(surface) ? 0.28 : 0.32);
  }
  if (look.fontUi) v['--font-body'] = fontStack(look.fontUi, 'ui');
  if (look.fontHeading) v['--font-display'] = fontStack(look.fontHeading, 'heading');
  if (look.fontEditor) v['--font-editor'] = fontStack(look.fontEditor, 'editor');
  if (look.fontMono) v['--font-mono'] = fontStack(look.fontMono, 'mono');
  return v;
}

/** The look as a stylesheet; it outranks the built-in light and dark blocks. */
export function lookCss(look: Look, base: 'dark' | 'light'): string {
  const vars = lookVars(look, base);
  const body = Object.entries(vars).map(([k, val]) => `  ${k}: ${val};`).join('\n');
  return body ? `:root:root[data-theme] {\n${body}\n}\n` : '';
}

/** The base a look runs on: its background decides, otherwise the light/dark switch. */
export const baseOf = (look: Look, fallback: 'dark' | 'light'): 'dark' | 'light' => (look.bg && parseHex(look.bg) ? (isLight(look.bg) ? 'light' : 'dark') : fallback);
