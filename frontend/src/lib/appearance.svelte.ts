import { api } from './api';
import { app } from './state.svelte';
import { vaultSettings } from './vaultSettings.svelte';
import { baseOf, lookCss, type Look } from './look';

/**
 * Look settings (Release 1.1): accent colour and density are per device (prefs); custom styles
 * are .css files in the vault's themes folder, switched on in Settings and shared by every device.
 */

/** Accent presets, each tuned for contrast on the dark and the light theme. */
export const ACCENTS: Record<string, { label: string; dark: string; light: string }> = {
  blue: { label: 'Blue', dark: '#78b6ff', light: '#1a66c4' },
  teal: { label: 'Teal', dark: '#5fd3c4', light: '#0d7268' },
  green: { label: 'Green', dark: '#7fd8a6', light: '#17774a' },
  violet: { label: 'Violet', dark: '#b6a2ff', light: '#6741c9' },
  amber: { label: 'Amber', dark: '#f4c46a', light: '#855a00' },
  rose: { label: 'Rose', dark: '#ff96b5', light: '#b3264f' }
};

export const DENSITY = { compact: 0.82, comfortable: 1, spacious: 1.18 } as const;
export type Density = keyof typeof DENSITY;

/** Overrides the accent tokens for both themes (blue is the built-in default, so it needs nothing). */
export function applyAccent(key: string) {
  const id = 'quantos-accent';
  document.getElementById(id)?.remove();
  const a = ACCENTS[key];
  if (!a || key === 'blue') return;
  const block = (c: string, ink: string) =>
    `--accent: ${c}; --line-focus: ${c}; --accent-strong: color-mix(in srgb, ${c} 75%, var(--text)); --accent-wash: color-mix(in srgb, ${c} 14%, var(--bg)); --accent-ink: ${ink};`;
  const style = document.createElement('style');
  style.id = id;
  style.textContent = `:root { ${block(a.dark, '#091321')} }\n:root[data-theme='light'] { ${block(a.light, '#ffffff')} }`;
  document.head.appendChild(style);
}

/** Puts a look's colours and fonts on the page (after the accent preset, so a custom accent wins). */
export function applyLook(look: Look, base: 'dark' | 'light') {
  let style = document.getElementById('quantos-look') as HTMLStyleElement | null;
  if (!style) { style = document.createElement('style'); style.id = 'quantos-look'; document.head.appendChild(style); }
  style.textContent = lookCss(look, base);
  document.documentElement.style.setProperty('zoom', look.scale && look.scale !== 1 ? String(look.scale) : '');
}

export function applyDensity(d: Density) {
  document.documentElement.style.setProperty('--d', String(DENSITY[d] ?? 1));
}

export interface ThemeFile { name: string; size: number; modified: string }

/** A look written as a vault theme file, so every device can switch it on. */
export function themeFileCss(name: string, look: Look, fallback: 'dark' | 'light') {
  const base = baseOf(look, fallback);
  return `/* ${name}: made in Quantos (Settings › Appearance). Base: ${base}. */\n${lookCss(look, base)}`;
}

class Themes {
  files = $state<ThemeFile[]>([]);
  error = $state('');

  get enabled(): string[] {
    const v = vaultSettings.data.themes;
    return Array.isArray(v) ? (v as string[]) : [];
  }

  async list() {
    try { this.files = await api.get<ThemeFile[]>('themes'); } catch { this.files = []; }
  }

  /** Puts each enabled theme's CSS on the page, after the built-in styles so it wins. Safe mode skips them. */
  async apply() {
    document.querySelectorAll('style[data-quantos-theme]').forEach((s) => s.remove());
    this.error = '';
    if (app.status?.safeMode) return;
    for (const name of this.enabled) {
      try {
        const res = await fetch(`/api/themes/${encodeURIComponent(name)}`, { credentials: 'same-origin', headers: { 'X-Quantos-Request': '1' } });
        if (!res.ok) { this.error = `Couldn't load ${name}.`; continue; }
        const style = document.createElement('style');
        style.dataset.quantosTheme = name;
        style.textContent = await res.text();
        document.head.appendChild(style);
      } catch { this.error = `Couldn't load ${name}.`; }
    }
  }

  async toggle(name: string, on: boolean) {
    const next = on ? [...new Set([...this.enabled, name])] : this.enabled.filter((n) => n !== name);
    await vaultSettings.set('themes', next.length ? next : null);
    await this.apply();
  }
}

export const themes = new Themes();

/** At start: settings first (they name the enabled themes), then the styles. */
export async function loadThemes() {
  if (!Object.keys(vaultSettings.data).length) await vaultSettings.load();
  await themes.apply();
}
