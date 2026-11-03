// @vitest-environment happy-dom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { EditorState, type TransactionSpec } from '@codemirror/state';
import type { EditorView } from '@codemirror/view';
import { CompletionContext } from '@codemirror/autocomplete';

vi.mock('../src/lib/api', () => ({ api: {} }));
vi.mock('../src/lib/docs.svelte', () => ({ docs: {} }));
vi.mock('../src/lib/layout.svelte', () => ({ layout: {} }));
vi.mock('../src/lib/links.svelte', () => ({ openNode: vi.fn() }));
vi.mock('../src/lib/state.svelte', () => ({ app: { toast: vi.fn() } }));
vi.mock('../src/lib/prefs.svelte', () => ({ prefs: {} }));
vi.mock('../src/lib/present.svelte', () => ({ presenter: {} }));
vi.mock('../src/lib/focus.svelte', () => ({ focus: {} }));
vi.mock('../src/lib/recorder.svelte', () => ({ recorder: {} }));

import { run } from '../src/lib/commands.svelte';
import { activeEditor } from '../src/lib/activeEditor';
import { app } from '../src/lib/state.svelte';
import { slashCompletion } from '../src/lib/editor/slash';

function editor(doc: string, from = 0, to = from, readOnly = false) {
  const dom = document.createElement('div');
  document.body.append(dom);
  const view = {
    state: EditorState.create({ doc, selection: { anchor: from, head: to }, extensions: [EditorState.readOnly.of(readOnly)] }),
    dom,
    dispatch(spec: TransactionSpec) { this.state = this.state.update(spec).state; },
    focus: vi.fn()
  };
  return view as unknown as EditorView;
}

beforeEach(() => {
  vi.useFakeTimers();
  vi.clearAllMocks();
  activeEditor.view = null;
});
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  document.body.replaceChildren();
  activeEditor.view = null;
});

describe('date and time slash commands', () => {
  it('uses the current local date, including after midnight with the app left open', async () => {
    vi.setSystemTime(new Date(2026, 9, 27, 23, 59));
    // Model Phoenix late evening, when UTC has already advanced to the next day.
    vi.spyOn(Date.prototype, 'toISOString').mockReturnValue('2026-10-28T06:59:00.000Z');
    vi.resetModules();
    const { slashCommands } = await import('../src/lib/editor/slash');
    const date = slashCommands.find(c => c.id === 'date')!;
    const first = editor('Before /date after', 7, 12);
    date.apply(first, 7, 12);
    expect(first.state.doc.toString()).toBe('Before 2026-10-27 after');
    expect(first.state.selection.main.head).toBe(17);
    vi.setSystemTime(new Date(2026, 9, 28, 0, 1));
    const next = editor('/date');
    date.apply(next, 0, 5);
    expect(next.state.doc.toString()).toBe('2026-10-28');
  });

  it('computes completion hints when opened and time again when applied', () => {
    vi.setSystemTime(new Date(2026, 9, 27, 12, 34));
    const completion = slashCompletion();
    const state = EditorState.create({ doc: '/' });
    const options = completion(new CompletionContext(state, 1, true))!.options;
    expect(options.find(o => o.label === "Today's date")!.detail).toBe('2026-10-27');
    const time = options.find(o => o.label === 'Current time')!;
    expect(time.detail).toBe('12:34');
    vi.setSystemTime(new Date(2026, 9, 27, 12, 35));
    const view = editor('/');
    if (typeof time.apply !== 'function') throw new Error('Expected a time insertion action');
    time.apply(view, time, 0, 1);
    expect(view.state.doc.toString()).toBe('12:35');
    expect(view.focus).toHaveBeenCalledOnce();
  });
});

describe("Insert today's date command", () => {
  it('replaces the active selection, restores focus and leaves the clipboard alone', () => {
    vi.setSystemTime(new Date(2026, 9, 28, 10));
    const clipboard = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue();
    const view = editor('Before replace after', 7, 14);
    activeEditor.view = view;
    run('daily.date');
    expect(view.state.doc.toString()).toBe('Before 2026-10-28 after');
    expect(view.state.selection.main.empty).toBe(true);
    expect(view.state.selection.main.head).toBe(17);
    expect(view.focus).toHaveBeenCalledOnce();
    expect(clipboard).not.toHaveBeenCalled();
  });

  it('inserts at a collapsed caret', () => {
    vi.setSystemTime(new Date(2026, 9, 28, 10));
    const view = editor('ab', 1);
    activeEditor.view = view;
    run('daily.date');
    expect(view.state.doc.toString()).toBe('a2026-10-28b');
  });

  it.each(['missing', 'detached', 'read-only'])('explains why insertion is unavailable (%s)', (reason) => {
    const view = editor('Keep this text', 0, 4, reason === 'read-only');
    if (reason === 'detached') view.dom.remove();
    activeEditor.view = reason === 'missing' ? null : view;
    run('daily.date');
    expect(view.state.doc.toString()).toBe('Keep this text');
    expect(app.toast).toHaveBeenCalledWith(expect.stringMatching(reason === 'read-only' ? /read-only/i : /note/i), 'info');
    expect(view.focus).not.toHaveBeenCalled();
  });
});
