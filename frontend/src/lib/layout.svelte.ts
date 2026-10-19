import { api } from './api';
import { app } from './state.svelte';

export type TabKind =
  | 'node' | 'daily' | 'search' | 'trash' | 'history' | 'diagnostics' | 'settings' | 'tasks' | 'myday'
  | 'timeline' | 'review' | 'query' | 'view' | 'dictionary' | 'attachments' | 'templates' | 'supertags' | 'graph' | 'notifications' | 'pdf' | 'ask' | 'replace' | 'focus' | 'habits';

export interface TabSpec { kind: TabKind; nodeId?: string; arg?: string; title?: string }

export interface Tab extends TabSpec {
  key: string;
  title: string;
  pinned: boolean;
  scroll: number;
  lastActive: number;
}

export interface Pane { id: string; tabs: Tab[]; active: string | null; back: TabSpec[]; forward: TabSpec[] }

export type Where = 'current' | 'new' | 'other';

/** Ten tabs per pane (BUILD-PLAN decision log). */
export const MAX_TABS = 10;

const titles: Record<TabKind, string> = {
  node: 'Note', daily: 'Daily', search: 'Search', trash: 'Trash', history: 'History', diagnostics: 'Vault diagnostics',
  settings: 'Settings', tasks: 'Tasks', myday: 'My Day', timeline: 'Timeline', review: 'Stale tasks', query: 'Query', view: 'View',
  dictionary: 'Metadata dictionary', attachments: 'Attachment auditor', templates: 'Templates', supertags: 'Supertags', graph: 'Graph',
  notifications: 'Notifications', pdf: 'PDF', ask: 'Ask your notes', replace: 'Find and replace', focus: 'Focus', habits: 'Habits'
};

export function tabKey(spec: TabSpec) {
  return [spec.kind, spec.nodeId ?? '', spec.arg ?? ''].join(':');
}

const newPane = (): Pane => ({ id: crypto.randomUUID(), tabs: [], active: null, back: [], forward: [] });

/** Before closing a tab its unsaved content is saved (BUILD-PLAN section 7). Registered by the document store. */
let flushTab: (tab: Tab) => Promise<void> = async () => {};
export function setTabFlusher(fn: (tab: Tab) => Promise<void>) { flushTab = fn; }

class Layout {
  panes = $state<Pane[]>([newPane()]);
  split = $state<'none' | 'vertical' | 'horizontal'>('none');
  activePane = $state(0);
  restoring = false;
  private saveTimer: ReturnType<typeof setTimeout> | null = null;

  get pane() { return this.panes[this.activePane] ?? this.panes[0]; }
  get activeTab(): Tab | null {
    const pane = this.pane;
    return pane?.tabs.find((t) => t.key === pane.active) ?? null;
  }

  async open(spec: TabSpec, where: Where = 'current') {
    let paneIndex = this.activePane;
    if (where === 'other') {
      if (this.panes.length === 1) this.splitPane('vertical', false);
      paneIndex = this.activePane === 0 ? 1 : 0;
    }
    const pane = this.panes[paneIndex];
    const key = tabKey(spec);
    const existing = pane.tabs.find((t) => t.key === key);
    if (existing) {
      if (spec.title) existing.title = spec.title;
      this.activate(paneIndex, key);
      return existing;
    }
    const tab: Tab = { ...spec, key, title: spec.title ?? titles[spec.kind], pinned: false, scroll: 0, lastActive: Date.now() };
    const current = pane.tabs.find((t) => t.key === pane.active);

    if (where === 'current' && current && !current.pinned) {
      // Navigate within the current tab, keeping back/forward history.
      await flushTab(current);
      pane.back = [...pane.back.slice(-49), specOf(current)];
      pane.forward = [];
      const index = pane.tabs.indexOf(current);
      pane.tabs[index] = tab;
    } else {
      if (pane.tabs.length >= MAX_TABS && !(await this.makeRoom(pane))) return null;
      const index = current ? pane.tabs.indexOf(current) + 1 : pane.tabs.length;
      pane.tabs.splice(index, 0, tab);
    }
    this.activate(paneIndex, key);
    return tab;
  }

  /** At the tab limit, close the least recently used unpinned tab; if every tab is pinned, ask. */
  private async makeRoom(pane: Pane): Promise<boolean> {
    const candidates = pane.tabs.filter((t) => !t.pinned && t.key !== pane.active).sort((a, b) => a.lastActive - b.lastActive);
    const victim = candidates[0] ?? pane.tabs.find((t) => !t.pinned);
    if (!victim) {
      app.dialog = {
        title: 'All tabs are pinned',
        message: `This pane already has ${MAX_TABS} pinned tabs. Unpin one to open another.`,
        actions: [{ label: 'OK', kind: 'primary', run: () => (app.dialog = null) }]
      };
      return false;
    }
    await flushTab(victim);
    pane.tabs = pane.tabs.filter((t) => t !== victim);
    return true;
  }

  activate(paneIndex: number, key: string) {
    const pane = this.panes[paneIndex];
    if (!pane) return;
    pane.active = key;
    const tab = pane.tabs.find((t) => t.key === key);
    if (tab) tab.lastActive = Date.now();
    this.activePane = paneIndex;
    if (tab?.nodeId && app.rightTab === 'files' && false) app.rightTab = 'inspector';
    this.persist();
  }

