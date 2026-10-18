import type { EditorView } from '@codemirror/view';

/**
 * The editor that last had focus, for commands that act on a selection. When it goes away (its tab
 * was switched), the note and caret position are kept, so a citation can still be added to it.
 */
export const activeEditor: { view: EditorView | null; docId: string | null; lastPos: number | null } = { view: null, docId: null, lastPos: null };

/** Which note each mounted editor shows (read at the moment of use: a new day's note gets its id later). */
export const editorDocs = new WeakMap<EditorView, () => string | null>();

/** Makes an editor the one commands act on, without waiting for a focus event. */
export function setActiveEditor(view: EditorView) {
  activeEditor.view = view;
  activeEditor.docId = editorDocs.get(view)?.() ?? null;
}
