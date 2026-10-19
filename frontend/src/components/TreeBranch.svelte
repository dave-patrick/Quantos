<script lang="ts">
  import type { TreeFolder, TreeNode } from '../lib/types';
  import Icon from './Icon.svelte';
  import Self from './TreeBranch.svelte';

  let { folder, depth, selected, activeId, onclick, onmove, onnewnote, onnewfolder, root = false }: {
    folder: TreeFolder; depth: number; selected: Set<string>; activeId: string | undefined;
    onclick: (e: MouseEvent, n: TreeNode) => void; onmove: (ids: string[], folder: string) => void;
    onnewnote: (folder?: string) => void; onnewfolder: (parent: string) => void; root?: boolean;
  } = $props();

  const labels: Record<string, string> = { nodes: 'Notes', daily: 'Daily', tasks: 'Tasks', archive: 'Archive', supertags: 'Supertags' };
  // Daily and Supertags start closed. Decided once: the tree reloads on every change, and a folder
  // the user opened must stay open.
  const startsClosed = () => !root && depth === 1 && (folder.name === 'daily' || folder.name === 'supertags');
  let open = $state(!startsClosed());
  let over = $state(false);

  function dragStart(e: DragEvent, n: TreeNode) {
    const ids = selected.has(n.id) ? [...selected] : [n.id];
    e.dataTransfer!.setData('application/x-quantos-nodes', JSON.stringify(ids));
    e.dataTransfer!.setData('text/plain', `[[${n.id}|${n.title}]]`);
    e.dataTransfer!.effectAllowed = 'copyMove';
  }

  function drop(e: DragEvent) {
    over = false;
    const data = e.dataTransfer?.getData('application/x-quantos-nodes');
    if (!data) return;
    e.preventDefault();
    onmove(JSON.parse(data), folder.path);
  }
</script>

{#if !root}
  <div class="folder" class:over role="group" aria-label={depth === 1 ? labels[folder.name] ?? folder.name : folder.name}
    ondragover={(e) => { if (e.dataTransfer?.types.includes('application/x-quantos-nodes')) { e.preventDefault(); over = true; } }}
    ondragleave={() => (over = false)} ondrop={drop} style="padding-left:{depth * 12}px">
    <button class="toggle" aria-expanded={open} onclick={() => (open = !open)}>
      <Icon name={open ? 'chevron-down' : 'chevron-right'} size={12} />
      <Icon name={open ? 'folder-open' : 'folder'} size={14} />
      <span>{depth === 1 ? labels[folder.name] ?? folder.name : folder.name}</span>
    </button>
    <button class="mini" title="New note here" onclick={() => onnewnote(folder.path)}><Icon name="plus" size={12} /></button>
    <button class="mini" title="New folder here" onclick={() => onnewfolder(folder.path)}><Icon name="folder" size={12} /></button>
  </div>
{/if}
{#if open || root}
  {#each folder.folders as child (child.path)}
    <Self folder={child} depth={depth + 1} {selected} {activeId} {onclick} {onmove} {onnewnote} {onnewfolder} />
  {/each}
  {#each folder.nodes as n (n.id)}
    <button class="row" class:on={n.id === activeId} class:sel={selected.has(n.id)} style="padding-left:{(depth + 1) * 12 + 14}px"
      draggable="true" ondragstart={(e) => dragStart(e, n)} onclick={(e) => onclick(e, n)} title={n.path}>
      <Icon name={n.type === 'task' ? 'check-square' : n.type === 'daily' ? 'calendar' : n.type === 'person' ? 'user' : n.type === 'project' ? 'briefcase' : 'file-text'} size={14} />
      <span>{n.title}</span>
    </button>
  {/each}
{/if}

<style>
  .folder { display: flex; align-items: center; border-radius: 5px; }
  .folder.over { background: var(--accent-wash); outline: 1px dashed var(--accent); }
  .toggle { flex: 1; display: flex; align-items: center; gap: 5px; border: 0; background: none; color: var(--text-muted); padding: calc(4px * var(--d)) 4px; font-size: 13px; text-align: left; min-width: 0; }
  .toggle span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 600; }
  .folder:hover { background: var(--bg-hover); }
  .mini { border: 0; background: none; color: var(--text-faint); padding: 3px; border-radius: 4px; opacity: 0; }
  .folder:hover .mini, .mini:focus-visible { opacity: 1; }
  .mini:hover { color: var(--accent); }
  .row { display: flex; align-items: center; gap: 7px; width: 100%; padding: calc(4px * var(--d)) 8px; border: 0; background: none; border-radius: 5px; color: var(--text-soft); font-size: 13.5px; text-align: left; }
  .row span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .row:hover { background: var(--bg-hover); }
  .row.on { background: var(--bg-active); color: var(--text); }
  .row.sel { background: color-mix(in srgb, var(--accent) 22%, transparent); }
</style>