  async close(paneIndex: number, key: string) {
    const pane = this.panes[paneIndex];
    const tab = pane?.tabs.find((t) => t.key === key);
    if (!pane || !tab) return;
    await flushTab(tab);
    const index = pane.tabs.indexOf(tab);
    pane.tabs = pane.tabs.filter((t) => t !== tab);
    if (pane.active === key) {
      const next = pane.tabs[Math.min(index, pane.tabs.length - 1)];
      pane.active = next?.key ?? null;
    }
    if (pane.tabs.length === 0 && this.panes.length > 1) this.closePane(paneIndex);
    this.persist();
  }

  async closeOthers(paneIndex: number, key: string) {
    const pane = this.panes[paneIndex];
    for (const t of [...pane.tabs]) if (t.key !== key && !t.pinned) await this.close(paneIndex, t.key);
  }

  togglePin(paneIndex: number, key: string) {
    const tab = this.panes[paneIndex]?.tabs.find((t) => t.key === key);
    if (!tab) return;
    tab.pinned = !tab.pinned;
    // Pinned tabs sit at the front.
    const pane = this.panes[paneIndex];
    pane.tabs = [...pane.tabs.filter((t) => t.pinned), ...pane.tabs.filter((t) => !t.pinned)];
    this.persist();
  }

  async back(paneIndex = this.activePane) { await this.travel(paneIndex, 'back'); }
  async forward(paneIndex = this.activePane) { await this.travel(paneIndex, 'forward'); }

  private async travel(paneIndex: number, direction: 'back' | 'forward') {
    const pane = this.panes[paneIndex];
    const stack = direction === 'back' ? pane.back : pane.forward;
    const target = stack.at(-1);
    if (!target) return;
    const current = pane.tabs.find((t) => t.key === pane.active);
    if (direction === 'back') pane.back = pane.back.slice(0, -1); else pane.forward = pane.forward.slice(0, -1);
    if (current) {
      await flushTab(current);
      if (direction === 'back') pane.forward = [...pane.forward, specOf(current)]; else pane.back = [...pane.back, specOf(current)];
      const key = tabKey(target);
      const other = pane.tabs.find((t) => t.key === key);
      if (other) { this.activate(paneIndex, key); return; }
      const tab: Tab = { ...target, key, title: target.title ?? titles[target.kind], pinned: false, scroll: 0, lastActive: Date.now() };
      pane.tabs[pane.tabs.indexOf(current)] = tab;
      this.activate(paneIndex, key);
    } else {
      await this.open(target, 'new');
    }
  }

  splitPane(direction: 'vertical' | 'horizontal', focusNew = true) {
    if (this.panes.length === 1) this.panes.push(newPane());
    this.split = direction;
    if (focusNew) this.activePane = 1;
    this.persist();
  }

  closePane(index: number) {
    if (this.panes.length === 1) return;
    this.panes = this.panes.filter((_, i) => i !== index);
    this.split = 'none';
    this.activePane = 0;
    this.persist();
  }

  retitle(nodeId: string, title: string) {
    for (const pane of this.panes) for (const tab of pane.tabs) if (tab.nodeId === nodeId && tab.kind === 'node') tab.title = title;
  }

  /** Tabs whose node was trashed stay open and show a placeholder with Restore. */
  allTabs() { return this.panes.flatMap((p) => p.tabs); }

  // ---- persistence: layout per workspace (restored when switching workspaces) ----
  serialize() {
    return {
      split: this.split,
      activePane: this.activePane,
      panes: this.panes.map((p) => ({ tabs: p.tabs.map((t) => ({ kind: t.kind, nodeId: t.nodeId, arg: t.arg, title: t.title, pinned: t.pinned, scroll: t.scroll })), active: p.active }))
    };
  }

  restore(state: any) {
    this.restoring = true;
    try {
      const panes: Pane[] = (state?.panes ?? []).slice(0, 2).map((p: any) => {
        const pane = newPane();
        pane.tabs = (p.tabs ?? []).slice(0, MAX_TABS).map((t: any) => ({
          kind: t.kind, nodeId: t.nodeId, arg: t.arg, key: tabKey(t), title: t.title ?? titles[t.kind as TabKind] ?? 'Tab',
          pinned: !!t.pinned, scroll: t.scroll ?? 0, lastActive: 0
        }));
        pane.active = pane.tabs.some((t) => t.key === p.active) ? p.active : pane.tabs[0]?.key ?? null;
        return pane;
      });
      this.panes = panes.length ? panes : [newPane()];
      this.split = this.panes.length > 1 ? (state.split === 'horizontal' ? 'horizontal' : 'vertical') : 'none';
      this.activePane = Math.min(state?.activePane ?? 0, this.panes.length - 1);
    } finally {
      this.restoring = false;
    }
  }

  persist() {
    if (this.restoring) return;
    if (this.saveTimer) clearTimeout(this.saveTimer);
    const workspace = app.workspace;
    this.saveTimer = setTimeout(() => {
      api.put(`workspace-state/${workspace}`, { layout: this.serialize() }).catch(() => {});
    }, 800);
  }

  async load(workspace: string) {
    try {
      const state = await api.get<any>(`workspace-state/${workspace}`);
      this.restore(state?.layout);
    } catch {
      this.restore(null);
    }
  }
}

function specOf(tab: Tab): TabSpec {
  return { kind: tab.kind, nodeId: tab.nodeId, arg: tab.arg, title: tab.title };
}

export const layout = new Layout();
