<script lang="ts">
  import { app } from '../lib/state.svelte';
  import { layout } from '../lib/layout.svelte';
  import { docs } from '../lib/docs.svelte';
  import { isWindowsClient } from '../lib/platform';
  import { notifications } from '../lib/notifications.svelte';
  import { prefs } from '../lib/prefs.svelte';
  import { focus, clock } from '../lib/focus.svelte';
  import Icon from './Icon.svelte';

  const activeDoc = $derived(layout.activeTab?.nodeId ? docs.peek(layout.activeTab.nodeId) : undefined);
  const vaultName = $derived(app.status?.vault?.split(/[\\/]/).filter(Boolean).slice(-2).join('\\') ?? '');
  const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
</script>

<footer class="status">
  <span class="item conn" class:off={!app.connected} title={app.connected ? 'Live updates connected' : 'Reconnecting to the backend…'}>
    <span class="dot"></span>{app.connected ? 'Connected' : 'Reconnecting…'}
  </span>
  {#if activeDoc}
    <span class="item" class:warn={activeDoc.status === 'conflict' || activeDoc.status === 'error'}>
      {#if activeDoc.status === 'saving'}Saving…{:else if activeDoc.status === 'dirty'}Unsaved changes{:else if activeDoc.status === 'conflict'}Conflict: choose a version{:else if activeDoc.status === 'error'}Save failed{:else if activeDoc.status === 'saved'}All changes saved{:else if activeDoc.status === 'readonly'}Read-only{:else if activeDoc.status === 'missing'}Note not found{:else if activeDoc.status === 'trashed'}In the trash{/if}
    </span>
  {/if}
  {#if activeDoc?.node?.locked}<span class="item"><Icon name="lock" size={12} /> Locked</span>{/if}
  <span class="spacer"></span>
  {#if focus.running}
    <button class="item link" class:brk={focus.running.phase === 'break'} title="Open Focus" onclick={() => layout.open({ kind: 'focus', title: 'Focus' }, 'current')}><Icon name="timer" size={12} /> {clock(focus.remaining)}{focus.running.title ? ` · ${focus.running.title.length > 28 ? focus.running.title.slice(0, 27) + '…' : focus.running.title}` : ''}</button>
    <button class="item link stop" title={focus.running.phase === 'break' ? 'End break' : 'Stop and log'} aria-label="Stop focus timer" onclick={() => focus.stop()}>■</button>
  {/if}
  {#if prefs.reading}
    <button class="item link" title="Notes are read-only on this device. Click (or Ctrl+Alt+R) to edit again." onclick={() => (prefs.reading = false)}><Icon name="book" size={12} /> Reading mode</button>
  {/if}
  {#if notifications.unread}
    <button class="item link" onclick={() => { app.rightOpen = true; app.rightTab = 'notifications'; }}><Icon name="bell" size={12} /> {notifications.unread}</button>
  {/if}
  <span class="item" title="Dates use this device's time zone">{zone}</span>
  <span class="item" title={app.status?.vault ?? ''}><Icon name="database" size={12} /> {vaultName}</span>
  <span class="item">{isWindowsClient ? 'Windows client' : 'Browser'}</span>
</footer>

<style>
  .status { grid-area: status; display: flex; align-items: center; gap: 2px; padding: 0 8px; border-top: 1px solid var(--line); background: var(--bg-raised); font: 11.5px/1 var(--font-mono); color: var(--text-faint); min-width: 0; overflow: hidden; }
  .item { display: inline-flex; align-items: center; gap: 6px; padding: 0 8px; white-space: nowrap; }
  .item.warn { color: var(--danger); }
  .spacer { flex: 1; }
  .dot { width: 7px; height: 7px; border-radius: 50%; background: var(--ok); }
  .conn.off .dot { background: var(--warn); animation: pulse 1s infinite alternate; }
  .link.brk { color: var(--ok); }
  .stop { padding: 0 6px 0 0; }
  .link { border: 0; background: none; color: var(--accent); font: inherit; cursor: pointer; height: 100%; }
  @keyframes pulse { to { opacity: .3; } }
</style>
