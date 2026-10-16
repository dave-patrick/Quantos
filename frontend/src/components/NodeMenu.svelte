<script lang="ts">
  import { api } from '../lib/api';
  import type { Doc } from '../lib/docs.svelte';
  import { layout } from '../lib/layout.svelte';
  import { app } from '../lib/state.svelte';
  import { prefs } from '../lib/prefs.svelte';
  import type { NodeDto } from '../lib/types';
  import Icon from './Icon.svelte';
  import { templatesStore } from '../lib/templates.svelte';

  let { doc, paneIndex, oncopymd, oncopyrich }: { doc: Doc; paneIndex: number; oncopymd: () => void; oncopyrich: () => void } = $props();
  let open = $state(false);
  let moving = $state(false);
  let applying = $state(false);

  async function act(fn: () => Promise<unknown>) {
    open = false;
    try { await fn(); } catch (e) { app.error(e); }
  }

  const merge = () => act(async () => {
    const title = prompt('Merge which note into this one? Type its exact title. It will move to the trash; links to it will point here.');
    if (!title?.trim()) return;
    const other = app.titles.find((t) => t.title.toLowerCase() === title.trim().toLowerCase() && t.id !== doc.id);
    if (!other) { app.toast('No other note has that title.', 'warn'); return; }
    await doc.flush();
    await api.post(`nodes/${doc.id}/merge`, { other: other.id });
    await doc.refresh();
    app.toast(`Merged “${other.title}”. It's in the trash if you need it back.`, 'ok');
  });

  const archive = () => act(async () => {
    const node = await api.post<NodeDto>(`nodes/${doc.id}/archive`, { archived: !doc.node?.archived });
    app.toast(node.archived ? 'Archived.' : 'Moved out of the archive.', 'ok');
  });

  const trash = () => act(async () => {
    await doc.flush();
    await api.post(`nodes/${doc.id}/trash`);
    app.toast('Moved to trash.', 'ok', { label: 'Undo', run: async () => {
      const entries = await api.get<any[]>('trash');
      const entry = entries.find((e) => e.id === doc.id);
      if (entry) await api.post(`trash/${entry.entryId}/restore`);
    } });
  });

  const moveTo = (ws: string) => act(async () => {
    await doc.flush();
    await api.post(`nodes/${doc.id}/move`, { workspace: ws });
    app.toast(`Moved to ${ws}.`, 'ok');
    moving = false;
  });

  const setTarget = () => act(async () => {
    const value = prompt('Writing target (words). 0 removes it.', String(prefs.writingTarget[doc.id] ?? 1000));
    if (value === null) return;
    const n = Math.max(0, Number(value) || 0);
    prefs.writingTarget = { ...prefs.writingTarget, [doc.id]: n };
  });

  const exportDoc = (format: 'md' | 'html' | 'pdf') => act(async () => {
    const result = await api.post<{ path: string }>(`export/node/${doc.id}`, { format });
    app.toast(`Exported to ${result.path}`, 'ok');
  });
</script>

<div class="wrap">
  <button class="icon-btn" aria-label="More actions" aria-expanded={open} onclick={() => (open = !open)}><Icon name="more" /></button>
  {#if open}
    <button class="backdrop" aria-label="Close" onclick={() => { open = false; moving = false; applying = false; }}></button>
    <div class="menu" role="menu">
      {#if applying}
        <div class="label">Apply template</div>
        {#each templatesStore.list as t (t.id)}
          <button role="menuitem" onclick={() => act(async () => { await doc.flush(); await api.post(`nodes/${doc.id}/apply-template`, { templateId: t.id }); applying = false; })}><Icon name="copy" size={14} /> {t.title}</button>
        {:else}<div class="label">No templates yet</div>{/each}
      {:else if moving}
        <div class="label">Move to workspace</div>
        {#each app.workspaces.filter((w) => w !== doc.node?.workspace) as ws (ws)}
          <button role="menuitem" onclick={() => moveTo(ws)}><Icon name="layers" size={14} /> {ws}</button>
        {/each}
      {:else}
        <button role="menuitem" onclick={() => { open = false; layout.open({ kind: 'node', nodeId: doc.id, title: doc.node?.title }, paneIndex === 0 ? 'other' : 'other'); }}><Icon name="split" size={14} /> Open in other pane</button>
        <button role="menuitem" onclick={() => { open = false; oncopymd(); }}><Icon name="copy" size={14} /> Copy as Markdown</button>
        <button role="menuitem" onclick={() => { open = false; oncopyrich(); }}><Icon name="copy" size={14} /> Copy as rich text</button>
        <button role="menuitem" onclick={setTarget}><Icon name="target" size={14} /> Writing target…</button>
        <button role="menuitem" onclick={() => (applying = true)}><Icon name="copy" size={14} /> Apply template…</button>
        <button role="menuitem" onclick={() => (moving = true)}><Icon name="layers" size={14} /> Move to workspace…</button>
        <button role="menuitem" onclick={merge}><Icon name="layers" size={14} /> Merge another note into this…</button>
        <button role="menuitem" onclick={archive}><Icon name="archive" size={14} /> {doc.node?.archived ? 'Unarchive' : 'Archive'}</button>
        <div class="sep"></div>
        <button role="menuitem" onclick={() => exportDoc('md')}><Icon name="download" size={14} /> Export Markdown</button>
        <button role="menuitem" onclick={() => exportDoc('html')}><Icon name="download" size={14} /> Export HTML</button>
        <button role="menuitem" onclick={() => exportDoc('pdf')}><Icon name="download" size={14} /> Export PDF</button>
        <div class="sep"></div>
        <button role="menuitem" class="danger" onclick={trash}><Icon name="trash" size={14} /> Move to trash</button>
      {/if}
    </div>
  {/if}
</div>

<style>
  .wrap { position: relative; }
  .backdrop { position: fixed; inset: 0; border: 0; background: transparent; z-index: 30; cursor: default; }
  .menu { position: absolute; right: 0; top: 32px; z-index: 31; min-width: 220px; padding: 5px; border-radius: 9px; background: var(--bg-raised); border: 1px solid var(--line-strong); box-shadow: var(--shadow); display: grid; }
  .menu button { display: flex; align-items: center; gap: 9px; text-align: left; padding: 7px 10px; border: 0; border-radius: 5px; background: none; color: var(--text-soft); font-size: 13.5px; }
  .menu button:hover { background: var(--bg-hover); color: var(--text); }
  .menu .danger { color: var(--danger); }
  .menu .label { padding: 6px 10px 4px; }
  .sep { height: 1px; background: var(--line); margin: 4px 2px; }
</style>
