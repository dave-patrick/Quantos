<script lang="ts">
  import { layout } from '../lib/layout.svelte';
  import Graph from './Graph.svelte';
  let hops = $state(2);
  const id = $derived(layout.activeTab?.nodeId ?? null);
</script>

<div class="local">
  <div class="bar"><span class="label">Local graph</span>
    <select class="input" bind:value={hops}><option value={1}>1 hop</option><option value={2}>2 hops</option></select></div>
  {#if id}<Graph centerId={id} {hops} height={420} labels={false} />{:else}<p class="faint">Open a note to see its neighbourhood.</p>{/if}
</div>

<style>
  .local { padding: 10px; }
  .bar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; }
  .bar .input { width: auto; height: 26px; font-size: 12px; }
</style>
