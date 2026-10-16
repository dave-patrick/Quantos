import { type Extension } from '@codemirror/state';
import { Decoration, EditorView, ViewPlugin, type DecorationSet, type ViewUpdate } from '@codemirror/view';
import { RangeSetBuilder } from '@codemirror/state';

/** Collapsible callouts ("> [!note]") get a tinted block. */
const CALLOUT = /^>\s*\[!(\w+)\][+-]?/;

function build(view: EditorView) {
  const builder = new RangeSetBuilder<Decoration>();
  for (const { from, to } of view.visibleRanges) {
    let inCallout = false;
    for (let pos = from; pos <= to; ) {
      const line = view.state.doc.lineAt(pos);
      if (CALLOUT.test(line.text)) inCallout = true;
      else if (!line.text.startsWith('>')) inCallout = false;
      if (inCallout) builder.add(line.from, line.from, Decoration.line({ class: 'cm-callout' }));
      // The [!type] marker reads as a label, not a link.
      const head = CALLOUT.exec(line.text);
      if (head) {
        const start = line.from + line.text.indexOf('[!');
        builder.add(start, line.from + head[0].length, Decoration.mark({ class: 'cm-callout-label' }));
      }
      pos = line.to + 1;
    }
  }
  return builder.finish();
}

export function writingExtras(): Extension {
  return ViewPlugin.fromClass(
    class {
      decorations: DecorationSet;
      constructor(view: EditorView) { this.decorations = build(view); }
      update(u: ViewUpdate) { if (u.docChanged || u.viewportChanged) this.decorations = build(u.view); }
    },
    { decorations: (v) => v.decorations }
  );
}

/** Typewriter scrolling: keep the cursor line vertically centred. */
export const typewriterScrolling: Extension = [
  EditorView.updateListener.of((u) => {
    if (!u.selectionSet && !u.docChanged) return;
    const head = u.state.selection.main.head;
    requestAnimationFrame(() => u.view.dispatch({ effects: EditorView.scrollIntoView(head, { y: 'center' }) }));
  }),
  EditorView.editorAttributes.of({ class: 'cm-typewriter' })
];
