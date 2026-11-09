import { beforeEach, expect, it, vi } from 'vitest';
const state = vi.hoisted(() => ({ status: { vault: 'C:\\Vault' }, linked: [], toast: vi.fn(), error: vi.fn() }));
vi.mock('../src/lib/state.svelte', () => ({ app: state }));
import { noteCopyText, copyNoteLocation } from '../src/lib/noteClipboard';

it.each([
  ['C:\\Vault', 'Notes/My note.md', 'C:\\Vault\\Notes', 'C:\\Vault\\Notes\\My note.md'],
  ['C:\\', 'Résumé.md', 'C:\\', 'C:\\Résumé.md'],
  ['/', 'Résumé.md', '/', '/Résumé.md'],
  ['/home/me', 'Notes/My note.md', '/home/me/Notes', '/home/me/Notes/My note.md'],
  ['\\\\server\\share\\', 'Résumé.md', '\\\\server\\share\\', '\\\\server\\share\\Résumé.md'],
])('copies locations under %s', (root, path, folder, full) => {
  expect(noteCopyText(path, 'folder', root, [])).toBe(folder);
  expect(noteCopyText(path, 'full', root, [])).toBe(full);
  expect(noteCopyText(path, 'filename', null, [])).toBe(path.split('/').at(-1));
});
it('uses the configured linked root even while unavailable', () => {
  const linked = [{ name: 'Videos', path: 'D:\\My Notes\\', exists: false }];
  expect(noteCopyText('linked/Videos/One.md', 'full', 'C:\\Vault', linked)).toBe('D:\\My Notes\\One.md');
  expect(noteCopyText('linked/Videos/One.md', 'folder', null, linked)).toBe('D:\\My Notes\\');
});
it('does not invent a location for unknown roots or traversal', () => {
  for (const path of ['linked/unknown/One.md', 'Notes/../One.md', 'Notes//One.md', ''])
    expect(() => noteCopyText(path, 'full', 'C:\\Vault', [])).toThrow();
  expect(() => noteCopyText('One.md', 'full', null, [])).toThrow();
  expect(() => noteCopyText('One.md', 'full', 'relative', [])).toThrow();
});
beforeEach(() => { state.toast.mockClear(); state.error.mockClear(); });
it('starts clipboard access immediately and confirms only after success', async () => {
  let done!: () => void;
  const writeText = vi.fn(() => new Promise<void>(resolve => { done = resolve; }));
  vi.stubGlobal('navigator', { clipboard: { writeText } });
  const pending = copyNoteLocation('Notes/One.md', 'filename');
  expect(writeText).toHaveBeenCalledWith('One.md');
  expect(state.toast).not.toHaveBeenCalled();
  done(); await pending;
  expect(state.toast).toHaveBeenCalled();
});
it('reports denied clipboard access without a success toast', async () => {
  const error = new Error('Denied');
  vi.stubGlobal('navigator', { clipboard: { writeText: vi.fn().mockRejectedValue(error) } });
  await copyNoteLocation('Notes/One.md', 'full');
  expect(state.error).toHaveBeenCalledWith(error);
  expect(state.toast).not.toHaveBeenCalled();
});
