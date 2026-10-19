<script lang="ts">
  import { api, ApiError } from '../lib/api';
  import type { Doc } from '../lib/docs.svelte';
  import { layout } from '../lib/layout.svelte';
  import { app } from '../lib/state.svelte';
  import { prefs } from '../lib/prefs.svelte';
  import type { NodeDto, TreeFolder } from '../lib/types';
  import Icon from './Icon.svelte';
  import { templatesStore } from '../lib/templates.svelte';

  let { doc, paneIndex, oncopymd, oncopyrich }: { doc: Doc; paneIndex: number; oncopymd: () => void; oncopyrich: () => void } = $props();
  let open = $state(false);
  let moving = $state(false);
  let applying = $state(false);
  let folders = $state<{ path: string; label: string }[] | null>(null);
  let menuEl = $state<HTMLDivElement | null>(null);
  let trigger = $state<HTMLButtonElement | null>(null);

  // Keyboard: focus the first item when the menu (or a submenu) opens; arrows move; Escape closes.
  $effect(() => { void moving; void applying; void folders; if (open) queueMicrotask(() => menuEl?.querySelector<HTMLElement>('[role=menuitem]')?.focus()); });
  function close() { open = false; moving = false; applying = false; folders = null; trigger?.focus(); }
  function menuKeys(e: KeyboardEvent) {
    const items = [...(menuEl?.querySelectorAll<HTMLElement>('[role=menuitem]') ?? [])];
    const i = items.indexOf(document.activeElement as HTMLElement);
    if (e.key === 'Escape') { e.preventDefault(); close(); }
    else if (e.key === 'ArrowDown') { e.preventDefault(); items[(i + 1) % items.length]?.focus(); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); items[(i - 1 + items.length) % items.length]?.focus(); }
  }

  /** Folders of this note's workspace, for keyboard users (the file tree moves by dragging). */
  async function showFolders() {
    const ws = doc.node?.workspace || app.workspace;
    const tree = await api.get<TreeFolder>(`tree?workspace=${ws}`);
    const out: { path: string; label: string }[] = [];
    const walk = (f: TreeFolder, prefix: string) => {
      for (const child of f.folders) {
        // Daily notes, tasks, the archive and supertags have fixed homes; notes go under Notes.
        if (!prefix && ['daily', 'tasks', 'archive', 'supertags'].includes(child.name)) continue;
        const label = prefix ? `${prefix} / ${child.name}` : child.name === 'nodes' ? 'Notes' : child.name;
        out.push({ path: child.path, label });
        walk(child, label);
      }
    };
    walk(tree, '');
    folders = out;
  }

  const moveToFolder = (folder: string) => act(async () => {
    await doc.flush();
    try { await api.post(`nodes/${doc.id}/move`, { folder }); }
    catch (err) {
      if (!(err instanceof ApiError && err.status === 409)) throw err;
      if (!(await app.confirm('Name already used', `${err.message}\nKeep both? This note gets a numbered name.`, 'Keep both'))) return;
      await api.post(`nodes/${doc.id}/move`, { folder, rename: true });
    }
    app.toast('Moved.', 'ok');
  });

  async function act(fn: () => Promise<unknown>) {
    open = false; folders = null; moving = false; applying = false;
    trigger?.focus();
    try { await fn(); } catch (e) { app.error(e); }
  }

  const merge = () => act(async () => {
    const other = await app.pickNote('Merge a note into this one', { message: 'The chosen note moves to the trash, and links to it will point here.', confirmLabel: 'Merge', exclude: doc.id });
    if (!other) return;
    await doc.flush();
    await api.post(`nodes/${doc.id}/merge`, { other: other.id });
    await doc.refresh();
    app.toast(`Merged “${other.title}”. It's in the trash if you need it back.`, 'ok');
  });

  const archive = () => act(async () => {
    const node = await api.post<NodeDto>(`nodes/${doc.id}/archive`, { archived: !doc.node?.archived });
    app.toast(node.archived ? 'Archived.' : 'Moved out of the archive.', 'ok');
  });

  const toggleLock = () => act(async () => {
    if (doc.node?.locked) await doc.setProperties({}, ['locked']);
    else await doc.setProperties({ locked: true });
    app.toast(doc.node?.locked ? 'Locked. Nobody can edit this note, including agents and automations, until you unlock it.' : 'Unlocked.', 'ok');
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
    const current = Number(doc.node?.properties?.writing_target) || prefs.writingTarget[doc.id] || 1000;
    const value = await app.ask('Writing target', { message: 'Number of words. 0 removes the target. It is saved with the note.', value: String(current), numeric: true, confirmLabel: 'Set' });
    if (value === null) return;
    const n = Math.max(0, Math.round(Number(value)) || 0);
    if (n > 0) await doc.setProperties({ writing_target: n });
    else await doc.setProperties({}, ['writing_target']);
    // The note holds it now; drop the copy this browser kept.
    if (prefs.writingTarget[doc.id] !== undefined) { const { [doc.id]: _, ...rest } = prefs.writingTarget; prefs.writingTarget = rest; }
  });

  const exportDoc = (format: 'md' | 'html' | 'pdf') => act(async () => {
    const result = await api.post<{ path: string }>(`export/node/${doc.id}`, { format });
    app.toast(`Exported to ${result.path}`, 'ok');
  });
</script>

<div class="wrap">
  <button class="icon-btn" bind:this={trigger} aria-label="More actions" aria-haspopup="menu" aria-expanded={open} onclick={() => (open = !open)}><Icon name="more" /></button>
  {#if open}
    <button class="backdrop" aria-label="Close" tabindex="-1" onclick={close}></button>
    <div class="menu" role="menu" tabindex="-1" bind:this={menuEl} onkeydown={menuKeys}>
      {#if folders}
        <div class="label">Move to folder</div>
        {#each folders as f (f.path)}
          <button role="menuitem" onclick={() => moveToFolder(f.path)}><Icon name="folder" size={14} /> {f.label}</button>
        {:else}<div class="label">No folders yet</div>{/each}
      {:else if applying}
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
        <button role="menuitem" onclick={() => showFolders().catch((e) => app.error(e))}><Icon name="folder" size={14} /> Move to folder…</button>
        <button role="menuitem" onclick={() => (moving = true)}><Icon name="layers" size={14} /> Move to workspace…</button>
        <button role="menuitem" onclick={merge}><Icon name="layers" size={14} /> Merge another note into this…</button>
        <button role="menuitem" onclick={toggleLock}><Icon name="lock" size={14} /> {doc.node?.locked ? 'Unlock' : 'Lock (read-only)'}</button>
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
