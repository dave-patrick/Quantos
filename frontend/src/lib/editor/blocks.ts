import { foldEffect, foldService, syntaxTree } from '@codemirror/language';
import type { StateEffect } from '@codemirror/state';
import { RangeSetBuilder, StateField, type EditorState, type Extension } from '@codemirror/state';
import { Decoration, EditorView, hoverTooltip, ViewPlugin, WidgetType, type DecorationSet } from '@codemirror/view';
import { api } from '../api';
import { app } from '../state.svelte';
import { renderMarkdown } from '../render';
import { openNode } from '../links.svelte';
import type { NodeDto } from '../types';

const ULID = '[0-9A-HJKMNP-TV-Z]{26}';
const EMBED_LINE = new RegExp(`^\\s*!\\[\\[(${ULID})(#[^\\]|]+)?(?:\\|[^\\]]+)?\\]\\]\\s*$`);

/** Embedded notes, blocks and headings (BUILD-PLAN 8C.12, 8A.14 document cards). Read-only; click to open. */
class EmbedWidget extends WidgetType {
  constructor(readonly id: string, readonly fragment: string | null, readonly from: number) { super(); }
  eq(o: EmbedWidget) { return o.id === this.id && o.fragment === this.fragment && o.from === this.from; }
  toDOM(view: EditorView) {
    const box = document.createElement('div');
    box.className = 'cm-embed';
    const head = document.createElement('div');
    head.className = 'cm-embed-head';
    const title = app.titleIndex.get(this.id)?.title ?? 'Embedded note';
    head.textContent = '↳ ' + title + (this.fragment && !this.fragment.startsWith('^') ? ' › ' + this.fragment : '');
    head.onclick = (e) => openNode(this.id, e.shiftKey ? 'other' : e.ctrlKey || e.metaKey ? 'new' : 'current');
    const edit = document.createElement('button');
    edit.className = 'cm-embed-edit';
    edit.textContent = 'Edit link';
    edit.onclick = (e) => { e.stopPropagation(); view.dispatch({ selection: { anchor: this.from + 3 } }); view.focus(); };
    head.appendChild(edit);
    const body = document.createElement('div');
    body.className = 'cm-embed-body';
    body.textContent = 'Loading…';
    box.append(head, body);
    const load = this.fragment
      ? api.get<{ text: string }>(`nodes/${this.id}/fragment?f=${encodeURIComponent(this.fragment)}`).then((r) => r.text)
      : api.get<NodeDto>(`nodes/${this.id}`).then((n) => (n.type === 'synced_block' ? n.body : n.body.split('\n').slice(0, 40).join('\n')));
    load.then((text) => { body.innerHTML = renderMarkdown(text); })
      .catch((err) => { body.textContent = err?.message ?? 'Not available'; box.classList.add('missing'); });
    return box;
  }
  ignoreEvent() { return true; }
  get estimatedHeight() { return 120; }
}

/** :::columns … +++ … ::: renders side by side (BUILD-PLAN 8A.4). */
class ColumnsWidget extends WidgetType {
  constructor(readonly parts: string[], readonly from: number) { super(); }
  eq(o: ColumnsWidget) { return o.parts.join('\u0000') === this.parts.join('\u0000') && o.from === this.from; }
  toDOM(view: EditorView) {
    const box = document.createElement('div');
    box.className = 'cm-columns';
    box.style.gridTemplateColumns = `repeat(${this.parts.length}, 1fr)`;
    for (const p of this.parts) {
      const col = document.createElement('div');
      col.className = 'cm-column';
      col.innerHTML = renderMarkdown(p);
      box.appendChild(col);
    }
    box.title = 'Click to edit the columns';
    box.onclick = () => { view.dispatch({ selection: { anchor: this.from + 3 } }); view.focus(); };
    return box;
  }
  ignoreEvent() { return false; }
}

class SyncLabel extends WidgetType {
  constructor(readonly start: boolean) { super(); }
  eq(o: SyncLabel) { return o.start === this.start; }
  toDOM() {
    const s = document.createElement('span');
    s.className = 'cm-sync-label';
    s.textContent = this.start ? '⟲ Synced block: edits here update every copy' : '';
    return s;
  }
}

