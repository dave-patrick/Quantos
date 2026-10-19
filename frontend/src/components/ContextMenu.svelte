<script lang="ts" module>
  /** One line in a context menu. */
  export interface ContextMenuItem {
    /** The text on the line. */
    label?: string;
    /** The icon in front of it. */
    icon?: string;
    /** Red, for anything destructive. */
    danger?: boolean;
    /** What the line does. */
    run?: () => void;
    /** A line between items. */
    separator?: boolean;
    /** A heading, with the items under it. */
    group?: string;
  }
</script>

<script lang="ts">
  import { menu } from '../lib/menu';
  import Icon from './Icon.svelte';

  /** The right-click menu: a list of things that can be done with whatever was clicked. */
  let {
    x,
    y,
    items,
    onclose
  }: { x: number; y: number; items: ContextMenuItem[]; onclose: () => void } = $props();
</script>

<button class="backdrop" aria-label="Close menu" onclick={onclose}></button>
<div class="menu" role="menu" tabindex="-1" style="left:{x}px; top:{y}px" use:menu={{ onclose }}>
  {#each items as item, i (i)}
    {#if item.separator}
      <hr />
    {:else if item.group}
      <div class="grouplabel">{item.group}</div>
    {:else}
      <button role="menuitem" class:danger={item.danger}
        onclick={() => { onclose(); item.run?.(); }}><Icon name={item.icon ?? 'zap'} size={14} /> {item.label}</button>
    {/if}
  {/each}
</div>

<style>
  .backdrop { position: fixed; inset: 0; border: 0; background: transparent; z-index: 45; cursor: default; }
  .menu { position: fixed; z-index: 46; min-width: 210px; padding: 5px; border-radius: 8px; background: var(--bg-raised); border: 1px solid var(--line-strong); box-shadow: var(--shadow); display: grid; }
  .menu button { display: flex; align-items: center; gap: 8px; text-align: left; padding: 7px 10px; border: 0; border-radius: 5px; background: none; color: var(--text-soft); font-size: 13.5px; }
  .menu button:hover { background: var(--bg-hover); color: var(--text); }
  .menu button.danger:hover { color: var(--danger); }
  .grouplabel { padding: 6px 10px 2px; font: 600 10px/1 var(--font-mono); letter-spacing: .08em; text-transform: uppercase; color: var(--text-faint); }
  hr { border: 0; border-top: 1px solid var(--line); margin: 4px 0; }
</style>
