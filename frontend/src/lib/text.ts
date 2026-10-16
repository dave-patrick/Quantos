import { diff3Merge } from 'node-diff3';

/** Smallest single replacement turning a into b: common prefix and suffix are kept. */
export function minimalChange(a: string, b: string): { from: number; to: number; insert: string } | null {
  if (a === b) return null;
  let start = 0;
  const max = Math.min(a.length, b.length);
  while (start < max && a.charCodeAt(start) === b.charCodeAt(start)) start++;
  let endA = a.length, endB = b.length;
  while (endA > start && endB > start && a.charCodeAt(endA - 1) === b.charCodeAt(endB - 1)) { endA--; endB--; }
  return { from: start, to: endA, insert: b.slice(start, endB) };
}

/**
 * Line-based three-way merge. Returns the merged text, or null when both sides changed the
 * same lines (a real conflict that needs the side-by-side comparison).
 */
export function merge3(base: string, mine: string, theirs: string): string | null {
  if (mine === base) return theirs;
  if (theirs === base || theirs === mine) return mine;
  const split = (s: string) => s.split('\n');
  const result = diff3Merge(split(mine), split(base), split(theirs), { excludeFalseConflicts: true });
  const out: string[] = [];
  for (const block of result) {
    if (block.ok) out.push(...block.ok);
    else return null;
  }
  return out.join('\n');
}

export function wordCount(text: string): number {
  const stripped = text.replace(/```[\s\S]*?```/g, ' ').replace(/[#>*_`~\-\[\]()|!]/g, ' ');
  const words = stripped.match(/[\p{L}\p{N}'’]+/gu);
  return words ? words.length : 0;
}

export const readingMinutes = (words: number) => Math.max(1, Math.round(words / 230));

export function slugTitle(text: string) {
  return text.trim().replace(/\s+/g, ' ').slice(0, 120);
}

export function escapeHtml(s: string) {
  return s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);
}

/** Search snippets mark hits with [[[ ]]]; turn them into <mark> safely. */
export function snippetHtml(snippet: string) {
  return escapeHtml(snippet).replaceAll('[[[', '<mark>').replaceAll(']]]', '</mark>');
}
