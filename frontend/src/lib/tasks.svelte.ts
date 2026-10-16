import { api } from './api';
import { onChange } from './events';
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
  effort: number | null;
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
}

export interface TaskInfo { status: string; due?: string | null; planned?: string | null; priority?: number | null; deleted?: boolean }

class Tasks {
  version = $state(0);
  list = $state<TaskDto[]>([]);
  private map = new Map<string, TaskDto>();
  private trashed = new Map<string, string>(); // task id -> trash entry id
  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    onChange((e) => {
      if (e.kind === 'Trashed' && e.id) this.trashed.set(e.id, e.data?.entryId);
      if (e.kind === 'Restored' && e.id) this.trashed.delete(e.id);
      if (e.kind === 'Deleted' && e.id) this.trashed.delete(e.id);
      if (e.kind !== 'Approval' && e.kind !== 'Notification') this.schedule();
    });
  }

  schedule() {
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.load().catch(() => {}), 300);
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

  async create(input: { title: string; workspace: string; due?: string | null; planned?: string | null; priority?: number | null; parent?: string | null; project?: string | null; source?: string | null; recurrence?: string | null; effort?: number | null; assignees?: string[] }) {
    const node = await api.post<NodeDto>('tasks', input);
    this.schedule();
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
