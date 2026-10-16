import { api } from './api';
import { activeEditor } from './activeEditor';
import { register } from './commands.svelte';
import { docs } from './docs.svelte';
import { openNode } from './links.svelte';
import { app } from './state.svelte';
import type { NodeDto } from './types';
import type { EditorView } from '@codemirror/view';

/**
 * Puts a block on its own lines after the cursor's line, selects `placeholder` inside it so typing
 * replaces it (or puts the cursor after the block), and gives the editor focus back.
 */
function insertBlock(view: EditorView, block: string, placeholder?: string) {
  const line = view.state.doc.lineAt(view.state.selection.main.head);
  const insert = (line.text.trim() ? '\n' : '') + block;
  const at = placeholder ? insert.indexOf(placeholder) : -1;
  const anchor = line.to + (at >= 0 ? at : insert.length);
  view.dispatch({ changes: { from: line.to, insert }, selection: { anchor, head: at >= 0 ? anchor + placeholder!.length : anchor }, scrollIntoView: true });
  view.focus();
}

function selection() {
  const view = activeEditor.view;
  if (!view) return null;
  const { from, to } = view.state.selection.main;
  return from === to ? null : { view, from, to, text: view.state.sliceDoc(from, to) };
}

async function extract(embed: boolean) {
  const sel = selection();
  if (!sel || !activeEditor.docId) { app.toast('Select the text to extract first.', 'warn'); return; }
  const title = prompt('Title for the new note', sel.text.split('\n')[0].replace(/^[#\-*\s]+/, '').slice(0, 60));
  if (!title?.trim()) return;
  const doc = docs.peek(activeEditor.docId);
  await doc?.flush();
  try {
    const result = await api.post<{ source: NodeDto; extracted: NodeDto }>(`nodes/${activeEditor.docId}/extract`, { text: sel.text, title: title.trim(), embed });
    await doc?.refresh();
    app.toast(`Extracted to “${result.extracted.title}”.`, 'ok', { label: 'Open', run: () => openNode(result.extracted.id, 'other') });
  } catch (e) { app.error(e); }
}

async function makeSynced() {
  const sel = selection();
  if (!sel) { app.toast('Select the text to share first.', 'warn'); return; }
  try {
    const r = await api.post<{ id: string; markdown: string }>('synced-blocks', { content: sel.text });
    sel.view.dispatch({ changes: { from: sel.from, to: sel.to, insert: r.markdown } });
    app.toast('Synced block created. Insert it anywhere with “Insert synced block”.', 'ok');
  } catch (e) { app.error(e); }
}

async function insertSynced() {
  const view = activeEditor.view;
  if (!view) return;
  const blocks = await api.get<{ id: string; title: string; content: string; uses: number }[]>('synced-blocks');
  if (!blocks.length) { app.toast('No synced blocks yet. Select text and use “Make selection a synced block”.', 'info'); return; }
  app.dialog = {
    title: 'Insert synced block',
    message: 'Edits to any copy update every copy.',
    actions: [
      ...blocks.slice(0, 8).map((b) => ({
        label: `${b.title} (${b.uses} use${b.uses === 1 ? '' : 's'})`,
        run: () => {
          app.dialog = null;
          insertBlock(view, `<!--sync:${b.id}-->\n${b.content}\n<!--/sync-->\n`);
        }
      })),
      { label: 'Cancel', run: () => (app.dialog = null) }
    ]
  };
}

async function embedSelection() {
  const view = activeEditor.view;
  if (!view) return;
  const id = prompt('Embed which note? Type its exact title');
  if (!id) return;
  const hit = app.titles.find((t) => t.title.toLowerCase() === id.trim().toLowerCase());
  if (!hit) { app.toast('No note with that title.', 'warn'); return; }
  insertBlock(view, `![[${hit.id}|${hit.title}]]\n`);
}

register(
  { id: 'edit.extract', title: 'Extract selection to a new note (link)', group: 'Edit', keys: 'Mod+Alt+E', icon: 'file', run: () => extract(false) },
  { id: 'edit.extract.embed', title: 'Extract selection to a new note (embed)', group: 'Edit', icon: 'layers', run: () => extract(true) },
  { id: 'edit.synced', title: 'Make selection a synced block', group: 'Edit', icon: 'refresh', run: makeSynced },
  { id: 'edit.synced.insert', title: 'Insert synced block…', group: 'Edit', icon: 'refresh', run: insertSynced },
  { id: 'edit.embed', title: 'Embed a note…', group: 'Edit', icon: 'layers', run: embedSelection },
  { id: 'edit.columns', title: 'Insert two columns', group: 'Edit', icon: 'split', run: () => {
    const view = activeEditor.view; if (!view) return;
    insertBlock(view, ':::columns\nLeft column\n+++\nRight column\n:::\n', 'Left column');
  } },
  { id: 'edit.callout', title: 'Insert collapsible callout', group: 'Edit', icon: 'info', run: () => {
    const view = activeEditor.view; if (!view) return;
    insertBlock(view, '> [!note]- Title\n> Hidden detail.\n', 'Title');
  } }
);
