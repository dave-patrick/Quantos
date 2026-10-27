// @vitest-environment happy-dom
import { afterEach, describe, expect, it } from 'vitest';
import { openTaskPopup, taskPopup } from '../src/lib/taskPopup.svelte';
afterEach(() => { taskPopup.id = null; taskPopup.returnTo = null; document.body.replaceChildren(); });
describe('return from a task popup', () => {
  it('restores the original note scroll after focus restoration scrolls it away', () => {
    const scroller = document.createElement('div');
    Object.defineProperties(scroller, { scrollHeight: { value: 4000 }, clientHeight: { value: 600 } });
    scroller.innerHTML = '<div class="cm-editor"><div class="cm-content" contenteditable="true"><span>Task</span></div></div>';
    document.body.append(scroller); scroller.scrollTop = 1800;
    openTaskPopup('first', scroller.querySelector('span'));
    scroller.scrollTop = 0;
    taskPopup.returnTo?.();
    expect(scroller.scrollTop).toBe(1800);
    expect(document.activeElement).toBe(scroller.querySelector('.cm-content'));
  });
  it('keeps the note as the return destination when opening a nested task', () => {
    const note = document.createElement('div'), popup = document.createElement('div');
    Object.defineProperties(note, { scrollHeight: { value: 4000 }, clientHeight: { value: 600 } });
    Object.defineProperties(popup, { scrollHeight: { value: 1200 }, clientHeight: { value: 600 } });
    document.body.append(note, popup); note.scrollTop = 1800;
    openTaskPopup('first', note); const original = taskPopup.returnTo;
    openTaskPopup('second', popup);
    expect(taskPopup.id).toBe('second'); expect(taskPopup.returnTo).toBe(original);
  });
});
