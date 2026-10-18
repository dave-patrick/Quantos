<script lang="ts">
  import { ago } from '../../lib/today.svelte';
  import { onMount } from 'svelte';
  import { api, ApiError } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { onChange } from '../../lib/events';
  import { openNode } from '../../lib/links.svelte';
  import { awaitApproval } from '../../lib/approvals.svelte';
  import type { NodeDto, TrashEntry } from '../../lib/types';
  import Icon from '../Icon.svelte';

  let entries = $state<TrashEntry[]>([]);
  let selected = $state<Set<string>>(new Set());

  async function load() { entries = await api.get<TrashEntry[]>('trash'); }
  onMount(() => { load(); return onChange((e) => { if (['Trashed', 'Restored', 'Deleted'].includes(e.kind)) load(); }); });

  async function restore(entry: TrashEntry) {
    try {
      const node = await api.post<NodeDto>(`trash/${entry.entryId}/restore`);
      app.toast(node.path !== entry.originalPath ? `Restored as ${node.path} because the original name was taken.` : `Restored “${entry.title}”.`, 'ok',
        { label: 'Open', run: () => openNode(node.id, 'new', node.title) });
    } catch (e) { app.error(e); }
  }

  async function restoreSelected() {
    for (const e of entries.filter((x) => selected.has(x.entryId))) await restore(e);
    selected = new Set();
  }

  async function remove(entry: TrashEntry) {
    const ok = await app.confirm('Delete permanently?', `“${entry.title}” will be removed from the trash for good. Earlier versions stay in history.`, 'Delete permanently', 'danger');
    if (!ok) return;
    try {
      await api.del(`trash/${entry.entryId}`);
    } catch (err) {
      if (err instanceof ApiError && err.approval) {
        const approved = await awaitApproval(err.approval);
        if (!approved) return;
        try { await api.del(`trash/${entry.entryId}?approval=${approved}`); app.toast('Deleted permanently.', 'ok'); }
        catch (e2) { app.error(e2); }
      } else app.error(err);
    }
  }

  function toggle(id: string) {
    const next = new Set(selected);
    if (next.has(id)) next.delete(id); else next.add(id);
    selected = next;
  }
</script>

<div class="scroll view">
  <div class="inner">
    <header>
      <h1>Trash</h1>
      <p class="muted">Items stay here until you delete them. Trash never empties itself.</p>
      {#if selected.size}<button class="btn primary" onclick={restoreSelected}>Restore {selected.size} selected</button>{/if}
    </header>
    {#if entries.length === 0}
      <div class="empty-state"><h3>Trash is empty</h3></div>
    {:else}
      <table>
        <thead><tr><th></th><th>Name</th><th>Was at</th><th>Trashed</th><th>Linked from</th><th></th></tr></thead>
        <tbody>
          {#each entries as entry (entry.entryId)}
            <tr>
              <td><input type="checkbox" checked={selected.has(entry.entryId)} onchange={() => toggle(entry.entryId)} aria-label="Select {entry.title}" /></td>
              <td><Icon name={entry.type === 'task' ? 'check-square' : 'file-text'} size={14} /> {entry.title}</td>
              <td class="mono faint">{entry.originalPath}</td>
              <td class="faint">{ago(entry.trashedAt)}</td>
              <td class="faint">{entry.referrers.length ? `${entry.referrers.length} note${entry.referrers.length === 1 ? '' : 's'}` : '—'}</td>
              <td class="actions">
                <button class="btn" onclick={() => restore(entry)}>Restore</button>
                <button class="btn danger" onclick={() => remove(entry)}>Delete…</button>
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    {/if}
  </div>
</div>

<style>
  .view { flex: 1; padding: 0 28px; }
  .inner { max-width: 1100px; margin: 0 auto; padding: 24px 0 80px; }
  header { display: flex; align-items: center; gap: 16px; flex-wrap: wrap; margin-bottom: 18px; }
  h1 { font: 700 28px/1.2 var(--font-display); }
  header p { margin: 0; flex: 1; }
  table { width: 100%; border-collapse: collapse; font-size: 14px; }
  th { text-align: left; font: 600 11px var(--font-mono); letter-spacing: .08em; text-transform: uppercase; color: var(--text-faint); padding: 6px 8px; border-bottom: 1px solid var(--line); }
  td { padding: 8px; border-bottom: 1px solid var(--line); vertical-align: middle; }
  td :global(.icon) { display: inline-block; vertical-align: -2px; margin-right: 6px; color: var(--text-muted); }
  .mono { font-size: 12px; }
  .actions { display: flex; gap: 6px; justify-content: flex-end; }
</style>
