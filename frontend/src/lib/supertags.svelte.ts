import { api } from './api';
import { onChange } from './events';
import { app } from './state.svelte';

export type FieldType = 'text' | 'number' | 'date' | 'datetime' | 'checkbox' | 'select' | 'multiselect' | 'node' | 'nodes' | 'url' | 'email' | 'person' | 'status';

export interface FieldDef {
  name: string;
  key: string;           // property key in frontmatter (namespaced when names conflict)
  type: FieldType;
  options?: string[];
  required?: boolean;
  default?: unknown;
  description?: string;
  inherited?: string;    // supertag the field came from
}

export interface Supertag {
  id: string;
  name: string;
  parent?: string | null;
  color?: string | null;
  icon?: string | null;
  scope: string;         // '' for global, else workspace name
  fields: FieldDef[];    // own fields
  allFields: FieldDef[]; // including inherited
  template?: string | null;
  autoApplyTemplate?: boolean;
  layout?: string | null;
  description?: string;
  count: number;
  path: string;
  error?: string | null;
}

class Supertags {
  private byWorkspace = $state<Record<string, Supertag[]>>({});
  private sequence = new Map<string, number>();
  get list() { return this.listFor(app.workspace); }
  listFor(workspace: string) { return this.byWorkspace[workspace] ?? []; }
  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    onChange((e) => {
      if (e.kind === 'Reindexed' || e.path?.includes('supertags/') || e.kind === 'Updated' || e.kind === 'Created' || e.kind === 'Moved') {
        if (this.timer) clearTimeout(this.timer);
        this.timer = setTimeout(() => {
          for (const workspace of new Set([app.workspace, ...Object.keys(this.byWorkspace)])) void this.load(workspace);
        }, 500);
      }
    });
  }

  async load(workspace = app.workspace) {
    const request = (this.sequence.get(workspace) ?? 0) + 1;
    this.sequence.set(workspace, request);
    try {
      const list = await api.get<Supertag[]>(`supertags?workspace=${encodeURIComponent(workspace)}`);
      if (this.sequence.get(workspace) === request) this.byWorkspace[workspace] = list;
    } catch {
      if (this.sequence.get(workspace) === request) this.byWorkspace[workspace] = [];
    }
  }

  byName(name: string, workspace = app.workspace) { return this.listFor(workspace).find((t) => t.name.toLowerCase() === name.toLowerCase()); }

  get tree(): { tag: Supertag; depth: number; count: number }[] {
    const children = new Map<string, Supertag[]>();
    for (const t of this.list) {
      const key = t.parent?.toLowerCase() ?? '';
      children.set(key, [...(children.get(key) ?? []), t]);
    }
    const out: { tag: Supertag; depth: number; count: number }[] = [];
    const walk = (parent: string, depth: number, seen: Set<string>) => {
      for (const t of (children.get(parent) ?? []).sort((a, b) => a.name.localeCompare(b.name))) {
        if (seen.has(t.name.toLowerCase())) continue;
        out.push({ tag: t, depth, count: t.count });
        walk(t.name.toLowerCase(), depth + 1, new Set([...seen, t.name.toLowerCase()]));
      }
    };
    walk('', 0, new Set());
    // Orphans whose parent is missing.
    for (const t of this.list) if (!out.some((o) => o.tag === t)) out.push({ tag: t, depth: 0, count: t.count });
    return out;
  }
}

export const supertags = new Supertags();
