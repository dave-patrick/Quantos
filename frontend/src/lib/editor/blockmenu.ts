import { Prec, type Extension } from '@codemirror/state';
import { EditorView, keymap } from '@codemirror/view';
import { blockAt } from './blockinfo';
import { openBlockMenu } from '../editorMenu.svelte';

/**
 * The right mouse button on a block: it offers what can be done with that block, from copying it
 * to turning it into something else. The keyboard way in is Shift+F10.
 */
export function blockMenu(): Extension {
  return [
    EditorView.domEventHandlers({
      contextmenu(event, view) {
        if (view.state.readOnly) return false;
        const pos = view.posAtCoords({ x: event.clientX, y: event.clientY });
        if (pos === null) return false;
        event.preventDefault();
        openBlockMenu(event.clientX, event.clientY, blockAt(view.state, pos), view);
        view.focus();
        return true;
      }
    }),
    Prec.high(keymap.of([{ key: 'Shift-F10', run: (view) => { openBlockMenuAtCursor(view); return true; } }]))
  ];
}

/** Opens the menu for the block at the cursor, for the keyboard. */
export function openBlockMenuAtCursor(view: EditorView) {
  const pos = view.state.selection.main.head;
  const coords = view.coordsAtPos(pos);
  openBlockMenu(coords?.left ?? 0, coords?.bottom ?? 0, blockAt(view.state, pos), view);
}
