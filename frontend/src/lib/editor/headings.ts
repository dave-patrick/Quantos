import { foldEffect, foldService, foldedRanges, unfoldEffect } from '@codemirror/language';
import { Prec, StateEffect, type EditorState, type Extension, type Range } from '@codemirror/state';
import { Decoration, EditorView, ViewPlugin, WidgetType, type DecorationSet, type ViewUpdate } from '@codemirror/view';
import { fencedLines } from './blockinfo';

/** Sent when the heading folding is turned on or off, so the fold gutter looks again. */
export const headingsChanged = StateEffect.define<null>();

/** A heading on a line: how deep it is and where its text sits. */
export interface Heading {
  /** 1 for "#", 6 for "######". */
  level: number;
  /** Where the heading's text starts. */
  from: number;
  /** Where the heading's text ends. */
  to: number;
}

const HEADING = /^(#{1,6})(.*)$/;

/**
 * The heading on a line, if the line is one. A "#" inside a code block is code, not a heading.
 */
export function headingOn(state: EditorState, lineNumber: number): Heading | null {
  const line = state.doc.line(lineNumber);
  const m = HEADING.exec(line.text);
  if (!m) return null;
  if (fencedLines(state.doc).has(lineNumber)) return null;
  const gap = /^\s*/.exec(m[2])![0].length;
  const from = line.from + m[1].length + gap;
  return { level: m[1].length, from, to: from + m[2].slice(gap).length };
}

/**
 * The section a heading holds: the lines under it, up to the next heading of the same or a
 * deeper level. A heading with nothing under it has no section to fold.
 */
export function sectionOf(state: EditorState, lineNumber: number): { from: number; to: number } | null {
  const head = headingOn(state, lineNumber);
  if (!head) return null;
  const line = state.doc.line(lineNumber);
  let last = 0;
  for (let n = lineNumber + 1; n <= state.doc.lines; n++) {
    const next = state.doc.line(n);
    if (next.text.trim() === '') continue;
    const other = headingOn(state, n);
    if (other && other.level <= head.level) break;
    last = next.to;
  }
  if (!last) return null;
  return { from: line.to, to: last };
}

/**
 * Folding for headings: a heading folds away everything under it, down to the next heading of
 * the same or a deeper level. It is put ahead of the Markdown language's own folding so that
 * this is the one that decides.
 */
export const headingFolding: Extension = Prec.highest(
  foldService.of((state, lineStart) => {
    const line = state.doc.lineAt(lineStart);
    if (line.from !== lineStart) return null;
    return sectionOf(state, line.number);
  })
);

/** The little arrow on a heading that folds and unfolds its section. */
class HeadingToggle extends WidgetType {
  constructor(readonly level: number, readonly open: boolean, readonly range: { from: number; to: number }) {
    super();
  }

  eq(other: HeadingToggle) {
    return other.level === this.level && other.open === this.open && other.range.from === this.range.from && other.range.to === this.range.to;
  }

  toDOM(view: EditorView) {
    const el = document.createElement('span');
    el.className = 'cm-heading-toggle';
    el.textContent = this.open ? '▾' : '▸';
    el.title = this.open ? 'Collapse this section' : 'Expand this section';
    el.addEventListener('mousedown', (e) => {
      e.preventDefault();
      e.stopPropagation();
      toggleSection(view, this.range, this.open);
    });
    return el;
  }

  ignoreEvent() { return true; }
}

/** Folds the section, or unfolds it again when it is already folded. */
function toggleSection(view: EditorView, range: { from: number; to: number }, open: boolean) {
  if (open) {
    view.dispatch({ effects: foldEffect.of(range) });
    return;
  }
  let target: { from: number; to: number } | null = null;
  foldedRanges(view.state).between(range.from, range.to, (from, to) => {
    if (from <= range.from && to >= range.to) target = { from, to };
  });
  if (target) view.dispatch({ effects: unfoldEffect.of(target) });
}

/** True when the section is folded away. */
function isFolded(state: EditorState, range: { from: number; to: number }): boolean {
  let folded = false;
  foldedRanges(state).between(range.from, range.to, (from, to) => {
    if (from <= range.from && to >= range.to) folded = true;
  });
  return folded;
}

/** The arrows on the headings that are on screen. */
function arrows(view: EditorView): DecorationSet {
  const state = view.state;
  const marks: Range<Decoration>[] = [];
  for (const { from, to } of view.visibleRanges) {
    const first = state.doc.lineAt(from).number;
    const last = state.doc.lineAt(to).number;
    for (let n = first; n <= last; n++) {
      const head = headingOn(state, n);
      if (!head) continue;
      const section = sectionOf(state, n);
      if (!section) continue;
      const open = !isFolded(state, section);
      marks.push(Decoration.widget({ widget: new HeadingToggle(head.level, open, section), side: -1 }).range(state.doc.line(n).from));
    }
  }
  return Decoration.set(marks, true);
}

class HeadingArrows {
  decorations: DecorationSet;

  constructor(view: EditorView) {
    this.decorations = arrows(view);
  }

  update(update: ViewUpdate) {
    const folded = update.transactions.some((tr) => tr.effects.some((e) => e.is(foldEffect) || e.is(unfoldEffect)));
    if (update.docChanged || update.viewportChanged || update.selectionSet || folded) {
      this.decorations = arrows(update.view);
    }
  }
}

/** The arrows on headings, so a section can be folded without hunting for the gutter. */
export const headingArrows: Extension = ViewPlugin.fromClass(HeadingArrows, { decorations: (v) => v.decorations });
