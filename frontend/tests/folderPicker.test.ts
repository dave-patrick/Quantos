import { beforeEach, describe, expect, it, vi } from 'vitest';
const mock = vi.hoisted(() => ({ listeners: new Set<(m: any) => void>(), send: vi.fn() }));
vi.mock('../src/lib/platform', () => ({ bridge: {
  send: mock.send,
  on: (fn: (m: any) => void) => { mock.listeners.add(fn); return () => mock.listeners.delete(fn); }
} }));
import { startFolderPick } from '../src/lib/folderPicker';
function reply(extra: object) {
  const request = mock.send.mock.calls[0][0];
  for (const listener of [...mock.listeners]) listener({ type: 'folder-picked', requestId: request.requestId, ...extra });
}
beforeEach(() => { mock.listeners.clear(); mock.send.mockReset(); });
describe('native folder picker', () => {
  it.each(['C:\\', '\\\\server\\share\\', 'D:\\My Notes'])('retains selected absolute path %s', (path) => {
    const done = vi.fn();
    startFolderPick(path, done);
    expect(mock.send.mock.calls[0][0].path).toBe(path);
    reply({ path, error: null });
    expect(done).toHaveBeenCalledWith({ path, error: undefined });
    expect(mock.listeners.size).toBe(0);
  });
  it('reports cancellation and errors without inventing a path', () => {
    const done = vi.fn();
    startFolderPick('D:\\Notes', done);
    reply({ path: null });
    expect(done).toHaveBeenCalledWith({ path: null, error: undefined });
    mock.send.mockClear();
    startFolderPick('D:\\Notes', done);
    reply({ path: null, error: 'Unavailable' });
    expect(done).toHaveBeenLastCalledWith({ path: null, error: 'Unavailable' });
  });
  it('ignores unrelated, malformed and disposed replies', () => {
    const done = vi.fn();
    const dispose = startFolderPick('', done);
    reply({ path: 'C:\\', requestId: 'other' });
    reply({ path: 42 });
    expect(done).not.toHaveBeenCalled();
    dispose();
    reply({ path: 'C:\\' });
    expect(done).not.toHaveBeenCalled();
    expect(mock.listeners.size).toBe(0);
  });
  it('cleans up a synchronous send failure', () => {
    mock.send.mockImplementation(() => { throw new Error('Disconnected'); });
    const done = vi.fn();
    startFolderPick('', done);
    expect(done).toHaveBeenCalledWith({ path: null, error: 'Disconnected' });
    expect(mock.listeners.size).toBe(0);
  });
});
