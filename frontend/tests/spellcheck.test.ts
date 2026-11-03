// @vitest-environment happy-dom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { EditorState } from '@codemirror/state';
import { EditorView } from '@codemirror/view';
import { markdown } from '@codemirror/lang-markdown';
import { spellCheckExtension, toggleSpellCheck } from '../src/lib/editor/spellcheck';

let view: EditorView | undefined;
afterEach(() => { view?.destroy(); document.body.replaceChildren(); });
function open(text: string) {
  view = new EditorView({ parent: document.body, state: EditorState.create({ doc: text, extensions: [markdown(), ...spellCheckExtension()] }) });
  view.dispatch({ effects: toggleSpellCheck.of(true) });
  return view;
}
const errors = () => [...document.querySelectorAll('.cm-spell-error')].map((el) => el.textContent);

describe('offline spell checking', () => {
  it('recognises everyday English and inflections while finding a typo', async () => {
    open('hello meeting project notes computer meetings running colour color organised organized can’t Quantos qzxwvvv');
    await vi.waitFor(() => expect(errors()).toEqual(['qzxwvvv']), { timeout: 10000 });
  });
  it('checks prose in lists and quotes, leaving code and URL destinations alone', async () => {
    open('- qzxwvvv\n> qzxwvvv\n`qzxwvvv` [meeting](https://qzxwvvv.test)\nhttps://qzxwvvv.test qzxwvvv_identifier 1234\n```\nqzxwvvv\n```');
    await vi.waitFor(() => expect(errors()).toEqual(['qzxwvvv', 'qzxwvvv']), { timeout: 10000 });
  });
  it('removes marks when switched off', () => {
    open('qzxwvvv');
    view!.dispatch({ effects: toggleSpellCheck.of(false) });
    expect(errors()).toEqual([]);
  });
});
