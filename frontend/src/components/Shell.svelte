<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../lib/state.svelte';
  import { handleGlobalKey } from '../lib/commands.svelte';
  import { watchDate } from '../lib/dates';
  import { isWindowsClient } from '../lib/platform';
  import { terminal } from '../lib/terminal.svelte';
  import Header from './Header.svelte';
  import LeftSidebar from './LeftSidebar.svelte';
  import RightSidebar from './RightSidebar.svelte';
  import PaneArea from './PaneArea.svelte';
  import StatusBar from './StatusBar.svelte';
  import CommandPalette from './CommandPalette.svelte';
  import Toasts from './Toasts.svelte';
  import Dialog from './Dialog.svelte';
  import HoverPreview from './HoverPreview.svelte';
  import TerminalDrawer from './TerminalDrawer.svelte';
  import Approvals from './Approvals.svelte';
  import CaptureDialog from './CaptureDialog.svelte';

  let today = $state('');
  onMount(() => watchDate((d) => (today = d)));

  function onkeydown(e: KeyboardEvent) {
    if (app.dialog) return;
    handleGlobalKey(e);
  }
</script>

<svelte:window {onkeydown} />

<div class="shell" class:left={app.leftOpen} class:right={app.rightOpen}>
  <Header />
  {#if app.leftOpen}<LeftSidebar />{/if}
  <main class="center">
    <PaneArea {today} />
    {#if isWindowsClient && terminal.open}<TerminalDrawer />{/if}
  </main>
  {#if app.rightOpen}<RightSidebar />{/if}
  <StatusBar />
</div>

<CommandPalette />
<HoverPreview />
<Approvals />
<CaptureDialog />
<Toasts />
<Dialog />

<style>
  .shell {
    height: 100%;
    display: grid;
    grid-template-columns: 0 minmax(0, 1fr) 0;
    grid-template-rows: 44px minmax(0, 1fr) var(--statusbar-h);
    grid-template-areas: 'header header header' 'left center right' 'status status status';
  }
  .shell.left { grid-template-columns: var(--sidebar-w) minmax(0, 1fr) 0; }
  .shell.right { grid-template-columns: 0 minmax(0, 1fr) var(--rightbar-w); }
  .shell.left.right { grid-template-columns: var(--sidebar-w) minmax(0, 1fr) var(--rightbar-w); }
  .center { grid-area: center; display: flex; flex-direction: column; min-width: 0; min-height: 0; background: var(--bg); }
  @media (max-width: 700px) {
    .shell, .shell.left, .shell.right, .shell.left.right { grid-template-columns: 0 minmax(0, 1fr) 0; }
    .shell.left :global(.sidebar), .shell.right :global(.rightbar) {
      position: fixed; top: 44px; bottom: var(--statusbar-h); width: min(300px, 86vw); z-index: 50; box-shadow: var(--shadow); display: flex;
    }
    .shell.left :global(.sidebar) { left: 0; }
    .shell.right :global(.rightbar) { right: 0; }
  }
  @media (min-width: 701px) and (max-width: 900px) {
    .shell.left.right { grid-template-columns: var(--sidebar-w) minmax(0, 1fr) 0; }
    .shell.left.right :global(.rightbar) { display: none; }
  }
</style>
