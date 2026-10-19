<script lang="ts">
  import { onDestroy, onMount } from 'svelte';
  import type { EditorView } from '@codemirror/view';
  import { api } from '../lib/api';
  import { docs, type Doc } from '../lib/docs.svelte';
  import { startEvents } from '../lib/events';
  import { app } from '../lib/state.svelte';
  import { prefs } from '../lib/prefs.svelte';
  import { displayKeys, handleGlobalKey, normalizeKey } from '../lib/commands.svelte';
  import { sendRange, type ScratchTarget } from '../lib/scratch';
  import type { NodeDto, NodeSummary } from '../lib/types';
  import Editor from './Editor.svelte';

  /**
   * The scratchpad: one note called "Scratchpad", saved like any other. It runs in its own always-on-top
   * window (Windows client) or docked in the right sidebar; the same note either way, kept in step.
   * "Send to" keys move the selection (or the paragraph at the cursor) to a place set in Settings › Shortcuts.
   */
  let { docked = false }: { docked?: boolean } = $props();
  let doc = $state<Doc | null>(null);
  let view = $state<EditorView | null>(null);
  let error = $state('');
  let note = $state('');
  let noteTimer: ReturnType<typeof setTimeout> | undefined;
  let menu = $state(false);

  onDestroy(() => { if (doc) docs.release(doc.id); });
  onMount(async () => {
    try {
      if (!docked) { await app.refreshStatus(); await app.loadTitles(); }
      const ws = app.status?.lastWorkspace ?? 'default';
      const found = await api.get<NodeSummary[]>(`resolve?title=Scratchpad&workspace=${ws}`);
      const id = found[0]?.id ?? (await api.post<NodeDto>('nodes', { title: 'Scratchpad', workspace: ws })).id;
      doc = docs.acquire(id);
      if (!docked) startEvents();
    } catch (e) { error = (e as Error).message; }
  });

  function say(text: string) { note = text; clearTimeout(noteTimer); noteTimer = setTimeout(() => (note = ''), 4000); }

  async function send(t: ScratchTarget) {
    menu = false;
    if (!view) return;
    const sel = view.state.selection.main;
    const r = sendRange(view.state.doc.toString(), sel.from, sel.to);
    if (!r) { say('Put the cursor in some text (or select it) first.'); return; }
    const workspace = app.status?.lastWorkspace ?? undefined;
    try {
      if (t.kind === 'note') {
        if (!t.noteId) { say(`“${t.label}” has no note chosen yet (Settings › Shortcuts).`); return; }
        await api.post(`nodes/${t.noteId}/append`, { text: r.text, heading: t.heading || null });
      } else await api.post('capture', { text: r.text, workspace, target: t.kind });
      // Moved, not copied: it leaves the scratchpad.
      view.dispatch({ changes: { from: r.from, to: r.to, insert: '' }, userEvent: 'delete' });
      say(`Sent to ${t.label}.`);
    } catch (e) { say((e as Error).message); }
  }

  function onkeydown(e: KeyboardEvent) {
    const combo = normalizeKey(e);
    const target = prefs.scratchTargets.find((t) => t.keys && t.keys === combo);
    if (target) { e.preventDefault(); e.stopPropagation(); send(target); return; }
    // In its own window, editing commands (convert to task, AI…) work too; docked, the main window already handles them.
    if (!docked && handleGlobalKey(e, (id) => id.startsWith('edit.') || id.startsWith('ai.'))) e.stopPropagation();
  }
</script>

<!-- Capture phase: a send key must win over the editor's own use of it (Ctrl+Enter inserts a line there). -->
<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="pad" class:docked onkeydowncapture={onkeydown}>
  <div class="body">
    {#if error}<p class="err">{error}</p>
    {:else if doc && doc.status !== 'loading'}
      <Editor text={doc.body} version={doc.externalVersion} docId={doc.id} bind:view onchange={(t) => doc!.edit(t)} onsave={() => doc!.save()} placeholder={docked ? 'Scratchpad: jot anything.' : 'Scratchpad: jot anything. It stays on top.'} />
    {/if}
  </div>
  <footer>
    <span class="state">{note || (doc?.status === 'saving' ? 'Saving…' : doc?.dirty ? 'Edited' : 'Saved')}</span>
    <span class="send">
      <button class="sendbtn" aria-expanded={menu} onclick={() => (menu = !menu)}>Send to…</button>
      {#if menu}
        <div class="menu" role="menu">
          {#each prefs.scratchTargets as t (t.id)}
            <button role="menuitem" onclick={() => send(t)}>{t.label}{#if t.keys}<kbd>{displayKeys(t.keys)}</kbd>{/if}</button>
          {/each}
        </div>
      {/if}
    </span>
  </footer>
</div>

<style>
  .pad { height: 100vh; display: flex; flex-direction: column; background: var(--bg-raised); --editor-size: 15px; }
  .pad.docked { height: auto; flex: 1; min-height: 0; --editor-size: 14.5px; }
  .body { flex: 1; min-height: 0; overflow: auto; padding: 6px 14px 10px; }
  .docked .body { padding: 6px 8px 10px; }
  .pad :global(.cm-content) { padding-bottom: 30px; }
  footer { flex: none; display: flex; align-items: center; padding: 3px 10px; font: 11px var(--font-mono); color: var(--text-faint); background: var(--bg-raised); border-top: 1px solid var(--line); }
  .state { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .send { position: relative; }
  .sendbtn { border: 0; background: none; color: var(--accent); font: 11px var(--font-mono); cursor: pointer; padding: 0 2px; }
  .menu { position: absolute; right: 0; bottom: 20px; display: grid; min-width: 220px; background: var(--bg-panel); border: 1px solid var(--line-strong); border-radius: 8px; padding: 4px; box-shadow: var(--shadow); }
  .menu button { display: flex; justify-content: space-between; gap: 10px; border: 0; background: none; color: var(--text); font: 13px var(--font-body); padding: 6px 8px; border-radius: 5px; text-align: left; cursor: pointer; }
  .menu button:hover, .menu button:focus-visible { background: var(--bg-hover); }
  kbd { font: 11px var(--font-mono); color: var(--text-faint); }
  .err { color: var(--danger); }
</style>
