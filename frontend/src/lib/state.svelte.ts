import { api, instanceId } from './api';
import { onChange } from './events';
import type { LinkedFolder, NodeSummary, Status } from './types';

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
    const narrow = () => typeof innerWidth === 'number' && innerWidth <= 700;
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
        const wide = typeof innerWidth !== 'number' || innerWidth > 700;
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
    onChange((e) => {
      if (['Created', 'Updated', 'Moved', 'Trashed', 'Restored', 'Deleted', 'Archived', 'Unarchived', 'Reindexed'].includes(e.kind)) this.scheduleTitles();
      if (e.kind === 'Reindexed') this.refreshStatus();
    });
  }

  /** Plain map for hot paths (editor decorations); rebuilt whenever titles reload. */
  titleIndex = new Map<string, NodeSummary>();

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

  scheduleTitles() {
    if (this.titleTimer) clearTimeout(this.titleTimer);
    this.titleTimer = setTimeout(() => this.loadTitles().catch(() => {}), 400);
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
  ask(title: string, options: { message?: string; value?: string; placeholder?: string; confirmLabel?: string; numeric?: boolean } = {}): Promise<string | null> {
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
