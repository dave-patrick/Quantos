<script lang="ts">
  import { layout } from '../lib/layout.svelte';
  import Pane from './Pane.svelte';
  let { today }: { today: string } = $props();

  let ratio = $state(0.5);
  let dragging = $state(false);
  const gutter = 8;

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

  function resizeByKey(e: KeyboardEvent) {
    const delta = layout.split === 'horizontal'
      ? (e.key === 'ArrowUp' ? -0.03 : e.key === 'ArrowDown' ? 0.03 : 0)
      : (e.key === 'ArrowLeft' ? -0.03 : e.key === 'ArrowRight' ? 0.03 : 0);
    if (!delta) return;
    e.preventDefault();
    ratio = Math.min(0.85, Math.max(0.15, ratio + delta));
  }
</script>

{#if layout.panes.length === 1}
  <div class="panes single">
    <Pane pane={layout.panes[0]} index={0} {today} />
  </div>
{:else}
  <div class="panes {layout.split}" style={layout.split === 'horizontal' ? `grid-template-rows: calc(${ratio*100}% - ${gutter*ratio}px) ${gutter}px calc(${(1-ratio)*100}% - ${gutter*(1-ratio)}px);` : `grid-template-columns: calc(${ratio*100}% - ${gutter*ratio}px) ${gutter}px calc(${(1-ratio)*100}% - ${gutter*(1-ratio)}px);`}>
    <Pane pane={layout.panes[0]} index={0} {today} />
    <button class="spacer" class:dragging type="button" aria-label="Resize split panes" aria-orientation={layout.split === 'horizontal' ? 'horizontal' : 'vertical'} onmousedown={start} onkeydown={resizeByKey}></button>
    <Pane pane={layout.panes[1]} index={1} {today} />
  </div>
{/if}

<style>
  .panes { flex: 1; display: grid; min-height: 0; min-width: 0; }
  .panes.single { grid-template-columns: 1fr; }
  .panes.vertical { grid-template-columns: minmax(0, 1fr) 8px minmax(0, 1fr); }
  .panes.horizontal { grid-template-rows: minmax(0, 1fr) 8px minmax(0, 1fr); }
  .spacer { appearance: none; border: 0; padding: 0; background: transparent; cursor: col-resize; width: 8px; touch-action: none; }
  .panes.horizontal .spacer { cursor: row-resize; height: 8px; width: auto; }
  .spacer:hover, .spacer.dragging, .spacer:focus-visible { background: color-mix(in srgb, var(--accent) 60%, transparent); outline: none; border-radius: 2px; }
</style>
