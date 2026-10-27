<script lang="ts">
  import { onMount, onDestroy } from 'svelte';
  import { onChange } from '../../lib/events';
  import { noteFolders, noteFolderLocation, parentFolder, folderLabels } from '../../lib/noteFolders';
  import WorkspaceSwitcher from '../WorkspaceSwitcher.svelte';
  import { layout, type Tab } from '../../lib/layout.svelte';
  import { api } from '../../lib/api';
  import type { NodeDto, TreeFolder } from '../../lib/types';
  import { app } from '../../lib/state.svelte';
  import { openNode } from '../../lib/links.svelte';
  import { newNote } from '../../lib/commands.svelte';
  import { ago } from '../../lib/today.svelte';
  import Icon from '../Icon.svelte';
  let { tab }: { tab: Tab } = $props();
  let query = $state('');
  const linked = $derived(app.linked.some(l => l.name === app.workspace));
  const root = $derived((linked ? 'linked/' : 'workspaces/') + app.workspace);
  const currentFolder = $derived(noteFolderLocation(tab.arg, root, linked));
  let tree = $state<TreeFolder | null>(null);
  let treeError = $state('');
  let request = 0;
  let timer: ReturnType<typeof setTimeout>;
  async function loadTree(workspace = app.workspace) {
    const serial = ++request;
    try {
      const result = await api.get<TreeFolder>(`tree?workspace=${encodeURIComponent(workspace)}&archived=true`);
      if (serial === request && workspace === app.workspace) { tree = result; treeError = ''; }
    } catch (e) { if (serial === request) treeError = 'Could not load all folders. Retry to refresh the list.'; }
  }
  $effect(() => { const workspace = app.workspace; tree = null; loadTree(workspace); });
  onMount(() => onChange(() => { clearTimeout(timer); timer = setTimeout(() => loadTree(), 300); }));
  onDestroy(() => { request++; clearTimeout(timer); });
  function browse(folder: string) {
    layout.open({ kind: 'notes', arg: folder === root ? undefined : 'folderpath:' + folder, title: 'Notes' }).catch(e => app.error(e));
  }
  const notes = $derived(app.titles.filter(n => n.workspace === app.workspace));
  const folders = $derived(noteFolders(tree, notes, root));
  const childFolders = $derived(folders.filter(f => f.path !== root && parentFolder(f.path).toLowerCase() === currentFolder.toLowerCase()));
  const currentNotes = $derived(notes.filter(n => parentFolder(n.path).toLowerCase() === currentFolder.toLowerCase())
    .filter(n => !query.trim() || (n.title + ' ' + n.tags.join(' ')).toLowerCase().includes(query.trim().toLowerCase()))
    .sort((a,b) => (b.modified ?? '').localeCompare(a.modified ?? '')));
  const crumbs = $derived.by(() => {
    const relative = currentFolder.slice(root.length).replace(/^\//, '');
    return relative ? relative.split('/').map((name, i, parts) => ({ name: i === 0 ? folderLabels[name] ?? name : name, path: root + '/' + parts.slice(0, i + 1).join('/') })) : [];
  });
  async function newFolder() {
    const name = await app.ask('New folder', { placeholder: 'Folder name', confirmLabel: 'Create' });
    if (!name?.trim()) return;
    const clean = name.trim();
    if (clean === '.' || clean === '..' || /[\\/]/.test(clean)) { app.toast('Use one folder name without slashes.', 'warn'); return; }
    try { const path = currentFolder + '/' + clean; await api.post('folders', { path }); await loadTree(); browse(path); }
    catch (e) { app.error(e); }
  }
  async function create() {
    try {
      if (currentFolder === root) { await newNote('current'); return; }
      const node = await api.post<NodeDto>('nodes', { title: 'Untitled', workspace: app.workspace, folder: currentFolder });
      await openNode(node.id, 'current', node.title);
    } catch(e) { app.error(e); }
  }
</script>

<section class="phone-notes">
  <header>
    <div class="heading"><h1>Notes</h1><button class="btn ghost" onclick={newFolder}><Icon name="folder" size={18} /> New folder</button></div>
    <div class="workspace"><WorkspaceSwitcher /></div>
    <nav class="breadcrumbs" aria-label="Folder location">
      <button class:current={currentFolder === root} aria-current={currentFolder === root ? 'page' : undefined} onclick={() => browse(root)}>All folders</button>
      {#each crumbs as crumb, i (crumb.path)}
        <Icon name="chevron-right" size={14} />
        <button class:current={i === crumbs.length - 1} aria-current={i === crumbs.length - 1 ? 'page' : undefined} onclick={() => browse(crumb.path)}>{crumb.name}</button>
      {/each}
    </nav>
    {#if treeError}<p class="folder-error">{treeError} <button class="btn ghost" onclick={() => loadTree()}>Retry</button></p>{/if}
    <input class="input" type="search" aria-label="Find a note in this folder" placeholder="Find a note…" bind:value={query} />
  </header>
  <div class="scroll note-list">
    {#each childFolders as folder (folder.path.toLowerCase())}
      <button class="folder-row" aria-label={`${folder.label}, ${folder.count} ${folder.count === 1 ? 'note' : 'notes'}`} onclick={() => browse(folder.path)}>
        <Icon name="folder" size={19} />
        <span class="folder-title">{folder.label}</span>
        <span class="folder-count">{folder.count}</span>
        <Icon name="chevron-right" size={18} />
      </button>
    {/each}
    {#each currentNotes as n (n.id)}
      <button class="note-row" onclick={() => openNode(n.id, 'current', n.title)}>
        <span class="note-title">{n.title}</span><span class="note-meta">{n.type}{#if n.archived} · archived{/if} {#if n.modified} · {ago(n.modified)}{/if}</span>
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
  .heading { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
  .workspace { margin: 4px 0 10px; }
  .folder-error { color: var(--warn); font-size: 14px; }
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
