import { applyAccent, applyDensity, applyLook, type Density } from './appearance.svelte';
import { DEFAULT_LOOK, baseOf, fontStack, type Look } from './look';
import { app } from './state.svelte';
import { DEFAULT_SCRATCH_TARGETS, type ScratchTarget } from './scratch';
import { defaultPhoneNavigation, normalizePhoneNavigation } from './phoneNavigation';

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
  /** Reading mode (Release 1.1): every note opens read-only on this device until it's turned off. */
  reading = $state(false);
  /** Collapse Markdown headings in the editor. */
  collapsibleHeadings = $state(true);
  /** Underline unknown words as you type. */
  spellCheck = $state(false);
  /** Left sidebar: items and sections hidden, and the order of the items (keys; unknown keys are ignored). */
  sidebarHidden = $state<string[]>([]);
  sidebarOrder = $state<string[]>([]);
  sidebarSections = $state<string[]>([]);
  /** Phone bottom bar, in order; More is always available. Local to this device. */
  phoneNavigation = $state<string[]>([...defaultPhoneNavigation]);
  /** Daily stream: leave out days with nothing written and no tasks. */
  dailyHideEmpty = $state(false);
  /** Notes browser: folders tree instead of a flat list. */
  notesTree = $state(false);
  /** Notes browser: show the note preview pane. */
  notesPreview = $state(true);
  /** Scratchpad "send to" shortcuts. */
  scratchTargets = $state<ScratchTarget[]>(DEFAULT_SCRATCH_TARGETS.map((t) => ({ ...t })));
  /** Theme: background, text, accent and fonts (see lib/look.ts). */
  look = $state<Look>({ ...DEFAULT_LOOK });

  constructor() {
    try {
      const saved = JSON.parse(localStorage.getItem('quantos.prefs') ?? '{}');
      Object.assign(this, saved);
      this.look = { ...DEFAULT_LOOK, ...(saved.look ?? {}) };
      this.phoneNavigation = normalizePhoneNavigation(saved.phoneNavigation);
    } catch { /* ignore */ }
    $effect.root(() => {
      $effect(() => {
        const data = { typewriter: this.typewriter, editorSize: this.editorSize, editorWidth: this.editorWidth, showWordCount: this.showWordCount, writingTarget: this.writingTarget, fontFamily: this.fontFamily, lineHeight: this.lineHeight, density: this.density, accent: this.accent, reading: this.reading, collapsibleHeadings: this.collapsibleHeadings, spellCheck: this.spellCheck, sidebarHidden: this.sidebarHidden, sidebarOrder: this.sidebarOrder, sidebarSections: this.sidebarSections, phoneNavigation: this.phoneNavigation, dailyHideEmpty: this.dailyHideEmpty, notesTree: this.notesTree, notesPreview: this.notesPreview, scratchTargets: this.scratchTargets, look: this.look };
        applyDensity(this.density);
        applyAccent(this.accent);
        // A custom background decides light or dark, so light-only details (diagrams, the accent preset) follow it.
        const base = baseOf(this.look, app.theme);
        if (this.look.bg && app.theme !== base) app.theme = base;
        applyLook(this.look, base);
        const fonts = { sans: "'Source Sans 3', 'Segoe UI', sans-serif", serif: "Georgia, Cambria, 'Times New Roman', serif", mono: "'IBM Plex Mono', Consolas, monospace", system: "system-ui, 'Segoe UI', sans-serif" };
        document.documentElement.style.setProperty('--font-editor', this.look.fontEditor ? fontStack(this.look.fontEditor, 'editor') : fonts[this.fontFamily]);
        document.documentElement.style.setProperty('--editor-line', String(this.lineHeight));
        document.documentElement.style.setProperty('--editor-size', this.editorSize + 'px');
        document.documentElement.style.setProperty('--editor-width', this.editorWidth + 'px');
        try { localStorage.setItem('quantos.prefs', JSON.stringify(data)); } catch { /* ignore */ }
      });
    });
  }
}

export const prefs = new Prefs();
