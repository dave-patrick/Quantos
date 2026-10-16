<script lang="ts">
  import { onMount } from 'svelte';
  import { api, ApiError } from '../lib/api';
  import { app } from '../lib/state.svelte';
  import { onChange } from '../lib/events';
  import { layout } from '../lib/layout.svelte';
  import { openNode, whereFrom } from '../lib/links.svelte';
  import type { NodeDto, TreeFolder, TreeNode } from '../lib/types';
  import Icon from './Icon.svelte';
  import TreeBranch from './TreeBranch.svelte';

  let tree = $state<TreeFolder | null>(null);
  let filter = $state('');
  let showArchive = $state(false);
  let selected = $state<Set<string>>(new Set());
  let lastClicked: string | null = null;
  let workspace = $state(app.workspace);
  let timer: ReturnType<typeof setTimeout>;

  async function load() {
    workspace = app.workspace;
    try { tree = await api.get<TreeFolder>(`tree?workspace=${workspace}&archived=${showArchive}`); } catch { tree = null; }
  }

  onMount(() => onChange(() => { clearTimeout(timer); timer = setTimeout(load, 250); }));
  $effect(() => { void app.workspace; void showArchive; load(); });

  const flat = $derived.by(() => {
    const out: TreeNode[] = [];
    const walk = (f: TreeFolder) => { f.folders.forEach(walk); out.push(...f.nodes); };
    if (tree) walk(tree);
    return out;
  });

  const matches = $derived(filter.trim() ? flat.filter((n) => n.title.toLowerCase().includes(filter.trim().toLowerCase())) : null);
  const activeId = $derived(layout.activeTab?.nodeId);

  function click(e: MouseEvent, node: TreeNode) {
    if (e.ctrlKey || e.metaKey) {
      const next = new Set(selected);
      if (next.has(node.id)) next.delete(node.id); else next.add(node.id);
      selected = next; lastClicked = node.id; return;
    }
    if (e.shiftKey && lastClicked && selected.size) {
      const ids = flat.map((n) => n.id);
      const [a, b] = [ids.indexOf(lastClicked), ids.indexOf(node.id)].sort((x, y) => x - y);
      selected = new Set([...selected, ...ids.slice(a, b + 1)]); return;
    }
    selected = new Set();
    lastClicked = node.id;
    openNode(node.id, whereFrom(e), node.title);
  }

  async function moveTo(ids: string[], folder: string) {
    for (const id of ids) {
      try { await api.post(`nodes/${id}/move`, { folder }); }
      catch (err) {
        if (err instanceof ApiError && err.status === 409) {
          const keep = await app.confirm('Name already used', `${err.message}\nKeep both? The moved note gets a numbered name.`, 'Keep both');
          if (keep) await api.post(`nodes/${id}/move`, { folder, rename: true }).catch((e) => app.error(e));
        } else app.error(err);
      }
    }
    selected = new Set();
  }

  async function batch(action: 'archive' | 'trash' | 'unarchive') {
    const ids = [...selected];
    if (action === 'trash' && !(await app.confirm('Move to trash?', `${ids.length} item(s) will move to the trash. You can restore them.`, 'Move to trash', 'danger'))) return;
    for (const id of ids) {
      try {
        if (action === 'trash') await api.post(`nodes/${id}/trash`);
        else await api.post(`nodes/${id}/archive`, { archived: action === 'archive' });
      } catch (e) { app.error(e); }
    }
    selected = new Set();
  }

  async function newNote(folder?: string) {
    try {
      const node = await api.post<NodeDto>('nodes', { title: 'Untitled', workspace: app.workspace, folder });
      openNode(node.id, 'new', node.title);
    } catch (e) { app.error(e); }
  }

  async function newFolder(parent: string) {
    const name = prompt('Folder name');
    if (!name?.trim()) return;
    const clean = name.trim().toLowerCase().replace(/[^\w\- ]+/g, '').replace(/\s+/g, '_');
    try { await api.post('folders', { path: `${parent}/${clean}` }); load(); } catch (e) { app.error(e); }
  }
</script>

<div class="files">
  <div class="tools">
    <input class="input filter" placeholder="Filter files" bind:value={filter} aria-label="Filter files" />
    <button class="icon-btn" title="New note" onclick={() => newNote()}><Icon name="plus" size={15} /></button>
    <button class="icon-btn" title="New folder" onclick={() => tree && newFolder(tree.path + '/nodes')}><Icon name="folder" size={15} /></button>
    <button class="icon-btn" title="Show archive" aria-pressed={showArchive} onclick={() => (showArchive = !showArchive)}><Icon name="archive" size={15} /></button>
  </div>
  {#if selected.size}
    <div class="batch">
      <span>{selected.size} selected</span>
      <button class="btn ghost" onclick={() => batch(showArchive ? 'unarchive' : 'archive')}>{showArchive ? 'Unarchive' : 'Archive'}</button>
      <button class="btn ghost" onclick={() => batch('trash')}>Trash</button>
      <button class="icon-btn" aria-label="Clear selection" onclick={() => (selected = new Set())}><Icon name="x" size={13} /></button>
    </div>
  {/if}
  <div class="scroll list">
    {#if matches}
      {#each matches as n (n.id)}
        <button class="row" class:on={n.id === activeId} class:sel={selected.has(n.id)} onclick={(e) => click(e, n)}>
          <Icon name={n.type === 'task' ? 'check-square' : n.type === 'daily' ? 'calendar' : 'file-text'} size={14} /> <span>{n.title}</span>
        </button>
      {:else}<p class="faint pad">No matches.</p>{/each}
    {:else if tree}
      <TreeBranch folder={tree} depth={0} {selected} {activeId} onclick={click} onmove={moveTo} onnewnote={newNote} onnewfolder={newFolder} root />
    {/if}
  </div>
</div>

<style>
  .files { flex: 1; display: flex; flex-direction: column; min-height: 0; }
  .tools { display: flex; gap: 2px; align-items: center; padding: 8px; }
  .filter { height: 28px; font-size: 13px; }
  .batch { display: flex; align-items: center; gap: 4px; padding: 4px 8px; margin: 0 8px 6px; border-radius: 7px; background: var(--accent-wash); color: var(--accent-strong); font-size: 12.5px; }
  .batch span { flex: 1; }
  .batch .btn { height: 24px; padding: 0 8px; font-size: 12px; }
  .list { flex: 1; padding: 0 4px 20px; }
  .row { display: flex; align-items: center; gap: 7px; width: 100%; padding: 4px 8px; border: 0; background: none; border-radius: 5px; color: var(--text-soft); font-size: 13.5px; text-align: left; }
  .row span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .row:hover { background: var(--bg-hover); }
  .row.on { background: var(--bg-active); color: var(--text); }
  .row.sel { background: color-mix(in srgb, var(--accent) 22%, transparent); }
  .pad { padding: 8px; font-size: 13px; }
</style>
