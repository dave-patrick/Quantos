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
  text = text.replace(/^﻿/, '');
  const lines = text.replace(/\r/g, '').split('\n').filter((l) => l.trim().length > 0);
  if (lines.length === 0) return { headers: [], rows: [] };
  let cells: string[][];
  if (lines[0].trim().startsWith('|')) {
    cells = lines.filter((l) => !/^\s*\|?\s*:?-{2,}/.test(l)).map((l) => l.trim().replace(/^\||\|$/g, '').split('|').map((c) => c.trim()));
  } else {
    cells = parseCsv(text).map((r) => r.map((c) => c.trim()));
  }
  // A single column without a title/name header is just a list of titles.
  if (cells.every((r) => r.length === 1) && !/^(title|name)$/i.test(cells[0][0])) return { headers: ['title'], rows: cells };
  const [headers, ...rows] = cells;
  if (rows.length === 0) return { headers: ['title'], rows: cells.map((r) => [r.join(' ')]) };
  return { headers: headers.map((h) => h.trim()), rows };
}

/** The separator a CSV file uses: tab, semicolon (Excel in many European locales) or comma. */
function detectDelimiter(text: string): string {
  const first = text.split(/\r?\n/, 1)[0] ?? '';
  const count = (ch: string) => first.split(ch).length - 1;
  if (count('\t') > 0) return '\t';
  return count(';') > count(',') ? ';' : ',';
}

/** CSV per RFC 4180: quoted cells may hold separators, doubled quotes and line breaks. Blank lines are skipped. */
export function parseCsv(text: string, delimiter = detectDelimiter(text)): string[][] {
  const rows: string[][] = [];
  let row: string[] = [], cell = '', quoted = false;
  const endRow = () => { row.push(cell); if (row.some((c) => c.trim() !== '')) rows.push(row); row = []; cell = ''; };
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (quoted) {
      if (ch === '"' && text[i + 1] === '"') { cell += '"'; i++; }
      else if (ch === '"') quoted = false;
      else cell += ch;
    } else if (ch === '"' && cell.trim() === '') { quoted = true; cell = ''; }
    else if (ch === delimiter) { row.push(cell); cell = ''; }
    else if (ch === '\n' || ch === '\r') { if (ch === '\r' && text[i + 1] === '\n') i++; endRow(); }
    else cell += ch;
  }
  if (cell !== '' || row.length) endRow();
  return rows;
}

/** Rows as CSV that Excel opens correctly: UTF-8 marker, CRLF line ends, quotes only where needed. */
export function toCsv(headers: string[], rows: string[][]): string {
  const quote = (v: string) => (/[",\r\n;]/.test(v) || /^\s|\s$/.test(v) ? `"${v.replace(/"/g, '""')}"` : v);
  return '﻿' + [headers, ...rows].map((r) => r.map((v) => quote(v ?? '')).join(',')).join('\r\n') + '\r\n';
}

/** Plain numbers and true/false become typed values; anything else (a code like "007", dates) stays text. */
export function typedValue(v: string): string | number | boolean {
  if (/^(true|false)$/i.test(v)) return v.toLowerCase() === 'true';
  if (/^-?(0|[1-9]\d{0,14})(\.\d+)?$/.test(v)) return Number(v);
  return v;
}

export const propertyKey = (header: string) => header.trim().toLowerCase().replace(/[^\p{L}\p{N}]+/gu, '_').replace(/^_+|_+$/g, '');
