<script lang="ts">
  import { onMount } from 'svelte';
  import { diffComm } from 'node-diff3';
  import { api } from '../../lib/api';
  import { app } from '../../lib/state.svelte';
  import { openNode } from '../../lib/links.svelte';
  import type { HistoryVersion, NodeDto } from '../../lib/types';

  let { id }: { id: string } = $props();
  let versions = $state<HistoryVersion[]>([]);
  let current = $state<NodeDto | null>(null);
  let selected = $state<string | null>(null);
  let content = $state('');
  let compare = $state(true);

  const kinds: Record<string, string> = { save: 'Saved', before: 'Before change', external: 'Changed outside Quantos', restore: 'Restored', conflict: 'Kept from conflict' };

  async function load() {
    versions = await api.get<HistoryVersion[]>(`nodes/${id}/history`);
    current = await api.get<NodeDto>(`nodes/${id}`).catch(() => null);
    if (versions.length && !selected) pick(versions[Math.min(1, versions.length - 1)].version);
  }

  async function pick(version: string) {
    selected = version;
    content = (await api.get<{ content: string }>(`nodes/${id}/history/${version}`)).content;
  }

  async function rollback() {
    if (!selected) return;
    const ok = await app.confirm('Restore this version?', 'The current version is kept in history, so you can come back to it.', 'Restore version');
    if (!ok) return;
    try {
      await api.post(`nodes/${id}/rollback`, { version: selected });
      app.toast('Version restored.', 'ok', { label: 'Open', run: () => openNode(id, 'current') });
      await load();
    } catch (e) { app.error(e); }
  }

  const rows = $derived.by(() => {
    if (!current || !compare) return null;
    return diffComm(content.split('\n'), current.content.split('\n'));
  });

  onMount(load);
</script>

<div class="history">
  <aside class="scroll list">
    <h2 class="label">Versions</h2>
    {#if !current}<p class="warn">The node isn't in the vault right now; you can still read its versions.</p>{/if}
    {#each versions as v (v.version)}
      <button class="ver" class:on={v.version === selected} onclick={() => pick(v.version)}>
        <span>{new Date(v.savedAt).toLocaleString()}</span>
        <span class="faint">{kinds[v.kind] ?? v.kind} · {(v.size / 1024).toFixed(1)} KB</span>
      </button>
    {:else}
      <p class="faint">No history yet.</p>
    {/each}
  </aside>
  <section class="scroll preview">
    <div class="bar">
      <label><input type="checkbox" bind:checked={compare} disabled={!current} /> Compare with current</label>
      <button class="btn primary" disabled={!selected || !current} onclick={rollback}>Restore this version</button>
    </div>
    {#if rows}
      <div class="diff">
        {#each rows as block, i (i)}
          {#if block.common}<pre>{block.common.join('\n')}</pre>
          {:else}
            {#if block.buffer1?.length}<pre class="old">{block.buffer1.join('\n')}</pre>{/if}
            {#if block.buffer2?.length}<pre class="new">{block.buffer2.join('\n')}</pre>{/if}
          {/if}
        {/each}
      </div>
    {:else}
      <pre class="plain">{content}</pre>
    {/if}
  </section>
</div>

<style>
  .history { flex: 1; display: grid; grid-template-columns: 280px 1fr; min-height: 0; }
  .list { border-right: 1px solid var(--line); padding: 14px 10px; }
  .list h2 { margin: 0 6px 8px; }
  .ver { display: grid; gap: 2px; width: 100%; text-align: left; border: 0; background: none; color: var(--text-soft); padding: 8px 10px; border-radius: 7px; font-size: 13.5px; }
  .ver:hover { background: var(--bg-hover); }
  .ver.on { background: var(--bg-active); color: var(--text); }
  .ver .faint { font-size: 12px; }
  .preview { padding: 0 20px 40px; }
  .bar { display: flex; justify-content: space-between; align-items: center; padding: 12px 0; position: sticky; top: 0; background: var(--bg); }
  .bar label { display: flex; gap: 6px; align-items: center; font-size: 14px; color: var(--text-soft); }
  pre { margin: 0; font: 13px/1.6 var(--font-mono); white-space: pre-wrap; overflow-wrap: anywhere; color: var(--text-soft); padding: 0 8px; }
  .old { background: color-mix(in srgb, var(--danger) 14%, transparent); color: var(--text); }
  .new { background: color-mix(in srgb, var(--ok) 14%, transparent); color: var(--text); }
  .plain { padding: 8px; }
  .warn { color: var(--warn); font-size: 13px; padding: 0 6px; }
</style>
