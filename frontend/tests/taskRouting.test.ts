// @vitest-environment happy-dom
import { beforeEach, describe, expect, it, vi } from 'vitest';
const mocks = vi.hoisted(() => ({ android: false, open: vi.fn(), popup: vi.fn(), close: vi.fn(), state: { id: null as string | null, busy: false, error: '' } }));
vi.mock('../src/lib/api', () => ({ api: {} }));
vi.mock('../src/lib/layout.svelte', () => ({ layout: { open: mocks.open } }));
vi.mock('../src/lib/state.svelte', () => ({ app: { titleIndex: new Map([['task', { type: 'task', title: 'Task' }], ['note', { type: 'note', title: 'Note' }]]) } }));
vi.mock('../src/lib/tasks.svelte', () => ({ tasks: { get: () => undefined } }));
vi.mock('../src/lib/platform', () => ({ get isAndroidClient() { return mocks.android; } }));
vi.mock('../src/lib/taskPopup.svelte', () => ({ taskPopup: mocks.state, openTaskPopup: mocks.popup, closeTaskPopup: mocks.close }));
import { openNode, taskHooks } from '../src/lib/links.svelte';

beforeEach(() => { vi.clearAllMocks(); mocks.android = false; mocks.state.id = null; mocks.state.busy = false; mocks.close.mockResolvedValue(true); });
describe('task popup platform routing', () => {
  it('opens tasks in a popup on Windows and browsers without replacing the current page', async () => {
    await openNode('task');
    expect(mocks.popup).toHaveBeenCalledWith('task');
    expect(mocks.open).not.toHaveBeenCalled();
  });
  it('keeps Android task and inline task navigation as pages', async () => {
    mocks.android = true;
    await openNode('task');
    taskHooks.openTask('task', new MouseEvent('click'));
    expect(mocks.open).toHaveBeenCalledTimes(2);
    expect(mocks.popup).not.toHaveBeenCalled();
  });
  it('retains explicit tab and pane requests', async () => {
    await openNode('task', 'new'); await openNode('task', 'other');
    expect(mocks.open.mock.calls.map((call) => call[1])).toEqual(['new', 'other']);
    expect(mocks.popup).not.toHaveBeenCalled();
  });
  it('navigates linked notes inside an active popup while preserving the workspace', async () => {
    mocks.state.id = 'task';
    await openNode('note');
    expect(mocks.popup).toHaveBeenCalledWith('note');
    expect(mocks.open).not.toHaveBeenCalled();
  });
  it('refuses explicit navigation if the popup cannot save and close', async () => {
    mocks.state.id = 'task'; mocks.close.mockResolvedValue(false);
    await openNode('task', 'other');
    expect(mocks.close).toHaveBeenCalled();
    expect(mocks.open).not.toHaveBeenCalled();
  });
});
