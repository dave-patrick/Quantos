import { api } from './api';
import { onChange } from './events';

export type ViewLayout = 'table' | 'list' | 'kanban' | 'grid' | 'gallery';

export interface Filter { field: string; op: 'eq' | 'neq' | 'contains' | 'empty' | 'notempty' | 'lt' | 'lte' | 'gt' | 'gte' | 'in' | 'tag'; value?: unknown }

export interface ViewQuery {
  types?: string[];
  tags?: string[];
  workspaces?: string[] | 'active' | 'all';
  text?: string;
  folder?: string;
  filters?: Filter[];
  includeArchived?: boolean;
}

export interface SavedView {
  id: string;
  name: string;
  icon?: string;
  query: ViewQuery;
  layout: ViewLayout;
  columns?: string[];
  sort?: { field: string; dir: 'asc' | 'desc' }[];
  group?: string[];
  kanbanField?: string;
  pinned?: boolean;
  virtualFolder?: boolean;
}

class Views {
  saved = $state<SavedView[]>([]);
  get pinned() { return this.saved.filter((v) => v.pinned); }

  constructor() {
    onChange((e) => { if (e.kind === 'Notification' && e.data?.config === 'views.json') this.load(); });
  }

  async load() {
    try { this.saved = (await api.get<{ views: SavedView[] }>('config/views.json'))?.views ?? []; }
    catch { this.saved = []; }
  }

  async save(view: SavedView) {
    const exists = this.saved.some((v) => v.id === view.id);
    this.saved = exists ? this.saved.map((v) => (v.id === view.id ? view : v)) : [...this.saved, view];
    await api.put('config/views.json', { views: this.saved });
  }

  async remove(id: string) {
    this.saved = this.saved.filter((v) => v.id !== id);
    await api.put('config/views.json', { views: this.saved });
  }

  get(id: string) { return this.saved.find((v) => v.id === id); }
}

export const views = new Views();
