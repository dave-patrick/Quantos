import { syntaxTree } from '@codemirror/language';
import type { EditorState } from '@codemirror/state';
import type { EditorView } from '@codemirror/view';

/** What a block is: a task, a heading, a list item, a quote, a code block or plain text. */
export type BlockKind = 'task' | 'heading' | 'list' | 'quote' | 'code' | 'text';

export interface BlockInfo {
  kind: BlockKind;
  /** Where the block's text starts (after the marker). */
  from: number;
  /** Where the block's text ends. */
  to: number;
  /** The line the block starts on. */
  lineFrom: number;
  /** The line the block ends on (the closing fence for a code block). */
  lineTo: number;
  /** The text of the block, without its marker. */
  text: string;
  /** The marker in front of it: "- ", "## ", "> ". */
  marker: string;
  /** The task this line is bound to, when it is one. */
  taskId?: string;
  /** Whether the task's box is ticked. */
  checked?: boolean;
  /** The language of a code block. */
  language?: string;
}

const TASK = /^(\s*)([-*+]|\d+[.)])\s+\[([ xX])\](\s+|$)(.*?)(\s*<!--task:([0-9A-HJKMNP-TV-Z]{26})-->)?\s*$/;
const HEADING = /^(\s*)(#{1,6})\s+(.*)$/;
const LIST = /^(\s*)([-*+]|\d+[.)])\s+(.*)$/;
const QUOTE = /^(\s*)(>.*)$/;
const FENCE = /^\s*(```+|~~~+)\s*(\S+)?/;

/**
 * The code block a position is in, found by scanning the fences. The Markdown parser is used when
 * it is there; the scan keeps this working in plain text too.
 */
function fenceAt(state: EditorState, pos: number): { from: number; to: number; language: string } | null {
  const trees: { from: number; to: number }[] = [];
  syntaxTree(state).iterate({
    enter(node) {
      if (node.name === 'FencedCode' && node.from <= pos && pos <= node.to) trees.push({ from: node.from, to: node.to });
      return false;
    }
  });
  if (trees[0]) {
    const first = state.doc.lineAt(trees[0].from);
    const language = FENCE.exec(first.text)?.[2] ?? '';
    return { from: trees[0].from, to: trees[0].to, language };
  }

  // No parser here: find the fence by hand.
  let open: { line: number; language: string } | null = null;
  for (let n = 1; n <= state.doc.lines; n++) {
    const m = FENCE.exec(state.doc.line(n).text);
    if (!m) continue;
    if (open === null) {
      open = { line: n, language: m[2] ?? '' };
      continue;
    }
    const from = state.doc.line(open.line).from;
    const to = state.doc.line(n).to;
    if (pos >= from && pos <= to) return { from, to, language: open.language };
    open = null;
  }
  return null;
}

/** The block at a position: the line it is on, or the whole code block it sits in. */
export function blockAt(state: EditorState, pos: number): BlockInfo {
  const line = state.doc.lineAt(pos);

  // A fenced code block: the whole fence is the block, whatever line the pointer is on.
  const fence = fenceAt(state, pos);
  if (fence) {
    const first = state.doc.lineAt(fence.from);
    const last = state.doc.lineAt(fence.to);
    return {
      kind: 'code',
      from: first.to + 1,
      to: Math.max(first.to + 1, last.from - 1),
      lineFrom: first.from,
      lineTo: last.to,
      text: state.doc.sliceString(first.to + 1, Math.max(first.to + 1, last.from - 1)),
      marker: first.text.slice(0, first.text.length - first.text.trimStart().length) + '```',
      language: fence.language
    };
  }

  const task = TASK.exec(line.text);
  if (task) {
    const markerLength = task[1].length + task[2].length + 1 + 3 + task[4].length;
    return {
      kind: 'task',
      from: line.from + markerLength,
      to: line.to,
      lineFrom: line.from,
      lineTo: line.to,
      text: task[5] ?? '',
      marker: line.text.slice(0, markerLength),
      taskId: task[7] ?? undefined,
      checked: task[3] !== ' '
    };
  }

  const heading = HEADING.exec(line.text);
  if (heading) {
    const marker = heading[1] + heading[2] + ' ';
    return { kind: 'heading', from: line.from + marker.length, to: line.to, lineFrom: line.from, lineTo: line.to, text: heading[3], marker };
  }

  const list = LIST.exec(line.text);
  if (list) {
    const marker = list[1] + list[2] + ' ';
    return { kind: 'list', from: line.from + marker.length, to: line.to, lineFrom: line.from, lineTo: line.to, text: list[3], marker };
  }

  const quote = QUOTE.exec(line.text);
  if (quote) {
    const marker = quote[1] + '> ';
    return { kind: 'quote', from: line.from + marker.length, to: line.to, lineFrom: line.from, lineTo: line.to, text: quote[2].slice(1).replace(/^\s/, ''), marker };
  }

  return { kind: 'text', from: line.from, to: line.to, lineFrom: line.from, lineTo: line.to, text: line.text, marker: '' };
}

/** Puts a new marker in front of the block, keeping its text. */
export function setMarker(view: EditorView, block: BlockInfo, marker: string) {
  view.dispatch({ changes: { from: block.lineFrom, to: block.lineFrom + block.marker.length, insert: marker } });
}

/** Replaces the block's own text. */
export function setText(view: EditorView, block: BlockInfo, text: string) {
  view.dispatch({ changes: { from: block.from, to: block.to, insert: text } });
}

/** The whole line, or the whole code block, as text. */
export function blockSource(state: EditorState, block: BlockInfo): string {
  if (block.kind === 'code') return state.doc.sliceString(block.lineFrom, block.lineTo + 1);
  return state.doc.sliceString(block.lineFrom, block.lineTo + 1);
}
