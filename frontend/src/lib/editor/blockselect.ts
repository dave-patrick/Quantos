import { EditorSelection, StateEffect, StateField, type EditorState, type Extension, type Range, type Text } from '@codemirror/state';
import { Decoration, EditorView, type DecorationSet } from '@codemirror/view';
import { blockAt, type BlockInfo } from './blockinfo';

/** A run of whole lines, the way a selection covers them. */
export interface LineRun {
  /** Where the first line starts. */
  from: number;
  /** Where the last line ends. */
  to: number;
  /** The number of the first line. */
  fromLine: number;
  /** The number of the last line. */
  toLine: number;
}

/**
 * The run of whole lines a selection range covers, when it covers whole lines. A selection that
 * starts or ends in the middle of a line is not a block selection.
 */
export function runOf(state: EditorState, range: { from: number; to: number }): LineRun | null {
  if (range.from >= range.to) return null;
  const first = state.doc.lineAt(range.from);
  if (first.from !== range.from) return null;
  let last = state.doc.lineAt(range.to);
  if (last.from === range.to) last = state.doc.line(last.number - 1);
  if (last.to !== range.to) return null;
  return { from: first.from, to: last.to, fromLine: first.number, toLine: last.number };
}

/** The run of lines a block covers. */
export function runOfBlock(state: EditorState, block: BlockInfo): LineRun {
  const first = state.doc.lineAt(block.lineFrom);
  const last = state.doc.lineAt(block.lineTo);
  return { from: first.from, to: last.to, fromLine: first.number, toLine: last.number };
}

/** The blocks a selection covers: one for each run of whole lines. */
export function selectionBlocks(state: EditorState): BlockInfo[] {
  const out: BlockInfo[] = [];
  for (const range of state.selection.ranges) {
    const run = runOf(state, range);
    if (!run) continue;
    for (let n = run.fromLine; n <= run.toLine; n++) {
      const block = blockAt(state, state.doc.line(n).from);
      const last = out[out.length - 1];
      if (last && last.kind === 'code' && block.kind === 'code' && last.lineFrom === block.lineFrom) continue;
      out.push(block);
    }
  }
  return out;
}

/** Whether a position is on one of the lines the selection covers. */
export function inSelection(state: EditorState, pos: number): boolean {
  const line = state.doc.lineAt(pos).number;
  for (const range of state.selection.ranges) {
    const run = runOf(state, range);
    if (run && run.fromLine <= line && line <= run.toLine) return true;
  }
  return false;
}

/** The band that shows which blocks are selected. */
function highlight(state: EditorState): DecorationSet {
  const marks: Range<Decoration>[] = [];
  for (const range of state.selection.ranges) {
    const run = runOf(state, range);
    if (!run) continue;
    for (let n = run.fromLine; n <= run.toLine; n++) {
      marks.push(Decoration.line({ class: 'cm-block-selected' }).range(state.doc.line(n).from));
    }
  }
  return Decoration.set(marks, true);
}

/** The band that shows the selected blocks. */
export const blockSelection: Extension = StateField.define<DecorationSet>({
  create: () => Decoration.none,
  update(value, tr) {
    if (tr.docChanged || tr.selection) return highlight(tr.state);
    return value;
  },
  provide: (f) => EditorView.decorations.from(f)
});

/** Where the block being dragged would land. */
const setDropLine = StateEffect.define<number | null>();

/** The line that shows where a dragged block will go. */
export const dropLine: Extension = StateField.define<number | null>({
  create: () => null,
  update(value, tr) {
    for (const e of tr.effects) if (e.is(setDropLine)) return e.value;
    if (tr.docChanged) return null;
    return value;
  },
  provide: (f) => EditorView.decorations.from(f, (v) => (v === null ? Decoration.none : Decoration.set([Decoration.line({ class: 'cm-drop-line' }).range(v)])))
});