function build(state: EditorState): DecorationSet {
  const builder = new RangeSetBuilder<Decoration>();
  const sel = state.selection.main;
  const touching = (from: number, to: number) => sel.from <= to && sel.to >= from;
  let fence = false;
  let inSync = false;
  for (let n = 1; n <= state.doc.lines; n++) {
    const line = state.doc.line(n);
    if (/^\s*(```|~~~)/.test(line.text)) { fence = !fence; continue; }
    if (fence) continue;

    if (/^<!--sync:[0-9A-HJKMNP-TV-Z]{26}-->\s*$/.test(line.text)) {
      inSync = true;
      if (!touching(line.from, line.to)) builder.add(line.from, line.to, Decoration.replace({ widget: new SyncLabel(true) }));
      builder.add(line.from, line.from, Decoration.line({ class: 'cm-sync-start' }));
      continue;
    }
    if (/^<!--\/sync-->\s*$/.test(line.text)) {
      inSync = false;
      builder.add(line.from, line.from, Decoration.line({ class: 'cm-sync-end' }));
      if (!touching(line.from, line.to)) builder.add(line.from, line.to, Decoration.replace({ widget: new SyncLabel(false) }));
      continue;
    }
    if (inSync) builder.add(line.from, line.from, Decoration.line({ class: 'cm-sync-line' }));

    if (/^:::\s*columns\s*$/i.test(line.text)) {
      let end = n + 1;
      while (end <= state.doc.lines && !/^:::\s*$/.test(state.doc.line(end).text)) end++;
      if (end <= state.doc.lines) {
        const last = state.doc.line(end);
        if (!touching(line.from, last.to)) {
          const inner = state.doc.sliceString(line.to + 1, last.from);
          builder.add(line.from, last.to, Decoration.replace({ widget: new ColumnsWidget(inner.split(/^\+\+\+\s*$/m).map((s) => s.trim()), line.from), block: true }));
          n = end;
          continue;
        }
      }
    }

    const m = EMBED_LINE.exec(line.text);
    if (m && !touching(line.from, line.to)) {
      builder.add(line.from, line.to, Decoration.replace({ widget: new EmbedWidget(m[1], m[2]?.slice(1) ?? null, line.from), block: true }));
    }
  }
  return builder.finish();
}

export const embedBlocks = StateField.define<DecorationSet>({
  create: build,
  update(value, tr) { return tr.docChanged || tr.selection ? build(tr.state) : value; },
  provide: (f) => EditorView.decorations.from(f)
});

/** Collapsible callouts: "> [!note]-" starts folded, "+" or nothing starts open (BUILD-PLAN 8A.3). */
const calloutFold = foldService.of((state, lineStart) => {
  const line = state.doc.lineAt(lineStart);
  if (!/^>\s*\[!\w+\][+-]?/.test(line.text)) return null;
  let end = line.to;
  for (let n = line.number + 1; n <= state.doc.lines; n++) {
    const next = state.doc.line(n);
    if (!next.text.startsWith('>')) break;
    end = next.to;
  }
  return end > line.to ? { from: line.to, to: end } : null;
});

const foldCollapsedCallouts = ViewPlugin.define((view) => {
  const effects: StateEffect<unknown>[] = [];
  for (let n = 1; n <= view.state.doc.lines; n++) {
    const line = view.state.doc.line(n);
    if (!/^>\s*\[!\w+\]-/.test(line.text)) continue;
    let end = line.to;
    for (let k = n + 1; k <= view.state.doc.lines && view.state.doc.line(k).text.startsWith('>'); k++) end = view.state.doc.line(k).to;
    if (end > line.to) effects.push(foldEffect.of({ from: line.to, to: end }));
  }
  if (effects.length) queueMicrotask(() => view.dispatch({ effects }));
  return {};
});

/** Footnote previews on hover (BUILD-PLAN 8A.10). */
const footnotes = hoverTooltip((view, pos) => {
  const line = view.state.doc.lineAt(pos);
  const re = /\[\^([^\]\s]+)\]/g;
  for (let m; (m = re.exec(line.text)); ) {
    const from = line.from + m.index, to = from + m[0].length;
    if (pos < from || pos > to || line.text.slice(m.index + m[0].length).startsWith(':')) continue;
    const doc = view.state.doc.toString();
    const def = new RegExp(`^\\[\\^${m[1].replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\]:\\s*(.*(?:\\n(?: {2,}|\\t).*)*)`, 'm').exec(doc);
    return {
      pos: from, end: to, above: true,
      create() {
        const dom = document.createElement('div');
        dom.className = 'cm-footnote-tip';
        dom.innerHTML = def ? renderMarkdown(def[1]) : '<em>No footnote text yet.</em>';
        return { dom };
      }
    };
  }
  void syntaxTree;
  return null;
});

export const blockExtras: Extension = [embedBlocks, calloutFold, foldCollapsedCallouts, footnotes];

export const blockTheme = EditorView.theme({
  '.cm-embed': { border: '1px solid var(--line-strong)', borderLeft: '3px solid var(--accent)', borderRadius: '8px', background: 'var(--bg-raised)', margin: '6px 0', padding: '6px 12px 8px', fontSize: '0.92em' },
  '.cm-embed.missing': { borderLeftColor: 'var(--danger)' },
  '.cm-embed-head': { display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontWeight: '700', color: 'var(--accent)', cursor: 'pointer', fontSize: '13px' },
  '.cm-embed-edit': { border: '0', background: 'none', color: 'var(--text-faint)', font: '11px var(--font-mono)', cursor: 'pointer' },
  '.cm-embed-body': { color: 'var(--text-soft)', maxHeight: '420px', overflow: 'auto' },
  '.cm-embed-body p, .cm-column p': { margin: '4px 0' },
  '.cm-columns': { display: 'grid', gap: '18px', border: '1px dashed var(--line-strong)', borderRadius: '8px', padding: '8px 12px', margin: '6px 0', cursor: 'text' },
  '.cm-column': { minWidth: '0' },
  '.cm-sync-line, .cm-sync-start, .cm-sync-end': { background: 'color-mix(in srgb, var(--ok) 7%, transparent)', borderLeft: '3px solid color-mix(in srgb, var(--ok) 60%, transparent)', paddingLeft: '10px !important' },
  '.cm-sync-label': { font: '600 11px var(--font-mono)', color: 'var(--ok)', letterSpacing: '.04em' },
  '.cm-footnote-tip': { maxWidth: '380px', padding: '6px 10px', fontSize: '13px', color: 'var(--text-soft)' }
});
