import { app } from './state.svelte';
import type { LinkedFolder } from './types';

export type NoteCopyMode = 'filename' | 'folder' | 'full';

/** Resolve the stored file name, never the note's display title. */
export function noteCopyText(path: string, mode: NoteCopyMode, vault: string | null, linked: LinkedFolder[]): string {
  const parts = path.replace(/\\/g, '/').split('/');
  if (parts.some(p => !p || p === '.' || p === '..')) throw new Error('The note path is invalid.');
  const filename = parts.at(-1)!;
  if (mode === 'filename') return filename;
  let root = vault;
  if (parts[0] === 'linked') {
    root = linked.find(folder => folder.name === parts[1])?.path ?? null;
    parts.splice(0, 2);
    if (!parts.length) throw new Error('The note path is invalid.');
  }
  if (!root || !(/^[A-Za-z]:[\\/]/.test(root) || root.startsWith('/') || /^\\\\[^\\]+\\[^\\]+/.test(root)))
    throw new Error('The note folder location is unavailable.');
  const separator = /^[A-Za-z]:/.test(root) || root.startsWith('\\\\') ? '\\' : '/';
  root = root.replace(/[\\/]/g, separator);
  // Keep a root's separator when there are no containing subfolders.
  const join = (segments: string[]) => segments.length
    ? root!.replace(/[\\/]+$/, '') + separator + segments.join(separator)
    : root!;
  return join(mode === 'folder' ? parts.slice(0, -1) : parts);
}

export async function copyNoteLocation(path: string, mode: NoteCopyMode): Promise<void> {
  try {
    const text = noteCopyText(path, mode, app.status?.vault ?? null, app.linked);
    await navigator.clipboard.writeText(text);
    app.toast('Copied to clipboard.', 'ok');
  } catch (error) { app.error(error); }
}
