import { describe, expect, it } from 'vitest';
import { WriteGate, writeKey } from '../src/lib/writeGate';

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

describe('popup write guard', () => {
  it('waits for submitted writes and returns their result unchanged', async () => {
    const gate = new WriteGate();
    const request = deferred<number>();
    const write = gate.run('subtask', () => request.promise);
    let finished = false;
    const waiting = gate.wait().then(() => { finished = true; });
    await Promise.resolve();
    expect(finished).toBe(false);
    request.resolve(42);
    expect(await write).toBe(42);
    await waiting;
    expect(finished).toBe(true);
  });
  it('keeps a failed edit blocked until that edit is retried, independently of other writes', async () => {
    const gate = new WriteGate();
    const before = gate.checkpoint();
    await expect(gate.run('title', async () => { throw new Error('offline'); })).rejects.toThrow('offline');
    await gate.run('body', async () => true);
    await gate.wait();
    expect(gate.failedSince(before)).toBe(true);
    await gate.run('title', async () => true);
    expect(gate.failedSince(before)).toBe(false);
  });
  it('never lets an older successful request clear a newer failure', async () => {
    const gate = new WriteGate();
    const older = deferred<boolean>();
    const write = gate.run('priority', () => older.promise);
    const before = gate.checkpoint();
    await expect(gate.run('priority', async () => { throw new Error('denied'); })).rejects.toThrow();
    older.resolve(true);
    await write;
    expect(gate.failedSince(before)).toBe(true);
  });
  it('excludes older errors from a new popup and supports explicit cancellation', async () => {
    const gate = new WriteGate();
    await expect(gate.run('title', async () => { throw new Error('offline'); })).rejects.toThrow();
    expect(gate.failedSince(gate.checkpoint())).toBe(false);
    gate.dismiss('title');
    expect(gate.failedSince(0)).toBe(false);
  });
  it('does not revive an older failure after a newer retry succeeds', async () => {
    const gate = new WriteGate();
    const older = deferred<boolean>();
    const write = gate.run('priority', () => older.promise);
    const rejected = expect(write).rejects.toThrow('old failure');
    await gate.run('priority', async () => true);
    older.reject(new Error('old failure'));
    await rejected;
    expect(gate.failedSince(0)).toBe(false);
  });
  it('separates metadata fields while matching retries with different values or hashes', () => {
    const key = writeKey('PATCH', 'nodes/a', { baseHash: 'old', set: { priority: 1 } });
    expect(key).toBe(writeKey('PATCH', 'nodes/a', { set: { priority: 2 }, baseHash: 'new' }));
    expect(key).not.toBe(writeKey('PATCH', 'nodes/a', { set: { due: '2026-10-27' } }));
    expect(key).not.toContain('old');
    expect(key).toBe(writeKey('PATCH', 'nodes/a', { remove: ['priority'] }));
  });
});
