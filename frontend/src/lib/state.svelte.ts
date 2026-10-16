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
    // Small windows start with the sidebars closed; they open as overlays.
    if (typeof innerWidth === 'number' && innerWidth <= 700) { this.leftOpen = false; this.rightOpen = false; }
    $effect.root(() => {
      $effect(() => {
        document.documentElement.dataset.theme = this.theme;
        try {
          localStorage.setItem('quantos.ui', JSON.stringify({ leftOpen: this.leftOpen, rightOpen: this.rightOpen, rightTab: this.rightTab, theme: this.theme }));
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
