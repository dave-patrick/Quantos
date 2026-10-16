import { autocompletion, closeBrackets, closeBracketsKeymap, completionKeymap } from '@codemirror/autocomplete';
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands';
import { markdown, markdownLanguage } from '@codemirror/lang-markdown';
import { bracketMatching, foldGutter, foldKeymap, foldService, indentOnInput, indentUnit } from '@codemirror/language';
import { languages } from '@codemirror/language-data';
import { highlightSelectionMatches, search, searchKeymap } from '@codemirror/search';
import { Compartment, EditorState, type Extension } from '@codemirror/state';
import {
  crosshairCursor, drawSelection, dropCursor, EditorView, highlightSpecialChars, keymap, placeholder as placeholderExt,
  rectangularSelection
} from '@codemirror/view';
import { quantosHighlight, quantosTheme } from './theme';
import { linkCompletion, wikiLinks, type LinkHooks } from './wikilinks';
import { taskLines, type TaskHooks } from './tasks';
import { writingExtras } from './writing';

export interface EditorOptions {
  text: string;
  hooks: LinkHooks & TaskHooks & { onChange(text: string): void; onSave?(): void; onFocus?(): void };
  readOnly?: boolean;
  raw?: boolean;
  placeholder?: string;
  extra?: Extension[];
}

/** Folding for nested list items (headings fold through the Markdown language itself). */
const listFolding = foldService.of((state, lineStart) => {
  const line = state.doc.lineAt(lineStart);
  const indent = /^(\s*)([-*+]|\d+[.)])\s/.exec(line.text);
  if (!indent) return null;
  const depth = indent[1].length;
  let end = line.to;
  for (let n = line.number + 1; n <= state.doc.lines; n++) {
    const next = state.doc.line(n);
    if (next.text.trim() === '') continue;
    const d = /^(\s*)/.exec(next.text)![1].length;
    if (d <= depth) break;
    end = next.to;
  }
  return end > line.to ? { from: line.to, to: end } : null;
});

export const typewriter = new Compartment();
export const editable = new Compartment();

export function createEditor(parent: HTMLElement, options: EditorOptions): EditorView {
  const { hooks } = options;
  const extensions: Extension[] = [
    highlightSpecialChars(),
    history(),
    drawSelection(),
    dropCursor(),
    EditorState.allowMultipleSelections.of(true),
    rectangularSelection(),          // Alt+drag: column selection
    crosshairCursor(),
    indentOnInput(),
    indentUnit.of('  '),
    bracketMatching(),
    closeBrackets(),
    highlightSelectionMatches(),
    search({ top: true }),
    foldGutter({ openText: '▾', closedText: '▸' }),
    listFolding,
    EditorView.lineWrapping,
    markdown({ base: markdownLanguage, codeLanguages: languages, addKeymap: true }),
    quantosTheme,
    quantosHighlight,
    keymap.of([
      { key: 'Mod-s', preventDefault: true, run: () => { hooks.onSave?.(); return true; } },
      ...closeBracketsKeymap, ...defaultKeymap, ...searchKeymap, ...historyKeymap, ...foldKeymap, ...completionKeymap, indentWithTab
    ]),
    editable.of([EditorState.readOnly.of(!!options.readOnly), EditorView.editable.of(!options.readOnly)]),
    typewriter.of([]),
    EditorView.updateListener.of((u) => {
      if (u.docChanged) hooks.onChange(u.state.doc.toString());
      if (u.focusChanged && u.view.hasFocus) hooks.onFocus?.();
    })
  ];
  if (!options.raw) {
    extensions.push(wikiLinks(hooks), taskLines(hooks), writingExtras(), autocompletion({ override: [linkCompletion(hooks)], icons: false }));
  }
  if (options.placeholder) extensions.push(placeholderExt(options.placeholder));
  if (options.extra) extensions.push(...options.extra);

  return new EditorView({ parent, state: EditorState.create({ doc: options.text, extensions }) });
}

/** Applies an outside change (merge, server adjustment) as a minimal edit so the cursor stays put. */
export function syncText(view: EditorView, text: string) {
  const current = view.state.doc.toString();
  if (current === text) return;
  let start = 0;
  const max = Math.min(current.length, text.length);
  while (start < max && current.charCodeAt(start) === text.charCodeAt(start)) start++;
  let endA = current.length, endB = text.length;
  while (endA > start && endB > start && current.charCodeAt(endA - 1) === text.charCodeAt(endB - 1)) { endA--; endB--; }
  view.dispatch({ changes: { from: start, to: endA, insert: text.slice(start, endB) }, userEvent: 'external' });
}
