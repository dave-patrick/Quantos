<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../lib/state.svelte';
  import { layout, type TabKind, type TabSpec } from '../lib/layout.svelte';
  import { capture } from '../lib/capture.svelte';
  import { run } from '../lib/commands.svelte';
  import { views } from '../lib/views.svelte';
  import { terminal } from '../lib/terminal.svelte';
  import { prefs } from '../lib/prefs.svelte';
  import { phoneDestinations, normalizePhoneNavigation } from '../lib/phoneNavigation';
  import wordmark from '../assets/quantos-wordmark.svg?raw';
  import Icon from './Icon.svelte';
  import Pane from './Pane.svelte';
  import PhonePanel from './PhonePanel.svelte';
  import WorkspaceSwitcher from './WorkspaceSwitcher.svelte';
  import TerminalDrawer from './TerminalDrawer.svelte';

  let { today }: { today: string } = $props();
  let more = $state(false);
  const active = $derived(layout.activeTab);
  const destinations = $derived(normalizePhoneNavigation(prefs.phoneNavigation).map(kind => phoneDestinations.find(d => d.kind === kind)!));
  const roots = $derived(destinations.map(d => d.kind));
  const leaf = $derived(!!active && (!roots.includes(active.kind) || active.arg === 'advanced'));
  const destination = $derived(more || app.rightOpen || active?.arg === 'advanced' ? 'more' : active?.kind === 'node' && roots.includes('notes') ? 'notes' : active && roots.includes(active.kind) ? active.kind : 'more');
  const groups: { title: string; items: { kind: TabKind; title: string; icon: string; arg?: string }[] }[] = [
    { title: 'Plan', items: [
      { kind: 'daily', title: 'Daily notes', icon: 'calendar' }, { kind: 'timeline', title: 'Timeline', icon: 'calendar' },
      { kind: 'focus', title: 'Focus', icon: 'timer' }, { kind: 'habits', title: 'Habits', icon: 'check-square' },
      { kind: 'goals', title: 'Goals', icon: 'target' }, { kind: 'sprints', title: 'Sprints', icon: 'zap' },
      { kind: 'gantt', title: 'Gantt', icon: 'list' }, { kind: 'review', title: 'Stale tasks', icon: 'history' }
    ] },
    { title: 'Explore', items: [
      { kind: 'search', title: 'Search everything', icon: 'search' }, { kind: 'ask', title: 'Ask your notes', icon: 'sparkles' },
      { kind: 'graph', title: 'Graph', icon: 'graph' }, { kind: 'map', title: 'Map', icon: 'map' },
      { kind: 'replace', title: 'Find and replace', icon: 'replace' }
    ] },
    { title: 'Organise', items: [
      { kind: 'notes', title: 'Advanced notes browser', icon: 'folder', arg: 'advanced' },
      { kind: 'templates', title: 'Templates', icon: 'copy' }, { kind: 'supertags', title: 'Supertags', icon: 'tag' },
      { kind: 'dictionary', title: 'Metadata dictionary', icon: 'book' }, { kind: 'attachments', title: 'Attachments', icon: 'paperclip' },
      { kind: 'trash', title: 'Trash', icon: 'trash' }, { kind: 'diagnostics', title: 'Vault diagnostics', icon: 'activity' }
    ] }
  ];

  async function open(spec: TabSpec) {
    try { await layout.open(spec); more = false; app.rightOpen = false; }
    catch (e) { app.error(e); }
  }
  async function back() {
    if (app.rightOpen) app.rightOpen = false;
    else if (more) more = false;
    else if (layout.pane.back.length) await layout.back();
    else await open({ kind: 'notes' });
  }
  function escape(e: KeyboardEvent) {
    if (e.key !== 'Escape' || e.defaultPrevented || app.dialog || app.asking || app.paletteOpen || capture.open || terminal.open) return;
    if (app.rightOpen || more) { e.preventDefault(); app.rightOpen = false; more = false; }
  }
  function showMore() { app.rightOpen = false; more = !more; }

  onMount(() => {
    app.leftOpen = false; app.rightOpen = false;
    // Move the old desktop-style landing screen to Notes once, keeping open notes and saved pages.
    if (!localStorage.getItem('quantos.phone.navigation')) {
      localStorage.setItem('quantos.phone.navigation', '1');
      if (active?.kind !== 'node') open({ kind: 'notes' });
    }
  });
