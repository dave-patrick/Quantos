import { syntaxTree } from '@codemirror/language';
import { RangeSetBuilder, type Extension } from '@codemirror/state';
import { Decoration, EditorView, ViewPlugin, WidgetType, type DecorationSet, type ViewUpdate } from '@codemirror/view';
import type { CompletionContext, CompletionResult } from '@codemirror/autocomplete';

export interface LinkHooks {
  titleOf(id: string): string | undefined;
  openLink(target: string, event: MouseEvent): void;
  hoverLink?(target: string, element: HTMLElement | null): void;
  searchTitles(query: string): { id: string; title: string; type: string; workspace: string }[];
}

const LINK = /(!?)\[\[([^\[\]|#\n]+?)(#[^\[\]|\n]+)?(?:\|([^\[\]\n]+))?\]\]/g;
const ULID = /^[0-9A-HJKMNP-TV-Z]{26}$/;
const hidden = Decoration.replace({});

class TitleWidget extends WidgetType {
  constructor(readonly text: string, readonly target: string, readonly embed: boolean, readonly cls = 'cm-wikilink') { super(); }
  eq(other: TitleWidget) { return other.text === this.text && other.target === this.target && other.cls === this.cls; }
  toDOM() {
    const span = document.createElement('span');
    span.className = this.cls + (this.embed ? ' cm-embed-link' : '');
    span.dataset.target = this.target;
    span.textContent = this.text;
    return span;
  }
  ignoreEvent() { return false; }
}

function inCode(view: EditorView, pos: number) {
  const node = syntaxTree(view.state).resolveInner(pos, 1);
  for (let n: typeof node | null = node; n; n = n.parent) {
    if (/Code|CodeBlock|FencedCode|InlineCode|CodeText/.test(n.name)) return true;
  }
  return false;
}

function build(view: EditorView, hooks: LinkHooks) {
  const builder = new RangeSetBuilder<Decoration>();
  const sel = view.state.selection;
  for (const { from, to } of view.visibleRanges) {
    const text = view.state.doc.sliceString(from, to);
    LINK.lastIndex = 0;
    for (let m; (m = LINK.exec(text)); ) {
      const start = from + m.index;
      const end = start + m[0].length;
      if (inCode(view, start)) continue;
      const target = m[2].trim();
      const resolved = ULID.test(target);
      const display = m[4] ?? (resolved ? hooks.titleOf(target) ?? 'Untitled' : target);
      const touching = sel.ranges.some((r) => r.from <= end && r.to >= start);
      const cls = resolved ? (hooks.titleOf(target) ? 'cm-wikilink' : 'cm-wikilink cm-wikilink-missing') : 'cm-wikilink cm-wikilink-unresolved';
      if (touching) {
        builder.add(start, end, Decoration.mark({ class: 'cm-wikilink-raw', attributes: { 'data-target': target } }));
        continue;
      }
      builder.add(start, end, Decoration.replace({ widget: new TitleWidget((m[1] ? '↳ ' : '') + display + (m[3] && !m[3].startsWith('#^') ? ' › ' + m[3].slice(1) : ''), target, !!m[1], cls) }));
    }
  }
  return builder.finish();
}

export function wikiLinks(hooks: LinkHooks): Extension {
  const plugin = ViewPlugin.fromClass(
    class {
      decorations: DecorationSet;
      constructor(view: EditorView) { this.decorations = build(view, hooks); }
      update(u: ViewUpdate) {
        if (u.docChanged || u.viewportChanged || u.transactions.some((t) => t.reconfigured)) this.decorations = build(u.view, hooks);
        // Only rebuild when the selection moves to a different line: every cursor move within a
        // line is not worth re-running the whole scan (mirrors tasks.ts).
        if (u.selectionSet && !u.docChanged && u.startState.doc.lineAt(u.startState.selection.main.head).number !== u.state.doc.lineAt(u.state.selection.main.head).number) this.decorations = build(u.view, hooks);
      }
    },
    {
      decorations: (v) => v.decorations,
      provide: (p) => EditorView.atomicRanges.of((view) => view.plugin(p)?.decorations ?? Decoration.none)
    }
  );

  const clicks = EditorView.domEventHandlers({
    mousedown(event, view) {
      const el = (event.target as HTMLElement).closest('.cm-wikilink, .cm-wikilink-raw') as HTMLElement | null;
      if (!el?.dataset.target) return false;
      const raw = el.classList.contains('cm-wikilink-raw');
      if (raw && !(event.ctrlKey || event.metaKey)) return false; // editing the raw link
      event.preventDefault();
      hooks.openLink(el.dataset.target, event);
      void view;
      return true;
    },
    mouseover(event) {
      const el = (event.target as HTMLElement).closest('.cm-wikilink') as HTMLElement | null;
      if (el?.dataset.target) hooks.hoverLink?.(el.dataset.target, el);
      return false;
    },
    mouseout(event) {
      const el = (event.target as HTMLElement).closest('.cm-wikilink') as HTMLElement | null;
      if (el) hooks.hoverLink?.('', null);
      return false;
    }
  });

  return [plugin, clicks];
}

/** Completion for [[ links: picks a node and inserts [[id|Title]]. */
export function linkCompletion(hooks: LinkHooks) {
  return (context: CompletionContext): CompletionResult | null => {
    const before = context.matchBefore(/!?\[\[[^\[\]\n|]*$/);
    if (!before) return null;
    const open = before.text.indexOf('[[');
    const query = before.text.slice(open + 2);
    const from = before.from;
    const after = context.state.sliceDoc(context.pos, context.pos + 2);
    const to = after === ']]' ? context.pos + 2 : context.pos;
    const bang = before.text.startsWith('!') ? '!' : '';
    const matches = hooks.searchTitles(query).slice(0, 30);
    const options = matches.map((n) => ({
      label: n.title,
      detail: n.type === 'note' ? n.workspace : `${n.type} · ${n.workspace}`,
      apply: (view: EditorView) => {
        const insert = `${bang}[[${n.id}|${n.title}]]`;
        view.dispatch({ changes: { from, to, insert }, selection: { anchor: from + insert.length } });
      }
    }));
    if (query.trim() && !matches.some((m) => m.title.toLowerCase() === query.trim().toLowerCase())) {
      options.push({
        label: `Link to new note “${query.trim()}”`,
        detail: 'created when opened',
        apply: (view: EditorView) => {
          const insert = `${bang}[[${query.trim()}]]`;
          view.dispatch({ changes: { from, to, insert }, selection: { anchor: from + insert.length } });
        }
      });
    }
    return { from, options, filter: false };
  };
}
