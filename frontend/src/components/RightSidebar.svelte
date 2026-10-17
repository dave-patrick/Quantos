<script lang="ts">
  import { app, type RightTab } from '../lib/state.svelte';
  import { notifications } from '../lib/notifications.svelte';
  import FileTree from './FileTree.svelte';
  import LinksPanel from './LinksPanel.svelte';
  import InspectorPanel from './InspectorPanel.svelte';
  import NotificationsPanel from './NotificationsPanel.svelte';
  import LocalGraph from './LocalGraph.svelte';
  import Icon from './Icon.svelte';

  const tabs: { key: RightTab; icon: string; label: string }[] = [
    { key: 'files', icon: 'folder', label: 'Files' },
    { key: 'inspector', icon: 'tag', label: 'Properties' },
    { key: 'links', icon: 'link', label: 'Links' },
    { key: 'graph', icon: 'graph', label: 'Graph' },
    { key: 'notifications', icon: 'bell', label: 'Notifications' }
  ];

  /** Arrow keys move between the panel tabs (tabs pattern); only the selected one is in the Tab order. */
  function tabKeys(e: KeyboardEvent, i: number) {
    const next = e.key === 'ArrowRight' ? (i + 1) % tabs.length : e.key === 'ArrowLeft' ? (i - 1 + tabs.length) % tabs.length
      : e.key === 'Home' ? 0 : e.key === 'End' ? tabs.length - 1 : -1;
    if (next < 0) return;
    e.preventDefault();
    app.rightTab = tabs[next].key;
    const strip = (e.currentTarget as HTMLElement).parentElement;
    queueMicrotask(() => strip?.querySelectorAll<HTMLElement>('[role=tab]')[next]?.focus());
  }
</script>

<aside class="rightbar" aria-label="Inspector">
  <div class="tabs" role="tablist">
    {#each tabs as t, i (t.key)}
      <button role="tab" aria-selected={app.rightTab === t.key} tabindex={app.rightTab === t.key ? 0 : -1} aria-label={t.label} title={t.label} onkeydown={(e) => tabKeys(e, i)} class:on={app.rightTab === t.key} onclick={() => (app.rightTab = t.key)}>
        <Icon name={t.icon} size={15} />
        {#if t.key === 'notifications' && notifications.unread}<span class="pip">{notifications.unread}</span>{/if}
      </button>
    {/each}
  </div>
  <div class="panel">
    {#if app.rightTab === 'files'}<FileTree />
    {:else if app.rightTab === 'inspector'}<InspectorPanel />
    {:else if app.rightTab === 'links'}<LinksPanel />
    {:else if app.rightTab === 'graph'}<LocalGraph />
    {:else}<NotificationsPanel />{/if}
  </div>
</aside>

<style>
  .rightbar { grid-area: right; display: flex; flex-direction: column; min-height: 0; background: var(--bg-raised); border-left: 1px solid var(--line); }
  .tabs { display: flex; gap: 2px; padding: 6px 6px 0; border-bottom: 1px solid var(--line); flex: none; }
  .tabs button { position: relative; flex: 1; display: grid; place-items: center; height: 32px; border: 0; background: none; color: var(--text-faint); border-radius: 6px 6px 0 0; }
  .tabs button:hover { color: var(--text); background: var(--bg-hover); }
  .tabs button.on { color: var(--accent); box-shadow: inset 0 -2px 0 var(--accent); }
  .pip { position: absolute; top: 3px; right: 8px; min-width: 15px; height: 15px; padding: 0 4px; border-radius: 8px; background: var(--accent); color: var(--accent-ink); font: 700 9.5px/15px var(--font-mono); }
  .panel { flex: 1; min-height: 0; display: flex; flex-direction: column; }
</style>
