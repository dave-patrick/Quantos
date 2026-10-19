import type { EditorView } from '@codemirror/view';
import type { BlockInfo } from './editor/blockinfo';

/** The block menu: what the right mouse button offers for the block under the pointer. */
export interface BlockMenuState {
  open: boolean;
  x: number;
  y: number;
  block: BlockInfo | null;
  /** The editor the menu was opened in. */
  view: EditorView | null;
}

export const editorMenu = $state<BlockMenuState>({ open: false, x: 0, y: 0, block: null, view: null });

export function openBlockMenu(x: number, y: number, block: BlockInfo, view: EditorView) {
  editorMenu.open = true;
  editorMenu.x = x;
  editorMenu.y = y;
  editorMenu.block = block;
  editorMenu.view = view;
}

export function closeBlockMenu() {
  editorMenu.open = false;
  editorMenu.block = null;
  editorMenu.view = null;
}
