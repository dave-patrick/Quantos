// @vitest-environment happy-dom
import { expect, it, vi } from 'vitest';
const mock = vi.hoisted(() => ({ get: vi.fn(), app: { workspace: 'default' } }));
vi.mock('../src/lib/api', () => ({ api: { get: mock.get } }));
vi.mock('../src/lib/state.svelte', () => ({ app: mock.app }));
vi.mock('../src/lib/events', () => ({ onChange: vi.fn() }));
import { supertags } from '../src/lib/supertags.svelte';
it('keeps scopes separate and ignores an older response for the same workspace', async () => {
  const replies: ((v: unknown) => void)[] = [];
  mock.get.mockImplementation(() => new Promise(resolve => replies.push(resolve)));
  const first = supertags.load('default');
  const second = supertags.load('default');
  const video = supertags.load('videos');
  replies[1]([{ id: 'new', name: 'review' }]); await second;
  replies[0]([{ id: 'old', name: 'review' }]); await first;
  replies[2]([{ id: 'video', name: 'review' }]); await video;
  expect(supertags.byName('review')?.id).toBe('new');
  expect(supertags.byName('review', 'videos')?.id).toBe('video');
  mock.app.workspace = 'videos';
  expect(supertags.list[0].id).toBe('video');
  expect(mock.get).toHaveBeenCalledWith('supertags?workspace=videos');
});
