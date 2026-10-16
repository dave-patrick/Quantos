import { autocompletion, closeBrackets, closeBracketsKeymap, completionKeymap } from '@codemirror/autocomplete';
import { defaultKeymap, history, historyKeymap, indentWithTab } from '@codemirror/commands';
import { markdown, markdownLanguage } from '@codemirror/lang-markdown';
import { bracketMatching, foldGutter, foldKeymap, foldService, indentOnInput, indentUnit } from '@codemirror/language';
import { languages } from '@codemirror/language-data';
import { highlightSelectionMatches, search, searchKeymap } from '@codemirror/search';
import { Compartment, EditorState, Prec, type Extension } from '@codemirror/state';
import {
  crosshairCursor, drawSelection, dropCursor, EditorView, highlightSpecialChars, keymap, placeholder as placeholderExt,
  rectangularSelection
} from '@codemirror/view';
import { quantosHighlight, quantosTheme } from './theme';
import { linkCompletion, wikiLinks, type LinkHooks } from './wikilinks';
import { taskLines, type TaskHooks } from './tasks';
import { writingExtras } from './writing';
import { queryBlocks } from './queryblocks';
import { blockExtras, blockTheme } from './blocks';
import { runCode, runCodeTheme } from './runcode';
import { attachmentDrop, inlineMedia, mediaTheme } from './attachments';

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

/**
 * Enter on an empty list item, task item or quote line ends it (an indented item steps out one level),
 * as in most editors. CodeMirror's own Markdown Enter would first insert a blank line and keep the list.
 */
function exitEmptyItem(view: EditorView): boolean {
  const { state } = view;
  const sel = state.selection.main;
  if (state.selection.ranges.length > 1 || !sel.empty) return false;
  const line = state.doc.lineAt(sel.head);
  const m = /^(\s*)(?:(?:[-*+]|\d+[.)])\s+(?:\[[ xX]\]\s*)?|>\s*)$/.exec(line.text);
  if (!m || sel.head !== line.to) return false;
  const indent = m[1];
  if (indent.length && !line.text.trimStart().startsWith('>')) {
    const drop = indent.endsWith('\t') ? 1 : Math.min(2, indent.length);
    view.dispatch({ changes: { from: line.from, to: line.from + drop }, userEvent: 'input' });
  } else {
    view.dispatch({ changes: { from: line.from, to: line.to }, selection: { anchor: line.from }, userEvent: 'input' });
  }
  return true;
}

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
    Prec.high(keymap.of([{ key: 'Enter', run: exitEmptyItem }])),
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
    extensions.push(wikiLinks(hooks), taskLines(hooks), writingExtras(), queryBlocks, blockExtras, blockTheme, runCode, runCodeTheme, attachmentDrop, inlineMedia, mediaTheme, autocompletion({ override: [linkCompletion(hooks)], icons: false }));
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
