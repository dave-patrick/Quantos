import { applyAccent, applyDensity, type Density } from './appearance.svelte';

/** Per-device writing preferences (kept in this browser; vault settings live in settings.json). */
class Prefs {
  typewriter = $state(false);
  editorSize = $state(17);
  editorWidth = $state(760);
  showWordCount = $state(true);
  writingTarget = $state<Record<string, number>>({});
  fontFamily = $state<'sans' | 'serif' | 'mono' | 'system'>('sans');
  lineHeight = $state(1.65);
  density = $state<Density>('comfortable');
  accent = $state('blue');

  constructor() {
    try {
      const saved = JSON.parse(localStorage.getItem('quantos.prefs') ?? '{}');
      Object.assign(this, saved);
    } catch { /* ignore */ }
    $effect.root(() => {
      $effect(() => {
        const data = { typewriter: this.typewriter, editorSize: this.editorSize, editorWidth: this.editorWidth, showWordCount: this.showWordCount, writingTarget: this.writingTarget, fontFamily: this.fontFamily, lineHeight: this.lineHeight, density: this.density, accent: this.accent };
        applyDensity(this.density);
        applyAccent(this.accent);
        const fonts = { sans: "'Source Sans 3', 'Segoe UI', sans-serif", serif: "Georgia, Cambria, 'Times New Roman', serif", mono: "'IBM Plex Mono', Consolas, monospace", system: "system-ui, 'Segoe UI', sans-serif" };
        document.documentElement.style.setProperty('--font-editor', fonts[this.fontFamily]);
        document.documentElement.style.setProperty('--editor-line', String(this.lineHeight));
        document.documentElement.style.setProperty('--editor-size', this.editorSize + 'px');
        document.documentElement.style.setProperty('--editor-width', this.editorWidth + 'px');
        try { localStorage.setItem('quantos.prefs', JSON.stringify(data)); } catch { /* ignore */ }
      });
    });
  }
}

export const prefs = new Prefs();
