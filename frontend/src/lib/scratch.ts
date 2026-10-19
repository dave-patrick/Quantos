/**
 * Scratchpad "send to" shortcuts: a key sends the selection (or the paragraph the cursor is in) to a place
 * and takes it out of the scratchpad. The places and their keys are the person's (prefs.scratchTargets).
 */
export interface ScratchTarget {
  id: string;
  label: string;
  keys: string;                                   // command format, e.g. "Mod+Enter"
  kind: 'daily' | 'inbox' | 'task' | 'note';
  noteId?: string;                                // kind 'note': where to add it
  noteTitle?: string;
  heading?: string;                               // kind 'note': under this heading (made if missing)
}

export const DEFAULT_SCRATCH_TARGETS: ScratchTarget[] = [
  { id: 'daily', label: "Today's daily note", keys: 'Mod+Enter', kind: 'daily' },
  { id: 'inbox', label: 'A new note', keys: 'Mod+Shift+Enter', kind: 'inbox' },
  { id: 'task', label: 'A new task', keys: 'Mod+Alt+Enter', kind: 'task' }
];

export const KIND_LABELS: Record<ScratchTarget['kind'], string> = { daily: "Today's daily note", inbox: 'A new note', task: 'A new task', note: 'The end of a note' };

/**
 * What a send takes: the selection, or else the paragraph around the cursor (the run of non-blank lines).
 * Offsets are into the whole text; `to` includes one following line break so no gap is left behind.
 */
export function sendRange(doc: string, from: number, to: number): { from: number; to: number; text: string } | null {
  if (to > from) {
    const text = doc.slice(from, to).trim();
    return text ? { from, to, text } : null;
  }
  const lines = doc.split('\n');
  let pos = 0, line = 0;
  for (; line < lines.length; line++) { if (from <= pos + lines[line].length) break; pos += lines[line].length + 1; }
  if (line >= lines.length || !lines[line].trim()) return null;
  let first = line, last = line;
  while (first > 0 && lines[first - 1].trim()) first--;
  while (last < lines.length - 1 && lines[last + 1].trim()) last++;
  const start = lines.slice(0, first).reduce((n, l) => n + l.length + 1, 0);
  let end = start + lines.slice(first, last + 1).join('\n').length;
  if (doc[end] === '\n') end++;
  return { from: start, to: end, text: lines.slice(first, last + 1).join('\n').trim() };
}
