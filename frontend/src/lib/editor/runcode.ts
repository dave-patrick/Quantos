import { RangeSetBuilder, StateField, type EditorState } from '@codemirror/state';
import { Decoration, EditorView, WidgetType, type DecorationSet } from '@codemirror/view';
import { bridge, isWindowsClient } from '../platform';

/**
 * PowerShell code blocks get a Run button in the Windows client. Code in notes never runs by
 * itself: the client shows exactly what will run, and where, before running it.
 */
class RunWidget extends WidgetType {
  constructor(readonly code: string) { super(); }
  eq(o: RunWidget) { return o.code === this.code; }
  toDOM() {
    const el = document.createElement('span');
    el.className = 'cm-run-code';
    if (isWindowsClient) {
      const b = document.createElement('button');
      b.textContent = '▶ Run';
      b.title = 'Run this PowerShell (you will be asked to confirm)';
      b.onclick = (e) => { e.preventDefault(); bridge.send({ type: 'run-code', code: this.code }); };
      el.appendChild(b);
    } else {
      el.textContent = 'Running code requires the Windows client';
    }
    return el;
  }
  ignoreEvent() { return true; }
}

function build(state: EditorState): DecorationSet {
  const builder = new RangeSetBuilder<Decoration>();
  for (let n = 1; n <= state.doc.lines; n++) {
    const line = state.doc.line(n);
    if (!/^\s*```\s*(powershell|pwsh|ps1|ps)\s*$/i.test(line.text)) continue;
    let end = n + 1;
    while (end <= state.doc.lines && !/^\s*```\s*$/.test(state.doc.line(end).text)) end++;
    if (end > state.doc.lines) break;
    const code = state.doc.sliceString(line.to + 1, state.doc.line(end).from).replace(/\n$/, '');
    if (code.trim()) builder.add(line.to, line.to, Decoration.widget({ widget: new RunWidget(code), side: 1 }));
    n = end;
  }
  return builder.finish();
}

export const runCode = StateField.define<DecorationSet>({
  create: build,
  update(v, tr) { return tr.docChanged ? build(tr.state) : v; },
  provide: (f) => EditorView.decorations.from(f)
});

export const runCodeTheme = EditorView.theme({
  '.cm-run-code': { marginLeft: '12px', font: '600 11px var(--font-mono)', color: 'var(--text-faint)' },
  '.cm-run-code button': { border: '1px solid var(--line-strong)', background: 'var(--bg-raised)', color: 'var(--ok)', borderRadius: '5px', padding: '1px 8px', cursor: 'pointer', font: 'inherit' }
});
