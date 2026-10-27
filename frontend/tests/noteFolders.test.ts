import { describe, expect, it } from 'vitest';
import type { TreeFolder, TreeNode } from '../src/lib/types';
import { noteFolders, noteFolderLocation, parentFolder } from '../src/lib/noteFolders';
const root = 'workspaces/default';
const folder = (path: string, folders: TreeFolder[] = [], nodes: TreeNode[] = []): TreeFolder => ({ path, name: path.split('/').at(-1)!, folders, nodes });
const node = (id: string, path: string): TreeNode => ({ id, path, title: id, type: 'note', modified: null, archived: false });
describe('physical note folders', () => {
  it('keeps Notes, Tasks and Daily as separate containing folders', () => {
    const notes = [node('a', root+'/nodes/one.md'), node('b', root+'/tasks/two.md'), node('c', root+'/daily/2026/day.md')];
    const entries = noteFolders(null, notes, root);
    expect(entries.filter(f => parentFolder(f.path) === root).map(f => f.label)).toEqual(['Daily', 'Notes', 'Tasks']);
    expect(entries.find(f => f.path === root+'/daily/2026')?.count).toBe(1);
  });
  it('includes empty directories from the actual folder tree', () => {
    const entries = noteFolders(folder(root, [folder(root+'/nodes', [folder(root+'/nodes/Empty')]), folder(root+'/archive')]), [], root);
    expect(entries.find(f => f.path === root+'/nodes/Empty')?.count).toBe(0);
    expect(entries.find(f => f.path === root+'/archive')?.label).toBe('Archive');
  });
  it('counts nested notes once even when both the tree and title index contain them', () => {
    const n = node('a', root+'/nodes/Projects/one.md');
    const entries = noteFolders(folder(root, [folder(root+'/nodes', [folder(root+'/nodes/Projects', [], [n])])]), [n], root);
    expect(entries.find(f => f.path === root+'/nodes')?.count).toBe(1);
    expect(entries.find(f => f.path === root)?.count).toBe(1);
  });
  it('keeps actual linked-folder names and excludes another workspace', () => {
    const entries = noteFolders(null, [node('a','linked/videos/Research/one.md'),node('b','workspaces/default/nodes/two.md')], 'linked/videos');
    expect(entries.map(f => f.path)).toEqual(['linked/videos','linked/videos/Research']);
  });
  it('keeps archive contents and does not merge identically named nested folders', () => {
    const entries = noteFolders(null,[node('a',root+'/nodes/Work/a.md'),node('b',root+'/archive/Work/b.md')],root);
    expect(entries.filter(f=>f.name==='Work')).toHaveLength(2);
    expect(entries.find(f=>f.path===root+'/archive')?.count).toBe(1);
  });
  it('restores new locations and migrates the previous phone folder format', () => {
    expect(noteFolderLocation('folderpath:'+root+'/tasks',root,false)).toBe(root+'/tasks');
    expect(noteFolderLocation('folder:Projects/Work',root,false)).toBe(root+'/nodes/Projects/Work');
    expect(noteFolderLocation('folder:Research','linked/videos',true)).toBe('linked/videos/Research');
    expect(noteFolderLocation('folderpath:workspaces/other/nodes',root,false)).toBe(root);
    expect(noteFolderLocation('folder:../tasks',root,false)).toBe(root);
  });
});
