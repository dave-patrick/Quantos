import type { NodeSummary, TreeFolder } from './types';

export interface NoteFolder { path: string; name: string; label: string; count: number }
export const folderLabels: Record<string, string> = { nodes: 'Notes', daily: 'Daily', tasks: 'Tasks', archive: 'Archive', supertags: 'Supertags' };
export const parentFolder = (path: string) => path.slice(0, path.lastIndexOf('/'));
export function inFolder(path: string, root: string) {
  return !path.includes('\\') && !path.split('/').some(p => !p || p === '.' || p === '..')
    && (path.toLowerCase() === root.toLowerCase() || path.toLowerCase().startsWith(root.toLowerCase() + '/'));
}
/** Keep physical storage directories and empty folders; never merge Notes, Tasks, Daily or Archive. */
export function noteFolders(tree: TreeFolder | null, notes: Pick<NodeSummary, 'id' | 'path'>[], root: string): NoteFolder[] {
  const folders = new Map<string, NoteFolder>();
  const files = new Map<string, string>();
  const ensure = (path: string) => {
    if (!inFolder(path, root)) return;
    if (folders.has(path.toLowerCase())) return;
    if (path !== root) ensure(parentFolder(path));
    const name = path.slice(path.lastIndexOf('/') + 1);
    folders.set(path.toLowerCase(), { path, name, label: parentFolder(path).toLowerCase() === root.toLowerCase() ? folderLabels[name] ?? name : name, count: 0 });
  };
  const walk = (f: TreeFolder) => {
    if (!inFolder(f.path, root)) return;
    ensure(f.path);
    for (const n of f.nodes) if (inFolder(n.path, root)) files.set(n.id, n.path);
    f.folders.forEach(walk);
  };
  ensure(root);
  if (tree) walk(tree);
  for (const n of notes) if (inFolder(n.path, root)) files.set(n.id, n.path);
  for (const file of files.values()) {
    let path = parentFolder(file); ensure(path);
    while (inFolder(path, root)) {
      const folder = folders.get(path.toLowerCase()); if (folder) folder.count++;
      if (path.toLowerCase() === root.toLowerCase()) break;
      path = parentFolder(path);
    }
  }
  return [...folders.values()].sort((a, b) => a.path.localeCompare(b.path, undefined, { numeric: true }));
}
/** Older phone layouts stored a path relative to the Notes directory. */
export function noteFolderLocation(arg: string | undefined, root: string, linked: boolean) {
  const path = arg?.startsWith('folderpath:') ? arg.slice(11)
    : arg?.startsWith('folder:') ? root + (linked ? '/' : '/nodes/') + arg.slice(7) : root;
  return inFolder(path, root) ? path : root;
}
