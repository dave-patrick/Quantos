import { activeEditor } from './activeEditor';
import { isoDate } from './dates';
import { app } from './state.svelte';

/** Insert at the last focused note's selection, retained while the command palette is open. */
export function insertTodaysDate() {
  const view = activeEditor.view;
  if (!view?.dom.isConnected) {
    app.toast('Click into a note first, then run the command again.', 'info');
    return;
  }
  if (view.state.readOnly) {
    app.toast('This note is read-only.', 'info');
    return;
  }
  const { from, to } = view.state.selection.main;
  const date = isoDate();
  view.dispatch({ changes: { from, to, insert: date }, selection: { anchor: from + date.length }, scrollIntoView: true, userEvent: 'input' });
  view.focus();
}
