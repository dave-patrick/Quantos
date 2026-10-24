<script lang="ts">
  import { app } from '../lib/state.svelte';
  import { layout } from '../lib/layout.svelte';
  import { run, keysFor, displayKeys } from '../lib/commands.svelte';
  import { toolbar } from '../lib/toolbar.svelte';
  import { isWindowsClient } from '../lib/platform';
  import Icon from './Icon.svelte';
  import WorkspaceSwitcher from './WorkspaceSwitcher.svelte';
  // The wordmark (brand/quantos-wordmark.svg) is inlined: its letters take the text colour, so it suits light and dark.
  import wordmark from '../assets/quantos-wordmark.svg?raw';
  import { capture } from '../lib/capture.svelte';

  const pane = $derived(layout.pane);
</script>

<header class="header">
  <div class="brand">
    <button class="icon-btn" aria-label="Toggle left sidebar" aria-pressed={app.leftOpen} onclick={() => (app.leftOpen = !app.leftOpen)}>
      <Icon name={app.leftOpen ? 'panel-left-close' : 'panel-left-open'} size={18} stroke={2} />
    </button>
    <span class="wordmark" role="img" aria-label="Quantos">{@html wordmark}</span>
    <WorkspaceSwitcher />
  </div>

  <nav class="toolbar" aria-label="Quick actions">
    <button class="icon-btn" title="Back ({displayKeys(keysFor('nav.back'))})" disabled={!pane.back.length} onclick={() => layout.back()}><Icon name="arrow-left" /></button>
    <button class="icon-btn" title="Forward ({displayKeys(keysFor('nav.forward'))})" disabled={!pane.forward.length} onclick={() => layout.forward()}><Icon name="arrow-right" /></button>
    <span class="sep"></span>
    {#each toolbar.items as item (item.id)}
      <button class="tool" title="{item.title}{keysFor(item.id) ? ` (${displayKeys(keysFor(item.id))})` : ''}" onclick={() => run(item.id)}>
        <Icon name={item.icon ?? 'zap'} size={15} /><span>{item.label}</span>
      </button>
    {/each}
    <button class="tool" title="Quick Capture" onclick={() => capture.show()}>
      <Icon name="inbox" size={15} /><span>Quick Capture</span>
    </button>
  </nav>

  <div class="right">
    <button class="search" onclick={() => run('palette.files')}>
      <Icon name="search" size={14} /> <span>Search or jump to…</span> <kbd>{displayKeys(keysFor('palette.files'))}</kbd>
    </button>
    {#if !isWindowsClient}<span class="requires-windows" title="Global capture shortcuts, the always-on-top scratchpad, native notifications and the terminal need the Windows client.">Browser</span>{/if}
    <button class="icon-btn" aria-label="Toggle right sidebar" aria-pressed={app.rightOpen} onclick={() => (app.rightOpen = !app.rightOpen)}>
      <Icon name={app.rightOpen ? 'panel-right-close' : 'panel-right-open'} size={18} stroke={2} />
    </button>
  </div>
</header>

<style>
  .header { grid-area: header; display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 0 8px;
    border: 1px solid var(--line); border-radius: 12px; background: linear-gradient(180deg, var(--bg-raised), color-mix(in srgb, var(--bg-raised) 85%, black 5%)); min-width: 0; margin: 8px 8px 4px; box-shadow: 0 4px 12px rgb(0 0 0 / 0.2), 0 1px 3px rgb(0 0 0 / 0.15); }
  .brand { display: flex; align-items: center; gap: 8px; min-width: 0; }
  .brand > .icon-btn, .right > .icon-btn { width: 32px; height: 32px; border-radius: 8px; }
  .wordmark { display: inline-flex; height: 23.75px; margin: 0 4px 0 2px; color: var(--text); flex: none; }
  .wordmark :global(svg) { height: 100%; width: auto; }
  .toolbar { display: flex; align-items: center; gap: 2px; min-width: 0; overflow: hidden; }
  .toolbar .icon-btn:disabled { opacity: .35; cursor: default; background: none; }
  .sep { width: 1px; height: 18px; background: var(--line); margin: 0 6px; }
  .tool { display: inline-flex; align-items: center; gap: 6px; height: 28px; padding: 0 9px; border: 0; border-radius: 6px; background: transparent; color: var(--text-muted); font-size: 13px; font-weight: 600; white-space: nowrap; }
  .tool:hover { background: var(--bg-hover); color: var(--text); }
  .right { display: flex; align-items: center; gap: 8px; }
  .search { display: flex; align-items: center; gap: 8px; height: 30px; padding: 0 8px 0 10px; width: 260px; border-radius: 7px;
    border: 1px solid var(--line-strong); background: var(--bg-input); color: var(--text-faint); font-size: 13px; }
  .search span { flex: 1; text-align: left; }
  .search:hover { border-color: var(--accent); color: var(--text-muted); }
  @media (max-width: 1150px) { .tool span { display: none; } .search { width: 180px; } }
  @media (max-width: 720px) { .toolbar { display: none; } .search span { display: none; } .search { width: auto; } }
</style>