</script>

<svelte:window onkeydown={escape} />

<div class="phone-shell">
  <header class="phone-header">
    {#if app.rightOpen || more}
      <h1>{app.rightOpen ? 'Details' : 'More'}</h1>
      <button class="icon-btn" aria-label={app.rightOpen ? 'Close details' : 'Close More'} onclick={back}><Icon name="x" size={22} /></button>
    {:else}
      {#if leaf}
        <button class="icon-btn" aria-label="Back" onclick={back}><Icon name="arrow-left" size={22} /></button>
        <span class="screen-title">{active?.kind === 'node' ? 'Note' : active?.title}</span>
      {:else}
        <span class="phone-wordmark" role="img" aria-label="Quantos">{@html wordmark}</span>
      {/if}
      <div class="phone-actions">
        {#if !leaf}<button class="icon-btn" aria-label="Back" disabled={!layout.pane.back.length} onclick={back}><Icon name="arrow-left" size={21} /></button>{/if}
        <button class="icon-btn" aria-label="Forward" disabled={!layout.pane.forward.length} onclick={() => layout.forward()}><Icon name="arrow-right" size={21} /></button>
        <button class="icon-btn" aria-label="Search notes" onclick={() => open({ kind: 'search' })}><Icon name="search" size={21} /></button>
        <button class="capture-button" onclick={() => capture.show()}><Icon name="inbox" size={19} /><span class="capture-label">Capture</span></button>
      </div>
    {/if}
  </header>

  <main class="phone-main">
    <!-- Keep the editor mounted while opening More or Details, including its unsaved draft. -->
    <div class="phone-workspace" hidden={more || app.rightOpen}>
      <Pane pane={layout.pane} index={layout.activePane} {today} />
    </div>
    {#if app.rightOpen}<PhonePanel {today} />
    {:else if more}
      <div class="phone-more scroll">
        <div class="workspace-row"><span>Workspace</span><WorkspaceSwitcher /></div>
        <section aria-label="Notes and settings">
          <button class="more-row" onclick={() => open({ kind: 'notes' })}><Icon name="file-text" /> Notes <Icon name="chevron-right" /></button>
          <button class="more-row" onclick={() => open({ kind: 'settings' })}><Icon name="settings" /> Settings <Icon name="chevron-right" /></button>
          <button class="more-row" onclick={() => { more = false; app.rightOpen = true; }}><Icon name="panel-right" /> Files and note details <Icon name="chevron-right" /></button>
          <button class="more-row" onclick={() => open({ kind: 'notifications' })}><Icon name="bell" /> Notifications <Icon name="chevron-right" /></button>
        </section>
        {#if views.saved.length}
          <section><h2>Saved views</h2>{#each views.saved as v (v.id)}<button class="more-row" onclick={() => open({ kind: 'view', arg: v.id, title: v.name })}><Icon name={v.icon ?? 'list'} /> {v.name}<Icon name="chevron-right" /></button>{/each}</section>
        {/if}
        {#each groups as group (group.title)}
          <section><h2>{group.title}</h2>{#each group.items as item (item.kind)}<button class="more-row" onclick={() => open(item)}><Icon name={item.icon} />{item.title}<Icon name="chevron-right" /></button>{/each}</section>
        {/each}
        <section>
          <h2>Workspace tools</h2>
          <button class="more-row" onclick={() => { more = false; terminal.toggle(); }}><Icon name="terminal" /> Terminal <Icon name="chevron-right" /></button>
          <button class="more-row" onclick={() => { more = false; run('palette.commands'); }}><Icon name="command" /> All commands <Icon name="chevron-right" /></button>
          <details class="open-pages"><summary>Open pages</summary>{#each layout.panes as pane, i (pane.id)}{#each pane.tabs as tab (tab.key)}<button class="more-row" onclick={() => { layout.activate(i, tab.key); more = false; }}><Icon name="file-text" /> {tab.title}<Icon name="chevron-right" /></button>{/each}{/each}</details>
          <button class="more-row" onclick={() => open({ kind: 'help' })}><Icon name="help-circle" /> Help <Icon name="chevron-right" /></button>
          <button class="more-row" onclick={() => open({ kind: 'about' })}><Icon name="info" /> About Quantos <Icon name="chevron-right" /></button>
        </section>
      </div>
    {/if}
    {#if terminal.open}<TerminalDrawer />{/if}
  </main>

  <nav class="phone-nav" aria-label="Main navigation" style:grid-template-columns={`repeat(${destinations.length + 1}, minmax(0, 1fr))`}>
    {#each destinations as d (d.kind)}
      <button aria-current={destination === d.kind ? 'page' : undefined} onclick={() => d.kind === 'capture' ? capture.show() : open({ kind: d.kind })}><span class="nav-icon"><Icon name={d.icon} size={22} /></span><span>{d.label}</span></button>
    {/each}
    <button aria-current={destination === 'more' ? 'page' : undefined} onclick={showMore}><span class="nav-icon"><Icon name="menu" size={22} /></span><span>More</span></button>
  </nav>
</div>

<style>
  @media (max-width: 440px) { .capture-label { display: none; } .phone-actions { gap: 0; } .phone-header { gap: 0; padding: 0 4px; } .phone-wordmark :global(svg) { max-width: 80px; } }
  .phone-shell { height: 100%; display: grid; grid-template-rows: 56px minmax(0, 1fr) 68px; background: var(--bg-panel); }
  .phone-header { display: flex; align-items: center; gap: 8px; padding: 0 12px; border-bottom: 1px solid var(--line); min-width: 0; }
  .phone-header h1, .screen-title { flex: 1; min-width: 0; font: 600 20px/1.2 var(--font-body); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .phone-wordmark { display: flex; height: 22px; color: var(--text); margin-right: auto; min-width: 0; }
  .phone-wordmark :global(svg) { height: 100%; width: auto; max-width: 132px; }
  .phone-actions { display: flex; align-items: center; gap: 8px; margin-left: auto; }
  .icon-btn { min-width: 48px; min-height: 48px; background: transparent; flex-shrink: 0; }
  .capture-button { display: flex; align-items: center; justify-content: center; gap: 8px; min-height: 48px; padding: 0 12px; border: 0; border-radius: 24px; background: var(--accent-wash); color: var(--accent-strong); font-weight: 600; }
  .phone-main, .phone-workspace { display: flex; flex-direction: column; min-height: 0; min-width: 0; }
  .phone-workspace { flex: 1; }
  .phone-workspace[hidden] { display: none; }
  .phone-nav { display: grid; grid-template-columns: repeat(4, minmax(0,1fr)); gap: 8px; padding: 4px 8px; border-top: 1px solid var(--line); background: var(--bg-raised); }
  .phone-nav button { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 2px; min-width: 0; padding: 0; border: 0; background: transparent; color: var(--text-muted); font: 600 12px/1.3 var(--font-body); }
  .phone-nav button > span:last-child { max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .nav-icon { display: flex; justify-content: center; align-items: center; width: 56px; height: 30px; border-radius: 18px; }
  .phone-nav button[aria-current='page'] { color: var(--accent-strong); }
  .phone-nav button[aria-current='page'] .nav-icon { background: var(--accent-wash); }
  .phone-more { flex: 1; padding: 8px 16px 24px; }
  .workspace-row { display: flex; align-items: center; justify-content: space-between; min-height: 56px; gap: 16px; color: var(--text-muted); }
  section { padding: 12px 0; border-bottom: 1px solid var(--line); }
  h2 { font: 700 18px/1.4 var(--font-display); margin: 8px 0 12px; }
  .more-row { display: flex; align-items: center; gap: 16px; min-height: 52px; width: 100%; text-align: left; border: 0; border-radius: 8px; padding: 8px; color: var(--text); background: transparent; font: 400 16px/1.4 var(--font-body); }
  .more-row:hover { background: var(--bg-hover); }
  .more-row :global(.icon:last-child) { margin-left: auto; color: var(--text-faint); flex-shrink: 0; }
  .open-pages summary { min-height: 48px; padding: 12px 8px; cursor: pointer; }
</style>
