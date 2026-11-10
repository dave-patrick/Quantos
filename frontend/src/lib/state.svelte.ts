import { api, ApiError, instanceId } from './api';
import { onChange, onResync } from './events';
import type { LinkedFolder, NodeSummary, Status } from './types';
import { isCompactAndroid } from './platform';

export interface Toast { id: number; message: string; kind: 'info' | 'ok' | 'warn' | 'error'; action?: { label: string; run: () => void } }

export type RightTab = 'files' | 'inspector' | 'links' | 'notifications' | 'graph' | 'scratch' | 'tasks' | 'quickadd' | 'myday';

class AppState {
  status = $state<Status | null>(null);
  workspace = $state('default');
  workspaces = $state<string[]>([]);
  linked = $state<LinkedFolder[]>([]);
  titles = $state<NodeSummary[]>([]);
  leftOpen = $state(true);
  rightOpen = $state(true);
  rightTab = $state<RightTab>('files');
  /** The right sidebar split in two: the lower panel's tab (null when not split) and the upper panel's share of the height. */
  rightTab2 = $state<RightTab | null>(null);
  rightSplit = $state(0.5);
  /** The right sidebar as a stack of panels, top to bottom. */
  rightPanels = $state<RightTab[]>(['files']);
  /** Each panel's share of the height (sums to 1). */
  rightHeights = $state<number[]>([1]);
  toasts = $state<Toast[]>([]);
  theme = $state<'dark' | 'light'>('dark');
  paletteOpen = $state(false);
  paletteMode = $state<'files' | 'commands'>('files');
  connected = $state(false);
  dialog = $state<{ title: string; message: string; actions: { label: string; kind?: string; run: () => void }[] } | null>(null);
  /** A question with a text answer, or a choice of note (see ask and pickNote). */
  asking = $state<AskSpec | null>(null);

  private titleTimer: ReturnType<typeof setTimeout> | null = null;
  /** Ids changed since the last title refresh; null means reload the whole list. */
  private changedTitles: Set<string> | null = new Set();
  private toastId = 0;

  constructor() {
    try {
      const saved = localStorage.getItem('quantos.ui');
      if (saved) {
        const ui = JSON.parse(saved);
        this.leftOpen = ui.leftOpen ?? true;
        this.rightOpen = ui.rightOpen ?? true;
        this.rightTab = ui.rightTab ?? 'files';
        this.rightTab2 = ui.rightTab2 ?? null;
        this.rightSplit = typeof ui.rightSplit === 'number' ? ui.rightSplit : 0.5;
        this.theme = ui.theme ?? 'dark';
        // The new stack layout: migrate the old one/two-slot layout into a list.
        if (Array.isArray(ui.rightPanels) && ui.rightPanels.length) {
          this.rightPanels = ui.rightPanels.filter((t: string) => t);
          if (!this.rightPanels.length) this.rightPanels = ['files'];
          this.rightHeights = Array.isArray(ui.rightHeights) && ui.rightHeights.length === this.rightPanels.length
            ? ui.rightHeights.map((n: number) => (typeof n === 'number' && n > 0 ? n : 1))
            : this.rightPanels.map(() => 1);
        } else {
          this.rightPanels = this.rightTab2 ? [this.rightTab, this.rightTab2] : [this.rightTab];
          this.rightHeights = this.rightPanels.map(() => 1);
        }
      }
    } catch { /* storage unavailable */ }
    // Small windows start with the sidebars closed (they open as overlays). That isn't saved, and
    // the saved layout comes back when the window is wide again.
    const saved = { left: this.leftOpen, right: this.rightOpen };
    const narrow = () => typeof innerWidth === 'number' && (innerWidth <= 700 || isCompactAndroid());
    if (narrow()) { this.leftOpen = false; this.rightOpen = false; }
    if (typeof window !== 'undefined') {
      let wasNarrow = narrow();
      addEventListener('resize', () => {
        const now = narrow();
        if (wasNarrow && !now) { this.leftOpen = saved.left; this.rightOpen = saved.right; }
        if (!wasNarrow && now) { saved.left = this.leftOpen; saved.right = this.rightOpen; this.leftOpen = false; this.rightOpen = false; }
        wasNarrow = now;
      });
    }
    $effect.root(() => {
      $effect(() => {
        document.documentElement.dataset.theme = this.theme;
      });
      // Persist UI layout to localStorage, debounced so rapid changes (theme toggle, sidebar open/close)
      // don't hammer the disk on every tick.
      let uiTimer: ReturnType<typeof setTimeout> | null = null;
      $effect(() => {
        const wide = typeof innerWidth !== 'number' || innerWidth > 700 && !isCompactAndroid();
        // Capture reactive state before scheduling: reads inside setTimeout aren't tracked by
        // Svelte effects, which otherwise leaves panel changes unsaved after the first render.
        const leftOpen = this.leftOpen;
        const rightOpen = this.rightOpen;
        const rightTab = this.rightTab;
        const rightTab2 = this.rightTab2;
        const rightSplit = this.rightSplit;
        const rightPanels = [...this.rightPanels];
        const rightHeights = [...this.rightHeights];
        const theme = this.theme;
        if (uiTimer) clearTimeout(uiTimer);
        uiTimer = setTimeout(() => {
          try {
            const prev = JSON.parse(localStorage.getItem('quantos.ui') ?? '{}');
            localStorage.setItem('quantos.ui', JSON.stringify({ leftOpen: wide ? leftOpen : prev.leftOpen ?? true, rightOpen: wide ? rightOpen : prev.rightOpen ?? true, rightTab, rightTab2, rightSplit, rightPanels, rightHeights, theme }));
          } catch { /* ignore */ }
        }, 200);
      });
    });
    onResync(() => this.scheduleTitles(null));
    onChange((e) => {
      if (e.kind === 'Reindexed') { this.scheduleTitles(null); this.refreshStatus(); return; }
      if (!e.id) return;
      // The event carries the note's new list entry: apply it directly, no request needed.
      if (e.summary && ['Created', 'Updated', 'Moved', 'Restored', 'Archived', 'Unarchived'].includes(e.kind)) this.applyTitles(new Map([[e.id, e.summary]]));
      else if (e.kind === 'Trashed' || e.kind === 'Deleted') this.applyTitles(new Map([[e.id, null]]));
      else if (['Created', 'Updated', 'Moved', 'Restored', 'Archived', 'Unarchived'].includes(e.kind)) this.scheduleTitles(e.id);
    });
  }

