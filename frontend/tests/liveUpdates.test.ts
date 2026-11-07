// @vitest-environment happy-dom
// Rebuild 3 (2026-11-07): events carry data, numbered; stores patch themselves and reload only after a gap.
import { afterEach, describe, expect, it, vi } from 'vitest';

describe('event stream numbering', () => {
  afterEach(() => { vi.unstubAllGlobals(); vi.resetModules(); });

  it('asks pages to reload after a gap in the numbers or a reconnect, not otherwise', async () => {
    const sources: FakeSource[] = [];
    class FakeSource {
      listeners = new Map<string, (e: MessageEvent) => void>();
      onerror: (() => void) | null = null;
      constructor() { sources.push(this); }
      addEventListener(name: string, fn: (e: MessageEvent) => void) { this.listeners.set(name, fn); }
      close() {}
      emit(name: string, data: unknown = {}) { this.listeners.get(name)?.({ data: JSON.stringify(data) } as MessageEvent); }
    }
    vi.stubGlobal('EventSource', FakeSource);
    vi.stubGlobal('BroadcastChannel', undefined);
    vi.useFakeTimers();
    const events = await import('../src/lib/events');
    const resync = vi.fn();
    const changes: number[] = [];
    events.onResync(resync);
    events.onChange((e) => changes.push(e.seq ?? 0));
    events.startEvents();

    const s = sources[0];
    s.emit('hello');
    for (const seq of [1, 2, 3]) s.emit('change', { kind: 'Updated', id: 'x', seq });
    await Promise.resolve();
    expect(resync).not.toHaveBeenCalled();

    s.emit('change', { kind: 'Updated', id: 'x', seq: 6 }); // 4 and 5 were dropped
    await Promise.resolve();
    expect(resync).toHaveBeenCalledTimes(1);
    expect(changes).toEqual([1, 2, 3, 6]);

    s.onerror?.(); // connection lost, then back
    vi.advanceTimersByTime(1000);
    sources[1].emit('hello');
    await Promise.resolve();
    expect(resync).toHaveBeenCalledTimes(2);
    sources[1].emit('change', { kind: 'Updated', id: 'x', seq: 9 }); // numbering continues after the reload: no new gap
    await Promise.resolve();
    expect(resync).toHaveBeenCalledTimes(2);
    vi.useRealTimers();
  });

  it('tells content changes from notifications', async () => {
    const events = await import('../src/lib/events');
    expect(events.isContentChange({ kind: 'Updated' } as never)).toBe(true);
    expect(events.isContentChange({ kind: 'Notification' } as never)).toBe(false);
    expect(events.isContentChange({ kind: 'Approval' } as never)).toBe(false);
  });
});

describe('tasks store', () => {
  afterEach(() => { vi.resetModules(); vi.doUnmock('../src/lib/api'); vi.doUnmock('../src/lib/events'); vi.useRealTimers(); });

  it('refreshes only the changed task and its parent, and ignores note saves', async () => {
    const handlers: ((e: unknown) => void)[] = [];
    vi.doMock('../src/lib/events', () => ({ onChange: (h: (e: unknown) => void) => handlers.push(h), onResync: () => {} }));
    class ApiError extends Error { constructor(public status: number) { super('x'); } }
    const task = (id: string, parent: string | null = null, dependsOn: string[] = []) => ({ id, title: id, parent, dependsOn, status: 'todo' });
    let trashedC = false;
    const get = vi.fn(async (url: string) => {
      if (url === 'tasks?include=all') return [task('A'), task('B', 'A'), task('C', null, ['B'])];
      if (url === 'trash') return [];
      if (url === 'tasks/B') return { ...task('B', 'A'), status: 'done' };
      if (url === 'tasks/A') return task('A');
      if (url === 'tasks/C' && !trashedC) return task('C', null, ['B']);
      throw new ApiError(404);
    });
    vi.doMock('../src/lib/api', () => ({ api: { get, post: vi.fn() }, ApiError }));
    vi.useFakeTimers();
    const { tasks } = await import('../src/lib/tasks.svelte');
    await tasks.load();
    get.mockClear();

    const fire = (e: unknown) => handlers.forEach((h) => h(e));
    fire({ kind: 'Updated', id: 'NOTE', summary: { id: 'NOTE', type: 'note' } });
    await vi.advanceTimersByTimeAsync(400);
    expect(get).not.toHaveBeenCalled();

    fire({ kind: 'Updated', id: 'B', summary: { id: 'B', type: 'task' } });
    await vi.advanceTimersByTimeAsync(400);
    expect(get.mock.calls.map((c) => c[0]).sort()).toEqual(['tasks/A', 'tasks/B', 'tasks/C']);
    expect(tasks.get('B')?.status).toBe('done');
    expect(tasks.list).toHaveLength(3);

    get.mockClear();
    trashedC = true;
    fire({ kind: 'Trashed', id: 'C', data: { type: 'task', entryId: 'e1' } });
    await vi.advanceTimersByTimeAsync(400);
    expect(tasks.get('C')).toBeUndefined();
    expect(tasks.info('C')?.deleted).toBe(true);
    expect(get).not.toHaveBeenCalledWith('tasks?include=all');
  });
});
