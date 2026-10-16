import type { EditorView } from '@codemirror/view';

/** The editor that last had focus, for commands that act on a selection. */
export const activeEditor: { view: EditorView | null; docId: string | null } = { view: null, docId: null };
