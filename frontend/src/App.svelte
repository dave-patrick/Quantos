<script lang="ts">
  import { onMount } from 'svelte';
  import { ApiError, onUnauthorized } from './lib/api';
  import { startEvents, connection } from './lib/events';
  import { app } from './lib/state.svelte';
  import { layout } from './lib/layout.svelte';
  import { tasks } from './lib/tasks.svelte';
  import { bridge, isWindowsClient } from './lib/platform';
  import { docs } from './lib/docs.svelte';
  import { supertags } from './lib/supertags.svelte';
  import { views } from './lib/views.svelte';
  import { templatesStore } from './lib/templates.svelte';
  import { loadCustomizations } from './lib/customize.svelte';
  import { plugins } from './lib/plugins.svelte';
  import { openNode } from './lib/links.svelte';
  import './lib/editorCommands';
  import Shell from './components/Shell.svelte';
  import Setup from './components/Setup.svelte';
  import Splash from './components/Splash.svelte';
  import CaptureWindow from './components/CaptureWindow.svelte';
  import ScratchpadWindow from './components/ScratchpadWindow.svelte';

  const route = location.pathname;
  let phase = $state<'loading' | 'auth' | 'setup' | 'ready' | 'error'>('loading');
  let message = $state('');

  async function boot() {
    try {
      const status = await app.refreshStatus();
      if (route === '/capture' || route === '/scratchpad') { phase = 'ready'; return; }
      if (!status.vault) { phase = 'setup'; return; }
      const workspaces = status.workspaces ?? [];
      app.workspace = status.lastWorkspace && workspaces.includes(status.lastWorkspace) ? status.lastWorkspace : workspaces.includes('default') ? 'default' : workspaces[0] ?? 'default';
      await Promise.all([
        app.loadTitles(), tasks.load().catch(() => {}), layout.load(app.workspace), supertags.load(), views.load(), templatesStore.load(), loadCustomizations()
      ]);
      if (layout.panes.every((p) => p.tabs.length === 0)) await layout.open({ kind: 'daily', title: 'Daily' });
      // Deep link from the CLI or a notification: /note/<id>
      const deep = /^\/note\/([0-9A-HJKMNP-TV-Z]{26})$/.exec(route);
      if (deep) { await openNode(deep[1], 'new'); history.replaceState(null, '', '/'); }
      startEvents();
      phase = 'ready';
      plugins.start();
      if (status.syncedFolderWarning) app.toast(status.syncedFolderWarning, 'warn');
      const recovered = status.reconcile?.recovered.length ?? 0;
      if (recovered) app.toast(`Recovered ${recovered} interrupted operation(s) at startup. See Vault diagnostics.`, 'warn');
      if (status.safeMode) app.toast('Safe mode is on: plugins are turned off.', 'info');
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) phase = 'auth';
      else { phase = 'error'; message = (err as Error).message; }
    }
  }

  onMount(() => {
    onUnauthorized(() => (phase = 'auth'));
    connection.listeners.add((c) => (app.connected = c));
    bridge.send({ type: 'ready' });
    bridge.on(async (msg) => {
      if (msg?.type === 'flush') {
        await docs.flushAll();
        bridge.send({ type: 'pending-saves', count: docs.pendingCount() });
      } else if (msg?.type === 'open-node' && msg.id) {
        openNode(msg.id, 'new');
      } else if (msg?.type === 'run-result') {
        app.dialog = { title: msg.failed ? 'The code reported errors' : 'Output', message: String(msg.output || '(no output)').slice(0, 4000), actions: [{ label: 'OK', kind: 'primary', run: () => (app.dialog = null) }] };
      }
    });
    boot();
  });
</script>

{#if phase === 'ready' && route === '/capture'}
  <CaptureWindow />
{:else if phase === 'ready' && route === '/scratchpad'}
  <ScratchpadWindow />
{:else if phase === 'ready'}
  <Shell />
{:else if phase === 'setup'}
  <Setup onopened={() => { phase = 'loading'; boot(); }} />
{:else if phase === 'auth'}
  <Splash title="Open Quantos from the app">
    <p>This browser session isn't signed in. Sign-in links are single-use and expire after a minute.</p>
    <p>{isWindowsClient ? 'Restart the Quantos window.' : 'Use “Open in browser” in the Windows client, or run'} {#if !isWindowsClient}<code>Quantos.Backend.exe --open</code>.{/if}</p>
  </Splash>
{:else if phase === 'error'}
  <Splash title="Can't reach the Quantos backend">
    <p>{message}</p>
    <button class="btn primary" onclick={() => { phase = 'loading'; boot(); }}>Try again</button>
  </Splash>
{:else}
  <Splash title="" loading />
{/if}