/** The modifier that adds to a selection: Ctrl on Windows and Linux, Cmd on a Mac. */
function addModifier(event: MouseEvent): boolean {
  const mac = typeof navigator !== 'undefined' && /Mac|iP(hone|ad|od)/.test(navigator.userAgent);
  return mac ? event.metaKey : event.ctrlKey;
}

/** True when the press landed on ordinary text rather than on a link, a button or a widget. */
function onText(event: MouseEvent): boolean {
  const el = event.target as HTMLElement | null;
  return !!el && el.classList.contains('cm-line');
}

/** Adds a block to the selection, or takes it out again. */
function toggleBlock(view: EditorView, pos: number): void {
  const state = view.state;
  const block = blockAt(state, pos);
  const run = runOfBlock(state, block);
  const runs: LineRun[] = [];
  for (const range of state.selection.ranges) {
    const other = runOf(state, range);
    if (other) runs.push(other);
  }
  const hit = runs.findIndex((r) => !(r.toLine < run.fromLine || run.toLine < r.fromLine));
  const next = hit >= 0 ? runs.filter((_, i) => i !== hit) : [...runs, run];
  next.sort((a, b) => a.fromLine - b.fromLine);
  if (!next.length) {
    view.dispatch({ selection: { anchor: pos } });
    return;
  }
  const ranges = next.map((r) => EditorSelection.range(r.from, r.to));
  const main = next.findIndex((r) => r.fromLine <= run.fromLine && run.toLine <= r.toLine);
  view.dispatch({ selection: EditorSelection.create(ranges, main < 0 ? 0 : main) });
}

/** Extends the block selection from where it started to the block that was clicked. */
function extendBlock(view: EditorView, pos: number): void {
  const state = view.state;
  const block = blockAt(state, pos);
  const clicked = runOfBlock(state, block);
  const main = state.selection.main;
  const anchor = state.doc.lineAt(main.anchor).number;
  const runs: LineRun[] = [];
  for (const range of state.selection.ranges) {
    if (range === main) continue;
    const other = runOf(state, range);
    if (other) runs.push(other);
  }
  const from = Math.min(anchor, clicked.fromLine);
  const to = Math.max(anchor, clicked.toLine);
  const first = state.doc.line(from);
  const last = state.doc.line(to);
  runs.push({ from: first.from, to: last.to, fromLine: from, toLine: to });
  runs.sort((a, b) => a.fromLine - b.fromLine);
  const ranges = runs.map((r) => EditorSelection.range(r.from, r.to));
  view.dispatch({ selection: EditorSelection.create(ranges, ranges.length - 1) });
}

/** Where a block would land if it were dropped at these coordinates. */
function targetAt(view: EditorView, x: number, y: number): number | null {
  const pos = view.posAtCoords({ x, y });
  if (pos === null) return null;
  const state = view.state;
  const line = state.doc.lineAt(pos);
  const coords = view.coordsAtPos(line.from);
  const below = coords ? y > (coords.top + coords.bottom) / 2 : false;
  if (!below) return line.from;
  if (line.number === state.doc.lines) return state.doc.length;
  return line.to + 1;
}

/** The part of the document between two places, with the given ranges taken out of it. */
function without(doc: Text, from: number, to: number, cut: { from: number; to: number }[]): string {
  let out = '';
  let pos = from;
  for (const r of cut) {
    if (r.to <= from || r.from >= to) continue;
    out += doc.sliceString(pos, Math.max(pos, Math.min(r.from, to)));
    pos = Math.max(pos, r.to);
  }
  out += doc.sliceString(Math.max(pos, from), to);
  return out;
}

/**
 * Moves blocks to where they were dropped. The blocks keep their order and land together, even
 * when they were not next to each other to begin with.
 */
