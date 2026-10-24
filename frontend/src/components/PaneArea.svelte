<script lang="ts">
  import { layout } from '../lib/layout.svelte';
  import Pane from './Pane.svelte';
  let { today }: { today: string } = $props();

  let ratio = $state(0.5);
  let dragging = $state(false);

  function start(e: MouseEvent) {
    e.preventDefault();
    dragging = true;
    const container = (e.currentTarget as HTMLElement).parentElement!;
    const rect = container.getBoundingClientRect();
    const horizontal = layout.split === 'horizontal';
    const move = (ev: MouseEvent) => {
      const pos = horizontal ? ev.clientY - rect.top : ev.clientX - rect.left;
      const size = horizontal ? rect.height : rect.width;
      ratio = Math.min(0.85, Math.max(0.15, pos / size));
    };
    const up = () => {
      dragging = false;
      window.removeEventListener('mousemove', move);
      window.removeEventListener('mouseup', up);
    };
    window.addEventListener('mousemove', move);
    window.addEventListener('mouseup', up);
  }
</script>

{#if layout.panes.length === 1}
  <div class="panes single">
    <Pane pane={layout.panes[0]} index={0} {today} />
  </div>
{:else}
  <div class="panes {layout.split}" style={layout.split === 'horizontal' ? `grid-template-rows: ${ratio*100}% 8px ${ (1-ratio)*100}%;` : `grid-template-columns: ${ratio*100}% 8px ${ (1-ratio)*100}%;`}>
    <Pane pane={layout.panes[0]} index={0} {today} />
    <div class="spacer" class:dragging onmousedown={start}></div>
    <Pane pane={layout.panes[1]} index={1} {today} />
  </div>
{/if}

<style>
  .panes { flex: 1; display: grid; min-height: 0; min-width: 0; }
  .panes.single { grid-template-columns: 1fr; }
  .panes.vertical { grid-template-columns: 1fr 8px 1fr; }
  .panes.horizontal { grid-template-rows: 1fr 8px 1fr; }
  .spacer { background: transparent; cursor: col-resize; width: 8px; touch-action: none; }
  .panes.horizontal .spacer { cursor: row-resize; height: 8px; }
  .splitter:hover, .splitter.dragging { background: var(--accent); }
</style>