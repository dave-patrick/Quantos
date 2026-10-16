<script lang="ts">
  import { onMount } from 'svelte';
  import { onChange } from '../lib/events';
  import { runQueryText, type QueryResult } from '../lib/query';
  import DataTable from './DataTable.svelte';
  import Icon from './Icon.svelte';

  let { text, onedit }: { text: string; onedit: () => void } = $props();
  let result = $state<QueryResult | null>(null);
  let timer: ReturnType<typeof setTimeout>;

  async function run() {
    try { result = await runQueryText(text); }
    catch (e) { result = { rows: [], total: 0, columns: [], error: (e as Error).message }; }
  }

  onMount(() => {
    run();
    return onChange(() => { clearTimeout(timer); timer = setTimeout(run, 500); });
  });
</script>

<div class="qblock">
  <div class="head">
    <Icon name="filter" size={13} /> <span>Query</span>
    {#if result && !result.error}<span class="faint">{result.total} result{result.total === 1 ? '' : 's'}</span>{/if}
    <button class="edit" onclick={onedit}><Icon name="code" size={12} /> Edit query</button>
  </div>
  {#if result?.error}
    <p class="error">{result.error}</p>
  {:else if result}
    <DataTable rows={result.rows} columns={result.columns} onchanged={run} />
  {/if}
</div>

<style>
  .qblock { margin: 6px 0; font-family: var(--font-body); }
  .head { display: flex; align-items: center; gap: 8px; font: 600 11px var(--font-mono); text-transform: uppercase; letter-spacing: .07em; color: var(--text-faint); margin-bottom: 4px; }
  .edit { margin-left: auto; display: inline-flex; gap: 4px; align-items: center; border: 0; background: none; color: var(--text-faint); font: inherit; cursor: pointer; }
  .edit:hover { color: var(--accent); }
  .error { color: var(--warn); font-size: 13px; margin: 4px 0; }
</style>
