import { api } from './api';
import { activeEditor } from './activeEditor';
import { register } from './commands.svelte';
import { docs } from './docs.svelte';
import { openNode } from './links.svelte';
import { app } from './state.svelte';
import { ai, aiJob } from './ai.svelte';
import { layout } from './layout.svelte';
import type { NodeDto } from './types';
import type { EditorView } from '@codemirror/view';
import { toTaskLines } from './totask';

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

/** The editor the user last typed in; says so when there isn't one, instead of doing nothing. */
function editorOrHint() {
  const view = activeEditor.view;
  if (!view?.dom.isConnected) { app.toast('Click into a note first, then run the command again.', 'info'); return null; }
  return view;
}

/** Turns the current line, or every line the selection touches, into task lines. */
function convertToTasks() {
  const view = editorOrHint();
  if (!view) return;
  const { state } = view;
  if (state.readOnly) { app.toast('This note is read-only.', 'info'); return; }
  const sel = state.selection.main;
  const from = state.doc.lineAt(sel.from).from, to = state.doc.lineAt(sel.to).to;
  const { text, changed } = toTaskLines(state.sliceDoc(from, to));
  if (!changed) { app.toast('Already tasks (or nothing to convert).', 'info'); return; }
  view.dispatch({ changes: { from, to, insert: text }, selection: { anchor: from + text.length }, userEvent: 'input' });
  view.focus();
  app.toast(changed === 1 ? 'Converted to a task.' : `Converted ${changed} lines to tasks.`, 'ok');
}

async function extract(embed: boolean) {
  const sel = selection();
  if (!sel || !activeEditor.docId) { app.toast('Select the text to extract first.', 'warn'); return; }
  const title = await app.ask('Extract to a new note', { message: 'Title for the new note', value: sel.text.split('\n')[0].replace(/^[#\-*\s]+/, '').slice(0, 60), confirmLabel: 'Extract' });
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
  const view = editorOrHint();
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
  const view = editorOrHint();
  if (!view) return;
  const hit = await app.pickNote('Embed a note', { confirmLabel: 'Embed', exclude: activeEditor.docId ?? undefined });
  if (!hit) return;
  insertBlock(view, `![[${hit.id}|${hit.title}]]\n`);
}

register(
  { id: 'edit.totask', title: 'Convert to task (line or selected lines)', group: 'Edit', keys: 'Mod+Alt+T', icon: 'check-square', run: convertToTasks },
  { id: 'edit.extract', title: 'Extract selection to a new note (link)', group: 'Edit', keys: 'Mod+Alt+E', icon: 'file', run: () => extract(false) },
  { id: 'edit.extract.embed', title: 'Extract selection to a new note (embed)', group: 'Edit', icon: 'layers', run: () => extract(true) },
  { id: 'edit.synced', title: 'Make selection a synced block', group: 'Edit', icon: 'refresh', run: makeSynced },
  { id: 'edit.synced.insert', title: 'Insert synced block…', group: 'Edit', icon: 'refresh', run: insertSynced },
  { id: 'edit.embed', title: 'Embed a note…', group: 'Edit', icon: 'layers', run: embedSelection },
  { id: 'edit.columns', title: 'Insert two columns', group: 'Edit', icon: 'split', run: () => {
    const view = editorOrHint(); if (!view) return;
    insertBlock(view, ':::columns\nLeft column\n+++\nRight column\n:::\n', 'Left column');
  } },
  { id: 'edit.callout', title: 'Insert collapsible callout', group: 'Edit', icon: 'info', run: () => {
    const view = editorOrHint(); if (!view) return;
    insertBlock(view, '> [!note]- Title\n> Hidden detail.\n', 'Title');
  } }
);

// ---- AI on the selected text (Release 1.1) ----------------------------------------------------

/**
 * Sends the selection (or the whole note, for summaries) to the AI provider with an instruction and
 * shows the result; the note changes only when the person picks Replace or Insert below.
 */
async function aiOnText(title: string, instruction: string | null, wholeNoteIfEmpty = false) {
  if (!ai.loaded) await ai.load();
  if (!ai.chatReady) { app.toast('Set up AI first: Settings › AI.', 'info', { label: 'Open', run: () => layout.open({ kind: 'settings', title: 'Settings', arg: 'ai' }, 'current') }); return; }
  const view = editorOrHint();
  if (!view) return;
  let { from, to } = view.state.selection.main;
  if (from === to) {
    if (!wholeNoteIfEmpty) { app.toast('Select some text first.', 'warn'); return; }
    from = 0; to = view.state.doc.length;
  }
  const text = view.state.sliceDoc(from, to);
  if (!text.trim()) { app.toast('There is no text to work on.', 'warn'); return; }
  const ask = instruction ?? await app.ask(title, { placeholder: 'For example: make it friendlier, translate into French, turn into a list', confirmLabel: 'Go' });
  if (!ask) return;
  const noteTitle = activeEditor.docId ? docs.peek(activeEditor.docId)?.node?.title ?? null : null;
  const whole = from === 0 && to === view.state.doc.length;
  await aiJob.run(title, ask, text, noteTitle, {
    // The dialog is modal, so the range is still the selected text when the result is applied.
    replace: whole ? undefined : (result) => view.dispatch({ changes: { from, to, insert: result }, selection: { anchor: from + result.length }, userEvent: 'input.ai' }),
    insertBelow: (result) => {
      const line = view.state.doc.lineAt(Math.min(to, view.state.doc.length));
      view.dispatch({ changes: { from: line.to, insert: `\n\n${result}` }, selection: { anchor: line.to + 2 + result.length }, userEvent: 'input.ai' });
    }
  });
}

register(
  { id: 'ai.rewrite', title: 'AI: Rewrite selection…', group: 'AI', icon: 'sparkles', run: () => aiOnText('Rewrite selection', null) },
  { id: 'ai.improve', title: 'AI: Improve writing', group: 'AI', icon: 'sparkles', run: () => aiOnText('Improve writing', 'Improve the clarity and flow. Keep the meaning, tone and length.') },
  { id: 'ai.fix', title: 'AI: Fix spelling and grammar', group: 'AI', icon: 'sparkles', run: () => aiOnText('Fix spelling and grammar', 'Fix spelling, grammar and punctuation only. Change nothing else.') },
  { id: 'ai.shorter', title: 'AI: Make shorter', group: 'AI', icon: 'sparkles', run: () => aiOnText('Make shorter', 'Make it about half as long, keeping every important point.') },
  { id: 'ai.summarize', title: 'AI: Summarise (selection or whole note)', group: 'AI', icon: 'sparkles', run: () => aiOnText('Summary', 'Summarise this as a few concise bullet points.', true) },
  { id: 'ai.actions', title: 'AI: List action items (selection or whole note)', group: 'AI', icon: 'sparkles', run: () => aiOnText('Action items', 'List the action items as Markdown tasks ("- [ ] …"), naming who and when where the text says.', true) },
  { id: 'ai.continue', title: 'AI: Continue writing', group: 'AI', icon: 'sparkles', run: () => aiOnText('Continue writing', 'Continue the text with one or two paragraphs in the same voice. Reply with only the new text.', true) }
);