  /** Fast raw reactive map for hot paths; rebuilt whenever titles reload. */
  titleIndex = $state.raw(new Map<string, NodeSummary>());

  async refreshStatus() {
    this.status = await api.get<Status>('status');
    this.workspaces = this.status.workspaces ?? [];
    this.linked = this.status.linked ?? [];
    return this.status;
  }

  async loadTitles() {
    const titles = await api.get<NodeSummary[]>('nodes?archived=true');
    this.titleIndex = new Map(titles.map((t) => [t.id, t]));
    this.titles = titles;
  }

  /**
   * Refreshes the title list a moment after changes settle. A change to a few known notes fetches
   * only those notes; anything else (a reindex, a reconnect, a burst of many changes, or a caller
   * that doesn't say which note) reloads the whole list. Reloading every note's details after each
   * save was the slow path in large vaults.
   */
  scheduleTitles(id?: string | null) {
    if (id && this.changedTitles) this.changedTitles.add(id);
    else this.changedTitles = null;
    if (this.titleTimer) clearTimeout(this.titleTimer);
    this.titleTimer = setTimeout(() => this.flushTitles().catch(() => {}), 400);
  }

  private async flushTitles() {
    const ids = this.changedTitles;
    this.changedTitles = new Set();
    if (!ids || ids.size > 25 || this.titles.length === 0) return this.loadTitles();
    if (ids.size === 0) return;
    const fresh = new Map<string, NodeSummary | null>();
    try {
      await Promise.all([...ids].map(async (id) => {
        try { fresh.set(id, await api.get<NodeSummary>(`nodes/${encodeURIComponent(id)}/summary`)); }
        catch (e) { if (e instanceof ApiError && e.status === 404) fresh.set(id, null); else throw e; }
      }));
    } catch {
      return this.loadTitles(); // an older backend without the endpoint, or a network blip
    }
    this.applyTitles(fresh);
  }

  /** Puts changed notes at the front (the server's newest-first order) and drops removed ones (null). */
  private applyTitles(fresh: Map<string, NodeSummary | null>) {
    if (fresh.size === 0) return;
    const changed = [...fresh.values()].filter((n): n is NodeSummary => !!n);
    const titles = [...changed, ...this.titles.filter((t) => !fresh.has(t.id))];
    const index = new Map(this.titleIndex);
    for (const [id, n] of fresh) { if (n) index.set(id, n); else index.delete(id); }
    this.titleIndex = index;
    this.titles = titles;
  }

  isOwnEvent(origin: string | null) {
    return !!origin && origin.endsWith('#' + instanceId);
  }

  toast(message: string, kind: Toast['kind'] = 'info', action?: Toast['action']) {
    const id = ++this.toastId;
    this.toasts = [...this.toasts, { id, message, kind, action }];
    setTimeout(() => this.dismiss(id), kind === 'error' ? 9000 : 4500);
  }

  dismiss(id: number) {
    this.toasts = this.toasts.filter((t) => t.id !== id);
  }

  error(err: unknown) {
    const message = err instanceof Error ? err.message : String(err);
    this.toast(message, 'error');
  }

  /** Asks for a line of text; resolves to the trimmed answer, or null when cancelled. */
  ask(title: string, options: { message?: string; value?: string; placeholder?: string; confirmLabel?: string; numeric?: boolean; folder?: boolean } = {}): Promise<string | null> {
    return new Promise((resolve) => {
      this.asking = { title, ...options, done: (answer) => { this.asking = null; resolve(answer === null ? null : answer.trim()); } };
    });
  }

  /** Asks the user to choose a note by typing part of its title. */
  pickNote(title: string, options: { message?: string; confirmLabel?: string; exclude?: string } = {}): Promise<NodeSummary | null> {
    return new Promise((resolve) => {
      this.asking = {
        title, ...options, notes: true, placeholder: 'Type part of a title',
        done: (answer) => { this.asking = null; resolve(answer ? this.titleIndex.get(answer) ?? null : null); }
      };
    });
  }

  confirm(title: string, message: string, confirmLabel = 'OK', kind = 'primary'): Promise<boolean> {
    return new Promise((resolve) => {
      this.dialog = {
        title, message,
        actions: [
          { label: 'Cancel', run: () => { this.dialog = null; resolve(false); } },
          { label: confirmLabel, kind, run: () => { this.dialog = null; resolve(true); } }
        ]
      };
    });
  }
}

export const app = new AppState();

export interface AskSpec {
  folder?: boolean;
  title: string;
  message?: string;
  value?: string;
  placeholder?: string;
  confirmLabel?: string;
  numeric?: boolean;
  /** Choose a note: the answer is its id. */
  notes?: boolean;
  exclude?: string;
  done: (answer: string | null) => void;
}
