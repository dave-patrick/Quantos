<script lang="ts">
  import { onMount, tick } from 'svelte';
  import { api } from '../lib/api';
  import { bridge } from '../lib/platform';
  import { normalizeKey } from '../lib/commands.svelte';
  import { prefs } from '../lib/prefs.svelte';

  /** The global quick-capture window (Windows client), routed to a workspace (BUILD-PLAN 8E.1-2). */
  const UNSENT = 'quantos.capture.unsent';
  // Unsent text survives this window reloading (e.g. signing back in after the backend restarted).
  let text = $state(read());
  function read() { try { return sessionStorage.getItem(UNSENT) ?? ''; } catch { return ''; } }
  $effect(() => { try { if (text) sessionStorage.setItem(UNSENT, text); else sessionStorage.removeItem(UNSENT); } catch { /* storage unavailable */ } });
  let target = $state<'daily' | 'inbox' | 'task'>('daily');
  let workspace = $state('');
  let workspaces = $state<string[]>([]);
  let area = $state<HTMLTextAreaElement | null>(null);
  let message = $state('');

  onMount(async () => {
    const status = await api.get<{ workspaces: string[]; lastWorkspace: string | null }>('status');
    workspaces = status.workspaces ?? ['default'];
    workspace = localStorage.getItem('quantos.capture.ws') ?? status.lastWorkspace ?? workspaces[0];
    target = (localStorage.getItem('quantos.capture.target') as typeof target) ?? 'daily';
    focus();
    bridge.on((m) => { if (m?.type === 'popup-shown') { message = ''; focus(); } });
  });

  function focus() { tick().then(() => area?.focus()); }

  async function submit(to?: 'daily' | 'inbox' | 'task') {
    if (!text.trim()) return;
    const where = to ?? target;
    try {
      await api.post('capture', { text, workspace, target: where });
      localStorage.setItem('quantos.capture.ws', workspace);
      localStorage.setItem('quantos.capture.target', where);
      text = '';
      message = 'Captured.';
      setTimeout(() => bridge.send({ type: 'close-popup' }), 350);
    } catch (e) { message = (e as Error).message; }
  }

  function onkeydown(e: KeyboardEvent) {
    // The keys the person set for the scratchpad's "send to" work here too.
    const combo = normalizeKey(e);
    const send = prefs.scratchTargets.find((t) => t.keys && t.keys === combo && (t.kind === 'daily' || t.kind === 'inbox' || t.kind === 'task'));
    if (send) { e.preventDefault(); submit(send.kind as 'daily' | 'inbox' | 'task'); return; }
    if (e.key === 'Enter' && (e.ctrlKey || !e.shiftKey)) { e.preventDefault(); submit(); return; }
    // Ctrl+T captures straight to a task.
    if (e.ctrlKey && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 't') { e.preventDefault(); submit('task'); }
  }
</script>

<main class="capture">
  <textarea aria-label="Capture" bind:this={area} bind:value={text} {onkeydown} placeholder="Capture a thought. Enter saves, Ctrl+T saves a task, Shift+Enter adds a line."></textarea>
  <footer>
    <select bind:value={target} aria-label="Destination"><option value="daily">Today's note</option><option value="inbox">New note</option><option value="task">New task</option></select>
    <select bind:value={workspace} aria-label="Workspace">{#each workspaces as w (w)}<option value={w}>{w}</option>{/each}</select>
    <span class="msg">{message}</span>
    <button class="btn primary" onclick={() => submit()} disabled={!text.trim()}>Capture</button>
  </footer>
</main>

<style>
  .capture { height: 100vh; display: flex; flex-direction: column; background: var(--bg-raised); padding: 10px; gap: 8px; }
  textarea { flex: 1; resize: none; border: 1px solid var(--line-strong); border-radius: 8px; background: var(--bg-input); padding: 10px 12px; font-size: 16px; line-height: 1.5; }
  textarea:focus { outline: none; border-color: var(--accent); }
  footer { display: flex; gap: 8px; align-items: center; }
  select { height: 30px; border-radius: 6px; border: 1px solid var(--line-strong); background: var(--bg-input); padding: 0 6px; }
  .msg { flex: 1; font-size: 13px; color: var(--ok); }
</style>
