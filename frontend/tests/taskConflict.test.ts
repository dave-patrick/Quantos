// @vitest-environment happy-dom
import { beforeEach, describe, expect, it, vi } from 'vitest';
vi.mock('../src/lib/api', () => ({ api: { post: vi.fn().mockResolvedValue({}) }, ApiError: class extends Error {} }));
vi.mock('../src/lib/events', () => ({ connection: { listeners: new Set() }, onChange: vi.fn() }));
vi.mock('../src/lib/layout.svelte', () => ({ layout: { retitle: vi.fn() }, setTabFlusher: vi.fn() }));
vi.mock('../src/lib/state.svelte', () => ({ app: { error: vi.fn(), toast: vi.fn() } }));
import { Doc } from '../src/lib/docs.svelte';
import { writeGate, writeKey } from '../src/lib/writeGate';
import type { NodeDto } from '../src/lib/types';

beforeEach(() => localStorage.clear());
describe('closing a resolved task conflict', () => {
  for (const raw of [false, true]) it(`keeps the disk decision after reopening (${raw ? 'source' : 'body'})`, async () => {
    const doc = new Doc('conflict-' + raw);
    const disk = { id: doc.id, title: 'Task', hash: 'disk', body: 'disk body', content: 'disk content' } as NodeDto;
    doc.node = disk;
    doc.base = 'original';
    doc.body = 'mine';
    doc.raw = raw;
    doc.rawText = 'mine';
    doc.conflict = { disk, mine: 'mine', reason: 'Changed outside' };
    doc.status = 'conflict';
    doc.writeDraft();
    expect(localStorage.getItem('quantos-draft:' + doc.id)).not.toBeNull();
    const before = writeGate.checkpoint();
    const key = writeKey(raw ? 'PUT' : 'PATCH', `nodes/${doc.id}`, raw ? { content: 'mine', baseHash: 'old' } : { body: 'mine', baseHash: 'old' });
    await expect(writeGate.run(key, async () => { throw new Error('409'); })).rejects.toThrow();
    await doc.resolve('disk');
    expect(doc.conflict).toBeNull();
    expect(doc.dirty).toBe(false);
    expect(doc.status).toBe('saved');
    expect(writeGate.failedSince(before)).toBe(false);
    expect(localStorage.getItem('quantos-draft:' + doc.id)).toBeNull();
  });
});
