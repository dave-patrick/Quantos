<script lang="ts">
  import { layout, type Tab } from '../../lib/layout.svelte';
  import { api } from '../../lib/api';
  import type { NodeDto } from '../../lib/types';
  import { app } from '../../lib/state.svelte';
  import { openNode } from '../../lib/links.svelte';
  import { newNote } from '../../lib/commands.svelte';
  import { ago } from '../../lib/today.svelte';
  import Icon from '../Icon.svelte';
  let { tab }: { tab: Tab } = $props();
  let query = $state('');
  const currentFolder = $derived(tab.arg?.startsWith('folder:') ? tab.arg.slice(7) : '');
  function browse(folder: string) {
    layout.open({ kind: 'notes', arg: folder ? 'folder:' + folder : undefined, title: 'Notes' }).catch(e => app.error(e));
  }
  const notes = $derived(app.titles.filter(n => !n.archived && n.workspace === app.workspace));
  const currentParts = $derived(currentFolder ? currentFolder.split('/') : []);

  // Paths are vault-relative. Strip the workspace/storage prefix so folders
  // with the same relative path appear together, as they do in NotesView.
  function folderOf(path: string, workspace: string): string[] {
    const parts = path.split('/');
    if (!path || path.startsWith('/') || path.endsWith('/') || path.includes('\\') || parts.some(p => !p || p === '.' || p === '..')) return [];
    let prefix = 0;
    if (parts[0] === 'workspaces' && parts.length >= 4 && parts[1].toLowerCase() === workspace.toLowerCase()) prefix = 3;
    else if (parts[0] === 'linked' && parts.length >= 3 && parts[1].toLowerCase() === workspace.toLowerCase()) prefix = 2;
    else return [];
    return parts.slice(prefix, -1);
  }
  const ordinal = (a: string, b: string) => a < b ? -1 : a > b ? 1 : 0;
  const compareFolder = (a: string, b: string) => ordinal(a.toLowerCase(), b.toLowerCase()) || ordinal(a, b);
  const folders = $derived.by(() => {
    const paths = new Map<string, { path: string; count: number }>();
    for (const n of notes) {
      const parts = folderOf(n.path, app.workspace);
      for (let i = 1; i <= parts.length; i++) {
        const path = parts.slice(0, i).join('/');
        const key = path.toLowerCase();
        const existing = paths.get(key);
        if (!existing) paths.set(key, { path, count: 1 });
        else {
          if (compareFolder(path, existing.path) < 0) existing.path = path;
          existing.count++;
        }
      }
    }
    return [...paths.values()].sort((a, b) => compareFolder(a.path, b.path)).map(({ path, count }) => ({
      path, name: path.split('/').at(-1)!, count
    }));
  });
  const childFolders = $derived(folders.filter(f => {
    const parts = f.path.split('/');
    return parts.length === currentParts.length + 1
      && parts.slice(0, -1).join('/').toLowerCase() === currentFolder.toLowerCase();
  }));
  const currentNotes = $derived(notes.filter(n => folderOf(n.path, app.workspace).join('/').toLowerCase() === currentFolder.toLowerCase())
    .filter(n => !query.trim() || (n.title + ' ' + n.tags.join(' ')).toLowerCase().includes(query.trim().toLowerCase()))
    .sort((a,b) => (b.modified ?? '').localeCompare(a.modified ?? '')));
  const crumbs = $derived(currentParts.map((_, i) => ({ name: currentParts[i], path: currentParts.slice(0, i + 1).join('/') })));
  async function create() {
    try {
      if (!currentFolder) { await newNote('current'); return; }
      const root = app.linked.some(l => l.name === app.workspace) ? `linked/${app.workspace}` : `workspaces/${app.workspace}/nodes`;
      const node = await api.post<NodeDto>('nodes', { title: 'Untitled', workspace: app.workspace, folder: root + '/' + currentFolder });
      await openNode(node.id, 'current', node.title);
    } catch(e) { app.error(e); }
  }
</script>

