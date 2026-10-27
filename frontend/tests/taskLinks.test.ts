// @vitest-environment happy-dom
import { describe, expect, it, vi } from 'vitest';
import { EditorState } from '@codemirror/state';
import { EditorView } from '@codemirror/view';
import { taskLines } from '../src/lib/editor/tasks';
const id = '01M5AZYBPFP1F42NEQCKXCPQG6';
function editor() {
  const openTask = vi.fn(), toggleTask = vi.fn();
  const host = document.createElement('div'); document.body.append(host);
  const view = new EditorView({ parent: host, state: EditorState.create({ doc: `- [ ] Call the bank <!--task:${id}-->`, extensions: [taskLines({ openTask, toggleTask })] }) });
  return { host, view, openTask, toggleTask };
}
describe('task links in notes', () => {
  it('opens the full task from its title without moving the note selection', () => {
    const { host, view, openTask, toggleTask } = editor();
    const selection = view.state.selection.main.anchor;
    host.querySelector('.cm-task-title')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
    expect(openTask).toHaveBeenCalledWith(id, expect.any(MouseEvent));
    expect(view.state.selection.main.anchor).toBe(selection);
    expect(toggleTask).not.toHaveBeenCalled();
    view.destroy(); host.remove();
  });
  it('supports keyboard activation of the task button', () => {
    const { host, view, openTask } = editor();
    const chip = host.querySelector('button.cm-task-chip');
    expect(chip).not.toBeNull();
    chip!.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, detail: 0 }));
    expect(openTask).toHaveBeenCalledWith(id, expect.any(MouseEvent));
    view.destroy(); host.remove();
  });
  it('still toggles the checkbox independently', () => {
    const { host, view, openTask, toggleTask } = editor();
    host.querySelector('.cm-task-check')!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true }));
    expect(toggleTask).toHaveBeenCalledWith(id, true);
    expect(openTask).not.toHaveBeenCalled();
    expect(view.state.doc.toString()).toContain('[x]');
    view.destroy(); host.remove();
  });
});
