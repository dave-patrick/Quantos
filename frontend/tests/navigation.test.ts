// @vitest-environment happy-dom
import { beforeEach, describe, expect, it, vi } from 'vitest';
vi.mock('../src/lib/api', () => ({ api: { put: vi.fn().mockResolvedValue({}) } }));
vi.mock('../src/lib/state.svelte', () => ({ app: { workspace: 'default' } }));
import { layout, setTabFlusher } from '../src/lib/layout.svelte';
beforeEach(() => { layout.restore(null); setTabFlusher(async () => {}); });
describe('workspace navigation', () => {
  it('restores note position going back and forward', async () => {
    const a = await layout.open({ kind: 'node', nodeId: 'a' });
    a!.scroll = 720;
    const b = await layout.open({ kind: 'node', nodeId: 'b' });
    b!.scroll = 240;
    await layout.back();
    expect(layout.activeTab?.nodeId).toBe('a');
    expect(layout.activeTab?.scroll).toBe(720);
    await layout.forward();
    expect(layout.activeTab?.nodeId).toBe('b');
    expect(layout.activeTab?.scroll).toBe(240);
  });
  it('records switching to existing tabs and clears forward on a new destination', async () => {
    await layout.open({ kind: 'notes' });
    await layout.open({ kind: 'tasks' }, 'new');
    await layout.open({ kind: 'notes' });
    await layout.back();
    expect(layout.activeTab?.kind).toBe('tasks');
    await layout.open({ kind: 'settings' });
    expect(layout.pane.forward).toHaveLength(0);
  });
  it('retraces folders and returns to the same folder after opening a note', async () => {
    await layout.open({ kind: 'notes' });
    await layout.open({ kind: 'notes', arg: 'folder:Projects' });
    await layout.open({ kind: 'notes', arg: 'folder:Projects/Work' });
    await layout.open({ kind: 'node', nodeId: 'folder-note' });
    await layout.back();
    expect(layout.activeTab?.arg).toBe('folder:Projects/Work');
    await layout.back();
    expect(layout.activeTab?.arg).toBe('folder:Projects');
    await layout.forward();
    expect(layout.activeTab?.arg).toBe('folder:Projects/Work');
  });
  it('never replaces a pinned tab during history travel', async () => {
    const a = await layout.open({ kind: 'notes' });
    layout.togglePin(0, a!.key);
    const b = await layout.open({ kind: 'tasks' });
    layout.togglePin(0, b!.key);
    await layout.back();
    expect(layout.activeTab?.kind).toBe('notes');
    expect(layout.pane.tabs).toHaveLength(2);
    expect(layout.pane.tabs.every(t => t.pinned)).toBe(true);
  });
});
