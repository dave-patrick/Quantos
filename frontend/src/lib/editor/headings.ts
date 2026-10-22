import { foldEffect, foldService } from '@codemirror/language';
import { Prec, StateEffect, type EditorState, type Extension, type Range } from '@codemirror/state';
import { Decoration, EditorView, ViewPlugin, type DecorationSet, type ViewUpdate } from '@codemirror/view';
import { fencedLines } from './blockinfo';

/** Sent when the heading folding is turned on or off, so the fold gutter looks again. */
export const headingsChanged = StateEffect.define<null>();

/** A heading on a line: how deep it is, where its text sits and the fold mark it carries. */
export interface Heading {
  /** 1 for "#", 6 for "######". */
  level: number;
  /** Where the heading's text starts. */
  from: number;
  /** Where the heading's text ends. */
  to: number;
  /** The fold mark: "-" means the section starts folded, "+" means it starts open. */
  mark?: '-' | '+';
  /** Where the mark is written, so the editor can style it. */
  markFrom?: number;
  markTo?: number;
}

/**
 * A heading, with the optional fold mark after the hashes: "## [-] Title" is a heading that
 * starts folded. The mark is written in the note, so the section is still folded next time.
 */
const HEADING = /^(#{1,6})(\s*)?(?:\[([+-])\])?(\s*)?(.*)$/;

/**
 * The heading on a line, if the line is one. A "#" inside a code block is code, not a heading.
 */
export function headingOn(state: EditorState, lineNumber: number): Heading | null {
  const line = state.doc.line(lineNumber);
  const m = HEADING.exec(line.text);
  if (!m) return null;
  if (fencedLines(state.doc).has(lineNumber)) return null;
  const level = m[1].length;
  let at = line.from + level + (m[2]?.length ?? 0);
  let markFrom = 0;
  let markTo = 0;
  if (m[3]) {
    markFrom = at;
    markTo = at + m[3].length + 2;
    at = markTo;
  }
  const from = at + (m[4]?.length ?? 0);
  const text = m[5] ?? '';
  return { level, from, to: from + text.length, mark: m[3] as '-' | '+' | undefined, markFrom, markTo };
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

/**
 * A heading marked "[-]" is folded when the note opens, and stays folded because the mark is in
 * the note. The same trick the collapsible callouts use.
 *
 * The editor is made before the note's text arrives, so the headings are looked for again
 * whenever the text changes. A heading is only folded once: unfolding it by hand sticks.
 */
export const foldMarkedHeadings = ViewPlugin.define((view) => {
  const folded = new Set<string>();
  const foldNew = () => {
    const effects: StateEffect<unknown>[] = [];
    for (let n = 1; n <= view.state.doc.lines; n++) {
      const head = headingOn(view.state, n);
      if (!head || head.mark !== '-') continue;
      const key = view.state.doc.sliceString(head.from, head.to);
      if (folded.has(key)) continue;
      const section = sectionOf(view.state, n);
      if (!section) continue;
      folded.add(key);
      effects.push(foldEffect.of({ from: section.from, to: section.to }));
    }
    if (effects.length) queueMicrotask(() => view.dispatch({ effects }));
  };
  queueMicrotask(foldNew);
  return {
    update(update: ViewUpdate) {
      if (update.docChanged) foldNew();
    }
  };
});

/**
 * The fold marks on the headings that are on screen: the "[-]" that says a section starts folded.
 * The arrow itself lives in the fold gutter, so a heading only ever has one.
 */
function marks(view: EditorView): DecorationSet {
  const state = view.state;
  const ranges: Range<Decoration>[] = [];
  for (const { from, to } of view.visibleRanges) {
    const first = state.doc.lineAt(from).number;
    const last = state.doc.lineAt(to).number;
    for (let n = first; n <= last; n++) {
      const head = headingOn(state, n);
      if (!head?.markFrom) continue;
      ranges.push(Decoration.mark({ class: 'cm-heading-mark' }).range(head.markFrom, head.markTo));
    }
  }
  return Decoration.set(ranges, true);
}

class HeadingMarks {
  decorations: DecorationSet;

  constructor(view: EditorView) {
    this.decorations = marks(view);
  }

  update(update: ViewUpdate) {
    if (update.docChanged || update.viewportChanged) {
      this.decorations = marks(update.view);
    }
  }
}

/** The fold marks on the headings, so a section can be told apart from the rest at a glance. */
export const headingArrows: Extension = ViewPlugin.fromClass(HeadingMarks, { decorations: (v) => v.decorations });

/**
 * The heading that covers a place: the heading on that line, or the nearest one above it. Used by
 * the command that folds the heading at the cursor.
 */
export function headingNear(state: EditorState, pos: number): { line: number; heading: Heading } | null {
  const number = state.doc.lineAt(pos).number;
  for (let n = number; n >= 1; n--) {
    const heading = headingOn(state, n);
    if (heading) return { line: n, heading };
  }
  return null;
}
