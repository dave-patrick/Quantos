<script lang="ts">
  import { onMount } from 'svelte';
  import { watchWindowAppearance } from './lib/windowAppearance';
  import { ApiError, onUnauthorized } from './lib/api';
  import { startEvents, connection } from './lib/events';
  import { app } from './lib/state.svelte';
  import { layout } from './lib/layout.svelte';
  import { tasks } from './lib/tasks.svelte';
  import { bridge, isWindowsClient, isAndroidClient, isCompactAndroid } from './lib/platform';
  import { capture } from './lib/capture.svelte';
  import { terminal } from './lib/terminal.svelte';
  import { presenter } from './lib/present.svelte';
  import { aiJob } from './lib/ai.svelte';
  import './styles/android.css';
  import { docs, leaveFor } from './lib/docs.svelte';
  import { supertags } from './lib/supertags.svelte';
  import { views } from './lib/views.svelte';
  import { templatesStore } from './lib/templates.svelte';
  import { loadCustomizations } from './lib/customize.svelte';
  import { loadThemes } from './lib/appearance.svelte';
  import { announceVersion } from './lib/updates.svelte';
  import { plugins } from './lib/plugins.svelte';
  import { openNode } from './lib/links.svelte';
  import './lib/editorCommands';
  import './lib/client.svelte';
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
      loadThemes().catch(() => {});
      announceVersion().catch(() => {});
      if (route === '/capture' || route === '/scratchpad') { phase = 'ready'; return; }
      if (!status.vault) { phase = 'setup'; return; }
      const workspaces = status.workspaces ?? [];
      const scopes = [...workspaces, ...(status.linked ?? []).map((l) => l.name)];
      app.workspace = status.lastWorkspace && scopes.includes(status.lastWorkspace) ? status.lastWorkspace : workspaces.includes('default') ? 'default' : workspaces[0] ?? scopes[0] ?? 'default';
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
      if (err instanceof ApiError && err.status === 401) signedOut();
      else { phase = 'error'; message = (err as Error).message; }
    }
  }

  /** In the Windows client a lost sign-in (e.g. the backend restarted) is renewed by the window itself. */
  let reauthAsked = false;
  function signedOut() {
    phase = 'auth';
    if ((!isWindowsClient && !isAndroidClient) || reauthAsked) return;
    reauthAsked = true;
    let recent: number[] = [];
    try { recent = (JSON.parse(sessionStorage.getItem('quantos-reauth') ?? '[]') as number[]).filter((t) => Date.now() - t < 60_000); } catch { /* storage unavailable */ }
    if (recent.length >= 3) return; // something is wrong; leave the message up rather than loop
    try { sessionStorage.setItem('quantos-reauth', JSON.stringify([...recent, Date.now()])); } catch { /* storage unavailable */ }
    docs.keepDrafts();
    bridge.send({ type: 'reauth', path: location.pathname });
  }

  onMount(() => {
    const stopWindowAppearance = watchWindowAppearance();
    onUnauthorized(signedOut);
    connection.listeners.add((c) => (app.connected = c));
    bridge.send({ type: 'ready' });
    bridge.on(async (msg) => {
      if (msg?.type === 'back') {
        const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
        (document.activeElement ?? document.body).dispatchEvent(escape);
        if (escape.defaultPrevented) return;
        if (presenter.active) presenter.close();
        else if (aiJob.open) aiJob.close();
        else if (app.asking) app.asking.done(null);
        else if (app.dialog) app.dialog = null;
        else if (capture.open) capture.open = false;
        else if (app.paletteOpen) app.paletteOpen = false;
        else if (terminal.open) terminal.open = false;
        else if (app.leftOpen && (innerWidth <= 700 || isCompactAndroid())) app.leftOpen = false;
        else if (app.rightOpen && (innerWidth <= 700 || isCompactAndroid())) app.rightOpen = false;
        else if (layout.pane.back.length) await layout.back();
        else { docs.keepDrafts(); bridge.send({ type: 'exit' }); }
      } else if (msg?.type === 'mcp-copied') {
        app.toast('MCP connection copied, including its agent key.', 'ok');
      } else if (msg?.type === 'flush') {
        docs.keepDrafts();
        await docs.flushAll();
        bridge.send({ type: 'pending-saves', count: docs.pendingCount() });
      } else if (msg?.type === 'signin' && typeof msg.url === 'string') {
        leaveFor(msg.url);
      } else if (msg?.type === 'open-node' && msg.id) {
        openNode(msg.id, 'new');
      } else if (msg?.type === 'run-result') {
        app.dialog = { title: msg.failed ? 'The code reported errors' : 'Output', message: String(msg.output || '(no output)').slice(0, 4000), actions: [{ label: 'OK', kind: 'primary', run: () => (app.dialog = null) }] };
      }
    });
    boot();
    return stopWindowAppearance;
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
    <p>{isWindowsClient ? 'Quantos is signing this window back in. If this stays, choose Exit from the tray icon and start Quantos again.' : 'Use “Open in browser” in the Windows client, or run'} {#if !isWindowsClient}<code>Quantos.Backend.exe --open</code>.{/if}</p>
  </Splash>
{:else if phase === 'error'}
  <Splash title="Can't reach the Quantos backend">
    <p>{message}</p>
    <button class="btn primary" onclick={() => { phase = 'loading'; boot(); }}>Try again</button>
  </Splash>
{:else}
  <Splash title="" loading />
{/if}