function moveBlocks(view: EditorView, blocks: BlockInfo[], target: number): void {
  const state = view.state;
  const doc = state.doc;
  const runs = blocks.map((b) => runOfBlock(state, b));
  const cut = runs.map((r) => ({ from: r.from, to: r.to < doc.length ? r.to + 1 : r.to }));
  if (cut.some((c) => target >= c.from && target <= c.to)) return;
  const text = blocks.map((b, i) => doc.sliceString(runs[i].from, runs[i].to)).join('\n');
  const before = without(doc, 0, target, cut);
  const after = without(doc, target, doc.length, cut);
  let insert = text;
  let start = 0;
  if (before && !before.endsWith('\n')) { insert = '\n' + insert; start = 1; }
  if (after && !insert.endsWith('\n')) insert += '\n';
  const changes = state.changes([...cut, { from: target, insert }]);
  const at = changes.mapPos(target, -1) + start;
  view.dispatch({
    changes,
    selection: { anchor: at, head: at + text.length },
    userEvent: 'move.drop'
  });
}

/**
 * Selecting blocks and moving them: Ctrl-click adds a block to the selection, Ctrl-Shift-click
 * stretches it, and dragging the selection moves the blocks to where they are dropped.
 */
export function blockSelectionAndDrag(): Extension {
  let drag: { blocks: BlockInfo[]; pos: number; x: number; y: number; active: boolean; target: number | null } | null = null;
  let move: ((e: MouseEvent) => void) | null = null;
  let up: ((e: MouseEvent) => void) | null = null;

  const stop = () => {
    if (move) document.removeEventListener('mousemove', move);
    if (up) document.removeEventListener('mouseup', up);
    window.removeEventListener('blur', stop);
    move = null;
    up = null;
    drag = null;
  };

  const onMove = (e: MouseEvent, v: EditorView) => {
    if (!drag) return;
    if (!drag.active) {
      if (Math.abs(e.clientX - drag.x) < 5 && Math.abs(e.clientY - drag.y) < 5) return;
      drag.active = true;
    }
    e.preventDefault();
    const target = targetAt(v, e.clientX, e.clientY);
    if (target !== drag.target) {
      drag.target = target;
      v.dispatch({ effects: setDropLine.of(target) });
    }
  };

  const onUp = (e: MouseEvent, v: EditorView) => {
    const current = drag;
    if (!current) return;
    if (current.active) {
      e.preventDefault();
      const target = current.target ?? targetAt(v, e.clientX, e.clientY);
      if (target !== null) moveBlocks(v, current.blocks, target);
    } else {
      // A plain click on a selected block puts the caret back where it was pressed.
      v.dispatch({ selection: { anchor: current.pos } });
    }
    v.dispatch({ effects: setDropLine.of(null) });
    stop();
  };

  return [
    EditorView.domEventHandlers({
      mousedown(event, v) {
        if (event.button !== 0) return false;
        if (!onText(event)) return false;
        if (drag) stop();
        const pos = v.posAtCoords({ x: event.clientX, y: event.clientY });
        if (pos === null) return false;

        if (addModifier(event) && event.shiftKey) {
          event.preventDefault();
          extendBlock(v, pos);
          return true;
        }
        if (addModifier(event)) {
          event.preventDefault();
          toggleBlock(v, pos);
          return true;
        }

        // A press on a selected block may turn into a drag that moves the blocks.
        const blocks = selectionBlocks(v.state);
        if (!blocks.length) return false;
        const line = v.state.doc.lineAt(pos).number;
        const on = blocks.some((b) => {
          const run = runOfBlock(v.state, b);
          return run.fromLine <= line && line <= run.toLine;
        });
        if (!on) return false;
        // The editor keeps its hands off this press: the blocks stay selected while they are
        // dragged, and a plain click puts the caret back where it was pressed.
        event.preventDefault();
        v.focus();
        drag = { blocks, pos, x: event.clientX, y: event.clientY, active: false, target: null };
        move = (e) => onMove(e, v);
        up = (e) => onUp(e, v);
        document.addEventListener('mousemove', move);
        document.addEventListener('mouseup', up);
        // A drag that ends outside the window still has to be let go of.
        window.addEventListener('blur', stop);
        return true;
      },
      // The browser's own drag would drag the words; the blocks move instead.
      dragstart(event) {
        if (!drag) return false;
        event.preventDefault();
        return true;
      }
    })
  ];
}
