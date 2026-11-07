import { api, ApiError } from './api';
import { onChange, onResync } from './events';
import type { NodeDto, TrashEntry } from './types';

export type TaskStatus = 'todo' | 'doing' | 'waiting' | 'done' | 'cancelled';

export interface TaskDto {
  id: string;
  title: string;
  workspace: string;
  path: string;
  status: TaskStatus;
  priority: number | null;
  due: string | null;
  planned: string | null;
  effort: number | string | null;
  project: string | null;
  assignees: string[];
  waitingOn: string | null;
  parent: string | null;
  dependsOn: string[];
  recurrence: string | null;
  series: string | null;
  occurrence: string | null;
  completed: string | null;
  source: string | null;
  timeBlock: { start: string; end: string } | null;
  reminders: { at: string; snoozedUntil?: string | null; fired?: string | null }[];
  tags: string[];
  modified: string | null;
  created: string | null;
  archived: boolean;
  blocked: boolean;
  subtaskCount: number;
  subtasksDone: number;
  effortRollup: number | null;
  stage: string | null;
  myDay: string | null;
  myDayOrder: number | null;
  rolledOver: number;
}

export interface TaskInfo { status: string; due?: string | null; planned?: string | null; priority?: number | null; deleted?: boolean }

class Tasks {
  version = $state(0);
  list = $state<TaskDto[]>([]);
  private map = new Map<string, TaskDto>();
  private trashed = new Map<string, string>(); // task id -> trash entry id
  private timer: ReturnType<typeof setTimeout> | null = null;
  /** Task ids to refresh; null means reload every task. */
  private changed: Set<string> | null = new Set();

  constructor() {
    onResync(() => this.schedule());
    onChange((e) => {
      if (e.kind === 'Trashed' && e.id && e.data?.type === 'task') this.trashed.set(e.id, e.data?.entryId);
      if (e.kind === 'Restored' && e.id) this.trashed.delete(e.id);
      if (e.kind === 'Deleted' && e.id) this.trashed.delete(e.id);
      if (e.kind === 'Reindexed') { this.schedule(); return; }
      if (!e.id || ['Approval', 'Notification', 'ShellJob', 'Conflict'].includes(e.kind)) return;
      // Only tasks change task data. An event that says the node isn't a task (and that we don't
      // hold as one) is ignored, instead of reloading every task after every note save.
      const isTask = e.summary ? e.summary.type === 'task' : e.kind === 'Trashed' ? e.data?.type === 'task' : undefined;
      if (isTask === false && !this.map.has(e.id)) return;
      this.schedule(e.id);
    });
  }

  /** Refreshes the given task (and the tasks whose numbers depend on it) soon; no id reloads them all. */
  schedule(id?: string) {
    if (id && this.changed) this.changed.add(id); else this.changed = null;
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.flush().catch(() => {}), 300);
  }

  private async flush() {
    const ids = this.changed;
    this.changed = new Set();
    if (!ids || this.list.length === 0) return this.load();
    if (ids.size === 0) return;
    // A task's parent shows subtask counts, and tasks that depend on it show "blocked": refresh those too.
    const affected = new Set(ids);
    for (const id of ids) {
      const old = this.map.get(id);
      if (old?.parent) affected.add(old.parent);
      for (const t of this.list) if (t.dependsOn.includes(id)) affected.add(t.id);
    }
    if (affected.size > 25) return this.load();
    const fresh = new Map<string, TaskDto | null>();
    try {
      await Promise.all([...affected].map(async (id) => {
        try { fresh.set(id, await api.get<TaskDto>(`tasks/${encodeURIComponent(id)}`)); }
        catch (e) { if (e instanceof ApiError && e.status === 404) fresh.set(id, null); else throw e; }
      }));
    } catch { return this.load(); }
    // A new parent (a task moved under another) needs its counts refreshed too.
    const parents = [...fresh.values()].map((t) => t?.parent).filter((p): p is string => !!p && !fresh.has(p));
    for (const p of parents) {
      try { fresh.set(p, await api.get<TaskDto>(`tasks/${encodeURIComponent(p)}`)); } catch { /* left as it was */ }
    }
    const map = new Map(this.map);
    for (const [id, t] of fresh) { if (t) map.set(id, t); else map.delete(id); }
    const added = [...fresh.values()].filter((t): t is TaskDto => !!t && !this.map.has(t.id));
    this.list = [...this.list.filter((t) => !fresh.has(t.id) || fresh.get(t.id)).map((t) => map.get(t.id) ?? t), ...added];
    this.map = map;
    this.version++;
  }

  async load() {
    const [list, trash] = await Promise.all([api.get<TaskDto[]>('tasks?include=all'), api.get<TrashEntry[]>('trash')]);
    this.map = new Map(list.map((t) => [t.id, t]));
    this.trashed = new Map(trash.filter((e) => e.type === 'task').map((e) => [e.id, e.entryId]));
    this.list = list;
    this.version++;
  }

  get(id: string) { return this.map.get(id); }

  info(id: string): TaskInfo | undefined {
    if (this.trashed.has(id)) return { status: 'deleted', deleted: true };
    const t = this.map.get(id);
    return t ? { status: t.status, due: t.due, planned: t.planned, priority: t.priority } : undefined;
  }

  async setStatus(id: string, status: TaskStatus) {
    const updated = await api.post<TaskDto>(`tasks/${id}/status`, { status });
    this.map.set(id, updated);
    this.list = this.list.map((t) => (t.id === id ? updated : t));
    this.version++;
    return updated;
  }

  async update(id: string, set: Record<string, unknown>, remove: string[] = []) {
    const updated = await api.post<TaskDto>(`tasks/${id}/update`, { set, remove });
    this.map.set(id, updated);
    this.list = this.list.map((t) => (t.id === id ? updated : t));
    this.version++;
    return updated;
  }

  async create(input: { title: string; workspace: string; due?: string | null; planned?: string | null; priority?: number | null; parent?: string | null; project?: string | null; source?: string | null; recurrence?: string | null; effort?: number | string | null; assignees?: string[] }) {
    const node = await api.post<NodeDto>('tasks', input);
    this.schedule(node.id);
    return node;
  }

  async restore(id: string) {
    let entry = this.trashed.get(id);
    if (!entry) {
      const trash = await api.get<TrashEntry[]>('trash');
      entry = trash.find((e) => e.id === id)?.entryId;
    }
    if (!entry) throw new Error('That task is no longer in the trash.');
    await api.post(`trash/${entry}/restore`);
    this.trashed.delete(id);
    this.version++;
  }
}

export const tasks = new Tasks();
