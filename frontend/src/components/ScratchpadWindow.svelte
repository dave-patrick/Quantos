<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../lib/api';
  import { docs, type Doc } from '../lib/docs.svelte';
  import { startEvents } from '../lib/events';
  import { app } from '../lib/state.svelte';
  import type { NodeDto, NodeSummary } from '../lib/types';
  import Editor from './Editor.svelte';

  /** The always-on-top scratchpad (Windows client): one note called "Scratchpad", saved like any other. */
  let doc = $state<Doc | null>(null);
  let error = $state('');

  onMount(async () => {
    try {
      await app.refreshStatus();
      await app.loadTitles();
      const ws = app.status?.lastWorkspace ?? 'default';
      const found = await api.get<NodeSummary[]>(`resolve?title=Scratchpad&workspace=${ws}`);
      const id = found[0]?.id ?? (await api.post<NodeDto>('nodes', { title: 'Scratchpad', workspace: ws })).id;
      doc = docs.acquire(id);
      startEvents();
    } catch (e) { error = (e as Error).message; }
  });
</script>

<main class="pad">
  {#if error}<p class="err">{error}</p>
  {:else if doc && doc.status !== 'loading'}
    <Editor text={doc.body} version={doc.externalVersion} docId={doc.id} onchange={(t) => doc!.edit(t)} onsave={() => doc!.save()} placeholder="Scratchpad: jot anything. It stays on top." />
  {/if}
  <footer>{doc?.status === 'saving' ? 'Saving…' : doc?.dirty ? 'Edited' : 'Saved'}</footer>
</main>

<style>
  .pad { height: 100vh; overflow: auto; background: var(--bg-raised); padding: 6px 14px 26px; --editor-size: 15px; }
  .pad :global(.cm-content) { padding-bottom: 40px; }
  footer { position: fixed; bottom: 0; left: 0; right: 0; padding: 3px 10px; font: 11px var(--font-mono); color: var(--text-faint); background: var(--bg-raised); border-top: 1px solid var(--line); }
  .err { color: var(--danger); }
</style>
