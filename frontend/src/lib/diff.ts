import { diffComm } from 'node-diff3';

/**
 * Revision comparison (Release 1.1): line diffs with the changed words marked inside each line,
 * unchanged stretches folded, and frontmatter compared property by property.
 */
export interface Segment { text: string; kind: 'same' | 'del' | 'add' }
export interface Row {
  kind: 'same' | 'del' | 'add' | 'mod';
  oldNo?: number; newNo?: number;
  old?: Segment[]; new?: Segment[];
}
export type Block = { kind: 'rows'; rows: Row[]; change: number | null } | { kind: 'fold'; rows: Row[] };
export interface PropChange { key: string; before: string | null; after: string | null }
export interface Comparison { props: PropChange[]; blocks: Block[]; added: number; removed: number; changes: number; wordsAdded: number; wordsRemoved: number }

const TOKEN = /\s+|[\p{L}\p{N}_'’-]+|[^\s\p{L}\p{N}_]/gu;
const tokens = (s: string) => s.match(TOKEN) ?? [];
const words = (s: string) => (s.match(/[\p{L}\p{N}_'’-]+/gu) ?? []).length;

/** Word-level difference between two versions of a line. */
export function wordDiff(a: string, b: string): { old: Segment[]; new: Segment[] } {
  const old: Segment[] = [], neu: Segment[] = [];
  const push = (list: Segment[], text: string, kind: Segment['kind']) => {
    if (!text) return;
    const last = list[list.length - 1];
    if (last && last.kind === kind) last.text += text; else list.push({ text, kind });
  };
  for (const part of diffComm(tokens(a), tokens(b))) {
    if (part.common) { const t = part.common.join(''); push(old, t, 'same'); push(neu, t, 'same'); }
    else { push(old, (part.buffer1 ?? []).join(''), 'del'); push(neu, (part.buffer2 ?? []).join(''), 'add'); }
  }
  return { old, new: neu };
}

/** Splits a file into its frontmatter entries (top-level key → its lines) and its body. */
export function splitFrontmatter(content: string): { props: Map<string, string>; body: string } {
  const m = /^﻿?---\r?\n([\s\S]*?)\r?\n(?:---|\.\.\.)\r?\n?/.exec(content);
  const props = new Map<string, string>();
  if (!m) return { props, body: content };
  let key: string | null = null;
  for (const line of m[1].split(/\r?\n/)) {
    const k = /^([^\s#][^:]*):(?:\s|$)/.exec(line);
    if (k) { key = k[1].trim(); props.set(key, line.slice(k[0].length).trim()); }
    else if (key) props.set(key, (props.get(key) + '\n' + line).trim());
  }
  return { props, body: content.slice(m[0].length) };
}

/** Lines that are only clock bookkeeping (`modified:`) aren't a change worth showing. */
const QUIET = new Set(['modified']);

export function compare(before: string, after: string, context = 3): Comparison {
  const a = splitFrontmatter(before), b = splitFrontmatter(after);
  const props: PropChange[] = [];
  for (const key of new Set([...a.props.keys(), ...b.props.keys()])) {
    if (QUIET.has(key)) continue;
    const x = a.props.get(key) ?? null, y = b.props.get(key) ?? null;
    if (x !== y) props.push({ key, before: x, after: y });
  }

  const rows: Row[] = [];
  let oldNo = 0, newNo = 0, added = 0, removed = 0, wordsAdded = 0, wordsRemoved = 0;
  const linesA = a.body.replace(/\r\n/g, '\n').split('\n'), linesB = b.body.replace(/\r\n/g, '\n').split('\n');
  for (const part of diffComm(linesA, linesB)) {
    if (part.common) { for (const l of part.common) rows.push({ kind: 'same', oldNo: ++oldNo, newNo: ++newNo, old: [{ text: l, kind: 'same' }], new: [{ text: l, kind: 'same' }] }); continue; }
    const dels = part.buffer1 ?? [], adds = part.buffer2 ?? [];
    removed += dels.length; added += adds.length;
    const paired = Math.min(dels.length, adds.length);
    for (let i = 0; i < Math.max(dels.length, adds.length); i++) {
      if (i < paired) {
        const w = wordDiff(dels[i], adds[i]);
        wordsRemoved += w.old.filter((s) => s.kind === 'del').reduce((n, s) => n + words(s.text), 0);
        wordsAdded += w.new.filter((s) => s.kind === 'add').reduce((n, s) => n + words(s.text), 0);
        rows.push({ kind: 'mod', oldNo: ++oldNo, newNo: ++newNo, ...w });
      } else if (i < dels.length) {
        wordsRemoved += words(dels[i]);
        rows.push({ kind: 'del', oldNo: ++oldNo, old: [{ text: dels[i], kind: 'del' }] });
      } else {
        wordsAdded += words(adds[i]);
        rows.push({ kind: 'add', newNo: ++newNo, new: [{ text: adds[i], kind: 'add' }] });
      }
    }
  }

  // Unchanged lines more than `context` away from a change fold away.
  const near = rows.map(() => false);
  rows.forEach((r, i) => { if (r.kind !== 'same') for (let j = Math.max(0, i - context); j <= Math.min(rows.length - 1, i + context); j++) near[j] = true; });
  const blocks: Block[] = [];
  let changes = 0;
  for (let i = 0; i < rows.length;) {
    if (!near[i]) {
      const start = i;
      while (i < rows.length && !near[i]) i++;
      blocks.push({ kind: 'fold', rows: rows.slice(start, i) });
    } else {
      const start = i;
      while (i < rows.length && near[i]) i++;
      const slice = rows.slice(start, i);
      blocks.push({ kind: 'rows', rows: slice, change: slice.some((r) => r.kind !== 'same') ? changes++ : null });
    }
  }
  return { props, blocks, added, removed, changes, wordsAdded, wordsRemoved };
}