<section class="phone-notes">
  <header>
    <h1>Notes</h1><p class="faint">{app.workspace}</p>
    <nav class="breadcrumbs" aria-label="Folder location">
      <button class:current={!currentFolder} aria-current={!currentFolder ? 'page' : undefined} onclick={() => browse('')}>All notes</button>
      {#each crumbs as crumb, i (crumb.path)}
        <Icon name="chevron-right" size={14} />
        <button class:current={i === crumbs.length - 1} aria-current={i === crumbs.length - 1 ? 'page' : undefined} onclick={() => browse(crumb.path)}>{crumb.name}</button>
      {/each}
    </nav>
    <input class="input" type="search" aria-label="Find a note in this folder" placeholder="Find a note…" bind:value={query} />
  </header>
  <div class="scroll note-list">
    {#each childFolders as folder (folder.path.toLowerCase())}
      <button class="folder-row" aria-label={`${folder.name}, ${folder.count} ${folder.count === 1 ? 'note' : 'notes'}`} onclick={() => browse(folder.path)}>
        <Icon name="folder" size={19} />
        <span class="folder-title">{folder.name}</span>
        <span class="folder-count">{folder.count}</span>
        <Icon name="chevron-right" size={18} />
      </button>
    {/each}
    {#each currentNotes as n (n.id)}
      <button class="note-row" onclick={() => openNode(n.id, 'current', n.title)}>
        <span class="note-title">{n.title}</span><span class="note-meta">{n.type} {#if n.modified} · {ago(n.modified)}{/if}</span>
        <Icon name="chevron-right" size={18} />
      </button>
    {:else}
      {#if !childFolders.length}<div class="empty-state"><h2>{query ? 'No matching notes' : 'No notes here yet'}</h2><p>{query ? 'Try another title, or use Search above to search note text.' : 'Create a note, or connect your shared vault in More › Settings.'}</p></div>{/if}
    {/each}
  </div>
  <button class="new-note" onclick={create}><Icon name="plus" size={24} /> New note</button>
</section>

<style>
  .phone-notes { display: flex; flex-direction: column; flex: 1; min-height: 0; position: relative; }
  header { padding: 16px 16px 10px; }
  h1 { font: 700 28px/1.2 var(--font-display); margin: 0 0 4px; }
  header p { font-size: 14px; margin-bottom: 10px; }
  header input { width: 100%; }
  .note-list { flex: 1; padding: 0 16px 96px; }
  .breadcrumbs { display: flex; align-items: center; gap: 3px; min-width: 0; margin: 0 0 10px; color: var(--text-faint); overflow-x: auto; }
  .breadcrumbs button { border: 0; background: transparent; color: var(--text-muted); padding: 4px 2px; white-space: nowrap; font: 500 13px var(--font-body); }
  .breadcrumbs button.current { color: var(--text); font-weight: 650; }
  .breadcrumbs :global(.icon) { flex: none; }
  .folder-row { width: 100%; display: flex; align-items: center; gap: 10px; min-height: 48px; padding: 8px 4px; text-align: left; border: 0; border-bottom: 1px solid var(--line); background: transparent; color: var(--text); }
  .folder-row:hover, .note-row:hover { background: var(--bg-hover); }
  .folder-row :global(.icon) { flex: none; color: var(--accent-strong); }
  .folder-title { flex: 1; min-width: 0; overflow-wrap: anywhere; font: 600 15px/1.3 var(--font-body); }
  .folder-count { color: var(--text-faint); font: 12px var(--font-mono); }
  .note-row { width: 100%; display: grid; grid-template-columns: minmax(0,1fr) 24px; gap: 3px 12px; min-height: 48px; padding: 8px 4px; text-align: left; border: 0; border-bottom: 1px solid var(--line); background: transparent; color: var(--text); }
  .note-row:hover { background: var(--bg-hover); }
  .note-title { font: 600 15px/1.3 var(--font-body); overflow-wrap: anywhere; }
  .note-meta { grid-column: 1; font-size: 12px; line-height: 1.25; color: var(--text-muted); }
  .note-row :global(.icon) { grid-column: 2; grid-row: 1 / 3; align-self: center; color: var(--text-faint); }
  .new-note { position: absolute; bottom: 16px; right: 16px; display: flex; align-items: center; gap: 12px; min-height: 56px; padding: 0 20px; border: 0; border-radius: 16px; background: var(--accent); color: var(--accent-ink); font: 600 16px var(--font-body); box-shadow: var(--shadow); }
</style>
