import { api, instanceId } from './api';
import { onChange } from './events';
import type { NodeSummary, Status } from './types';

export interface Toast { id: number; message: string; kind: 'info' | 'ok' | 'warn' | 'error'; action?: { label: string; run: () => void } }

export type RightTab = 'files' | 'inspector' | 'links' | 'notifications' | 'graph';

class AppState {
  status = $state<Status | null>(null);
  workspace = $state('default');
  workspaces = $state<string[]>([]);
  titles = $state<NodeSummary[]>([]);
  leftOpen = $state(true);
  rightOpen = $state(true);
  rightTab = $state<RightTab>('files');
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
        this.theme = ui.theme ?? 'dark';
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
        try {
          const wide = typeof innerWidth !== 'number' || innerWidth > 700;
          const prev = JSON.parse(localStorage.getItem('quantos.ui') ?? '{}');
          localStorage.setItem('quantos.ui', JSON.stringify({ leftOpen: wide ? this.leftOpen : prev.leftOpen ?? true, rightOpen: wide ? this.rightOpen : prev.rightOpen ?? true, rightTab: this.rightTab, theme: this.theme }));
        } catch { /* ignore */ }
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
