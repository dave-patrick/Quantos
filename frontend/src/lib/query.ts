import { api } from './api';
import { app } from './state.svelte';
import type { ViewQuery } from './views.svelte';

export interface QueryRow {
  id: string; title: string; type: string; workspace: string; path: string; tags: string[];
  created: string | null; modified: string | null; archived: boolean; properties: Record<string, unknown>; cover?: string | null;
}

export interface QueryResult { rows: QueryRow[]; total: number; columns: string[]; error: string | null }

export function runQuery(query: ViewQuery & { sort?: { field: string; dir: string }[]; columns?: string[]; limit?: number }) {
  return api.post<QueryResult>(`query?workspace=${encodeURIComponent(app.workspace)}`, {
    ...query,
    workspaces: query.workspaces ?? 'active',
    filters: (query.filters ?? []).map((f) => ({ field: f.field, op: f.op, value: f.value ?? null }))
  });
}

export function runQueryText(text: string) {
  return api.post<QueryResult>('query/text', { text, workspace: app.workspace });
}

const BUILTIN = new Set(['title', 'type', 'workspace', 'created', 'modified', 'path', 'tags']);
export const isBuiltin = (field: string) => BUILTIN.has(field);

export function cellValue(row: QueryRow, field: string): unknown {
  switch (field) {
    case 'title': return row.title;
    case 'type': return row.type;
    case 'workspace': return row.workspace;
    case 'created': return row.created;
    case 'modified': return row.modified;
    case 'path': return row.path;
    case 'tags': return row.tags;
    default: return row.properties[field];
  }
}

export function display(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (Array.isArray(value)) return value.map(display).join(', ');
  if (typeof value === 'object') return JSON.stringify(value);
  const s = String(value);
  return s.replace(/\[\[[0-9A-HJKMNP-TV-Z]{26}(?:#[^\]|]+)?\|([^\]]+)\]\]/g, '$1');
}

export const linkTarget = (value: unknown) => typeof value === 'string' ? /^\[\[([0-9A-HJKMNP-TV-Z]{26})/.exec(value)?.[1] ?? null : null;

/** Parses pasted rows: tab-separated (spreadsheets), CSV, or a Markdown table. First row is the header. */
export function parsePasted(text: string): { headers: string[]; rows: string[][] } {
  const lines = text.replace(/\r/g, '').split('\n').filter((l) => l.trim().length > 0);
  if (lines.length === 0) return { headers: [], rows: [] };
  let cells: string[][];
  if (lines[0].trim().startsWith('|')) {
    cells = lines.filter((l) => !/^\s*\|?\s*:?-{2,}/.test(l)).map((l) => l.trim().replace(/^\||\|$/g, '').split('|').map((c) => c.trim()));
  } else if (lines[0].includes('\t')) {
    cells = lines.map((l) => l.split('\t').map((c) => c.trim()));
  } else {
    cells = lines.map(parseCsvLine);
  }
  // A single column without a title/name header is just a list of titles.
  if (cells.every((r) => r.length === 1) && !/^(title|name)$/i.test(cells[0][0])) return { headers: ['title'], rows: cells };
  const [headers, ...rows] = cells;
  if (rows.length === 0) return { headers: ['title'], rows: cells.map((r) => [r.join(' ')]) };
  return { headers: headers.map((h) => h.trim()), rows };
}

function parseCsvLine(line: string): string[] {
  const out: string[] = [];
  let cur = '', quoted = false;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (quoted) {
      if (ch === '"' && line[i + 1] === '"') { cur += '"'; i++; }
      else if (ch === '"') quoted = false;
      else cur += ch;
    } else if (ch === '"') quoted = true;
    else if (ch === ',') { out.push(cur.trim()); cur = ''; }
    else cur += ch;
  }
  out.push(cur.trim());
  return out;
}

export const propertyKey = (header: string) => header.trim().toLowerCase().replace(/[^\p{L}\p{N}]+/gu, '_').replace(/^_+|_+$/g, '');
