<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { layout } from '../../lib/layout.svelte';
  import { relativeTime } from '../../lib/dates';

  interface Info { path: string; hash: string; size: number; references: number; modified: string; extension: string }
  let items = $state<Info[]>([]);
  let onlyOrphans = $state(true);

  async function load() { items = await api.get<Info[]>('attachments/audit'); }
  onMount(load);

  const shown = $derived(onlyOrphans ? items.filter((i) => i.references === 0) : items);
  const orphanBytes = $derived(items.filter((i) => i.references === 0).reduce((s, i) => s + i.size, 0));
  const fmt = (n: number) => (n > 1048576 ? (n / 1048576).toFixed(1) + ' MB' : (n / 1024).toFixed(0) + ' KB');

  async function trash(i: Info) {
    try { await api.post('attachments/trash', { path: i.path }); app.toast('Moved to trash. You can restore it from the trash.', 'ok'); load(); }
    catch (e) { app.error(e); }
  }

  async function trashAll() {
    const list = items.filter((i) => i.references === 0);
    if (!(await app.confirm('Move unused attachments to trash?', `${list.length} file(s), ${fmt(orphanBytes)}. They can be restored from the trash.`, 'Move to trash'))) return;
    for (const i of list) await api.post('attachments/trash', { path: i.path }).catch((e) => app.error(e));
    load();
  }
</script>

<div class="scroll view">
  <div class="inner">
    <h1>Attachment auditor</h1>
    <p class="muted">{items.length} attachment(s). {items.filter((i) => i.references === 0).length} aren't used by any note ({fmt(orphanBytes)}). Identical files are stored once.</p>
    <div class="bar">
      <label><input type="checkbox" bind:checked={onlyOrphans} /> Only unused</label>
      {#if orphanBytes > 0}<button class="btn" onclick={trashAll}>Move all unused to trash</button>{/if}
    </div>
    <table>
      <thead><tr><th>File</th><th>Size</th><th>Used by</th><th>Added</th><th></th></tr></thead>
      <tbody>
        {#each shown as i (i.path)}
          <tr>
            <td>
              {#if ['.png', '.jpg', '.jpeg', '.gif', '.webp', '.svg'].includes(i.extension)}<img src="/api/files/{i.path}" alt="" />{/if}
              <span class="mono">{i.path.split('/').pop()}</span>
            </td>
            <td>{fmt(i.size)}</td>
            <td>{i.references ? `${i.references} note(s)` : 'nothing'}</td>
            <td class="faint">{relativeTime(i.modified)}</td>
            <td class="actions">
              {#if i.extension === '.pdf'}<button class="btn ghost" onclick={() => layout.open({ kind: 'pdf', arg: i.path, title: 'PDF' }, 'new')}>Open</button>{/if}
              {#if !i.references}<button class="btn ghost" onclick={() => trash(i)}>Trash</button>{/if}
            </td>
          </tr>
        {:else}
          <tr><td colspan="5" class="faint">Nothing to show.</td></tr>
        {/each}
      </tbody>
    </table>
  </div>
</div>

<style>
  .view { flex: 1; padding: 0 28px; }
  .inner { max-width: 1000px; margin: 0 auto; padding: 24px 0 80px; }
  h1 { font: 700 28px/1.2 var(--font-display); }
  .bar { display: flex; gap: 12px; align-items: center; margin: 12px 0; font-size: 14px; }
  table { width: 100%; border-collapse: collapse; font-size: 13.5px; }
  th { text-align: left; font: 600 11px var(--font-mono); text-transform: uppercase; letter-spacing: .07em; color: var(--text-faint); padding: 6px 8px; border-bottom: 1px solid var(--line); }
  td { padding: 6px 8px; border-bottom: 1px solid var(--line); vertical-align: middle; }
  td img { width: 36px; height: 36px; object-fit: cover; border-radius: 4px; vertical-align: middle; margin-right: 8px; }
  .mono { font: 12px var(--font-mono); }
  .actions { text-align: right; }
</style>
