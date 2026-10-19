import { syntaxTree } from '@codemirror/language';
import { StateField, type EditorState, type Range } from '@codemirror/state';
import { Decoration, EditorView, WidgetType, type DecorationSet } from '@codemirror/view';
import { drawDiagram, drawMath, findMath } from '../diagrams';

/**
 * Math and ```mermaid fences show rendered unless the cursor is inside them (Release 1.1).
 * Clicking a rendered one puts the cursor in its source.
 */
class MathWidget extends WidgetType {
  constructor(readonly tex: string, readonly display: boolean, readonly from: number) { super(); }
  eq(o: MathWidget) { return o.tex === this.tex && o.display === this.display && o.from === this.from; }
  toDOM(view: EditorView) {
    const el = document.createElement(this.display ? 'div' : 'span');
    el.className = 'cm-math';
    el.title = 'Click to edit';
    drawMath(el, this.tex, this.display);
    el.onmousedown = (e) => { e.preventDefault(); view.dispatch({ selection: { anchor: this.from + (this.display ? 2 : 1) } }); view.focus(); };
    return el;
  }
  ignoreEvent() { return true; }
}

class DiagramWidget extends WidgetType {
  constructor(readonly source: string, readonly from: number) { super(); }
  eq(o: DiagramWidget) { return o.source === this.source && o.from === this.from; }
  toDOM(view: EditorView) {
    const el = document.createElement('div');
    el.className = 'cm-diagram';
    el.title = 'Click to edit';
    drawDiagram(el, this.source);
    el.onmousedown = (e) => { e.preventDefault(); view.dispatch({ selection: { anchor: view.state.doc.lineAt(this.from).to } }); view.focus(); };
    return el;
  }
  ignoreEvent() { return true; }
  get estimatedHeight() { return 200; }
}

function build(state: EditorState): DecorationSet {
  const decos: Range<Decoration>[] = [];
  const sel = state.selection.ranges;
  const touched = (from: number, to: number) => sel.some((r) => r.from <= to && r.to >= from);
  const doc = state.doc;

  syntaxTree(state).iterate({
    enter(node) {
      if (node.name !== 'FencedCode') return;
      const first = doc.lineAt(node.from);
      if (!/^\s*(```|~~~)\s*mermaid\s*$/i.test(first.text)) return false;
      const last = doc.lineAt(node.to);
      if (last.number === first.number || touched(first.from, last.to)) return false;
      const source = doc.sliceString(first.to + 1, last.from).replace(/\n$/, '');
      decos.push(Decoration.replace({ widget: new DiagramWidget(source, first.from), block: true }).range(first.from, last.to));
      return false;
    }
  });

  const text = doc.toString();
  if (text.includes('$')) {
    for (const m of findMath(text)) {
      if (touched(m.from, m.to)) continue;
      const a = doc.lineAt(m.from), b = doc.lineAt(m.to);
      // $$ … $$ on lines of its own becomes a block; otherwise it sits in the line.
      const ownLines = m.display && a.from === m.from && b.to === m.to;
      decos.push(Decoration.replace({ widget: new MathWidget(m.tex, m.display, m.from), block: ownLines }).range(m.from, m.to));
    }
  }
  return Decoration.set(decos, true);
}

export const richBlocks = StateField.define<DecorationSet>({
  create: build,
  update(value, tr) { return tr.docChanged || tr.selection ? build(tr.state) : value; },
  provide: (f) => EditorView.decorations.from(f)
});

export const richTheme = EditorView.theme({
  '.cm-math': { cursor: 'pointer' },
  'div.cm-math': { padding: '6px 0', textAlign: 'center', whiteSpace: 'normal' },
  '.cm-diagram': { cursor: 'pointer', whiteSpace: 'normal', padding: '8px 0' }
});
