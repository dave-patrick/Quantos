import { StateEffect, StateField, RangeSetBuilder, type Extension } from '@codemirror/state';
import { Decoration, type DecorationSet, ViewPlugin, type ViewUpdate } from '@codemirror/view';
import type { EditorView } from '@codemirror/view';
import { syntaxTree } from '@codemirror/language';
import { app } from '../state.svelte';

export const toggleSpellCheck = StateEffect.define<boolean>();
const dictionaryReady = StateEffect.define<null>();
const enabled = StateField.define<boolean>({
  create: () => false,
  update(value, tr) {
    for (const effect of tr.effects) if (effect.is(toggleSpellCheck)) return effect.value;
    return value;
  }
});

// Load once, only when enabled. Both dictionaries ship in the application; no note text leaves it.
let dictionary: typeof import('../spellingDictionary') | undefined;
let loading: Promise<void> | undefined;
function load() {
  return loading ??= import('../spellingDictionary').then((module) => { dictionary = module; }).catch((error) => {
    loading = undefined;
    app.error(new Error(`Spell check could not load: ${error instanceof Error ? error.message : String(error)}`));
  });
}

const words = /[\p{L}\p{N}_]+(?:['’][\p{L}]+)*/gu;
const excluded = new Set(['FencedCode', 'CodeBlock', 'InlineCode', 'URL', 'Autolink', 'HTMLBlock', 'HTMLTag', 'Comment']);
function build(view: EditorView): DecorationSet {
  if (!dictionary || !view.state.field(enabled)) return Decoration.none;
  const result = new RangeSetBuilder<Decoration>();
  const tree = syntaxTree(view.state);
  let lastEnd = -1;
  for (const { from, to } of view.visibleRanges) {
    for (let pos = from; pos <= to && pos < view.state.doc.length;) {
      const line = view.state.doc.lineAt(pos);
      const urls = [...line.text.matchAll(/\b(?:https?:\/\/|www\.)[^\s<>]+/gi)];
      words.lastIndex = 0;
      for (const match of line.text.matchAll(words)) {
        const start = line.from + match.index!, end = start + match[0].length;
        if (start < lastEnd || start < from || start >= to) continue;
        lastEnd = end;
        // This feature checks English prose, not numbers, identifiers or other scripts.
        if (!/^[A-Za-z]+(?:['’][A-Za-z]+)*$/.test(match[0])) continue;
        if (urls.some((url) => match.index! >= url.index! && match.index! < url.index! + url[0].length)) continue;
        let node = tree.resolveInner(start, 1);
        let skip = false;
        for (;;) {
          if (excluded.has(node.name)) { skip = true; break; }
          if (!node.parent) break;
          node = node.parent;
        }
        if (!skip && !dictionary.correct(match[0])) result.add(start, end, Decoration.mark({ class: 'cm-spell-error' }));
      }
      pos = line.to + 1;
    }
  }
  return result.finish();
}

const checker = ViewPlugin.fromClass(class {
  decorations: DecorationSet;
  private alive = true;
  private waiting = false;
  constructor(private view: EditorView) { this.decorations = build(view); this.ensureDictionary(); }
  private ensureDictionary() {
    if (dictionary || this.waiting || !this.view.state.field(enabled)) return;
    this.waiting = true;
    void load().then(() => {
      this.waiting = false;
      if (this.alive && dictionary) this.view.dispatch({ effects: dictionaryReady.of(null) });
    });
  }
  update(update: ViewUpdate) {
    this.ensureDictionary();
    if (update.docChanged || update.viewportChanged || update.startState.field(enabled) !== update.state.field(enabled)
      || update.transactions.some((tr) => tr.effects.some((effect) => effect.is(dictionaryReady)))
      || syntaxTree(update.startState) !== syntaxTree(update.state)) this.decorations = build(update.view);
  }
  destroy() { this.alive = false; }
}, { decorations: (plugin) => plugin.decorations });

export function spellCheckExtension(): Extension[] { return [enabled, checker]; }
