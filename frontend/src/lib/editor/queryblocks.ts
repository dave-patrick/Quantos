import { syntaxTree } from '@codemirror/language';
import { RangeSetBuilder, StateField, type EditorState } from '@codemirror/state';
import { Decoration, EditorView, WidgetType, type DecorationSet } from '@codemirror/view';
import { mount, unmount } from 'svelte';
import QueryBlock from '../../components/QueryBlock.svelte';

/** A ```query fence shows live, editable results unless the cursor is inside it (BUILD-PLAN 8C.10). */
class QueryWidget extends WidgetType {
  constructor(readonly text: string, readonly from: number) { super(); }
  eq(other: QueryWidget) { return other.text === this.text && other.from === this.from; }
  toDOM(view: EditorView) {
    const host = document.createElement('div');
    host.className = 'cm-query-block';
    const component = mount(QueryBlock, {
      target: host,
      props: { text: this.text, onedit: () => { view.dispatch({ selection: { anchor: this.from + 9 } }); view.focus(); } }
    });
    (host as HTMLElement & { cmp?: unknown }).cmp = component;
    return host;
  }
  destroy(dom: HTMLElement) {
    const cmp = (dom as HTMLElement & { cmp?: Record<string, unknown> }).cmp;
    if (cmp) unmount(cmp);
  }
  ignoreEvent() { return true; }
  get estimatedHeight() { return 160; }
}

function build(state: EditorState): DecorationSet {
  const builder = new RangeSetBuilder<Decoration>();
  const sel = state.selection.main;
  syntaxTree(state).iterate({
    enter(node) {
      if (node.name !== 'FencedCode') return;
      const first = state.doc.lineAt(node.from);
      if (!/^\s*(```|~~~)\s*query\s*$/i.test(first.text)) return false;
      if (sel.from <= node.to && sel.to >= node.from) return false;
      const last = state.doc.lineAt(node.to);
      const body = state.doc.sliceString(first.to + 1, last.from).replace(/\n$/, '');
      builder.add(first.from, last.to, Decoration.replace({ widget: new QueryWidget(body, first.from), block: true }));
      return false;
    }
  });
  return builder.finish();
}

export const queryBlocks = StateField.define<DecorationSet>({
  create: build,
  update(value, tr) { return tr.docChanged || tr.selection ? build(tr.state) : value; },
  provide: (f) => EditorView.decorations.from(f)
});
