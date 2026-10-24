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
  const newItem = $derived(toolbar.items.find((item) => item.id === 'note.new'));
  const splitItem = $derived(toolbar.items.find((item) => item.id === 'pane.split.right'));
  const centerItems = $derived(toolbar.items.filter((item) => item.id !== 'note.new' && item.id !== 'pane.split.right'));
</script>

<header class="header">
  <div class="brand">
    <button class="icon-btn" aria-label="Toggle left sidebar" aria-pressed={app.leftOpen} onclick={() => (app.leftOpen = !app.leftOpen)}>
      <Icon name={app.leftOpen ? 'panel-left-close' : 'panel-left-open'} size={18} stroke={2} />
    </button>
    <span class="wordmark" role="img" aria-label="Quantos">{@html wordmark}</span>
    <WorkspaceSwitcher />
    {#if newItem}
      <button class="tool" class:icon-only={!toolbar.showLabels} title="{newItem.title}{keysFor(newItem.id) ? ` (${displayKeys(keysFor(newItem.id))})` : ''}" onclick={() => run(newItem.id)}>
        <Icon name={newItem.icon ?? 'plus'} size={15} />{#if toolbar.showLabels}<span>{newItem.label}</span>{/if}
      </button>
    {/if}
  </div>

  <nav class="toolbar" aria-label="Quick actions">
    <button class="icon-btn" title="Back ({displayKeys(keysFor('nav.back'))})" disabled={!pane.back.length} onclick={() => layout.back()}><Icon name="arrow-left" /></button>
    <button class="icon-btn" title="Forward ({displayKeys(keysFor('nav.forward'))})" disabled={!pane.forward.length} onclick={() => layout.forward()}><Icon name="arrow-right" /></button>
    <span class="sep"></span>
    {#each centerItems as item (item.id)}
      <button class="tool" title="{item.title}{keysFor(item.id) ? ` (${displayKeys(keysFor(item.id))})` : ''}" onclick={() => run(item.id)}>
        <Icon name={item.icon ?? 'zap'} size={15} /><span>{item.label}</span>
      </button>
    {/each}
  </nav>

  <div class="right">
    {#if splitItem}
      <button class="tool" class:icon-only={!toolbar.showLabels} title="{splitItem.title}{keysFor(splitItem.id) ? ` (${displayKeys(keysFor(splitItem.id))})` : ''}" onclick={() => run(splitItem.id)}>
        <Icon name={splitItem.icon ?? 'split'} size={15} />{#if toolbar.showLabels}<span>{splitItem.label}</span>{/if}
      </button>
    {/if}
    <button class="tool" class:icon-only={!toolbar.showLabels} title="Quick Capture" onclick={() => capture.show()}>
      <Icon name="inbox" size={15} />{#if toolbar.showLabels}<span>Quick Capture</span>{/if}
    </button>
    <button class="search" class:labelled={toolbar.showLabels} aria-label="Search or jump to" title="Search or jump to ({displayKeys(keysFor('palette.files'))})" onclick={() => run('palette.files')}>
      <Icon name="search" size={16} />{#if toolbar.showLabels}<span>Search</span>{/if}
    </button>
    {#if !isWindowsClient}<span class="requires-windows" title="Global capture shortcuts, the always-on-top scratchpad, native notifications and the terminal need the Windows client.">Browser</span>{/if}
    <button class="icon-btn" aria-label="Toggle right sidebar" aria-pressed={app.rightOpen} onclick={() => (app.rightOpen = !app.rightOpen)}>
      <Icon name={app.rightOpen ? 'panel-right-open' : 'panel-right-close'} size={18} stroke={2} />
    </button>
  </div>
</header>

<style>
  .header { grid-area: header; display: grid; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: 12px; padding: 0 8px;
    border: 1px solid var(--line); border-radius: 12px; background: linear-gradient(180deg, var(--bg-raised), color-mix(in srgb, var(--bg-raised) 85%, black 5%)); min-width: 0; margin: 4px 8px 0; box-shadow: 0 4px 12px rgb(0 0 0 / 0.2), 0 1px 3px rgb(0 0 0 / 0.15); }
  .brand { display: flex; align-items: center; gap: 8px; min-width: 0; }
  .brand > .icon-btn, .right > .icon-btn { width: 32px; height: 32px; border-radius: 8px; }
  .header .brand > .icon-btn, .header .right > .icon-btn { background: transparent; }
  .header .brand > .icon-btn:hover, .header .right > .icon-btn:hover { background: transparent; color: var(--text); }
  .header .icon-btn[aria-pressed='true'] { background: transparent; color: var(--accent-strong); }
  .wordmark { display: inline-flex; height: 23.75px; margin: 0 4px 0 2px; color: var(--text); flex: none; }
  .wordmark :global(svg) { height: 100%; width: auto; }
  .toolbar { display: flex; align-items: center; justify-content: center; gap: 2px; min-width: 0; overflow: hidden; }
  .toolbar .icon-btn:disabled { opacity: .35; cursor: default; background: none; }
  .sep { width: 1px; height: 18px; background: var(--line); margin: 0 6px; }
  .tool { display: inline-flex; align-items: center; justify-content: center; gap: 6px; height: 28px; padding: 0 9px; border: 0; border-radius: 6px; background: transparent; color: var(--text-muted); font-size: 13px; font-weight: 600; white-space: nowrap; }
  .tool.icon-only { width: 32px; padding: 0; }
  .tool:hover { background: var(--bg-hover); color: var(--text); }
  .right { display: flex; align-items: center; gap: 4px; min-width: max-content; }
  .search { display: flex; align-items: center; justify-content: center; gap: 6px; width: 32px; height: 30px; padding: 0; border: 0; border-radius: 7px;
    background: transparent; color: var(--text-muted); cursor: pointer; }
  .search.labelled { width: auto; padding: 0 8px; }
  .search:hover { background: transparent; color: var(--text); }
  @media (max-width: 1150px) { .tool span { display: none; } }
  @media (max-width: 720px) { .toolbar { display: none; } }
</style>
