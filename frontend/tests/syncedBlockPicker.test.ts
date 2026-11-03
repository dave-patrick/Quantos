// @vitest-environment happy-dom
import { afterEach, expect, it, vi } from 'vitest';
import { EditorState, type TransactionSpec } from '@codemirror/state';
import type { EditorView } from '@codemirror/view';

vi.mock('../src/lib/api', () => ({ api: { get: vi.fn() } }));
vi.mock('../src/lib/docs.svelte', () => ({ docs: {} }));
vi.mock('../src/lib/layout.svelte', () => ({ layout: {} }));
vi.mock('../src/lib/links.svelte', () => ({ openNode: vi.fn() }));
vi.mock('../src/lib/state.svelte', () => ({ app: { toast: vi.fn(), error: vi.fn(), dialog: null } }));
vi.mock('../src/lib/prefs.svelte', () => ({ prefs: {} }));
vi.mock('../src/lib/present.svelte', () => ({ presenter: {} }));
vi.mock('../src/lib/focus.svelte', () => ({ focus: {} }));
vi.mock('../src/lib/recorder.svelte', () => ({ recorder: {} }));
vi.mock('../src/lib/ai.svelte', () => ({ ai: {}, aiJob: {} }));

import { api } from '../src/lib/api';
import { activeEditor } from '../src/lib/activeEditor';
import { run } from '../src/lib/commands.svelte';
import { syncedBlockPicker, matchingBlocks } from '../src/lib/syncedBlockPicker.svelte';
import '../src/lib/editorCommands';

const blocks = Array.from({ length: 12 }, (_, i) => ({ id: `block-${i}`, title: `Block ${i}`, content: `Content ${i}`, uses: i }));
function editor(readOnly = false) {
  const dom = document.createElement('div'); document.body.append(dom);
  return { dom, state: EditorState.create({ doc: 'Original', extensions: EditorState.readOnly.of(readOnly) }),
    dispatch(spec: TransactionSpec) { this.state = this.state.update(spec).state; }, focus: vi.fn() } as unknown as EditorView;
}
afterEach(() => { syncedBlockPicker.request?.done(null); activeEditor.view = null; document.body.replaceChildren(); vi.clearAllMocks(); });

it('offers every block and inserts a later match into the original editor after palette focus moves', async () => {
  vi.mocked(api.get).mockResolvedValue(blocks);
  const original = editor(); activeEditor.view = original;
  const done = run('edit.synced.insert');
  await vi.waitFor(() => expect(syncedBlockPicker.request?.blocks).toHaveLength(12));
  const other = editor(); activeEditor.view = other;
  syncedBlockPicker.request!.done(blocks[10]); await done;
  expect(original.state.doc.toString()).toContain('<!--sync:block-10-->\nContent 10\n<!--/sync-->');
  expect(other.state.doc.toString()).toBe('Original');
  expect(original.focus).toHaveBeenCalled();
});
it('searches every title and content without truncating unfiltered results', () => {
  expect(matchingBlocks(blocks, '')).toHaveLength(12);
  expect(matchingBlocks(blocks, 'content 10')).toEqual([blocks[10]]);
});
it('cancels without writing and returns editor focus', async () => {
  vi.mocked(api.get).mockResolvedValue(blocks); const view = editor(); activeEditor.view = view;
  const done = run('edit.synced.insert'); await vi.waitFor(() => expect(syncedBlockPicker.request).not.toBeNull());
  syncedBlockPicker.request!.done(null); await done;
  expect(view.state.doc.toString()).toBe('Original'); expect(view.focus).toHaveBeenCalled();
});
it('does not fetch for a read-only editor', async () => {
  activeEditor.view = editor(true); await run('edit.synced.insert'); expect(api.get).not.toHaveBeenCalled();
});
it('does not write if the original editor is detached while choosing', async () => {
  vi.mocked(api.get).mockResolvedValue(blocks); const view = editor(); activeEditor.view = view;
  const done = run('edit.synced.insert'); await vi.waitFor(() => expect(syncedBlockPicker.request).not.toBeNull());
  view.dom.remove(); syncedBlockPicker.request!.done(blocks[10]); await done;
  expect(view.state.doc.toString()).toBe('Original');
});
it('checks read-only again when the choice is applied', async () => {
  vi.mocked(api.get).mockResolvedValue(blocks); const view = editor(); activeEditor.view = view;
  const done = run('edit.synced.insert'); await vi.waitFor(() => expect(syncedBlockPicker.request).not.toBeNull());
  Object.assign(view, { state: EditorState.create({ doc: 'Original', extensions: EditorState.readOnly.of(true) }) });
  syncedBlockPicker.request!.done(blocks[10]); await done;
  expect(view.state.doc.toString()).toBe('Original');
});
