import type { EditorView } from '@codemirror/view';
import type { BlockInfo } from './editor/blockinfo';

/** The block menu: what the right mouse button offers for the block under the pointer. */
export interface BlockMenuState {
  open: boolean;
  x: number;
  y: number;
  /** The block the menu is about; the first of them when there are several. */
  block: BlockInfo | null;
  /** The blocks the menu works on: the ones that were selected, or just the one that was clicked. */
  blocks: BlockInfo[];
  /** The editor the menu was opened in. */
  view: EditorView | null;
}

export const editorMenu = $state<BlockMenuState>({ open: false, x: 0, y: 0, block: null, blocks: [], view: null });

export function openBlockMenu(x: number, y: number, blocks: BlockInfo[], view: EditorView) {
  editorMenu.open = true;
  editorMenu.x = x;
  editorMenu.y = y;
  editorMenu.blocks = blocks;
  editorMenu.block = blocks[0] ?? null;
  editorMenu.view = view;
}

export function closeBlockMenu() {
  editorMenu.open = false;
  editorMenu.block = null;
  editorMenu.blocks = [];
  editorMenu.view = null;
}
