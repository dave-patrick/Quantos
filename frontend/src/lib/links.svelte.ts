import { api } from './api';
import { layout, type Where } from './layout.svelte';
import { app } from './state.svelte';
import type { NodeDto, NodeSummary } from './types';
import { tasks } from './tasks.svelte';

export const ULID = /^[0-9A-HJKMNP-TV-Z]{26}$/;

export function whereFrom(event?: MouseEvent | KeyboardEvent | null): Where {
  if (!event) return 'current';
  if (event.shiftKey) return 'other';              // Shift-click opens in the other pane (confirmed)
  if (event.ctrlKey || event.metaKey || ('button' in event && event.button === 1)) return 'new';
  return 'current';
}

export function openNode(id: string, where: Where = 'current', title?: string) {
  const summary = app.titleIndex.get(id);
  return layout.open({ kind: 'node', nodeId: id, title: title ?? summary?.title ?? 'Note' }, where);
}

/** Resolves a typed title. Duplicates never get picked silently: the user chooses. */
export async function openByTitle(title: string, where: Where = 'current') {
  const [wsPrefix, name] = title.includes(':') ? [title.slice(0, title.indexOf(':')), title.slice(title.indexOf(':') + 1)] : [null, title];
  const workspace = wsPrefix ?? app.workspace;
  let candidates = await api.get<NodeSummary[]>(`resolve?title=${encodeURIComponent(name.trim())}&workspace=${encodeURIComponent(workspace)}`);
  if (candidates.length === 0 && !wsPrefix) candidates = await api.get<NodeSummary[]>(`resolve?title=${encodeURIComponent(name.trim())}`);
  if (candidates.length === 1) return openNode(candidates[0].id, where, candidates[0].title);
  if (candidates.length > 1) {
    app.dialog = {
      title: `More than one “${name.trim()}”`,
      message: 'Choose which one to open.',
      actions: [
        ...candidates.slice(0, 6).map((c) => ({ label: `${c.title} · ${c.workspace}${c.archived ? ' (archived)' : ''}`, run: () => { app.dialog = null; openNode(c.id, where, c.title); } })),
        { label: 'Cancel', run: () => (app.dialog = null) }
      ]
    };
    return null;
  }
  const ok = await app.confirm('Create note?', `There's no note called “${name.trim()}” yet. Create it in ${workspace}?`, 'Create');
  if (!ok) return null;
  const node = await api.post<NodeDto>('nodes', { title: name.trim(), workspace });
  return openNode(node.id, where, node.title);
}

export function openTarget(target: string, where: Where) {
  if (ULID.test(target)) return openNode(target, where);
  return openByTitle(target, where);
}

/** Hover previews (BUILD-PLAN 8A.13). */
class Hover {
  target = $state<string | null>(null);
  rect = $state<DOMRect | null>(null);
  private timer: ReturnType<typeof setTimeout> | null = null;

  show(target: string, element: HTMLElement | null) {
    if (this.timer) clearTimeout(this.timer);
    if (!target || !element) {
      this.timer = setTimeout(() => { this.target = null; }, 250);
      return;
    }
    this.timer = setTimeout(() => {
      this.target = target;
      this.rect = element.getBoundingClientRect();
    }, 450);
  }

  keep() { if (this.timer) clearTimeout(this.timer); }
  hide() { this.target = null; }
}
export const hover = new Hover();

function fuzzyScore(title: string, query: string): number {
  const t = title.toLowerCase();
  const q = query.toLowerCase().trim();
  if (!q) return 1;
  if (t === q) return 1000;
  if (t.startsWith(q)) return 500 - t.length;
  const idx = t.indexOf(q);
  if (idx >= 0) return 300 - idx;
  // Every word must appear; words matching at a word start score higher.
  let score = 100;
  for (const word of q.split(/\s+/).filter(Boolean)) {
    const at = t.indexOf(word);
    if (at < 0) return -1;
    score += at === 0 || /[\s\-_/(]/.test(t[at - 1]) ? 20 : 5;
  }
  return score;
}

export function searchTitles(query: string, limit = 50): NodeSummary[] {
  const ws = app.workspace;
  return app.titles
    .map((n) => {
      let best = fuzzyScore(n.title, query);
      for (const alias of n.aliases ?? []) best = Math.max(best, fuzzyScore(String(alias), query) - 1);
      return { n, raw: best, s: best + (n.workspace === ws ? 50 : 0) - (n.archived ? 100 : 0) };
    })
    .filter((x) => x.raw >= 0)
    .sort((a, b) => b.s - a.s)
    .slice(0, limit)
    .map((x) => x.n);
}

export const linkHooks = {
  titleOf: (id: string) => app.titleIndex.get(id)?.title,
  openLink: (target: string, event: MouseEvent) => { openTarget(target, whereFrom(event))?.catch?.((e: unknown) => app.error(e)); },
  hoverLink: (target: string, element: HTMLElement | null) => hover.show(target, element),
  searchTitles: (query: string) => searchTitles(query, 30)
};

export const tasksVersion = { get value() { return tasks.version; } };

export const taskHooks = {
  toggleTask: (taskId: string, checked: boolean) => { tasks.setStatus(taskId, checked ? 'done' : 'todo').catch((e) => app.error(e)); },
  taskInfo: (taskId: string) => tasks.info(taskId),
  openTask: (taskId: string, event: MouseEvent) => { openNode(taskId, whereFrom(event)); },
  restoreTask: (taskId: string) => { tasks.restore(taskId).catch((e) => app.error(e)); }
};
