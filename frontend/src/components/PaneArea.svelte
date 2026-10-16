<script lang="ts">
  import { layout } from '../lib/layout.svelte';
  import Pane from './Pane.svelte';
  let { today }: { today: string } = $props();
</script>

<div class="panes {layout.split}">
  {#each layout.panes as pane, i (pane.id)}
    <Pane {pane} index={i} {today} />
  {/each}
</div>

<style>
  .panes { flex: 1; display: grid; min-height: 0; min-width: 0; }
  .panes.vertical { grid-template-columns: 1fr 1fr; }
  .panes.horizontal { grid-template-rows: 1fr 1fr; }
  .panes.vertical > :global(.pane + .pane) { border-left: 1px solid var(--line); }
  .panes.horizontal > :global(.pane + .pane) { border-top: 1px solid var(--line); }
</style>
