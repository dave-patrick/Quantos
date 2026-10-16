<script lang="ts">
  import { onDestroy } from 'svelte';
  import { layout } from '../lib/layout.svelte';
  import { docs, type Doc } from '../lib/docs.svelte';
  import { relativeTime } from '../lib/dates';
  import Properties from './Properties.svelte';

  const id = $derived(layout.activeTab?.nodeId ?? null);
  let doc = $state<Doc | null>(null);
  let held: string | null = null;

  $effect(() => {
    const next = id;
    if (held === next) return;
    if (held) docs.release(held);
    held = next;
    doc = next ? docs.acquire(next) : null;
  });
  onDestroy(() => { if (held) docs.release(held); });
</script>

<div class="scroll inspector">
  {#if doc?.node}
    <h3 class="title">{doc.node.title}</h3>
    <dl class="facts">
      <dt>Type</dt><dd>{doc.node.type}</dd>
      <dt>Workspace</dt><dd>{doc.node.workspace || 'global'}</dd>
      <dt>Created</dt><dd>{relativeTime(doc.node.created)}</dd>
      <dt>Modified</dt><dd>{relativeTime(doc.node.modified)}</dd>
      <dt>File</dt><dd class="mono">{doc.node.path}</dd>
      <dt>Identity</dt><dd class="mono">{doc.node.id}</dd>
    </dl>
    <Properties {doc} compact />
  {:else}
    <p class="faint">Open a note to inspect its properties.</p>
  {/if}
</div>

<style>
  .inspector { flex: 1; padding: 12px; }
  .title { font: 700 16px/1.3 var(--font-display); margin-bottom: 10px; }
  .facts { display: grid; grid-template-columns: 80px 1fr; gap: 4px 10px; font-size: 12.5px; margin: 0 0 14px; }
  dt { color: var(--text-faint); }
  dd { margin: 0; color: var(--text-soft); overflow-wrap: anywhere; }
  .mono { font: 11px var(--font-mono); }
</style>
